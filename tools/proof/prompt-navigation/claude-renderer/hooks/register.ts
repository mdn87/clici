import type { EngineInterface, HookStream, ProcessSpawnChunk, ProcessSpawnResult, Register, Timer } from 'claude-code';
import { PromptAnchors, anchorColors } from './anchors.ts';

// Frames keep the helper's input lease alive; one lease lasts 750 ms in the
// helper, so a stalled plugin loses navigation within one lease.
const HEARTBEAT_MS = 100;
// Tools whose own dialog holds the keyboard until the call settles; a hook's
// `allow` verdict never dismisses them.
const INTERACTIVE_TOOLS = new Set(['AskUserQuestion', 'ExitPlanMode']);

type Bridge = {
    path: string;
    sequence: number;
    dirty: boolean;
    ended: boolean;
    pending?: Promise<void>;
    heartbeat?: Timer;
    stream?: HookStream<ProcessSpawnChunk, ProcessSpawnResult>;
};

// Composer ownership evidence. Navigation is offered only while every item
// says the composer has the keyboard; missing evidence withdraws it.
type Ownership = {
    anchors: PromptAnchors;
    bridge?: Bridge;
    lastSessionId: string | null;
    classicViewport: boolean; // A UserMessage drew in a non-fullscreen terminal viewport.
    dialogs: Set<string>; // Tool calls that may hold a permission or question dialog.
    panel: boolean; // A slash command may have opened a panel; released by the next prompt.
};

function ownsComposer(state: Ownership) {
    const { anchors } = state;
    return anchors.active && anchors.sessionId !== null && !anchors.overflow &&
        state.classicViewport && state.dialogs.size === 0 && !state.panel;
}

function frame(state: Ownership, observedAt: number, child: Bridge) {
    const report = state.anchors.report();
    const sessionId = report.sessionId ?? state.lastSessionId;
    if (sessionId === null) return undefined;
    state.lastSessionId = sessionId;
    const composerOwnsInput = ownsComposer(state);
    return {
        version: 1, sequence: ++child.sequence, observedAt, agent: 'claude-code', sessionId,
        epoch: report.epoch, composerOwnsInput, historyComplete: false,
        prompts: composerOwnsInput ? report.prompts : [],
    };
}

function stop(state: Ownership, child: Bridge) {
    child.ended = true;
    child.heartbeat?.cancel();
    void child.stream?.return({ code: null, signal: null }).catch(() => undefined);
    if (state.bridge === child) state.bridge = undefined;
}

// Publishes the current ownership frame; coalesces concurrent requests so
// writes never overlap and the newest state always lands last.
function publish($: EngineInterface, state: Ownership): Promise<void> {
    const child = state.bridge;
    if (!child || child.ended) return Promise.resolve();
    child.dirty = true;
    child.pending ??= (async () => {
        try {
            while (child.dirty && !child.ended) {
                child.dirty = false;
                const observedAt = await $.clock.now(); // Before the state is examined.
                const next = frame(state, observedAt, child);
                if (next) await $.fs.write(child.path, JSON.stringify(next));
            }
        } catch (error) {
            $.ui.log(`clici-renderer: frame publication stopped: ${String(error)}`, { to: 'debug' });
            stop(state, child);
        } finally {
            child.pending = undefined;
        }
    })();
    return child.pending;
}

function start($: EngineInterface, state: Ownership, helper: string, temp: string) {
    const child: Bridge = {
        path: `${temp.replaceAll('\\', '/')}/clici-renderer/${crypto.randomUUID()}.json`,
        sequence: 0, dirty: false, ended: false,
    };
    state.bridge = child;
    const stream = $.process.spawn({ argv: [helper, 'renderer-bridge-frames', child.path] });
    child.stream = stream;
    // The loop is the helper's life: it ends with the helper or with this module.
    void (async () => {
        try {
            for await (const { stream: pipe, text } of stream) {
                if (!text.includes('"action":"Observed"')) $.ui.log(`clici-renderer bridge ${pipe}: ${text.trimEnd()}`, { to: 'debug' });
            }
        } catch (error) {
            $.ui.log(`clici-renderer bridge unavailable: ${String(error)}`, { to: 'debug' });
        } finally {
            stop(state, child);
        }
    })();
    child.heartbeat = $.clock.every(HEARTBEAT_MS, () => { void publish($, state); });
}

export const register: Register = on => {
    const anchors = new PromptAnchors(() => crypto.randomUUID().replaceAll('-', ''));
    const state: Ownership = { anchors, lastSessionId: null, classicViewport: false, dialogs: new Set(), panel: false };
    let reportPath: string | undefined;
    let reportTimer: Timer | undefined;
    let reporting = false;
    let enabled = false;

    on('session.start', async ($, e, next) => {
        reportTimer?.cancel();
        // Not pinned to an engine version: a release that changes the drawing
        // fails Resolve in the helper, which passes keys through.
        enabled = await $.env.get('CLICI_CLAUDE_ANCHORS') === '1';
        reportPath = enabled ? await $.env.get('CLICI_CLAUDE_ANCHOR_REPORT') : undefined;
        anchors.reset(enabled && e.surface === 'terminal' && e.isInteractive);
        state.dialogs.clear();
        state.panel = false;
        const epoch = anchors.epoch;
        if (anchors.active) anchors.bind(await $.session.id(), epoch);
        if (reportPath) reportTimer = $.clock.every(250, () => {
            // Diagnostics must not hold up a render hook or a conversation append.
            if (!reportPath || reporting) return;
            reporting = true;
            void $.fs.write(reportPath, JSON.stringify(anchors.report()))
                .catch(() => reportTimer?.cancel()).finally(() => { reporting = false; });
        });
        if (anchors.active && state.bridge === undefined) {
            const helper = await $.env.get('CLICI_RENDERER_BRIDGE');
            const temp = await $.env.get('TEMP') ?? await $.env.get('TMP');
            if (helper && temp) start($, state, helper, temp);
        }
        await $.command.register({ name: 'clici-renderer', description: 'Inspect Clici renderer metadata' });
        void publish($, state);
        return next(e);
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });

    on('session.append', { door: 'prompt' }, async ($, e, next) => {
        const epoch = anchors.epoch;
        const eligible = e.agentId === undefined && e.origin.kind === 'composer' &&
            e.message.type === 'user' && !e.message.isMeta;
        const order = anchors.reserveOrder(); // Before any async lookup can reorder hooks.
        if (eligible && anchors.active && anchors.bind(await $.session.id(), epoch)) anchors.beginAppend(e.uuid, epoch, order);
        const stored = await next(e);
        if (eligible && stored.deny === undefined && stored.uuid === e.uuid &&
            stored.message.type === 'user' && !stored.message.isMeta) anchors.commit(stored.uuid, epoch);
        if (eligible) state.panel = false; // The composer accepted a submission.
        void publish($, state);
        return stored;
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });

    on('ui.render', { component: 'UserMessage' }, async ($, e, next) => {
        const epoch = anchors.epoch;
        const original = await next(e);
        if (e.surface === 'terminal' && e.viewport !== undefined && state.classicViewport !== (e.viewport.isFullscreen === false)) {
            state.classicViewport = e.viewport.isFullscreen === false;
            void publish($, state);
        }
        if (e.surface !== 'terminal' || e.viewport?.isFullscreen !== false ||
            e.props.origin.kind !== 'composer' || !anchors.active) return original;
        const nonce = anchors.render(e.requestId, epoch);
        if (!nonce) return original;
        const { Box, Text } = $.ui.resolve(e);
        const row = (end: boolean) => Text({ children: anchorColors(nonce, end).map(color => Text({ color, children: [' '] })) });
        const result = Box({ flexDirection: 'column', children: [row(false), original, row(true)] });
        return result;
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });

    // A tool call may open a permission dialog or a question; the keyboard is
    // withdrawn from the moment the call is raised until the verdict shows no
    // dialog, or the call settles.
    on('tool.call', async ($, e, next) => {
        state.dialogs.add(e.tool_use_id);
        void publish($, state);
        try {
            return await next(e);
        } finally {
            if (state.dialogs.delete(e.tool_use_id)) void publish($, state);
        }
    }).catch(($, e, next) => { if (state.dialogs.delete(e.tool_use_id)) void publish($, state); return next(e); });

    on('tool.check', async ($, e, next) => {
        const verdict = await next(e);
        if (e.tool_use_id !== undefined && verdict.decision !== 'ask' && !INTERACTIVE_TOOLS.has(e.tool) &&
            state.dialogs.delete(e.tool_use_id)) void publish($, state);
        return verdict;
    }).catch((_$, e, next) => next(e));

    on('session.end', async ($, e, next) => {
        anchors.reset(enabled && (e.reason === 'clear' || e.reason === 'resume'));
        state.dialogs.clear();
        state.panel = false;
        if (!anchors.active) { reportTimer?.cancel(); state.bridge?.heartbeat?.cancel(); }
        if (reportPath) await $.fs.write(reportPath, JSON.stringify(anchors.report()));
        await publish($, state); // Withdraw before the process or the transcript goes away.
        return next(e);
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });
    on('session.compact', async ($, e, next) => {
        const epoch = anchors.epoch;
        const result = await next(e);
        if (e.agentId === undefined && e.trigger !== 'precompute' && !result.skip && anchors.epoch === epoch) {
            anchors.reset(anchors.active);
            void publish($, state);
        }
        return result;
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });
    on('command.run', async ($, e, next) => {
        if (e.command === 'clici-renderer') return { text: JSON.stringify({ ...anchors.report(), composerOwnsInput: ownsComposer(state), bridge: state.bridge !== undefined }) };
        // Another command may open a panel the engine does not report; hold
        // navigation until the composer accepts a prompt again.
        state.panel = true;
        void publish($, state);
        return next(e);
    }).catch((_$, e, next) => next(e));
};

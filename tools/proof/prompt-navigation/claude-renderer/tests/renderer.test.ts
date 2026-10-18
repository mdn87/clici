import { expect, mock, test } from 'claude-code/testing';
import type { TestBody } from 'claude-code/testing';

type Engine = Parameters<TestBody>[0];
type On = Parameters<TestBody>[1];

const HELPER = 'C:/clici/helper/PromptNavigation.Probe.exe';
const TOOL = 'mcp__clici__probe';

type Frame = { version: number; sequence: number; observedAt: number; agent: string; sessionId: string; epoch: number;
    composerOwnsInput: boolean; historyComplete: boolean; prompts: { messageId: string; nonce: string }[] };

async function start($: Engine, on: On, enabled = true, version = '2.1.296', extra: Record<string, string> = {}) {
    mock.env(on, enabled ? { CLICI_CLAUDE_ANCHORS: '1', ...extra } : extra);
    on('session.version', () => ({ value: { version } }));
    on('session.id', () => ({ value: 'session-a' }));
    on('command.register', () => ({ value: { command: 'clici-renderer' } }));
    on('session.start', (_$, e) => ({ cwd: e.cwd }));
    on('session.end', (_$, e) => ({ sessionId: e.sessionId }));
    on('ui.render', () => ({ type: 'Text', children: ['UNCHANGED ORIGINAL'] }));
    await $.session.start({ cwd: '/synthetic', surface: 'terminal', isInteractive: true });
}

// The world beneath the plugin when a helper is configured: a mocked clock,
// the frame file, the spawned helper and the tool decision beneath.
function bridge(on: On) {
    const frames: Frame[] = [];
    const writes: string[] = [];
    const spawned: string[][] = [];
    const logs: string[] = [];
    const clock = mock.clock(on, { now: 1_700_000_000_000 });
    let exitHelper = () => undefined as void;
    let settleTool = () => undefined as void;
    let verdict: 'allow' | 'ask' = 'ask';
    let failWrites = 0;
    let failSpawns = 0;
    on('fs.write', (_$, e) => {
        if (failWrites > 0) { failWrites--; throw new Error('disk full'); }
        writes.push(e.path); frames.push(JSON.parse(e.text)); return { value: undefined };
    });
    on('ui.log', (_$, e) => { logs.push(e.text); return { value: undefined }; });
    on('command.run', () => ({ text: 'other command ran' }));
    on('tool.check', () => ({ decision: verdict }));
    on('tool.call', async () => { await new Promise<void>(resolve => { settleTool = resolve; }); return { result: 'ok' }; });
    on('process.spawn', async function* (_$, e) {
        spawned.push([...e.argv]);
        if (failSpawns > 0) { failSpawns--; throw new Error('access denied'); }
        yield { stream: 'stdout' as const, text: '{"version":1,"ready":true,"parentProcessId":1,"agent":"claude-code"}\n' };
        await new Promise<void>(resolve => { exitHelper = resolve; });
        return { value: { code: 1, signal: null } };
    });
    return { frames, writes, spawned, logs, clock, last: () => frames[frames.length - 1]!,
        exitHelper: () => exitHelper(), settleTool: () => settleTool(), allow: () => { verdict = 'allow'; },
        failWrite: () => { failWrites++; }, failSpawn: () => { failSpawns++; } };
}

const BRIDGE_ENV = { CLICI_RENDERER_BRIDGE: HELPER, TEMP: 'C:\\Users\\matt\\Temp' };
const prompt = (uuid: string) => ({ uuid, door: 'prompt' as const, origin: { kind: 'composer' as const },
    message: { type: 'user' as const, role: 'user' as const, content: [{ type: 'text' as const, text: 'same 日本語 🙂' }] } });
const render = (requestId: string, isFullscreen = false) => ({ component: 'UserMessage' as const, requestId,
    surface: 'terminal' as const, viewport: { columns: 100, rows: 30, isFullscreen },
    props: { text: 'same 日本語 🙂', origin: { kind: 'composer' as const }, isExpanded: false } });
const presentation = { args: '', origin: { kind: 'composer' as const }, presentation: { isFullscreen: false, columns: 100 } };
async function report($: Engine) {
    const result = await $.command.run({ command: 'clici-renderer', ...presentation });
    return JSON.parse(result.text!);
}

test('real hook runtime wraps original rendering with blank anchors and preserves stored text', async ($, on) => {
    await start($, on);
    const message = prompt('one');
    expect((await $.session.append(message)).message).toEqual(message.message);
    const tree = await $.ui.render(render('one'));
    expect(tree.type).toBe('Box');
    expect(JSON.stringify(tree)).toContain('UNCHANGED ORIGINAL');
    const result = await report($);
    expect(result.prompts.length).toBe(1);
    expect(result.prompts[0].messageId).toBe('one');
    expect(result.prompts[0].nonce.length).toBe(32);
    expect(result.navigationEligible).toBe(false);
    expect(result.bridge).toBe(false); // No helper configured: nothing was spawned.
    expect(JSON.stringify(result)).not.toContain('日本語');
});

test('render-only, subagent and meta rows never become main conversation bindings', async ($, on) => {
    await start($, on);
    await $.session.append({ ...prompt('subagent'), agentId: 'worker' });
    await $.session.append({ ...prompt('meta'), message: { ...prompt('meta').message, isMeta: true } });
    for (const id of ['resumed', 'subagent', 'meta']) await $.ui.render(render(id));
    expect((await report($)).prompts).toEqual([]);
});

test('fullscreen rendering is passed through unchanged', async ($, on) => {
    await start($, on);
    await $.session.append(prompt('one'));
    expect((await $.ui.render(render('one', true))).type).toBe('Text');
    expect((await report($)).prompts.length).toBe(1); // Unpainted pair cannot resolve in COM.
});

test('disabled adapter returns the original render tree', async ($, on) => {
    await start($, on, false);
    expect((await $.ui.render(render('one'))).type).toBe('Text');
    expect((await report($)).active).toBe(false);
});

test('a newer engine version still anchors prompts', async ($, on) => {
    await start($, on, true, '2.1.999');
    expect((await report($)).active).toBe(true);
});

test('session exit withdraws all bindings', async ($, on) => {
    await start($, on);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    await $.session.end({ reason: 'prompt_input_exit', sessionId: 'session-a', resume: { id: 'session-a' } });
    expect((await report($)).prompts).toEqual([]);
    expect((await report($)).active).toBe(false);
});

test('a configured helper is spawned on a private frame file and heartbeat frames carry the bindings', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await world.clock.settle();
    expect(world.spawned.length).toBe(1);
    const [helper, mode, path] = world.spawned[0]!;
    expect(helper).toBe(HELPER);
    expect(mode).toBe('renderer-bridge-frames');
    expect(path).toMatch(/^C:\/Users\/matt\/Temp\/clici-renderer\/[0-9a-f-]{36}\.json$/);
    // The engine hands the hook the path in the host's own spelling; it names the same file.
    expect(world.writes.every(written => written.replaceAll(String.fromCharCode(92), '/') === path)).toBe(true);
    expect(world.last().composerOwnsInput).toBe(false); // Nothing has drawn in a classic viewport yet.
    expect(world.last().prompts).toEqual([]);

    await $.session.append(prompt('one'));
    await $.ui.render(render('one'));
    await world.clock.settle();
    const owned = world.last();
    expect(owned.composerOwnsInput).toBe(true);
    expect(owned.prompts.length).toBe(1);
    expect(owned.prompts[0]!.messageId).toBe('one');
    expect(owned.prompts[0]!.nonce.length).toBe(32);
    expect(owned).toEqual({ version: 1, sequence: owned.sequence, observedAt: world.clock.now(), agent: 'claude-code',
        sessionId: 'session-a', epoch: owned.epoch, composerOwnsInput: true, historyComplete: false, prompts: owned.prompts });
    expect((await report($)).composerOwnsInput).toBe(true);
    expect((await report($)).bridge).toBe(true);

    const before = world.frames.length;
    await world.clock.advance(350);
    expect(world.frames.length).toBe(before + 3); // One frame per 100 ms heartbeat, each a fresh observation.
    const sequences = world.frames.map(frame => frame.sequence);
    expect(sequences).toEqual([...sequences].sort((a, b) => a - b));
    expect(new Set(sequences).size).toBe(sequences.length);
    expect(world.last().observedAt).toBe(world.clock.now() - 50); // Observed when the last heartbeat fired.
    expect(world.last().prompts).toEqual(owned.prompts);
});

test('a fullscreen draw withdraws ownership until a classic draw returns', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
    await $.ui.render(render('one', true));
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(false);
    expect(world.last().prompts).toEqual([]);
    await $.ui.render(render('one'));
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
});

test('a tool call that may hold a dialog withdraws ownership until it settles', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
    const call = $.tool.call({ tool: TOOL, tool_use_id: 'call-1' });
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(false);
    expect(world.last().prompts).toEqual([]);
    await world.clock.advance(200);
    expect(world.last().composerOwnsInput).toBe(false); // Heartbeats keep the withdrawal current.
    world.settleTool();
    expect((await call).result).toBe('ok');
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
    expect(world.last().prompts.length).toBe(1);
});

test('a verdict that opens no dialog releases the keyboard before the tool finishes', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    world.allow();
    const call = $.tool.call({ tool: TOOL, tool_use_id: 'call-2' });
    await world.clock.settle();
    const check = await $.tool.check({ tool: TOOL, input: {}, tool_use_id: 'call-2' } as Parameters<Engine['tool']['check']>[0]);
    expect(check.decision).toBe('allow');
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true); // Allowed: no permission dialog holds the keys.
    world.settleTool();
    await call;
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
    const query = await $.tool.check({ tool: TOOL, input: {} });
    expect(query.decision).toBe('allow'); // A query without a call changes nothing.
});

test('an interactive tool keeps the keyboard withdrawn whatever the verdict says', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    world.allow();
    const call = $.tool.call({ tool: 'AskUserQuestion', tool_use_id: 'ask-1', questions: [] });
    await world.clock.settle();
    await $.tool.check({ tool: 'AskUserQuestion', input: {}, tool_use_id: 'ask-1' } as Parameters<Engine['tool']['check']>[0]);
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(false);
    world.settleTool();
    await call;
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
});

test('another slash command withdraws ownership until the composer accepts a prompt', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
    expect((await $.command.run({ command: 'other', ...presentation })).text).toBe('other command ran');
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(false);
    expect((await report($)).composerOwnsInput).toBe(false); // The inspection command itself changes nothing.
    await $.session.append(prompt('two'));
    await world.clock.settle();
    expect(world.last().composerOwnsInput).toBe(true);
    expect(world.last().prompts.map(binding => binding.messageId)).toEqual(['one', 'two']);
});

test('session end publishes a withdrawal and stops the heartbeat', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    await world.clock.settle();
    await $.session.end({ reason: 'prompt_input_exit', sessionId: 'session-a', resume: { id: 'session-a' } });
    expect(world.last().composerOwnsInput).toBe(false);
    expect(world.last().prompts).toEqual([]);
    expect(world.last().sessionId).toBe('session-a'); // Withdrawal names the session it withdraws.
    const count = world.frames.length;
    await world.clock.advance(500);
    expect(world.frames.length).toBe(count);
});

// Advances past a replacement wait and lets the new helper's first frames land.
async function wait(world: ReturnType<typeof bridge>, ms: number) {
    await world.clock.advance(ms);
    await world.clock.settle();
}

test('a helper that exits is replaced on a fresh frame file and keeps the painted prompts', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    await world.clock.settle();
    const owned = world.last();
    world.exitHelper();
    await world.clock.settle();
    expect((await report($)).bridge).toBe(false);
    const count = world.frames.length;
    await wait(world, 999);
    expect(world.spawned.length).toBe(1);
    expect(world.frames.length).toBe(count); // Nothing renews the old helper's lease while it waits.
    await wait(world, 1);
    expect(world.spawned.length).toBe(2);
    const [oldPath, newPath] = world.spawned.map(argv => argv[2]!);
    expect(newPath).not.toBe(oldPath);
    expect(world.writes.slice(count).every(written => written.replaceAll(String.fromCharCode(92), '/') === newPath)).toBe(true);
    await wait(world, 300); // The replacement's own heartbeat publishes with no hook event.
    expect(world.frames.length).toBeGreaterThan(count + 2);
    expect(world.last().composerOwnsInput).toBe(true);
    expect(world.last().prompts).toEqual(owned.prompts);
    expect((await report($)).bridge).toBe(true);
});

test('replacements back off from 1 s to 30 s and stop after ten', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await world.clock.settle();
    const delays = [1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000, 30000, 30000];
    for (const [index, delay] of delays.entries()) {
        world.exitHelper();
        await wait(world, delay - 1);
        expect(world.spawned.length).toBe(index + 1);
        await wait(world, 1);
        expect(world.spawned.length).toBe(index + 2);
    }
    world.exitHelper();
    await wait(world, 120_000);
    expect(world.spawned.length).toBe(11);
    expect(world.logs.filter(line => line.includes('not replaced')).length).toBe(1);
    expect((await report($)).bridge).toBe(false);
});

test('a failed frame write and the stream end of one helper count as one failure', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await world.clock.settle();
    world.failWrite();
    await wait(world, 100);
    expect((await report($)).bridge).toBe(false);
    world.exitHelper(); // The same helper's stream now ends too.
    await wait(world, 1000);
    expect(world.spawned.length).toBe(2);
    world.exitHelper();
    await wait(world, 1999);
    expect(world.spawned.length).toBe(2); // One failure was counted, so the second wait is 2 s, not 4 s.
    await wait(world, 1);
    expect(world.spawned.length).toBe(3);
});

test('a helper that fails to start leaves no ghost bridge and is retried', async ($, on) => {
    const world = bridge(on);
    world.failSpawn();
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await world.clock.settle();
    expect((await report($)).bridge).toBe(false);
    await wait(world, 1000);
    expect(world.spawned.length).toBe(2);
    expect((await report($)).bridge).toBe(true);
});

test('session end never replaces the helper and cancels a pending replacement', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await world.clock.settle();
    world.exitHelper();
    await world.clock.settle();
    await $.session.end({ reason: 'prompt_input_exit', sessionId: 'session-a', resume: { id: 'session-a' } });
    await wait(world, 5000);
    expect(world.spawned.length).toBe(1);
});

test('a helper torn down by session end is not counted as a failure', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await world.clock.settle();
    await $.session.end({ reason: 'prompt_input_exit', sessionId: 'session-a', resume: { id: 'session-a' } });
    world.exitHelper();
    await wait(world, 5000);
    expect(world.spawned.length).toBe(1);
    expect(world.logs.some(line => line.includes('replacing'))).toBe(false);
});

test('a session start during a pending replacement runs exactly one helper', async ($, on) => {
    const world = bridge(on);
    await start($, on, true, '2.1.296', BRIDGE_ENV);
    await world.clock.settle();
    world.exitHelper();
    await world.clock.settle();
    await $.session.start({ cwd: '/synthetic', surface: 'terminal', isInteractive: true });
    await world.clock.settle();
    expect(world.spawned.length).toBe(2);
    await wait(world, 5000);
    expect(world.spawned.length).toBe(2);
});

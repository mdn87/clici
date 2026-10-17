import type { Register, Timer } from 'claude-code';
import { PromptAnchors, anchorColors } from './anchors.ts';

export const register: Register = on => {
    const anchors = new PromptAnchors(() => crypto.randomUUID().replaceAll('-', ''));
    let reportPath: string | undefined;
    let reportTimer: Timer | undefined;
    let reporting = false;
    let enabled = false;

    on('session.start', async ($, e, next) => {
        reportTimer?.cancel();
        enabled = await $.env.get('CLICI_CLAUDE_ANCHORS') === '1' &&
            (await $.session.version()).version === '2.1.296';
        reportPath = enabled ? await $.env.get('CLICI_CLAUDE_ANCHOR_REPORT') : undefined;
        anchors.reset(enabled && e.surface === 'terminal' && e.isInteractive);
        const epoch = anchors.epoch;
        if (anchors.active) anchors.bind(await $.session.id(), epoch);
        if (reportPath) reportTimer = $.clock.every(250, () => {
            // Diagnostics must not hold up a render hook or a conversation append.
            if (!reportPath || reporting) return;
            reporting = true;
            void $.fs.write(reportPath, JSON.stringify(anchors.report()))
                .catch(() => reportTimer?.cancel()).finally(() => { reporting = false; });
        });
        await $.command.register({ name: 'clici-renderer', description: 'Inspect Clici renderer metadata' });
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
        return stored;
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });

    on('ui.render', { component: 'UserMessage' }, async ($, e, next) => {
        const epoch = anchors.epoch;
        const original = await next(e);
        if (e.surface !== 'terminal' || e.viewport?.isFullscreen !== false ||
            e.props.origin.kind !== 'composer' || !anchors.active) return original;
        const nonce = anchors.render(e.requestId, epoch);
        if (!nonce) return original;
        const { Box, Text } = $.ui.resolve(e);
        const row = (end: boolean) => Text({ children: anchorColors(nonce, end).map(color => Text({ color, children: [' '] })) });
        const result = Box({ flexDirection: 'column', children: [row(false), original, row(true)] });
        return result;
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });

    on('session.end', async ($, e, next) => {
        anchors.reset(enabled && (e.reason === 'clear' || e.reason === 'resume'));
        if (!anchors.active) reportTimer?.cancel();
        if (reportPath) await $.fs.write(reportPath, JSON.stringify(anchors.report()));
        return next(e);
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });
    on('session.compact', async (_$, e, next) => {
        const epoch = anchors.epoch;
        const result = await next(e);
        if (e.agentId === undefined && e.trigger !== 'precompute' && !result.skip && anchors.epoch === epoch) anchors.reset(anchors.active);
        return result;
    }).catch((_$, e, next) => { anchors.reset(false); return next(e); });
    on('command.run', { command: 'clici-renderer' }, () => ({ text: JSON.stringify(anchors.report()) }));
};

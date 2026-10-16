import { expect, test } from 'claude-code/testing';
import type { TestBody } from 'claude-code/testing';

type Engine = Parameters<TestBody>[0];
type On = Parameters<TestBody>[1];

async function start($: Engine, on: On, id: string | (() => string) = 'session-a', interactive = true, observe?: (event: unknown) => void) {
    on('session.id', () => ({ value: typeof id === 'function' ? id() : id }));
    on('command.register', () => ({ value: { command: 'clici-prompt-ids' } }));
    on('session.start', (_$, e) => ({ cwd: e.cwd }));
    on('session.end', (_$, e) => ({ sessionId: e.sessionId }));
    on('ui.render', (_$, e) => { observe?.(e); return { type: 'Text', children: [e.component] }; });
    await $.session.start({ cwd: '/synthetic', surface: 'terminal', isInteractive: interactive });
}

async function report($: Engine) {
    const result = await $.command.run({ command: 'clici-prompt-ids', args: '', origin: { kind: 'composer' }, presentation: { isFullscreen: false, columns: 100 } });
    return JSON.parse(result.text!);
}

function prompt(uuid: string, text = 'repeat this prompt') {
    return { uuid, door: 'prompt' as const, origin: { kind: 'composer' as const },
        message: { type: 'user' as const, role: 'user' as const, content: [{ type: 'text' as const, text }] } };
}

function render(requestId: string, kind: 'composer' | 'unclassified' | 'task-notification' = 'composer') {
    return { component: 'UserMessage' as const, requestId, surface: 'terminal' as const,
        viewport: { columns: 100, rows: 30, isFullscreen: false },
        props: { text: 'repeat this prompt', origin: { kind }, isExpanded: false } };
}

test('native UUIDs separate identical multiline Unicode prompts and join append/render evidence', async ($, on) => {
    await start($, on);
    const first = prompt('message-1', 'same\n日本語 🧭');
    const second = prompt('message-2', 'same\n日本語 🧭');
    expect((await $.session.append(first)).uuid).toBe('message-1');
    await $.session.append(second);
    await $.session.append(first);
    await $.ui.render(render('message-1'));
    const result = await report($);
    expect(result.sessionId).toBe('session-a');
    expect(result.prompts).toEqual([
        { messageId: 'message-1', stored: true, renderRequested: true },
        { messageId: 'message-2', stored: true, renderRequested: false },
    ]);
    expect(JSON.stringify(result)).not.toContain('日本語');
    expect(result.navigationEligible).toBe(false);
});

test('only main-loop composer prompts qualify, irrespective of text that imitates a prompt', async ($, on) => {
    await start($, on);
    for (const kind of ['sdk', 'bridge', 'peer', 'unclassified', 'task-notification', 'scheduled-trigger'] as const) {
        await $.session.append({ ...prompt(kind), origin: { kind } });
    }
    await $.session.append({ ...prompt('subagent'), agentId: 'worker' });
    await $.session.append({ ...prompt('meta'), message: { ...prompt('meta').message, isMeta: true } });
    await $.session.append({ ...prompt('command'), door: 'command' });
    await $.ui.render(render('old-unstamped', 'unclassified'));
    await $.ui.render(render('notification', 'task-notification'));
    expect((await report($)).prompts).toEqual([]);
});

test('a resumed render supplies an ID but never implies history coverage or a buffer position', async ($, on) => {
    await start($, on);
    await $.ui.render(render('resumed-user-message'));
    const result = await report($);
    expect(result.prompts).toEqual([{ messageId: 'resumed-user-message', stored: false, renderRequested: true }]);
    expect(result.coverage).toBe('observed-only');
    expect(result.navigationEligible).toBe(false);
});

test('clear advances the epoch, then binds the next prompt to the new native session', async ($, on) => {
    let session = 'session-a';
    await start($, on, () => session);
    await $.session.append(prompt('old'));
    const before = await report($);
    await $.session.end({ reason: 'clear', sessionId: 'session-a', resume: { id: 'session-a' } });
    session = 'session-b';
    await $.session.append(prompt('new'));
    const after = await report($);
    expect(after.epoch).toBeGreaterThan(before.epoch);
    expect(after.sessionId).toBe('session-b');
    expect(after.prompts.map((p: { messageId: string }) => p.messageId)).toEqual(['new']);
});

test('exit and noninteractive sessions cannot retain or acquire prompt candidates', async ($, on) => {
    await start($, on);
    await $.session.append(prompt('old'));
    await $.session.end({ reason: 'prompt_input_exit', sessionId: 'session-a', resume: { id: 'session-a' } });
    await $.session.append(prompt('late'));
    await $.ui.render(render('late-render'));
    expect((await report($)).prompts).toEqual([]);
    expect((await report($)).active).toBe(false);
    await $.session.start({ cwd: '/synthetic', surface: 'terminal', isInteractive: false });
    await $.session.append(prompt('print-mode'));
    expect((await report($)).prompts).toEqual([]);
});

test('render props and stored prompt text are forwarded unchanged', async ($, on) => {
    let observed: unknown;
    await start($, on, 'session-a', true, e => { observed = e; });
    const input = prompt('unchanged', 'do not submit or rewind me');
    expect((await $.session.append(input)).message).toEqual(input.message);
    const event = render('unchanged');
    await $.ui.render(event);
    expect(observed).toEqual(event);
});

test('invalid metadata passes the stored message through once and withdraws all candidates', async ($, on) => {
    let calls = 0;
    let appends = 0;
    on('session.append', (_$, e, next) => { appends++; return next(e); });
    await start($, on, () => ++calls > 1 ? '' : 'session-a');
    const input = prompt('retained');
    expect((await $.session.append(input)).message).toEqual(input.message);
    expect(appends).toBe(1);
    expect((await report($)).active).toBe(false);
    expect((await report($)).prompts).toEqual([]);
});

test('only completed main compaction resets IDs; precompute, subagents and skips do not', async ($, on) => {
    let skip = true;
    const messages = [{ role: 'user' as const, text: 'synthetic transcript', toolUses: [] }];
    on('session.compact', () => skip ? { skip: 'fixture veto' } : { messages });
    await start($, on);
    await $.session.append(prompt('before-compact'));
    const before = await report($);
    await $.session.compact({ trigger: 'manual', messages });
    expect((await report($)).epoch).toBe(before.epoch);
    skip = false;
    await $.session.compact({ trigger: 'precompute', messages });
    await $.session.compact({ trigger: 'auto', agentId: 'worker', messages });
    expect((await report($)).epoch).toBe(before.epoch);
    await $.session.compact({ trigger: 'manual', messages });
    const after = await report($);
    expect(after.epoch).toBeGreaterThan(before.epoch);
    expect(after.prompts).toEqual([]);
});

import { expect, test } from 'claude-code/testing';
import type { TestBody } from 'claude-code/testing';

type Engine = Parameters<TestBody>[0];
type On = Parameters<TestBody>[1];

async function start($: Engine, on: On, enabled = true, version = '2.1.296') {
    on('env.get', (_$, e) => ({ value: e.name === 'CLICI_CLAUDE_ANCHORS' && enabled ? '1' : undefined }));
    on('session.version', () => ({ value: { version } }));
    on('session.id', () => ({ value: 'session-a' }));
    on('command.register', () => ({ value: { command: 'clici-renderer' } }));
    on('session.start', (_$, e) => ({ cwd: e.cwd }));
    on('session.end', (_$, e) => ({ sessionId: e.sessionId }));
    on('ui.render', () => ({ type: 'Text', children: ['UNCHANGED ORIGINAL'] }));
    await $.session.start({ cwd: '/synthetic', surface: 'terminal', isInteractive: true });
}

const prompt = (uuid: string) => ({ uuid, door: 'prompt' as const, origin: { kind: 'composer' as const },
    message: { type: 'user' as const, role: 'user' as const, content: [{ type: 'text' as const, text: 'same 日本語 🙂' }] } });
const render = (requestId: string, isFullscreen = false) => ({ component: 'UserMessage' as const, requestId,
    surface: 'terminal' as const, viewport: { columns: 100, rows: 30, isFullscreen },
    props: { text: 'same 日本語 🙂', origin: { kind: 'composer' as const }, isExpanded: false } });
async function report($: Engine) {
    const result = await $.command.run({ command: 'clici-renderer', args: '', origin: { kind: 'composer' },
        presentation: { isFullscreen: false, columns: 100 } });
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

test('unqualified engine version returns the original render tree', async ($, on) => {
    await start($, on, true, '2.1.999');
    expect((await $.ui.render(render('one'))).type).toBe('Text');
    expect((await report($)).active).toBe(false);
});

test('session exit withdraws all bindings', async ($, on) => {
    await start($, on);
    await $.session.append(prompt('one')); await $.ui.render(render('one'));
    await $.session.end({ reason: 'prompt_input_exit', sessionId: 'session-a', resume: { id: 'session-a' } });
    expect((await report($)).prompts).toEqual([]);
    expect((await report($)).active).toBe(false);
});

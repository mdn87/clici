import type { Register } from 'claude-code';
import { PromptIdentityLedger } from './identity.ts';

export const register: Register = on => {
    const ledger = new PromptIdentityLedger();

    on('session.start', async ($, e, next) => {
        ledger.reset(e.surface === 'terminal' && e.isInteractive);
        const epoch = ledger.epoch;
        await $.command.register({ name: 'clici-prompt-ids', description: 'Inspect development prompt-ID metadata (no navigation)' });
        if (ledger.active) ledger.bind(await $.session.id(), epoch);
        return next(e);
    }).catch((_$, e, next) => { ledger.reset(false); return next(e); });

    on('session.append', { door: 'prompt' }, async ($, e, next) => {
        const epoch = ledger.epoch;
        const stored = await next(e);
        if (e.agentId === undefined && e.origin.kind === 'composer' && stored.deny === undefined &&
            stored.message.type === 'user' && !stored.message.isMeta && ledger.active && ledger.bind(await $.session.id(), epoch)) {
            ledger.record(stored.uuid, 'stored', epoch);
        }
        return stored; // Never rewrite or veto a conversation row.
    }).catch((_$, e, next) => { ledger.reset(false); return next(e); });

    on('ui.render', { component: 'UserMessage' }, async ($, e, next) => {
        const epoch = ledger.epoch;
        if (e.surface === 'terminal' && e.props.origin.kind === 'composer' &&
            ledger.active && ledger.bind(await $.session.id(), epoch)) {
            ledger.record(e.requestId, 'renderRequested', epoch);
        }
        return next(e); // Original props and drawing, including the draft, stay intact.
    }).catch((_$, e, next) => { ledger.reset(false); return next(e); });

    on('session.end', (_$, e, next) => {
        // Clear/resume can continue in this process without a new session.start.
        ledger.reset(ledger.active && (e.reason === 'clear' || e.reason === 'resume'));
        return next(e);
    });

    on('session.compact', async (_$, e, next) => {
        const epoch = ledger.epoch;
        const result = await next(e);
        if (e.agentId === undefined && e.trigger !== 'precompute' && !result.skip && epoch === ledger.epoch) ledger.reset(ledger.active);
        return result;
    }).catch((_$, e, next) => { ledger.reset(false); return next(e); });

    // An explicit diagnostic command, not a navigation action. Prints IDs only;
    // nothing is persisted, no session-history files are read, no model is called.
    on('command.run', { command: 'clici-prompt-ids' }, () => ({ text: JSON.stringify(ledger.report()) }));
};

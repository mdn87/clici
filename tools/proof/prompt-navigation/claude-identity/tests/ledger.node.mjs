import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PromptIdentityLedger } from '../hooks/identity.ts';

function bound(capacity) {
    const ledger = new PromptIdentityLedger(capacity);
    ledger.reset(true);
    ledger.bind('session-a', ledger.epoch);
    return ledger;
}

test('old asynchronous results cannot populate a cleared or resumed generation', () => {
    const ledger = bound();
    const previous = ledger.epoch;
    ledger.reset(true);
    ledger.bind('session-b', ledger.epoch);
    assert.equal(ledger.bind('session-a', previous), false);
    ledger.record('late', 'stored', previous);
    assert.deepEqual(ledger.report().prompts, []);
    assert.equal(ledger.report().sessionId, 'session-b');
});

test('unexpected session changes fail closed even when the message ID is reused', () => {
    const ledger = bound();
    ledger.record('same-id', 'stored', ledger.epoch);
    assert.equal(ledger.bind('session-b', ledger.epoch), false);
    assert.equal(ledger.report().active, false);
    assert.deepEqual(ledger.report().prompts, []);
});

test('bounded metadata reports overflow without displacing an existing ID', () => {
    const ledger = bound(2);
    ledger.record('one', 'stored', ledger.epoch);
    ledger.record('two', 'stored', ledger.epoch);
    ledger.record('three', 'stored', ledger.epoch);
    ledger.record('one', 'renderRequested', ledger.epoch);
    const result = ledger.report();
    assert.equal(result.overflow, true);
    assert.deepEqual(result.prompts.map(p => p.messageId), ['one', 'two']);
    assert.equal(result.prompts[0].renderRequested, true);
    assert.equal(result.coverage, 'observed-only');
    assert.equal(result.navigationEligible, false);
});

test('blank or oversized IDs do not enter the ledger', () => {
    const ledger = bound();
    for (const id of ['', '   ', 'x'.repeat(513)]) ledger.record(id, 'stored', ledger.epoch);
    assert.deepEqual(ledger.report().prompts, []);
    assert.equal(ledger.bind('', ledger.epoch), false);
    assert.equal(ledger.report().active, false);
});

test('reports cannot mutate retained evidence and observation order is not deduplicated by text', () => {
    const ledger = bound();
    ledger.record('two', 'renderRequested', ledger.epoch);
    ledger.record('one', 'stored', ledger.epoch);
    ledger.record('two', 'stored', ledger.epoch);
    const copy = ledger.report();
    copy.prompts[0].messageId = 'changed';
    copy.prompts.length = 0;
    assert.deepEqual(ledger.report().prompts, [
        { messageId: 'two', stored: true, renderRequested: true },
        { messageId: 'one', stored: true, renderRequested: false },
    ]);
});

test('a new ledger and an ended session cannot acquire identities', () => {
    const ledger = new PromptIdentityLedger();
    assert.equal(ledger.bind('session-a', ledger.epoch), false);
    ledger.record('one', 'stored', ledger.epoch);
    assert.deepEqual(ledger.report().prompts, []);
    ledger.reset(true);
    ledger.bind('session-a', ledger.epoch);
    ledger.record('one', 'stored', ledger.epoch);
    ledger.reset(false);
    ledger.record('late', 'renderRequested', ledger.epoch);
    assert.deepEqual(ledger.report().prompts, []);
});

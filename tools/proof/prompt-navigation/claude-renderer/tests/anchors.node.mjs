import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PromptAnchors, anchorColors } from '../hooks/anchors.ts';

function registry(capacity = 4096) {
    let count = 0;
    const anchors = new PromptAnchors(() => (++count).toString(16).padStart(32, '0'), capacity);
    anchors.reset(true);
    anchors.bind('session-a', anchors.epoch);
    return anchors;
}

test('anchors encode exactly the six RGB cells the COM bridge expects', () => {
    const nonce = '0123456789abcdef0123456789abcdef';
    assert.deepEqual(anchorColors(nonce, false), ['#012345', '#6789ab', '#cdef01', '#234567', '#89abcd', '#efa1b2']);
    assert.equal(anchorColors(nonce, true).at(-1), '#efc3d4');
    assert.throws(() => anchorColors('not an ID', false));
});

test('duplicate text uses native identity; rerenders retain one nonce', () => {
    const a = registry();
    a.beginAppend('one', a.epoch); a.commit('one', a.epoch);
    a.beginAppend('two', a.epoch); a.commit('two', a.epoch);
    const one = a.render('one', a.epoch), two = a.render('two', a.epoch);
    assert.notEqual(one, two);
    assert.equal(a.render('one', a.epoch), one);
    assert.equal(a.report().prompts.length, 2);
});

test('render requests alone and denied appends are not eligible bindings', () => {
    const a = registry();
    a.render('resumed', a.epoch);
    a.beginAppend('denied', a.epoch); a.render('denied', a.epoch);
    assert.deepEqual(a.report().prompts, []);
    assert.equal(a.report().historyComplete, false);
    assert.equal(a.report().navigationEligible, false);
});

test('append invocation order survives asynchronous commit and render order', () => {
    const a = registry();
    a.beginAppend('one', a.epoch); a.beginAppend('two', a.epoch);
    a.commit('two', a.epoch); a.render('two', a.epoch);
    a.commit('one', a.epoch); a.render('one', a.epoch);
    assert.deepEqual(a.report().prompts.map(p => p.messageId), ['one', 'two']);
});

test('an unpainted newest prompt remains in the map for the physical resolver to refuse', () => {
    const a = registry();
    a.beginAppend('one', a.epoch); a.commit('one', a.epoch); a.render('one', a.epoch);
    a.beginAppend('two', a.epoch); a.commit('two', a.epoch);
    assert.deepEqual(a.report().prompts.map(p => p.messageId), ['one', 'two']);
});

test('clear, exit, session switches and late async completions withdraw old anchors', () => {
    const a = registry(), old = a.epoch;
    a.beginAppend('one', old);
    a.reset(true); a.bind('session-b', a.epoch);
    a.commit('one', old); a.render('one', old);
    assert.deepEqual(a.report().prompts, []);
    assert.equal(a.bind('session-c', a.epoch), false);
    assert.equal(a.active, false);
    assert.equal(a.render('late', a.epoch), undefined);
});

test('overflow and malformed identifiers cannot silently expand coverage', () => {
    const a = registry(1);
    a.beginAppend('one', a.epoch); a.commit('one', a.epoch); a.render('one', a.epoch);
    a.beginAppend('two', a.epoch);
    assert.equal(a.overflow, true);
    assert.equal(a.render('two', a.epoch), undefined);
    assert.equal(a.report().navigationEligible, false);
    const b = registry();
    for (const id of ['', ' ', 'x'.repeat(513)]) assert.equal(b.render(id, b.epoch), undefined);
});

test('published nonces are uppercase hex, as the helper map requires', () => {
    const a = new PromptAnchors(() => 'bc0eff841a98418d9cf1a7783910ff90');
    a.reset(true);
    a.bind('session-a', a.epoch);
    a.beginAppend('one', a.epoch); a.commit('one', a.epoch);
    assert.equal(a.render('one', a.epoch), 'BC0EFF841A98418D9CF1A7783910FF90');
    assert.equal(a.report().prompts[0].nonce, 'BC0EFF841A98418D9CF1A7783910FF90');
    assert.deepEqual(anchorColors('BC0EFF841A98418D9CF1A7783910FF90', false),
        anchorColors('bc0eff841a98418d9cf1a7783910ff90', false));
});

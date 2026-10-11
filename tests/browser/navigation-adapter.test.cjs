'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');

const source = readFileSync(resolve(__dirname, '../../src/ScadaSpace.App/Platforms/WebAssembly/WasmScripts/Navigation.js'), 'utf8');
function harness(hash = '', state = null) {
    const listeners = new Map(), calls = [];
    const location = { pathname: '/ScadaSpace/', search: '?session=1', hash };
    const history = {
        state,
        pushState(value, title, url) { this.state = value; calls.push({ method: 'push', value, title, url }); location.hash = url.slice(url.indexOf('#')); },
        replaceState(value, title, url) { this.state = value; calls.push({ method: 'replace', value, title, url }); location.hash = url.slice(url.indexOf('#')); },
        go(delta) { calls.push({ method: 'go', delta }); }
    };
    const document = { title: '' }, window = { addEventListener: (name, callback) => listeners.set(name, callback) };
    vm.runInNewContext(source, { window, location, history, document });
    return { api: window.scadaNavigation, history, location, document, calls, fire: name => listeners.get(name)() };
}
const change = (kind = 'Push', overrides = {}) => ({ kind, entry: 'a23c667f-f53f-471c-9a5a-8419aad17557', url: '/equipment/A?mode=auto', delta: -1, title: 'Equipment', ...overrides });

test('an empty fragment starts at the root route', () => assert.equal(harness().api.initial(), '/'));
test('deep-link escapes remain intact for managed validation', () => assert.equal(harness('#/equipment/%C5%81%C3%B3d%C5%BA?mode=a%2Bb').api.initial(), '/equipment/%C5%81%C3%B3d%C5%BA?mode=a%2Bb'));
test('navigation exports are immutable', () => assert.ok(Object.isFrozen(harness().api)));
test('push creates one entry and preserves application flags outside the fragment', () => {
    const h = harness(); h.api.publish(change()); assert.equal(h.calls.length, 1); assert.equal(h.calls[0].method, 'push'); assert.equal(h.calls[0].url, '/ScadaSpace/?session=1#/equipment/A?mode=auto'); assert.equal(h.document.title, 'Equipment');
});
test('replacement retains unrelated browser-history metadata', () => {
    const original = { hostMarker: 'keep', scadaEntry: 'old' }; const h = harness('', original); h.api.publish(change('Replace'));
    assert.equal(h.history.state.hostMarker, 'keep'); assert.equal(h.history.state.scadaEntry, change().entry); assert.equal(original.scadaEntry, 'old'); assert.equal(h.calls[0].method, 'replace');
});
test('traverse delegates exactly once to native history without pushing', () => {
    const h = harness(); h.api.publish(change('Traverse')); assert.equal(h.calls.length, 1); assert.equal(h.calls[0].method, 'go'); assert.equal(h.calls[0].delta, -1);
});
test('restore updates title without mutating the history stack', () => {
    const h = harness(); h.api.publish(change('Restore')); assert.equal(h.calls.length, 0); assert.equal(h.document.title, 'Equipment');
});
test('popstate observations are consumed only once', () => {
    const h = harness('#/b', { scadaEntry: change().entry }); h.fire('popstate'); assert.deepEqual(JSON.parse(h.api.take()), { url: '/b', entry: change().entry }); assert.equal(h.api.take(), '');
});
test('duplicate popstate/hashchange events coalesce to a single final observation', () => {
    const h = harness('#/a'); h.fire('popstate'); h.location.hash = '#/b'; h.fire('hashchange'); assert.equal(JSON.parse(h.api.take()).url, '/b'); assert.equal(h.api.take(), '');
});
test('invalid history-state identity is not forwarded as a structured payload', () => {
    const h = harness('#/', { scadaEntry: { unexpected: true } }); h.fire('popstate'); assert.equal(JSON.parse(h.api.take()).entry, '');
});
test('unknown operations are rejected without browser mutations', () => {
    const h = harness(); assert.throws(() => h.api.publish(change('Unknown'))); assert.equal(h.calls.length, 0);
});
test('external and excessive page URLs are rejected without browser mutations', () => {
    const h = harness(); for (const url of ['https://example.org', '//example.org', '/' + 'a'.repeat(4096)]) assert.throws(() => h.api.publish(change('Push', { url })));
    assert.equal(h.calls.length, 0);
});
test('nonfinite and fractional traversal distances are rejected', () => {
    const h = harness(); for (const delta of [NaN, Infinity, .5, 0]) assert.throws(() => h.api.publish(change('Traverse', { delta })));
    assert.equal(h.calls.length, 0);
});
test('page titles are assigned as text, not interpreted as HTML', () => {
    const h = harness(); h.api.publish(change('Push', { title: '<b>Plant</b>' })); assert.equal(h.document.title, '<b>Plant</b>');
});
test('native history failures propagate instead of reporting a successful push', () => {
    const h = harness(); h.history.pushState = () => { throw new Error('storage policy'); }; assert.throws(() => h.api.publish(change()), /storage policy/);
});

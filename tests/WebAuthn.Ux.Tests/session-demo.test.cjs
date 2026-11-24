const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const load = name => fs.readFileSync(path.join(__dirname, '../../LummaSimulator.Web/wwwroot/js/' + name), 'utf8');
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(resolve => setImmediate(resolve)); };

test('consent schema comparison ignores fixture recreation order while detecting DDL changes', () => {
    const { DatabaseSync } = require('node:sqlite');
    const read = file => fs.readFileSync(path.join(__dirname, '../..', file), 'utf8');
    const checks = read('tests/LummaSimulator.Web.Tests/SessionDemoChecks.cs');
    const sql = checks.match(/const string SchemaSql = "([^"]+)"/)[1];
    const originalSql = checks.match(/const string OriginalSchemaSql = "([^"]+)"/)[1];
    const statements = file => [...read(file).matchAll(/^\s*"(CREATE (?:TABLE|INDEX) [^"\r\n]+)"[,\r\n]/gm)].map(m => m[1]);
    const history = statements('LummaSimulator.Web/Services/Prizes/PrizeRoundHistoryMigration.cs');
    const visitor = statements('LummaSimulator.Web/Services/VisitorFlowMigration.cs');
    assert.equal(history.length, 3); assert.equal(visitor.length, 2);
    const db = new DatabaseSync(':memory:');
    try {
        // Source DDL only, no portal, identities, existing databases or consent API calls.
        for (const ddl of [...history, ...visitor]) db.exec(ddl);
        const snapshot = query => Object.values(db.prepare(query).get())[0];
        const before = snapshot(sql), originalBefore = snapshot(originalSql);
        db.exec('DROP TABLE PrizeRoundHistory; DROP TABLE PrizeRoundHistoryContract');
        for (const ddl of history) db.exec(ddl);
        assert.notEqual(snapshot(originalSql), originalBefore, 'The old assertion fails before consent is involved');
        assert.equal(snapshot(sql), before, 'The same schema definitions survive fixture reinitialization');
        for (const ddl of ['CREATE TABLE UnexpectedConsentTable(Id INTEGER)',
            'CREATE INDEX UnexpectedConsentIndex ON VisitorExercises(StartedAt)',
            'CREATE TRIGGER UnexpectedConsentTrigger AFTER INSERT ON VisitorExercises BEGIN SELECT 1; END',
            'ALTER TABLE VisitorExercises ADD COLUMN Unexpected TEXT']) {
            db.exec('BEGIN');
            db.exec(ddl);
            assert.notEqual(snapshot(sql), before, ddl);
            db.exec('ROLLBACK');
            assert.equal(snapshot(sql), before);
        }
    } finally { db.close(); }
});

function harness(script, authenticated = true) {
    const nodes = {}, events = {}, documentEvents = {}, requests = [], timers = [], historyCalls = [];
    function node(id) { return nodes[id] ??= { dataset: {}, hidden: false, disabled: false, checked: false, textContent: '', addEventListener(name, handler) { this[name] = handler; } }; }
    node('session-demo-consent').dataset.authenticated = String(authenticated);
    node('synthetic-session-view').dataset.viewId = 'synthetic-view';
    const document = { hidden: false, getElementById: node, querySelector: () => ({ value: 'test-only-csrf' }), addEventListener: (name, handler) => documentEvents[name] = handler };
    const window = { LummaI18n: { t: key => 'translated:' + key }, addEventListener: (name, handler) => events[name] = handler };
    const state = { reply: { requestId: 'request-one', status: 'Pending', eventName: 'Synthetic event', round: 1, alias: 'Synthetic alias one' }, ok: true, pending: null };
    vm.runInNewContext(load(script), { document, window, URLSearchParams, location: { hash: '#open=test-only-token', pathname: '/SessionDemo/synthetic-view' },
        history: { replaceState: (...args) => historyCalls.push(args) }, setInterval: handler => timers.push(handler),
        fetch: async (url, options) => { requests.push({ url, options }); if (state.pending) await state.pending;
            return { ok: state.ok, json: async () => ({ ...state.reply }) }; }
    });
    return { node, state, requests, events, document, documentEvents, historyCalls, tick: async () => { timers.forEach(handler => handler()); await flush(); } };
}

test('visitor consent stays unchecked and requires an explicit Allow click after authentication', async () => {
    const h = harness('session-demo-consent.js', false); await flush();
    assert.equal(h.requests.length, 0);
    h.events['lumma-authenticated'](); await flush();
    assert.equal(h.node('session-demo-consent').hidden, false);
    assert.equal(h.node('session-demo-allow').disabled, true);
    h.node('session-demo-allow').click(); await flush();
    assert.equal(h.requests.length, 1);
    h.node('session-demo-confirm').checked = true; h.node('session-demo-confirm').change();
    assert.equal(h.node('session-demo-allow').disabled, false);
    h.state.reply.status = 'Granted'; h.node('session-demo-allow').click(); await flush();
    const post = h.requests.at(-1);
    assert.equal(post.options.method, 'POST');
    assert.equal(post.options.headers['X-CSRF-TOKEN'], 'test-only-csrf');
    assert.deepEqual(JSON.parse(post.options.body), { requestId: 'request-one', allow: true, confirmed: true });
    assert.equal(h.node('session-demo-allow').hidden, true);
    assert.equal(h.node('session-demo-deny').textContent, 'translated:SessionDemoWithdraw');
});

test('consent withdrawal is explicit, scoped to the current request and hides the grant', async () => {
    const h = harness('session-demo-consent.js'); await flush();
    h.state.reply.status = 'Granted'; await h.tick();
    h.state.reply.status = 'Revoked'; h.node('session-demo-deny').click(); await flush();
    assert.deepEqual(JSON.parse(h.requests.at(-1).options.body), { requestId: 'request-one', allow: false, confirmed: true });
    assert.equal(h.node('session-demo-consent').hidden, true);
});

test('a new request resets the checkbox; polls and language changes never submit consent', async () => {
    const h = harness('session-demo-consent.js'); await flush();
    h.node('session-demo-confirm').checked = true;
    h.state.reply.requestId = 'request-two'; await h.tick();
    assert.equal(h.node('session-demo-confirm').checked, false);
    h.events['lumma-language-changed'](); await h.tick();
    assert.equal(h.requests.some(r => r.options.method === 'POST'), false);
    h.events.pagehide(); assert.equal(h.node('session-demo-consent').hidden, true);
});

test('synthetic view consumes the fragment once, removes it from history and never sends a visitor identity', async () => {
    const h = harness('session-demo-view.js'); await flush();
    assert.equal(h.historyCalls[0][2], '/SessionDemo/synthetic-view');
    assert.equal(h.requests[0].url, '/api/session-demo/views/synthetic-view/redeem');
    assert.deepEqual(JSON.parse(h.requests[0].options.body), { token: 'test-only-token' });
    assert.equal(h.requests[0].options.headers['X-CSRF-TOKEN'], 'test-only-csrf');
    await h.tick();
    assert.equal(h.requests[1].url, '/api/session-demo/views/synthetic-view');
    assert.equal(h.requests[1].options.body, undefined);
    assert.equal(h.node('synthetic-session-data').hidden, false);
    assert.equal(h.node('synthetic-session-alias').textContent, 'Synthetic alias one');
});

test('revocation clears displayed data and stops further access attempts', async () => {
    const h = harness('session-demo-view.js'); await flush();
    h.state.ok = false; await h.tick();
    assert.equal(h.node('synthetic-session-data').hidden, true);
    assert.equal(h.node('synthetic-session-alias').textContent, '');
    const count = h.requests.length; await h.tick(); assert.equal(h.requests.length, count);
});

test('background/back-cache transitions clear old synthetic data and require a new server read', async () => {
    const h = harness('session-demo-view.js'); await flush();
    h.document.hidden = true; h.documentEvents.visibilitychange();
    assert.equal(h.node('synthetic-session-data').hidden, true);
    h.document.hidden = false; h.documentEvents.visibilitychange(); await flush();
    assert.equal(h.node('synthetic-session-data').hidden, false);
    h.events.pagehide(); assert.equal(h.node('synthetic-session-data').hidden, true);
    h.state.ok = false; h.events.pageshow({ persisted: true }); await flush();
    assert.equal(h.node('synthetic-session-data').hidden, true);
});

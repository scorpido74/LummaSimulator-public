const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const script = fs.readFileSync(path.join(__dirname, '../../LummaSimulator.Web/wwwroot/js/passkeys.js'), 'utf8');

class Element {
    constructor(text = '') { this.textContent = text; this.dataset = {}; this.hidden = false; this.disabled = false; this.events = new Map(); this.attributes = {}; this.classes = new Set(); this.classList = { toggle: (name, on) => on ? this.classes.add(name) : this.classes.delete(name) }; }
    addEventListener(name, handler) { const list = this.events.get(name) || []; list.push(handler); this.events.set(name, list); }
    setAttribute(name, value) { this.attributes[name] = value; }
    getAttribute(name) { return this.attributes[name]; }
    replaceChildren() { this.children = []; this.textContent = ''; }
    append(...nodes) { this.children = (this.children || []).concat(nodes); this.textContent += nodes.map(node => typeof node==='string'?node:node.textContent).join(''); }
    focus() { this.focused = true; }
    click() { return Promise.all((this.events.get('click') || []).map(handler => handler())); }
}
function harness(config = {}) {
    const names = ['passkey-demo', 'register-passkey', 'authenticate-passkey', 'cancel-passkey', 'end-demo', 'webauthn-status', 'credential-count', 'success-count', 'registration-step', 'auth-confirmation', 'registration-state', 'verified-username', 'verified-time', 'verification-result', 'auth-confirmation-title', 'again-authenticate', 'confirmation-end-demo', 'verified-credential', 'new-visitor', 'visitor-number', 'prize-round-status', 'visitor-heading-name', 'visitor-sidebar-name', 'page-title', 'demo-welcome', 'passkey-options', 'participation-link','authentication-step','visitor-intro','visitor-session-details','start-clickfix','clickfix-warning-title'];
    const elements = Object.fromEntries(names.map(name => [name, new Element()]));
    elements['clickfix-entry'] = new Element();
    elements['clickfix-entry'].opens = 0;
    elements['clickfix-entry'].showModal = function () { this.opens++; };
    elements['credential-count'].textContent = String(config.credentials ?? 0);
    elements['success-count'].textContent = String(config.successes ?? 0);
    elements['passkey-demo'].dataset.route = 'laptop';
    elements['auth-confirmation'].hidden = true;
    elements['prize-round-status'].hidden = true;elements['participation-link'].hidden=true;
    elements['page-title'].dataset = { i18nTitle: 'Jouw passkey-demonstratie', titleSuffix: ' · Demo', titleAlias: '' };
    const timers = new Map(); let timerId = 0;
    const calls = { create: 0, get: 0, requests: [], browserOptions: [], signals: [] };
    const bytes = () => new Uint8Array([1, 2, 3]).buffer;
    const credential = { id: 'AQID', rawId: bytes(), type: 'public-key', response: { clientDataJSON: bytes(), attestationObject: bytes(), authenticatorData: bytes(), signature: bytes(), userHandle: bytes(), getTransports: () => ['internal'] }, getClientExtensionResults: () => ({}) };
    let finishBrowser;
    let retainedBrowserSignal;
    async function browser(kind, options) {
        calls[kind]++; calls.browserOptions.push(options.publicKey);
        calls.signals.push(options.signal);
        // Diagnostic fault injection, not a claim about Chrome's actual implementation.
        if (config.retainUntilAbort && retainedBrowserSignal && !retainedBrowserSignal.aborted)
            throw new DOMException('A request is already pending', 'InvalidStateError');
        retainedBrowserSignal = options.signal;
        if (config.browserError) throw config.browserError;
        if (config.pendingBrowser) return new Promise((resolve, reject) => {
            finishBrowser = () => resolve(credential);
            options.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
        });
        return credential;
    }
    const response = (status, data) => ({ status, ok: status < 400, json: async () => data });
    const fetch = async (url, options) => {
        calls.requests.push({ url, options });
        if (url.endsWith('/cancel')) {
            if (config.cancelError) throw new Error('Diagnostic cancellation failure');
            if (config.deferCancel) return new Promise(resolve => { config.finishCancel = () => resolve(response(200, { cancelled: true })); });
            return response(200, { cancelled: true });
        }
        if (url.endsWith('/options')) {
            if (config.pendingOptions) return new Promise((_, reject) => options.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true }));
            if (config.pendingServer) return response(409, { error: 'Er is al een passkey-aanvraag actief.' });
            if (config.optionsError) throw new Error(config.optionsErrorText || 'Network unavailable');
            return response(200, { challenge: 'AQID', user: { id: 'AQID' }, excludeCredentials: [], allowCredentials: [], authenticatorSelection: { userVerification: 'required' } });
        }
        if (config.verifyPendingText) return response(400, { error: 'Diagnostic pending request during verification' });
        if (config.serverError) return response(400, { error: 'Server-side verificatie afgewezen.' });
        const operation = url.includes('/register/') ? 'register' : 'authenticate';
        const receipt = { verified: !config.notVerified, operation, prizeLinkKey:config.prizeLinkKey||null,prizesEnabled:config.prizesEnabled===true, username: config.username || 'Visitor-TEST2345', verifiedAt: '2026-10-29T12:00:00Z', credentialCount: operation === 'register' ? 2 : 1, successfulAuthentications: operation === 'register' ? 0 : 7, verificationStatus: 'Verified', credentialAlias: 'Passkey 1', visitorNumber: operation === 'authenticate' ? 100 : null, eventName: 'Test event', round: operation === 'authenticate' ? config.round ?? null : null };
        if (config.deferVerifyBody) return { status: 200, ok: true, json: () => new Promise(resolve => { config.finishVerifyBody = () => resolve(receipt); }) };
        return response(200, receipt);
    };
    const windowEvents = new Map();
    const context = vm.createContext({
        document: { getElementById: id => elements[id],
            querySelector: selector => selector === 'title[data-i18n-title]' ? elements['page-title'] : { value: 'test-antiforgery' },
            querySelectorAll: selector => selector === '[data-visitor-display-name]' ? [elements['visitor-heading-name'], elements['visitor-sidebar-name']] : [],
            createElement: () => new Element() },
        window: { location: { assign: url => { calls.redirect = url; } }, isSecureContext: config.supported !== false, PublicKeyCredential: function () {}, addEventListener: (name, handler) => windowEvents.set(name, handler) },
        navigator: { credentials: { create: o => browser('create', o), get: o => browser('get', o) }, locks: config.noLocks ? undefined : { request: async (_, __, action) => action(config.otherTab ? null : {}) } },
        crypto: { randomUUID }, fetch, AbortController, AbortSignal, DOMException, atob, btoa,
        setTimeout: action => { timers.set(++timerId, action); return timerId; }, clearTimeout: id => timers.delete(id)
    });
    vm.runInContext(script, context);
    return { elements, calls, context, config, timers, windowEvents, finish: () => finishBrowser(), e: id => elements[id], loadAgain: () => vm.runInContext(script, context) };
}
async function until(condition) {
    for (let i = 0; i < 40; i++) { if (condition()) return; await new Promise(resolve => setImmediate(resolve)); }
    throw new Error('Expected asynchronous state did not occur');
}

test('QR authentication immediately uses the authoritative round alias in heading, sidebar, title and confirmation', async () => {
    const h = harness({ credentials: 1, round: { eventName: 'Synthetic event', number: 1, alias: 'Turbootter', status: 'Open' } });
    await h.e('authenticate-passkey').click();
    for (const id of ['visitor-heading-name', 'visitor-sidebar-name', 'verified-username']) assert.equal(h.e(id).textContent, 'Turbootter');
    assert.equal(h.e('page-title').textContent, 'WelcomeAliasPrefix Turbootter · Demo');
    assert.equal(h.e('prize-round-status').hidden, false);
    assert.doesNotMatch(h.e('prize-round-status').textContent, /Turbootter/);
    await h.e('again-authenticate').click();
    assert.equal(h.e('verified-username').textContent, 'Turbootter');
    h.windowEvents.get('lumma-language-changed')();
    assert.doesNotMatch(h.e('prize-round-status').textContent, /Turbootter/);
});

test('open demo authentication welcomes the validated alias without a round', async () => {
    const h = harness({ credentials: 1 });
    await h.e('authenticate-passkey').click();
    for (const id of ['visitor-heading-name', 'visitor-sidebar-name', 'verified-username']) assert.equal(h.e(id).textContent, 'Visitor-TEST2345');
    assert.equal(h.e('page-title').textContent, 'WelcomeAliasPrefix Visitor-TEST2345 · Demo');
    assert.equal(h.e('prize-round-status').hidden, true);
    assert.equal(h.e('page-title').dataset.titleAlias, 'Visitor-TEST2345');
});

test('double-click and competing actions never open two browser requests; repeated script attachment is ignored', async () => {
    const h = harness({ pendingBrowser: true }); h.loadAgain();
    const first = h.e('register-passkey').click();
    await until(() => h.calls.create === 1);
    assert.equal(h.e('register-passkey').disabled, true);
    assert.equal(h.e('authenticate-passkey').disabled, true);
    assert.equal(h.e('end-demo').disabled, true);
    await h.e('register-passkey').click(); await h.e('authenticate-passkey').click();
    assert.equal(h.calls.create, 1); assert.equal(h.calls.get, 0);
    h.finish(); await first;
    assert.equal(h.calls.requests.filter(r => r.url.endsWith('/verify')).length, 1);
});

test('same document supports registration then authentication then successful Opnieuw aanmelden with settled browser promises', async () => {
    const h = harness();
    await h.e('register-passkey').click();
    await h.e('authenticate-passkey').click();
    await h.e('again-authenticate').click();
    assert.equal(h.calls.create, 1);
    assert.equal(h.calls.get, 2);
    assert.deepEqual(h.calls.requests.map(r => r.url), [
        '/api/webauthn/register/options', '/api/webauthn/register/verify',
        '/api/webauthn/authenticate/options', '/api/webauthn/authenticate/verify',
        '/api/webauthn/authenticate/options', '/api/webauthn/authenticate/verify'
    ]);
    assert.equal(new Set(h.calls.signals).size, 3);
    assert.equal(h.calls.signals.every(signal => signal.aborted), true);
    assert.equal(h.e('auth-confirmation').hidden, false);
    assert.equal(h.e('again-authenticate').disabled, false);
    assert.equal(h.timers.size, 0);
});

test('controlled abort trial releases modeled retained request after registration before authentication', async () => {
    const h = harness({ retainUntilAbort: true });
    await h.e('register-passkey').click();
    assert.equal(h.calls.signals[0].aborted, true);
    await h.e('authenticate-passkey').click();
    assert.equal(h.e('auth-confirmation').hidden, false);
    assert.deepEqual(h.calls.requests.slice(-2).map(r => r.url), [
        '/api/webauthn/authenticate/options', '/api/webauthn/authenticate/verify'
    ]);
    assert.equal(h.calls.signals[1].aborted, true);
});

test('pending wording from verify response is classified as server verification, not a browser pending request', async () => {
    const h = harness({ credentials: 1, verifyPendingText: true });
    await h.e('authenticate-passkey').click();
    assert.equal(h.calls.requests.some(r => r.url.endsWith('/verify')), true);
    assert.doesNotMatch(h.e('webauthn-status').textContent, /nog een browseraanvraag/);
    assert.equal(h.e('webauthn-status').dataset.webauthnErrorStage, 'verify');
    assert.match(h.e('webauthn-status').textContent, /^Serververificatie:/);
});

test('controlled abort trial releases modeled retained request before Opnieuw aanmelden without a refresh', async () => {
    const h = harness({ credentials: 1, retainUntilAbort: true });
    await h.e('authenticate-passkey').click();
    assert.equal(h.e('auth-confirmation').hidden, false);
    await h.e('again-authenticate').click();
    assert.equal(h.e('auth-confirmation').hidden, false);
    assert.equal(h.calls.get, 2);
    assert.equal(h.calls.requests.filter(r => r.url.endsWith('/verify')).length, 2);
    assert.equal(h.calls.requests.some(r => r.url.endsWith('/cancel')), false);
    assert.equal(h.e('again-authenticate').disabled, false);
});

test('pagehide after success does not cancel; pagehide during browser request aborts that operation', async () => {
    const h = harness({ credentials: 1 });
    await h.e('authenticate-passkey').click();
    const completedRequests = h.calls.requests.length;
    h.windowEvents.get('pagehide')();
    assert.equal(h.calls.requests.length, completedRequests);
    h.config.pendingBrowser = true;
    const next = h.e('again-authenticate').click();
    await until(() => h.calls.get === 2);
    h.windowEvents.get('pagehide')();
    await next;
    assert.equal(h.calls.signals[1].aborted, true);
    assert.equal(h.calls.requests.filter(r => r.url.endsWith('/verify')).length, 1);
    assert.equal(h.e('again-authenticate').disabled, false);
});

test('pending wording during options never claims a pending browser request', async () => {
    const h = harness({ optionsError: true, optionsErrorText: 'Diagnostic pending request' });
    await h.e('register-passkey').click();
    assert.equal(h.calls.create, 0);
    assert.equal(h.e('webauthn-status').dataset.webauthnErrorStage, 'options');
    assert.match(h.e('webauthn-status').textContent, /^Opties ophalen:/);
    assert.doesNotMatch(h.e('webauthn-status').textContent, /nog een browseraanvraag/);
});

test('browser pending error is classified at browser stage and cleaned up before retry', async () => {
    const h = harness({ credentials: 1, browserError: new DOMException('A request is already pending', 'InvalidStateError') });
    await h.e('authenticate-passkey').click();
    assert.equal(h.e('webauthn-status').dataset.webauthnErrorStage, 'browser');
    assert.match(h.e('webauthn-status').textContent, /^Browserceremonie:.*nog een browseraanvraag/);
    assert.equal(h.calls.signals[0].aborted, true);
    h.config.browserError = null;
    await h.e('authenticate-passkey').click();
    assert.equal(h.e('auth-confirmation').hidden, false);
});

test('cancellation failure is distinct and cleanup uses a separate non-aborted signal', async () => {
    const h = harness({ credentials: 1, browserError: new DOMException('Cancelled', 'AbortError'), cancelError: true });
    await h.e('authenticate-passkey').click();
    assert.equal(h.e('webauthn-status').dataset.webauthnErrorStage, 'cancel');
    assert.match(h.e('webauthn-status').textContent, /Annulering:.*kon de annulering niet bevestigen/);
    const cleanup = h.calls.requests.at(-1);
    assert.equal(cleanup.options.signal.aborted, false);
    assert.notEqual(cleanup.options.signal, h.calls.signals[0]);
});

test('pagehide and finally share cleanup; old timeout cannot abort the next operation', async () => {
    const h = harness({ credentials: 1, pendingBrowser: true, deferCancel: true });
    const first = h.e('authenticate-passkey').click();
    await until(() => h.calls.get === 1);
    const oldTimeout = [...h.timers.values()][0];
    h.windowEvents.get('pagehide')();
    await until(() => Boolean(h.config.finishCancel));
    await h.e('again-authenticate').click();
    assert.equal(h.calls.get, 1);
    assert.equal(h.calls.requests.filter(r => r.url.endsWith('/cancel')).length, 1);
    h.config.finishCancel();
    await first;
    h.config.deferCancel = false;
    const next = h.e('again-authenticate').click();
    await until(() => h.calls.get === 2);
    oldTimeout();
    assert.equal(h.calls.signals[1].aborted, false);
    const cleanup = h.calls.requests.find(r => r.url.endsWith('/cancel'));
    const nextOptions = h.calls.requests.filter(r => r.url.endsWith('/options')).at(-1);
    assert.notEqual(cleanup.options.headers['X-Lumma-Operation'], nextOptions.options.headers['X-Lumma-Operation']);
    h.finish();
    await next;
    assert.equal(h.e('auth-confirmation').hidden, false);
});

test('final abort waits for verification response body and cannot erase confirmed success', async () => {
    const h = harness({ credentials: 1, deferVerifyBody: true });
    const action = h.e('authenticate-passkey').click();
    await until(() => Boolean(h.config.finishVerifyBody));
    assert.equal(h.calls.signals[0].aborted, false);
    assert.equal(h.e('again-authenticate').disabled, true);
    h.config.finishVerifyBody();
    await action;
    assert.equal(h.calls.signals[0].aborted, true);
    assert.equal(h.e('auth-confirmation').hidden, false);
    assert.equal(h.e('success-count').textContent, '7');
    assert.equal(h.calls.requests.some(r => r.url.endsWith('/cancel')), false);
});

test('server-assigned visitor number survives repeat sign-in and translates without another request', async () => {
    const h = harness({ credentials: 1 });
    let english = false;
    h.context.window.LummaI18n = { t: key => key === 'VisitorCongratulations'
        ? (english ? "Congratulations, you're visitor #{0}!" : 'Gefeliciteerd, jij bent bezoeker #{0}!') : key };
    await h.e('authenticate-passkey').click();
    assert.equal(h.e('visitor-number').textContent, 'Gefeliciteerd, jij bent bezoeker #100! · Test event');
    const requests = h.calls.requests.length;
    english = true;
    h.windowEvents.get('lumma-language-changed')();
    assert.equal(h.calls.requests.length, requests);
    assert.equal(h.e('visitor-number').textContent, "Congratulations, you're visitor #100! · Test event");
    await h.e('again-authenticate').click();
    assert.match(h.e('visitor-number').textContent, /#100/);
});

test('new visitor action is disabled until the active WebAuthn operation settles', async () => {
    const h = harness({ credentials: 1, pendingBrowser: true });
    const action = h.e('authenticate-passkey').click();
    await until(() => h.calls.get === 1);
    assert.equal(h.e('new-visitor').disabled, true);
    await h.e('cancel-passkey').click(); await action;
    assert.equal(h.e('new-visitor').disabled, false);
});

test('language change during an active WebAuthn ceremony updates text without abort, cancel, or a second browser request', async () => {
    const h = harness({ credentials: 1, pendingBrowser: true });
    let language = 'nl-NL';
    h.context.window.LummaI18n = { language: () => language, t: key => language === 'en-GB' ? ({
        'Nog een passkey toevoegen': 'Add another passkey',
        'Registratie voltooid': 'Registration complete',
        'Volg de aanwijzingen van je browser. Kies de provider waar jouw passkey is opgeslagen.': 'Follow your browser instructions.'
    }[key] || key) : key };
    const action = h.e('authenticate-passkey').click();
    await until(() => h.calls.get === 1);
    const signal = h.calls.signals[0];
    const options = h.calls.browserOptions[0];
    language = 'en-GB';
    h.windowEvents.get('lumma-language-changed')();
    assert.equal(signal.aborted, false);
    assert.equal(h.calls.get, 1);
    assert.equal(h.calls.requests.length, 1);
    assert.equal(h.calls.browserOptions[0], options);
    assert.equal(h.e('register-passkey').textContent, 'Add another passkey');
    assert.equal(h.e('webauthn-status').textContent, 'Follow your browser instructions.');
    assert.equal(h.e('again-authenticate').disabled, true);
    h.finish(); await action;
    assert.equal(h.e('auth-confirmation').hidden, false);
    assert.equal(h.calls.requests.some(r => r.url.endsWith('/cancel')), false);
    language = 'nl-NL';
    h.windowEvents.get('lumma-language-changed')();
    assert.equal(h.e('register-passkey').textContent, 'Nog een passkey toevoegen');
});
test('registration is marked complete, changes button text, and is never shown as authentication', async () => {
    const h = harness(); await h.e('register-passkey').click();
    assert.equal(h.e('register-passkey').textContent, 'Nog een passkey toevoegen');
    assert.equal(h.e('registration-state').textContent, 'Registratie voltooid');
    assert.equal(h.e('registration-step').classes.has('completed'), true);
    assert.equal(h.e('auth-confirmation').hidden, true);
    assert.equal(h.e('success-count').textContent, '0');
    assert.equal(h.e('credential-count').textContent, '2');
    assert.equal(h.calls.browserOptions[0].hints, undefined);
    assert.equal(h.calls.browserOptions[0].authenticatorSelection.authenticatorAttachment, undefined);
});
test('only server-verified authentication shows username/time/status and authoritative success count', async () => {
    const h = harness({ credentials: 1, successes: 3 }); await h.e('authenticate-passkey').click();
    assert.equal(h.e('auth-confirmation').hidden, false);
    assert.equal(h.e('verified-username').textContent, 'Visitor-TEST2345');
    assert.equal(h.e('verified-time').attributes.datetime, '2026-10-29T12:00:00Z');
    assert.match(h.e('verification-result').textContent, /Server-side geverifieerd/);
    assert.equal(h.e('success-count').textContent, '7');
    assert.equal(h.e('auth-confirmation-title').focused, true);
});
for (const failure of ['serverError', 'notVerified']) test(`server rejection ${failure} never increments count or shows success`, async () => {
    const h = harness({ credentials: 1, successes: 3, [failure]: true }); await h.e('authenticate-passkey').click();
    assert.equal(h.e('success-count').textContent, '3');
    assert.equal(h.e('auth-confirmation').hidden, true);
    assert.equal(h.e('authenticate-passkey').disabled, false);
    assert.equal(h.e('end-demo').disabled, false);
    assert.equal(h.e('cancel-passkey').hidden, true);
});
test('explicit cancellation aborts browser operation, releases matching server operation and allows retry', async () => {
    const h = harness({ credentials: 1, pendingBrowser: true });
    const first = h.e('authenticate-passkey').click(); await until(() => h.calls.get === 1);
    await h.e('cancel-passkey').click(); await first;
    assert.match(h.e('webauthn-status').textContent, /geannuleerd/);
    assert.equal(h.calls.requests.filter(r => r.url.endsWith('/verify')).length, 0);
    const requests = h.calls.requests;
    assert.equal(requests[0].options.headers['X-Lumma-Operation'], requests.at(-1).options.headers['X-Lumma-Operation']);
    assert.equal(h.e('authenticate-passkey').disabled, false);
    h.config.pendingBrowser = false; await h.e('authenticate-passkey').click();
    assert.equal(h.e('auth-confirmation').hidden, false);
});
test('cancellation while fetching options uses known operation ID without opening browser dialog', async () => {
    const h = harness({ pendingOptions: true }); const action = h.e('register-passkey').click();
    await until(() => h.calls.requests.length > 0); await h.e('cancel-passkey').click(); await action;
    assert.equal(h.calls.create, 0); assert.equal(h.calls.requests.at(-1).url.endsWith('/cancel'), true);
    assert.equal(h.e('register-passkey').disabled, false);
});
test('timeout aborts pending operation and restores controls without an optimistic counter increment', async () => {
    const h = harness({ credentials: 1, pendingBrowser: true }); const action = h.e('authenticate-passkey').click();
    await until(() => h.calls.get === 1); [...h.timers.values()][0](); await action;
    assert.match(h.e('webauthn-status').textContent, /verlopen/); assert.equal(h.e('success-count').textContent, '0');
    assert.equal(h.e('authenticate-passkey').disabled, false);
});
for (const name of ['AbortError', 'NotAllowedError', 'SecurityError', 'InvalidStateError']) test(`${name} restores UI and cancels only the failed operation`, async () => {
    const h = harness({ credentials: 1, browserError: new DOMException(name, name) }); await h.e('authenticate-passkey').click();
    assert.equal(h.e('end-demo').disabled, false); assert.equal(h.e('authenticate-passkey').disabled, false);
    assert.equal(h.e('success-count').textContent, '0'); assert.equal(h.e('auth-confirmation').hidden, true);
});
test('browser already-pending error is explained without forcing providers or resetting browser settings', async () => {
    const h = harness({ browserError: new Error('A request is already pending') }); await h.e('register-passkey').click();
    assert.match(h.e('webauthn-status').textContent, /nog een browseraanvraag/); assert.equal(h.e('register-passkey').disabled, false);
});
test('another-tab Web Lock prevents even an options request', async () => {
    const h = harness({ otherTab: true }); await h.e('register-passkey').click();
    assert.equal(h.calls.requests.length, 0); assert.equal(h.calls.create, 0); assert.match(h.e('webauthn-status').textContent, /andere tab/);
});
test('server pending guard works in browsers without Web Locks', async () => {
    const h = harness({ noLocks: true, pendingServer: true }); await h.e('register-passkey').click();
    assert.equal(h.calls.create, 0); assert.equal(h.e('register-passkey').disabled, false); assert.match(h.e('webauthn-status').textContent, /actief/);
});
test('network failure restores controls and never starts authenticator', async () => {
    const h = harness({ optionsError: true }); await h.e('register-passkey').click();
    assert.equal(h.calls.create, 0); assert.equal(h.e('end-demo').disabled, false); assert.equal(h.e('register-passkey').disabled, false);
});
test('unsupported context keeps WebAuthn controls disabled but allows ending demo', async () => {
    const h = harness({ supported: false }); await h.e('register-passkey').click();
    assert.equal(h.calls.requests.length, 0); assert.equal(h.e('register-passkey').disabled, true); assert.equal(h.e('end-demo').disabled, false);
});


test('verified credential alias is shown without pretending to identify its provider', async () => {
    const h = harness({ credentials: 1 }); await h.e('authenticate-passkey').click();
    assert.equal(h.e('verified-credential').textContent, 'Passkey 1');
});
test('Opnieuw aanmelden uses the same operation coordinator and disables both confirmation actions while pending', async () => {
    const h = harness({ credentials: 1 }); await h.e('authenticate-passkey').click();
    h.config.pendingBrowser = true;
    const next = h.e('again-authenticate').click(); await until(() => h.calls.get === 2);
    assert.equal(h.e('again-authenticate').disabled, true);
    assert.equal(h.e('confirmation-end-demo').disabled, true);
    await h.e('authenticate-passkey').click(); assert.equal(h.calls.get, 2);
    await h.e('cancel-passkey').click(); await next;
    assert.equal(h.e('again-authenticate').disabled, false);
    assert.equal(h.e('confirmation-end-demo').disabled, false);
});

test('registration and verified authentication stay on Demo; success remains for at least ten seconds', async () => {
 const h = harness();await h.e('register-passkey').click();
 assert.equal(h.e('registration-state').textContent,'Registratie voltooid');assert.equal(h.e('auth-confirmation').hidden,true);assert.equal(h.calls.redirect,undefined);
 await h.e('authenticate-passkey').click();assert.equal(h.e('auth-confirmation').hidden,false);assert.equal(h.calls.redirect,undefined);
 const alias=h.e('verified-username').textContent;
 await new Promise(resolve=>setTimeout(resolve,10100));
 assert.equal(h.e('auth-confirmation').hidden,false);assert.equal(h.e('verified-username').textContent,alias);assert.equal(h.calls.redirect,undefined);
 const rejected=harness({credentials:1,notVerified:true});await rejected.e('authenticate-passkey').click();assert.equal(rejected.e('auth-confirmation').hidden,true);assert.equal(rejected.calls.redirect,undefined);
});

test('authenticated welcome uses the server alias Echovalk without a new ceremony',async()=>{
 const h=harness({credentials:1,username:'Echovalk'});await h.e('authenticate-passkey').click();
 assert.match(h.e('demo-welcome').textContent,/WelcomeAliasPrefix Echovalk/);assert.equal(h.e('verified-username').textContent,'Echovalk');
 const before=h.calls.get;h.windowEvents.get('lumma-language-changed')();assert.equal(h.calls.get,before);assert.equal(h.e('auth-confirmation').hidden,false);
});

test('authenticated options disappear and prize wording follows only the server prize status',async()=>{
 const h=harness({credentials:1,prizeLinkKey:'RoundParticipationLink'});assert.equal(h.e('passkey-options').hidden,false);await h.e('authenticate-passkey').click();
 assert.equal(h.e('passkey-options').hidden,true);assert.equal(h.e('again-authenticate').hidden,true);assert.equal(h.e('participation-link').hidden,false);assert.equal(h.e('participation-link').textContent,'RoundParticipationLink');
 const y=harness({credentials:1,prizeLinkKey:'YubiKeyParticipationLink'});await y.e('authenticate-passkey').click();assert.equal(y.e('participation-link').textContent,'YubiKeyParticipationLink');
});

test('expired authentication restores sign-in options without opening a new browser ceremony',async()=>{
 const h=harness({credentials:1,username:'Echovalk'});await h.e('authenticate-passkey').click();const before=h.calls.get;
 h.windowEvents.get('lumma-authentication-expired')();assert.equal(h.e('auth-confirmation').hidden,true);assert.equal(h.e('passkey-options').hidden,false);assert.equal(h.calls.get,before);assert.equal(h.e('authenticate-passkey').focused,true);
});

test('each visitor step has one primary action and registration reveals sign-in without authenticating',async()=>{
 const h=harness();assert.equal(h.e('authenticate-passkey').hidden,true);assert.equal(h.e('authenticate-passkey').disabled,true);assert.equal(h.e('registration-step').hidden,false);assert.equal(h.e('register-passkey').classes.has('button-primary'),true);
 await h.e('register-passkey').click();assert.equal(h.e('registration-step').hidden,true);assert.equal(h.e('authentication-step').hidden,false);assert.equal(h.e('authenticate-passkey').hidden,false);assert.equal(h.e('authenticate-passkey').focused,true);assert.equal(h.e('register-passkey').classes.has('button-primary'),false);assert.equal(h.calls.get,0);assert.equal(h.e('auth-confirmation').hidden,true);
 await h.e('authenticate-passkey').click();assert.equal(h.e('auth-confirmation').hidden,false);assert.equal(h.e('passkey-options').hidden,true);assert.equal(h.e('visitor-intro').hidden,true);assert.equal(h.e('visitor-session-details').hidden,true);
});

test('an indistinguishable browser interruption explains how to retry the current step',async()=>{
 const h=harness({browserError:new DOMException('Undetermined','NotAllowedError')});await h.e('register-passkey').click();assert.match(h.e('webauthn-status').textContent,/VisitorBrowserInterrupted.*VisitorRetryRegistration/);assert.equal(h.e('auth-confirmation').hidden,true);assert.equal(h.e('register-passkey').disabled,false);
 h.config.browserError=null;await h.e('register-passkey').click();h.config.browserError=new DOMException('Undetermined','NotAllowedError');await h.e('authenticate-passkey').click();assert.match(h.e('webauthn-status').textContent,/VisitorBrowserInterrupted.*VisitorRetrySignIn/);assert.equal(h.e('authentication-step').hidden,false);assert.equal(h.e('auth-confirmation').hidden,true);
});

for(const prizesEnabled of [false,true])test('exercise invitation follows verified prize context: '+prizesEnabled,async()=>{
 const h=harness({prizesEnabled});await h.e('authenticate-passkey').click();assert.equal(h.e('start-clickfix').dataset.startKey,prizesEnabled?'ExerciseStartPrize':'ExerciseStartNeutral');assert.equal(h.e('clickfix-warning-title').dataset.i18n,prizesEnabled?'ExercisePrizeFailure':'ExerciseResultFailure');
});

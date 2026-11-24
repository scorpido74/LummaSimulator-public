const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../../LummaSimulator.Web/wwwroot/js/localization.js'), 'utf8');

function harness(audience = 'visitor', fail = false, alias = '') {
    const buttons = ['nl-NL', 'en-GB'].map(language => ({ dataset: { language }, disabled: false,
        setAttribute(name, value) { this[name] = value; }, addEventListener(_, handler) { this.click = handler; } }));
    const node = { dataset: { i18n: 'Greeting' }, textContent: '' };
    const title = { dataset: { i18nTitle: 'Greeting', titleSuffix: ' · Demo', titleAlias: alias }, textContent: '' };
    const notice = { textContent: '' };
    const events = [], requests = [];
    const form = { querySelectorAll: () => buttons, querySelector: () => ({ value: 'test-csrf' }),
        addEventListener(_, handler) { this.submit = handler; } };
    const document = { documentElement: { lang: '' }, querySelectorAll: selector => selector === '[data-i18n]' ? [node] : selector === '[data-i18n-title]' ? [title] : [],
        getElementById: id => ({ 'localization-data': { content: { textContent: JSON.stringify({ language: 'nl-NL', audience,
            catalogs: { 'nl-NL': { Greeting: 'Welkom', LanguageSaveFailed: 'Opslaan mislukt' }, 'en-GB': { Greeting: 'Welcome' } } }) } },
            'language-preference-form': form, 'language-notice': notice })[id] };
    const window = { dispatchEvent: event => events.push(event.type) };
    // No location/reload, navigator credentials, cookies or browser storage APIs exposed.
    vm.runInNewContext(script, { document, window, URLSearchParams, Event, fetch: async (url, options) => {
        requests.push({ url, options }); return { ok: !fail };
    } });
    return { buttons, node, title, notice, events, requests, document, window, form };
}

test('title translation preserves a round alias while an open-demo title stays generic', async () => {
    const round = harness('visitor', false, 'Turbootter');
    assert.equal(round.title.textContent, 'Welkom · Turbootter · Demo');
    await round.buttons[1].click();
    assert.equal(round.title.textContent, 'Welcome · Turbootter · Demo');
    const open = harness();
    await open.buttons[1].click();
    assert.equal(open.title.textContent, 'Welcome · Demo');
});

for (const audience of ['visitor', 'presenter']) test(`${audience}: in-place switch persists only its own audience and dispatches one text update`, async () => {
    const h = harness(audience);
    assert.equal(h.node.textContent, 'Welkom');
    await h.buttons[1].click();
    assert.equal(h.node.textContent, 'Welcome');
    assert.equal(h.document.documentElement.lang, 'en-GB');
    assert.deepEqual(h.events, ['lumma-language-changed']);
    assert.equal(h.requests[0].url, '/api/language');
    assert.equal(h.requests[0].options.body.get('audience'), audience);
    assert.equal(h.requests[0].options.headers['X-CSRF-TOKEN'], 'test-csrf');
    assert.equal(h.requests[0].options.signal, undefined);
    await h.buttons[0].click();
    assert.equal(h.node.textContent, 'Welkom');
});

test('failed preference save keeps current language and permits retry', async () => {
    const h = harness('visitor', true);
    await h.buttons[1].click();
    assert.equal(h.node.textContent, 'Welkom');
    assert.equal(h.notice.textContent, 'Opslaan mislukt');
    assert.equal(h.buttons[1].disabled, false);
    assert.equal(h.events.length, 0);
});

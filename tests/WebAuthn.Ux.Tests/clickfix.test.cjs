const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
class Element {
    constructor(text = '') { this.textContent = text; this.dataset = {}; this.hidden = false; this.disabled = false; this.events = new Map(); this.attributes = {}; this.classes = new Set(); this.classList = { toggle: (name, on) => on ? this.classes.add(name) : this.classes.delete(name) }; }
    addEventListener(name, handler) { const list = this.events.get(name) || []; list.push(handler); this.events.set(name, list); }
    setAttribute(name, value) { this.attributes[name] = value; }
    getAttribute(name) { return this.attributes[name]; }
    replaceChildren() { this.children = []; this.textContent = ''; }
    append(...nodes) { this.children = (this.children || []).concat(nodes); this.textContent += nodes.map(node => node.textContent).join(' '); }
    focus() { this.focused = true; }
    click() { return Promise.all((this.events.get('click') || []).map(handler => handler())); }
}

const exerciseScript = fs.readFileSync(path.join(__dirname, '../../LummaSimulator.Web/wwwroot/js/clickfix.js'), 'utf8');

for (const mode of ['missing executable', 'early process exit']) test(`browser startup reports ${mode} without waiting for the endpoint timeout`, () => {
    const { spawnSync } = require('node:child_process');
    const root = fs.mkdtempSync(path.join(require('node:os').tmpdir(), 'LummaClickFixStartup-'));
    const fixture = path.join(__dirname, '../LummaSimulator.Web.Tests/clickfix-browser.mjs');
    const started = Date.now();
    const result = spawnSync(process.execPath, [fixture], {
        input: JSON.stringify({ browser: mode === 'missing executable' ? path.join(root, 'missing-browser.exe') : process.execPath,
            browserVersion: 'synthetic-negative-fixture', temporaryRoot: root, cookie: 'synthetic-cookie-not-for-output', spki: 'synthetic-spki-not-for-output' }),
        encoding: 'utf8', timeout: 5000, windowsHide: true
    });
    assert.equal(result.error, undefined);
    assert.equal(result.status, 1);
    assert.ok(Date.now() - started < 5000, 'Early launch failure must not wait for the 15-second endpoint deadline');
    assert.match(result.stderr, mode === 'missing executable' ? /Browser launch failed: ENOENT/ : /Browser exited before publishing its debugging endpoint/);
    if (mode === 'early process exit') assert.match(result.stderr, /"premature":true/);
    assert.doesNotMatch(result.stderr, /synthetic-cookie-not-for-output|synthetic-spki-not-for-output/);
});
function exerciseHarness({ language = 'nl-NL', paired = false, fail = false, replay = false, expired = false, reload = false } = {}) {
    const ids = ['exercise', 'warning', 'run', 'command', 'progress', 'status', 'simulated', 'delivery', 'retry', 'start-clickfix', 'exercise-start-status', 'close', 'signed-in-surface', 'auth-confirmation', 'exercise-debrief','debrief','back','ok','cancel','copy','open','paste','run-close'];
    const elements = Object.fromEntries(ids.map(id => [id, new Element()]));
    elements.exercise.hidden=true;elements.exercise.dataset.exampleCommand='synthetic harmless example';elements['start-clickfix'].dataset.startKey='ExerciseStartNeutral';
    elements.command.value='';elements.run.hidden=true;elements.debrief.hidden=true;elements.ok.disabled=true;
    elements.exercise.dataset.attempt = paired ? 'synthetic-ticket' : '';
    const requests = [], timers = new Map(), events = new Map(); let timerId = 0;
    const redirects = [];
    const context = vm.createContext({ document: {
        getElementById: id => elements[id] || elements[id.replace('clickfix-', '')],
        querySelector: () => ({ value: 'synthetic-csrf' }), createElement: () => new Element()
    }, window: { LummaI18n: { t: key => language + ':' + key }, location: { assign: url => redirects.push(url), reload() {} }, addEventListener: (name, action) => events.set(name, action) },
    performance:{getEntriesByType:()=>[{type:reload?'reload':'navigate'}]}, AbortController, setTimeout: (action, delay) => { timers.set(++timerId, { action, delay }); return timerId; }, clearTimeout: id => timers.delete(id),
    fetch: async (url, options) => {
        requests.push({ url, options });
        return { ok: !expired && (!fail || url.includes('BeginExercise')), json: async () => url.includes('BeginExercise') ? { completed: replay, attempt: paired ? 'synthetic-ticket' : null } : url.includes('Choice') ? { actionType: 'ClickFixFollowSimulated', sequence: 1 } : { completed: true } };
    } });
    vm.runInContext(exerciseScript, context);
    return { elements, requests, timers, redirects, events, start: async () => { await elements['start-clickfix'].click(); for(let i=0;i<16;i++) await Promise.resolve(); }, loadAgain: () => vm.runInContext(exerciseScript, context),
        async tick(delay) {
            const timer = [...timers].find(([, value]) => value.delay === delay);
            assert.ok(timer, `Expected reading interval ${delay}`); timers.delete(timer[0]); timer[1].action();
            for (let i = 0; i < 15; i++) await Promise.resolve();
        } };
}
for (const language of ['nl-NL', 'en-GB']) test(`automatic exercise ${language}: reading time, duplicate load, simulated content and separate completion`, async () => {
    const h = exerciseHarness({ language, paired: true });
    h.loadAgain(); assert.equal(h.timers.size, 0); assert.equal(h.requests.length, 0); await Promise.all([h.start(),h.start()]); assert.equal(h.requests.length,1); assert.equal(h.timers.size,0);
    await h.elements.copy.click();await h.elements.open.click(); assert.equal(h.elements.warning.hidden, true); assert.equal(h.elements.run.hidden, false);
    await h.elements.paste.click(); assert.equal(h.elements.ok.disabled,false); assert.equal(h.requests.length,1);
    await h.elements.ok.click(); for(let i=0;i<20;i++)await Promise.resolve();
    assert.deepEqual(h.requests.map(r => r.url), ['/SignedIn?handler=BeginExercise', '/ClickFix?handler=Choice', '/ClickFix?handler=Complete']);
    assert.deepEqual(h.redirects, []);assert.equal(h.elements.debrief.hidden,false);await h.elements.back.click();assert.equal(h.elements.exercise.hidden,true);assert.equal(h.elements['start-clickfix'].focused,true);
    await h.elements.retry.click(); assert.equal(h.requests.length, 3);
});
test('leaving the exercise cancels visual timers without recording completion',async()=>{
 const h=exerciseHarness();await h.start();h.events.get('pagehide')();assert.equal(h.timers.size,0);assert.equal(h.requests.length,1);assert.equal(h.elements.exercise.hidden,true);
});

test('a fresh document after reload during Run has idle local progress and requires a new explicit start',async()=>{
 const old=exerciseHarness();await old.start();await old.elements.copy.click();await old.elements.open.click();assert.equal(old.elements.run.hidden,false);
 old.events.get('pagehide')();for(let i=0;i<20;i++)await Promise.resolve();
 assert.equal(old.timers.size,0);assert.equal(old.requests.filter(r=>r.url.includes('Complete')||r.url.includes('Choice')).length,0);
 const fresh=exerciseHarness({reload:true});assert.equal(fresh.elements['start-clickfix'].focused,true);assert.equal(fresh.elements.command.value,'');assert.equal(fresh.elements.exercise.hidden,true);assert.equal(fresh.elements.run.hidden,true);assert.equal(fresh.elements.debrief.hidden,true);assert.equal(fresh.elements.ok.disabled,true);assert.equal(fresh.requests.length,0);assert.equal(fresh.timers.size,0);
 await fresh.start();assert.equal(fresh.elements.exercise.hidden,false);assert.equal(fresh.elements.warning.hidden,false);assert.equal(fresh.elements.run.hidden,true);assert.equal(fresh.requests.length,1);
});

test('failed completion can be retried without starting a second exercise', async () => {
    const h = exerciseHarness({ fail: true }); await h.start();
    await h.elements.copy.click();await h.elements.open.click(); await h.elements.paste.click(); await h.elements.ok.click(); for(let i=0;i<20;i++)await Promise.resolve();
    assert.equal(h.elements.retry.hidden, false); assert.equal(h.redirects.length, 0);
    await h.elements.retry.click(); assert.equal(h.requests.length, 3); assert.equal(h.timers.size, 0);
});

test('automatic exercise exposes no clipboard, OS command, fullscreen or keyboard capture beyond active-overlay Escape', () => {
    assert.doesNotMatch(exerciseScript, /\b(?:XMLHttpRequest|WebSocket|EventSource|eval|Function|localStorage|sessionStorage|indexedDB|navigator|sendBeacon)\s*[.(]/);
    assert.doesNotMatch(exerciseScript, /innerHTML|outerHTML|document\.write|\.cookie|requestFullscreen|keyup|navigator\.clipboard/);
    assert.doesNotMatch(exerciseScript,/window\.location/);
    assert.match(exerciseScript,/event.key==='Escape'&&active/);

    const razor = fs.readFileSync(path.join(__dirname, '../../LummaSimulator.Web/Pages/Shared/_ClickFixExercise.cshtml'), 'utf8');
    assert.doesNotMatch(razor, /<iframe|download=|onclick=|cmd\.exe|javascript:/i);
    assert.match(razor, /<input id="clickfix-command"/);
    assert.match(razor, /readonly value=""/);
    assert.match(razor, /<noscript>/);
    assert.match(razor,/powershell.exe -NoProfile -Command &quot;&amp; \{ Write-Output 'Lumma awareness-demo' \}&quot;/);
    assert.match(razor,/readonly/);
    assert.doesNotMatch(razor, /<progress|ExerciseAutoHelp|ClickFixNotLinked/);
});

test('replay resets only the visual timeline and sends no second completion or presenter action',async()=>{
 const h=exerciseHarness({replay:true,paired:true});await h.start();assert.equal(h.elements.exercise.hidden,false);assert.equal(h.redirects.length,0);assert.equal(h.elements.exercise.dataset.attempt,'');
 await h.elements.copy.click();await h.elements.open.click();await h.elements.paste.click();await h.elements.ok.click();for(let i=0;i<20;i++)await Promise.resolve();
 assert.deepEqual(h.requests.map(r=>r.url),['/SignedIn?handler=BeginExercise']);assert.deepEqual(h.redirects,[]);assert.equal(h.elements.debrief.hidden,false);await h.elements.back.click();assert.equal(h.elements.exercise.hidden,true);assert.equal(h.elements['start-clickfix'].focused,true);
});
test('expired session cannot begin a replay or start visual timers',async()=>{
 const h=exerciseHarness({replay:true,expired:true});await h.start();assert.equal(h.elements.exercise.hidden,true);assert.equal(h.timers.size,0);assert.equal(h.redirects.length,0);assert.equal(h.elements['start-clickfix'].disabled,false);
});

test('close and Escape restore the green block and focus; reopening creates one fresh visual timeline',async()=>{
 const h=exerciseHarness();await h.start();assert.notEqual(h.elements['auth-confirmation'].inert,true);
 await h.elements.close.click();assert.equal(h.elements.exercise.hidden,true);assert.equal(h.elements['auth-confirmation'].inert,false);assert.equal(h.elements['start-clickfix'].focused,true);assert.equal(h.timers.size,0);
 await h.start();let prevented=false;h.events.get('keydown')({key:'Escape',preventDefault(){prevented=true;}});assert.equal(prevented,true);assert.equal(h.elements.exercise.hidden,true);assert.equal(h.timers.size,0);
 await h.start();await h.elements.copy.click();await h.elements.open.click();await h.elements.paste.click();await h.elements.ok.click();for(let i=0;i<20;i++)await Promise.resolve();
 assert.equal(h.requests.filter(r=>r.url.includes('Complete')).length,1);assert.equal(h.elements.debrief.hidden,false);await h.elements.back.click();assert.equal(h.elements.exercise.hidden,true);assert.match(h.elements['start-clickfix'].textContent,/ExerciseStartNeutral/);
});

test('copy, open and paste use only page state and closing clears the example',async()=>{
 const h=exerciseHarness();await h.start();await h.elements.open.click();assert.equal(h.elements.command.value,'');assert.equal(h.elements.ok.disabled,true);assert.equal(h.elements.paste.disabled,true);await h.elements.paste.click();assert.equal(h.elements.command.value,'');
 await h.elements['run-close'].click();assert.equal(h.elements['start-clickfix'].focused,true);await h.start();await h.elements.copy.click();await h.elements.open.click();assert.equal(h.elements.command.value,'');await h.elements.paste.click();assert.equal(h.elements.command.value,h.elements.exercise.dataset.exampleCommand);assert.equal(h.elements.ok.disabled,false);
 await h.elements.cancel.click();assert.equal(h.elements.command.value,'');assert.equal(h.elements.ok.disabled,true);await h.start();await h.elements.open.click();assert.equal(h.elements.paste.disabled,true);assert.equal(h.elements.command.value,'');assert.equal(h.requests.filter(r=>r.url.includes('Complete')).length,0);
});

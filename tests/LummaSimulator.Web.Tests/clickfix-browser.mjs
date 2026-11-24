// Only the isolated HTTPS fixture's new synthetic visitor session is supplied through stdin.
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
const chunks = [];
for await (const chunk of process.stdin) chunks.push(chunk);
const config = JSON.parse(Buffer.concat(chunks));
const profile = await mkdtemp(path.join(config.temporaryRoot, 'clickfix-browser-'));
const startedAt = performance.now(), browserStderr = [];
let launchError, exited = false, cleanupRequested = false, stderrBuffer = '';
const safeDiagnostic = text => {
    for (const secret of [config.cookie, config.spki].filter(Boolean)) text = text.split(secret).join('<redacted>');
    return text.replace(/(?:https?|wss?):\/\/\S+/g, '<url>').replace(/[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}/gi, '<id>');
};
console.error('ClickFix browser: ' + JSON.stringify({ executable: config.browser, version: config.browserVersion || 'not supplied', isolatedProfile: true }));
const browser = spawn(config.browser, ['--headless=new', '--no-first-run', '--disable-extensions',
    '--disable-background-networking', '--no-proxy-server', '--ignore-certificate-errors-spki-list=' + config.spki,
    '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
browser.on('error', error => { launchError = error; });
browser.on('exit', (code, signal) => {
    exited = true;
    console.error('ClickFix browser exit: ' + JSON.stringify({ code, signal, elapsedMs: Math.round(performance.now() - startedAt), premature: !cleanupRequested }));
});
browser.stderr.setEncoding('utf8');
browser.stderr.on('data', chunk => {
    stderrBuffer += chunk;
    const lines = stderrBuffer.split(/\r?\n/); stderrBuffer = lines.pop();
    for (const line of lines.filter(Boolean)) { browserStderr.push(safeDiagnostic(line)); if (browserStderr.length > 40) browserStderr.shift(); }
});
let socket;
const stage = name => console.error('ClickFix fixture stage: ' + name);
function trace(name, phase, outcome, durationMs, extra = {}) {
    console.error('ClickFix step: ' + JSON.stringify({ name, phase, elapsedMs: Math.round(performance.now() - startedAt), outcome, ...(durationMs === undefined ? {} : { durationMs }), ...extra }));
}
async function step(name, action) {
    const start = performance.now(); trace(name, 'before', 'pending');
    try { const result = await action(); trace(name, 'after', 'success', Math.round(performance.now() - start)); return result; }
    catch (error) { trace(name, 'after', 'error', Math.round(performance.now() - start)); throw error; }
}
try {
    stage('startup');
    let port;
    for (let i = 0; i < 150; i++) {
        if (launchError) throw new Error('Browser launch failed: ' + launchError.code);
        if (exited) throw new Error('Browser exited before publishing its debugging endpoint');
        try { port = Number((await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]); break; }
        catch { await new Promise(resolve => setTimeout(resolve, 100)); }
    }
    if (!port) throw new Error('Isolated browser debugging endpoint unavailable');
    console.error('ClickFix debugging endpoint ready after ' + Math.round(performance.now() - startedAt) + ' ms');
    stage('debugging endpoint');
    const targets = await step('Discover page target', async () => (await fetch(`http://127.0.0.1:${port}/json/list`)).json());
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await step('Connect page WebSocket', () => new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; }));
    let sequence = 0;
    const pending = new Map();
    const requests = [], failures = [], exercisePosts = { Choice: 0, Complete: 0 };
    socket.onmessage = ({ data }) => {
        const message = JSON.parse(data);
        if (message.id) {
            const callback = pending.get(message.id); pending.delete(message.id);
            if (!callback) { trace('Unmatched CDP response', 'response', 'unmatched', undefined, { sessionKind: message.sessionId === undefined ? 'page-root' : 'attached' }); return; }
            clearTimeout(callback.timeout);
            trace('CDP ' + callback.method, 'response', message.error ? 'protocol-error' : 'received', undefined,
                { expectedSession: 'page-root', sessionMatches: message.sessionId === undefined });
            if (message.error) callback.reject(new Error(message.error.message)); else callback.resolve(message.result);
        }
        if (message.method === 'Network.requestWillBeSent') {
            const request = message.params.request, url = new URL(request.url);
            requests.push(url.pathname);
            const handler = url.searchParams.get('handler');
            if (request.method === 'POST' && url.pathname === '/ClickFix' && Object.hasOwn(exercisePosts, handler)) exercisePosts[handler]++;
        }
        if (message.method === 'Runtime.exceptionThrown') failures.push('browser exception');
    };
    socket.onclose = () => {
        for (const callback of pending.values()) {
            trace('CDP ' + callback.method, 'response', 'connection-closed-without-response', undefined, { expectedSession: 'page-root' });
            clearTimeout(callback.timeout); callback.reject(new Error('Browser connection closed'));
        }
        pending.clear();
    };
    function command(method, params = {}) {
        const id = ++sequence;
        return step('CDP ' + method, () => new Promise((resolve, reject) => {
            const timeout = setTimeout(() => { pending.delete(id); trace('CDP ' + method, 'response', 'timeout', undefined, { expectedSession: 'page-root' }); reject(new Error('Browser command timed out: ' + method)); }, 8000);
            pending.set(id, { resolve, reject, timeout, method });
            socket.send(JSON.stringify({ id, method, params }));
        }));
    }
    async function evaluate(expression) {
        const value = await command('Runtime.evaluate', { expression, returnByValue: true });
        if (value.exceptionDetails) throw new Error('ClickFix browser assertion script failed');
        return value.result.value;
    }
    function check(condition, description) { if (!condition) throw new Error(description); }
    let domWaitSequence = 0;
    async function waitFor(expression, attempts=150, name='DOM wait ' + (++domWaitSequence)) {
        return step(name, async () => {
        for (let i = 0; i < attempts; i++) {
            try { if (await evaluate(expression)) return; }
            catch (error) {
                // A document navigation can destroy a context while an assertion is in flight.
                if (!/context|Cannot find|Browser assertion context/i.test(error.message)) throw error;
            }
            await new Promise(resolve => setTimeout(resolve, 100));
        }
        throw new Error('ClickFix browser timed out');
        });
    }
    const id = name => `document.getElementById('clickfix-${name}')`;
    stage('CDP Network.enable'); await command('Network.enable'); stage('CDP Page.enable'); await command('Page.enable'); stage('CDP Runtime.enable'); await command('Runtime.enable');
    await command('Emulation.setDeviceMetricsOverride', { width: 1200, height: 950, deviceScaleFactor: 1, mobile: false });
    if(!config.paired){
        // Fresh isolated profile and a virtual authenticator: actual portal verification, no physical key or live data.
        stage('CDP WebAuthn.enable'); await command('WebAuthn.enable');
        await command('WebAuthn.addVirtualAuthenticator',{options:{protocol:'ctap2',transport:'usb',hasResidentKey:true,hasUserVerification:true,isUserVerified:true,automaticPresenceSimulation:true}});
        await command('Page.navigate',{url:config.origin+'/Index'});
        const windowsHelloButton = "document.querySelector('form[data-method=\"windows-hello\"] button[type=\"submit\"]')";
        await waitFor(windowsHelloButton,150,'DOM Index Windows Hello action available');
        await step('DOM click Windows Hello action', () => evaluate(`${windowsHelloButton}.click()`));
        await waitFor("location.pathname==='/Demo'&&document.getElementById('register-passkey')",150,'DOM Demo registration available');
        check(await evaluate("document.getElementById('auth-confirmation').hidden"),'Fresh visitor has no authentication success');
        await step('DOM click register passkey', () => evaluate("document.getElementById('register-passkey').click()"));
        await waitFor("Number(document.getElementById('credential-count').textContent)>0&&!document.getElementById('authenticate-passkey').disabled",150,'DOM registration completed');
        check(await evaluate("document.getElementById('registration-step').classList.contains('completed')&&document.getElementById('auth-confirmation').hidden&&location.pathname==='/Demo'"),'Registration alone stays separate from sign-in success');
        check(await evaluate("!document.getElementById('authentication-step').hidden&&!document.getElementById('authenticate-passkey').hidden&&document.getElementById('registration-step').hidden"),'Registration reveals step two without automatic sign-in');
        await step('DOM click authenticate passkey', () => evaluate("document.getElementById('authenticate-passkey').click()"));
        await waitFor("!document.getElementById('auth-confirmation').hidden",150,'DOM sign-in confirmed');
    }else{
        await command('Network.setCookie',{name:'__Host-Lumma.Visitor',value:config.cookie,url:config.origin,path:'/',secure:true,httpOnly:true,sameSite:'Strict'});
        await command('Page.navigate',{url:config.origin+'/Demo'});
        await waitFor("document.readyState==='complete'&&document.getElementById('start-clickfix')&&!document.getElementById('auth-confirmation').hidden");
    }
    const signedInAlias=await evaluate("document.getElementById('verified-username').textContent.trim()");
    const signedInUrl=await evaluate('location.href');
    await new Promise(resolve=>setTimeout(resolve,10100));
    check(await evaluate("location.href==="+JSON.stringify(signedInUrl)+"&&location.pathname==='/Demo'&&!document.getElementById('auth-confirmation').hidden&&document.getElementById('verified-username').textContent.trim().length>0"),'Green sign-in block and alias remain on Demo for at least ten seconds without navigation');
    check(await evaluate("document.getElementById('clickfix-exercise').hidden&&document.getElementById('start-clickfix').tagName==='BUTTON'"),'Exercise waits for explicit click');
    check(await evaluate("document.getElementById('passkey-options').hidden&&!document.getElementById('again-authenticate')"),'Signed-in registration and duplicate sign-in controls are hidden');
    const pageHeight=await evaluate('document.documentElement.scrollHeight');
    await evaluate("document.getElementById('start-clickfix').click();document.getElementById('start-clickfix').click()");
    await waitFor("!document.getElementById('clickfix-exercise').hidden");
    check(await evaluate("getComputedStyle(document.getElementById('auth-confirmation')).visibility==='visible'&&!document.getElementById('auth-confirmation').inert"),'Green sign-in block remains visible and reachable');
    check(await evaluate('document.documentElement.scrollHeight')===pageHeight,'Overlay adds no page height');
    await evaluate("document.getElementById('clickfix-close').click()");
    check(await evaluate("document.getElementById('clickfix-exercise').hidden&&document.activeElement.id==='start-clickfix'"),'Closing restores focus to start');
    await evaluate("document.getElementById('start-clickfix').click()");await waitFor("!document.getElementById('clickfix-exercise').hidden");
    await command('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
    check(await evaluate("document.getElementById('clickfix-exercise').hidden&&document.activeElement.id==='start-clickfix'"),'Escape closes only the active overlay');
    // A real reload during Run must discard only local progress, without recording completion.
    const beforeInterrupted = { ...exercisePosts };
    await evaluate("document.getElementById('start-clickfix').click()");
    const exerciseReadiness = `(() => {
        const visible = element => Boolean(element?.checkVisibility({visibilityProperty:true,opacityProperty:true}));
        const usable = element => visible(element) && !element.disabled && !element.closest('[inert]');
        return {
            exerciseVisible: visible(${id('exercise')}),
            warningVisible: visible(${id('warning')}),
            copyUsable: usable(${id('copy')}),
            openUsable: usable(${id('open')}),
            startFailed: Boolean(document.getElementById('exercise-start-status')?.textContent.trim())
        };
    })()`;
    await step('DOM exercise ready before reload test', async () => {
        const deadline = performance.now() + 12000;
        while (true) {
            const state = await evaluate(exerciseReadiness);
            if (state.exerciseVisible && state.warningVisible && state.copyUsable && state.openUsable) return;
            if (state.startFailed || performance.now() >= deadline)
                throw new Error('Exercise start did not expose usable Copy and Open controls: ' + JSON.stringify(state));
            await new Promise(resolve => setTimeout(resolve, 100));
        }
    });
    await evaluate("document.getElementById('clickfix-copy').click();document.getElementById('clickfix-open').click()");
    await waitFor(`!${id('run')}?.hidden && ${id('paste')}?.disabled === false`,150,'DOM Run open with Paste enabled before reload');
    await command('Page.reload');
    await waitFor("document.readyState==='complete'&&document.getElementById('clickfix-exercise')?.dataset.attached==='true'");
    check(await evaluate("document.getElementById('clickfix-exercise').hidden&&document.getElementById('clickfix-run').hidden&&document.getElementById('clickfix-debrief').hidden&&document.getElementById('clickfix-ok').disabled&&document.getElementById('clickfix-command').value===''&&document.getElementById('clickfix-paste').disabled&&document.activeElement.id==='start-clickfix'&&!document.getElementById('start-clickfix').disabled"),'Reload during Run resets the current UI to idle');
    await new Promise(resolve=>setTimeout(resolve,10500));
    check(await evaluate("document.getElementById('clickfix-exercise').hidden&&!document.getElementById('auth-confirmation').hidden"),'Reload cannot resume an old timeline automatically and retains sign-in');
    check(exercisePosts.Complete===beforeInterrupted.Complete,'Interrupted reload records no completion');
    check(exercisePosts.Choice===beforeInterrupted.Choice,'Interrupted reload records no presenter choice');
    await evaluate("document.getElementById('start-clickfix').click()");
    await waitFor("!document.getElementById('clickfix-exercise').hidden");
    check(await evaluate("!document.getElementById('clickfix-warning').hidden&&document.getElementById('clickfix-run').hidden&&document.getElementById('clickfix-debrief').hidden&&document.getElementById('clickfix-ok').disabled"),'Explicit restart after reload begins at the warning, not the old Run step');
    for (const [width, height, scale, mobile] of [[1920,1080,1,false],[640,360,1,false],[390,844,1,true],[320,568,1,true],[160,284,1,false]]) {
        await command('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: scale, mobile });
        check(await evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Responsive exercise without horizontal overflow');
        check(await evaluate("getComputedStyle(document.getElementById('auth-confirmation')).visibility==='visible'&&!document.getElementById('auth-confirmation').inert"),'Green sign-in block remains visible and reachable');
        for (const language of ['nl-NL', 'en-GB']) {
            await evaluate(`document.querySelector('[data-language="${language}"]').click()`);
            await waitFor(`document.documentElement.lang === '${language}'`);
            const preview = await command('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
            await writeFile(path.join(config.temporaryRoot, `clickfix-${width}-${language}.png`), Buffer.from(preview.data, 'base64'));
        }
    }
    for(const control of ['clickfix-copy','clickfix-open','clickfix-close'])check(await evaluate(`(()=>{const e=document.getElementById('${control}');e.scrollIntoView({block:'nearest'});const r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight;})()`),'Warning control remains reachable in narrow reflow');
    // Only explicit browser controls advance the illustration; no OS action.
    await evaluate("document.getElementById('clickfix-copy').click();document.getElementById('clickfix-open').click()");
    await waitFor(`!${id('run')}?.hidden`);
    for(const [width,height] of [[640,360],[390,844],[160,284]]){
        await command('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
        for(const control of ['clickfix-command','clickfix-paste','clickfix-ok','clickfix-cancel','clickfix-run-close'])check(await evaluate(`(()=>{const e=document.getElementById('${control}');e.scrollIntoView({block:'nearest'});const r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight;})()`),'Run control remains reachable at mobile/200-percent-equivalent reflow');
    }
    await evaluate("document.getElementById('clickfix-copy').click();document.getElementById('clickfix-open').click()");
    check(await evaluate("document.getElementById('clickfix-command').value===''&&document.getElementById('clickfix-ok').disabled"),'Run opens empty with OK disabled');
    await evaluate("document.getElementById('clickfix-paste').click()");
    await waitFor("!document.getElementById('clickfix-ok').disabled",450);
    await evaluate("document.getElementById('clickfix-ok').click()");
    await waitFor("!document.getElementById('clickfix-debrief').hidden",450);
    await waitFor("document.getElementById('clickfix-exercise').dataset.completed==='true'",450);
    await evaluate("document.getElementById('clickfix-back').click()");
    await waitFor("document.getElementById('clickfix-exercise').hidden&&document.activeElement.id==='start-clickfix'",450);
    check(await evaluate("location.pathname==='/Demo'&&!document.getElementById('auth-confirmation').hidden"),'Completion restores green block and debrief in place');
    const authCalls=requests.filter(p=>p.includes('/api/webauthn/')).length,completionCalls=requests.filter(p=>p==='/ClickFix').length;
    const beforeReplay = { ...exercisePosts };
    await command('Page.reload');await waitFor("document.readyState==='complete'&&document.getElementById('start-clickfix')&&!document.getElementById('auth-confirmation').hidden");
    check(await evaluate("document.getElementById('demo-welcome').textContent.includes("+JSON.stringify(signedInAlias)+")&&document.getElementById('start-clickfix')"),'Reload retains alias, sign-in and repeat action');
    await evaluate("document.getElementById('start-clickfix').click()");await waitFor("!document.getElementById('clickfix-exercise').hidden");
    await evaluate("document.getElementById('clickfix-copy').click();document.getElementById('clickfix-open').click()");
    check(await evaluate("document.getElementById('clickfix-command').value===''&&document.getElementById('clickfix-ok').disabled"),'Run opens empty with OK disabled');
    await evaluate("document.getElementById('clickfix-paste').click()");
    await waitFor("!document.getElementById('clickfix-ok').disabled",450);
    await evaluate("document.getElementById('clickfix-ok').click()");
    await waitFor("!document.getElementById('clickfix-debrief').hidden",450);
    await waitFor("document.getElementById('clickfix-exercise').dataset.completed==='true'",450);
    await evaluate("document.getElementById('clickfix-back').click()");
    await waitFor("document.getElementById('clickfix-exercise').hidden&&document.activeElement.id==='start-clickfix'",450);
    check(requests.filter(p=>p.includes('/api/webauthn/')).length===authCalls,'Replay opens no WebAuthn dialog');
    check(requests.filter(p=>p==='/ClickFix').length===completionCalls,'Replay adds no completion or presenter action');
    check(exercisePosts.Complete===beforeReplay.Complete,'Replay sends no second Complete POST');
    check(exercisePosts.Choice===beforeReplay.Choice,'Replay sends no second Choice POST');
    check(failures.length===0,'No browser script exceptions');
    stage('completion and reload verified');
    console.log(JSON.stringify({ success: true }));
    // Some browser versions close the socket before acknowledging Browser.close.
    cleanupRequested = true;
    await command('Browser.close').catch(error => {
        if (!/Browser connection closed/.test(error.message)) throw error;
    });
} catch (error) {
    console.error('ClickFix browser failure: ' + safeDiagnostic(error.message));
    throw new Error(safeDiagnostic(error.message));
} finally {
    socket?.close();
    if (!exited && !launchError) {
        await Promise.race([new Promise(resolve => browser.once('exit', resolve)), new Promise(resolve => setTimeout(resolve, 3000))]);
        if (!exited) { cleanupRequested = true; browser.kill(); await new Promise(resolve => browser.once('exit', resolve)); }
    }
    if (stderrBuffer.trim()) browserStderr.push(safeDiagnostic(stderrBuffer.trim()));
    for (const line of browserStderr) console.error('ClickFix browser stderr: ' + line);
    // Retain only this test's profile inside the ignored isolated evidence directory.
}

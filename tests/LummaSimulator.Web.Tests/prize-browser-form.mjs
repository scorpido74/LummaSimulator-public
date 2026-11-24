// Real browser navigation, using only the isolated HTTP fixture's session.
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import path from 'node:path';
const chunks = [];
for await (const chunk of process.stdin) chunks.push(chunk);
const config = JSON.parse(Buffer.concat(chunks));
const profile = await mkdtemp(path.join(config.temporaryRoot, 'prize-browser-'));
const browser = spawn(config.browser, ['--headless=new', '--no-first-run', '--disable-extensions',
  '--disable-background-networking', '--no-proxy-server', '--ignore-certificate-errors-spki-list=' + config.spki,
  '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'],
  { windowsHide: true, stdio: 'ignore' });
let socket;
try {
  let port;
  for (let i = 0; i < 100; i++) {
    try { port = Number((await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]); break; }
    catch { await new Promise(r => setTimeout(r, 100)); }
  }
  if (!port) throw new Error('Browser debugging endpoint unavailable');
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let sequence = 0;
  const pending = new Map();
  const posts = new Map();
  let postedOrigin, postStatus, postPath, contentType;
  const onboardingPosts=[];
  socket.onmessage = ({ data }) => {
    const message = JSON.parse(data);
    if (message.id) {
      const callback = pending.get(message.id); pending.delete(message.id);
      if (!callback) return;
      clearTimeout(callback.timeout);
      if (message.error) callback.reject(new Error(message.error.message)); else callback.resolve(message.result);
    }
    const p = message.params;
    if (message.method === 'Network.requestWillBeSent' && p.request.method === 'POST') {
      posts.set(p.requestId, true); postPath = new URL(p.request.url).pathname;
    }
    if (message.method === 'Network.requestWillBeSentExtraInfo' && posts.has(p.requestId) && postedOrigin === undefined) {
      const headers = Object.fromEntries(Object.entries(p.headers).map(([k,v]) => [k.toLowerCase(),v]));
      postedOrigin = headers.origin; contentType = headers['content-type'];
    }
    if (message.method === 'Network.responseReceivedExtraInfo' && posts.has(p.requestId) && postStatus === undefined) postStatus = p.statusCode;
    if(config.onboarding&&message.method==='Network.responseReceived'&&posts.has(p.requestId))
      onboardingPosts.push({path:new URL(p.response.url).pathname,status:p.response.status});
  };
  socket.onclose = () => {
    for (const callback of pending.values()) { clearTimeout(callback.timeout); callback.reject(new Error('Browser connection closed')); }
    pending.clear();
  };
  function command(method, params = {}) {
    const id = ++sequence;
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => { pending.delete(id); reject(new Error('Browser command timed out: ' + method)); }, 8000);
      pending.set(id, { resolve, reject, timeout }); socket.send(JSON.stringify({ id, method, params }));
    });
  }
  async function evaluate(expression) {
    const result = await command('Runtime.evaluate', { expression, returnByValue: true });
    if (result.exceptionDetails) throw new Error('Browser form script failed');
    return result.result.value;
  }
  await command('Network.enable');
  await command('Page.enable');
  await command('Network.setCookie', { name: '__Host-Lumma.Presenter', value: config.cookie,
    url: config.origin, path: '/', secure: true, httpOnly: true, sameSite: 'Strict' });
  if(config.onboarding) {
    await command('Emulation.setDeviceMetricsOverride', { width: 360, height: 800, deviceScaleFactor: 1, mobile: true });
    await command('Page.navigate', { url: config.origin + '/Presenter/Onboarding' });
    let onboardingReady=false;
    for(let i=0;i<100;i++){
      onboardingReady=await evaluate(`document.readyState==='complete' && !!document.querySelector('[data-onboarding-submit]')`);
      if(onboardingReady)break;
      await new Promise(r=>setTimeout(r,100));
    }
    if(!onboardingReady)throw new Error('Onboarding form unavailable');
    const layout=await evaluate(`(() => {
      const steps=[...document.querySelectorAll('.onboarding-steps > li')];
      return steps.length===5 && steps.every(step=>!/^\\d+\\./.test(step.querySelector('h2').textContent.trim()))
        && steps.every(step=>step.getBoundingClientRect().right<=innerWidth);
    })()`);
    if(!layout)throw new Error('Onboarding numbering or narrow layout is incorrect');
    await evaluate(`(() => {
      Object.defineProperty(navigator.credentials, 'get', {value: async () => {throw new DOMException('Cancelled','NotAllowedError');} });
      const form=document.querySelector('[data-onboarding-submit]');
      form.elements.confirmed.checked=true;
      form.querySelector('button').click();
    })()`);
    let cancelled=false;
    for(let i=0;i<100;i++){
      cancelled=await evaluate(`document.getElementById('presenter-status').dataset.i18n==='OnboardingVerificationCancelled' && !document.querySelector('[data-onboarding-submit] button').disabled`);
      if(cancelled)break;
      await new Promise(r=>setTimeout(r,100));
    }
    const inputPreserved=await evaluate(`location.pathname==='/Presenter/Onboarding' && document.querySelector('[data-onboarding-submit] input[name=confirmed]').checked`);
    const state=await evaluate(`({key:document.getElementById('presenter-status')?.dataset.i18n || '', path:location.pathname})`);
    console.log(JSON.stringify({cancelled,inputPreserved,state,onboardingPosts}));
  } else {
  await command('Page.navigate', { url: config.origin + '/Presenter/Prizes/New?profileId=builtin-cybertruck' });
  let ready = false;
  for (let i=0;i<100;i++) {
    ready = await evaluate(`!!document.querySelector('form.prize-event-form input[name="OperationKey"]')`);
    if (ready) break;
    await new Promise(r => setTimeout(r, 100));
  }
  if (!ready) throw new Error('Authenticated event form unavailable');
  await evaluate(`(() => { const f=document.querySelector('form.prize-event-form');
    f.elements.Name.value='Browser regression event';
    f.requestSubmit(f.querySelector('button[name="start"][value="true"]')); })()`);
  let eventPage = false;
  for (let i=0;i<100;i++) {
    eventPage = await evaluate(`location.pathname.startsWith('/Presenter/Prizes/Event/')`);
    if (eventPage || postStatus >= 400) break;
    await new Promise(r => setTimeout(r, 100));
  }
  await command('Emulation.setDeviceMetricsOverride', { width: 360, height: 800, deviceScaleFactor: 1, mobile: true });
  await command('Page.navigate', { url: config.origin + '/Presenter/Configuration' });
  let configurationReady = false;
  for (let i=0;i<100;i++) {
    configurationReady = await evaluate(`document.readyState === 'complete' && !!document.querySelector('.network-plan') && !!document.querySelector('[data-helper-download]')`);
    if (configurationReady) break;
    await new Promise(r => setTimeout(r, 100));
  }
  if (!configurationReady) throw new Error('Administrator helper and network controls unavailable');
  const responsive = await evaluate(`(() => {
    const form = document.querySelector('.network-plan');
    const saved = 'https://portal.example.test';
    form.elements.portalOrigin.value = saved;
    return form.elements.portalOrigin.value === saved && form.elements.portalOrigin.getBoundingClientRect().width > 180
      && [...form.querySelectorAll('input:not([type=hidden])')].every(input => input.getBoundingClientRect().right <= innerWidth);
  })()`);
  if (!responsive) throw new Error('Network fields do not fit the narrow viewport');
  await evaluate(`(() => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: async value => { window.testCopiedUrl = value; } } });
    document.querySelector('[data-helper-download]').click();
  })()`);
  const copyOk = await evaluate(`window.testCopiedUrl === location.origin + '/downloads/presenter-helper?handler=Zip'`);
  if (!copyOk) throw new Error('Configuration copy action did not send the canonical HTTPS URL to the clipboard');
  // No cookies, antiforgery tokens, form values or names in diagnostic output.
  console.log(JSON.stringify({ postedOrigin, postStatus, postPath, contentType, eventPage, responsive, copyOk }));
  }
  await command('Browser.close').catch(error => { if (!/Browser connection closed/.test(error.message)) throw error; });
} finally {
  socket?.close();
  if (browser.exitCode === null) {
    await Promise.race([new Promise(r => browser.once('exit', r)), new Promise(r => setTimeout(r, 3000))]);
    if (browser.exitCode === null) browser.kill();
  }
  const relativeProfile = path.relative(path.resolve(config.temporaryRoot), path.resolve(profile));
  if (!relativeProfile || relativeProfile.startsWith('..') || path.isAbsolute(relativeProfile)) throw new Error('Unsafe fixture profile cleanup');
  await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
}

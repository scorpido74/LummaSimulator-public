const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
// Real Chromium, fresh profile, synthetic HTTP loopback fixture only; never opens the portal.
// This exercises browser Origin serialization, not ASP.NET authorization or production TLS.
test('native monitor form keeps exact Origin; no-referrer reproduces null while fetch preserves Origin', { timeout: 45000 }, async t => {
  const { createServer } = require('node:http');
  const { spawn } = require('node:child_process');
  const { once } = require('node:events');
  const browser = process.env.LUMMA_TEST_BROWSER || path.join(process.env.ProgramFiles || '', 'Google/Chrome/Application/chrome.exe');
  if (!fs.existsSync(browser)) { t.skip('Chrome unavailable; set LUMMA_TEST_BROWSER'); return; }
  const pageSource = fs.readFileSync(path.join(root, 'LummaSimulator.Web/Pages/Presenter/Integrations/Monitor.cshtml.cs'), 'utf8');
  const policy = pageSource.match(/Response.Headers\["Referrer-Policy"\]\s*=\s*"([^"]+)"/)?.[1];
  assert.equal(policy, 'same-origin');
  const observations = [];
  let origin;
  const server = createServer(async (req, res) => {
    const url = new URL(req.url, origin);
    if (req.method === 'POST') {
      for await (const chunk of req) { /* Only synthetic form content; never output it. */ }
      observations.push({ test: url.searchParams.get('case'), origin: req.headers.origin, site: req.headers['sec-fetch-site'] });
      res.end('<p>synthetic confirmation</p>'); return;
    }
    if (url.pathname === '/submit.js') {
      res.setHeader('Content-Type', 'text/javascript');
      res.end(`const f=document.querySelector('form'); if(location.search.includes('fetch')) { fetch(f.action,{method:'POST',body:new FormData(f)}).then(()=>document.body.textContent='done'); } else { f.requestSubmit(); }`); return;
    }
    res.setHeader('Content-Type', 'text/html');
    res.setHeader('Referrer-Policy', url.searchParams.get('case') === 'fixed' ? policy : 'no-referrer');
    res.setHeader('Cache-Control', 'no-store');
    res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'");
    res.end(`<form method="post" action="/change?case=${url.searchParams.get('case')}"><input name="operation" value="generate"><input name="confirmed" value="true"><input name="revision" value="0"><input name="__RequestVerificationToken" value="synthetic"></form><script src="/submit.js" defer></script>`);
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  origin = `http://127.0.0.1:${server.address().port}`;
  const profiles = path.join(root, 'artifacts'); fs.mkdirSync(profiles, { recursive: true });
  try {
    for (const name of ['original', 'fetch', 'fixed']) {
      const profile = fs.mkdtempSync(path.join(profiles, 'monitor-origin-browser-'));
      const environment = Object.fromEntries(Object.entries(process.env).filter(([key]) => !/^(LUMMA_MONITOR_KEY|Monitor__KeyHash|LUMMA_PORTAL_STOP_FILE)$/i.test(key)));
      const child = spawn(browser, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--disable-background-networking', '--no-proxy-server', '--remote-debugging-port=0', '--user-data-dir=' + profile, '--dump-dom', '--virtual-time-budget=2000', origin + '/form?case=' + name], { windowsHide: true, stdio: 'ignore', env: environment });
      try {
        const closed = once(child, 'close');
        const timer = setTimeout(() => child.kill(), 10000); // Only this retained synthetic browser child.
        let code; try { [code] = await closed; } finally { clearTimeout(timer); }
        assert.equal(code, 0, 'isolated headless browser exits normally');
      } finally { if (child.exitCode === null && child.signalCode === null) child.kill(); }
    }
    assert.deepEqual(observations, [
      { test: 'original', origin: 'null', site: 'same-origin' },
      { test: 'fetch', origin, site: 'same-origin' },
      { test: 'fixed', origin, site: 'same-origin' }
    ]);
  } finally { await new Promise(resolve => server.close(resolve)); }
});

// Static Razor-source previews only: no portal, database, authentication or endpoint activity.
// Run: node tests/WebAuthn.Ux.Tests/responsive-browser.mjs <new-evidence-directory> [--visitor-title]
import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn } from 'node:child_process';
const root = process.cwd();
const evidence = path.resolve(process.argv[2] || '');
if (!process.argv[2] || !evidence.startsWith(path.join(root,'artifacts') + path.sep)) throw new Error('Provide a new evidence directory under artifacts.');
await fs.mkdir(evidence); // Refuse overwriting existing evidence.
const profile = path.join(evidence,'browser-profile');
const browser = spawn(process.env.LUMMA_TEST_BROWSER || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    ['--headless=new','--no-first-run','--disable-extensions','--disable-background-networking','--no-proxy-server',
        '--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'], {windowsHide:true,stdio:'ignore'});
let launchError;
browser.on('error', e => { launchError=e; });
let socket;
try {
    let port;
    for(let i=0;i<80;i++) {
        if(launchError) throw launchError;
        try {port=Number((await fs.readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);break;} catch {}
        await new Promise(r=>setTimeout(r,100));
    }
    if(!port) throw new Error('Isolated preview browser unavailable');
    const targets=await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
    await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
    let sequence=0;const pending=new Map();
    socket.onmessage=({data})=>{const m=JSON.parse(data);if(!m.id)return;const p=pending.get(m.id);if(!p)return;pending.delete(m.id);clearTimeout(p.timeout);m.error?p.reject(new Error(m.error.message)):p.resolve(m.result);};
    function command(method,params={}) {return new Promise((resolve,reject)=>{const id=++sequence;const timeout=setTimeout(()=>{pending.delete(id);reject(new Error('CDP timeout: '+method));},5000);pending.set(id,{resolve,reject,timeout});socket.send(JSON.stringify({id,method,params}));});}
    async function evaluate(expression) {const result=await command('Runtime.evaluate',{expression,returnByValue:true});if(result.exceptionDetails)throw new Error('Preview script failed: '+result.exceptionDetails.text);return result.result.value;}
    await command('Page.enable');
    await command('Runtime.enable');
    const css=await fs.readFile('LummaSimulator.Web/wwwroot/css/site.css','utf8')+'\n'+await fs.readFile('LummaSimulator.Web/wwwroot/css/clickfix.css','utf8');
    const script=await fs.readFile('LummaSimulator.Web/wwwroot/js/clickfix.js','utf8');
    const layout=await fs.readFile('LummaSimulator.Web/Pages/Shared/_Layout.cshtml','utf8');
    const unescape=s=>s.replaceAll('&amp;','&').replaceAll('&lt;','<').replaceAll('&gt;','>').replaceAll('&quot;','"');
    let checks=0;
    for(const language of ['nl-NL','en-GB']) {
        const xml=await fs.readFile(`LummaSimulator.Web/Resources/Localization/UiText.${language}.resx`,'utf8');
        const catalog=Object.fromEntries([...xml.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)].map(m=>[unescape(m[1]),unescape(m[2])]));
        const translate=s=>s.replace(/<local-text key="([^"]+)"><\/local-text>/g,(_,key)=>catalog[key]||key);
        let header=layout.match(/<header[\s\S]*?<\/header>/)[0];
        header=header.replace(/@foreach[\s\S]*?\}\s*<span id="language-notice"/, '<button type="button">NL</button><button type="button">EN</button><span id="language-notice"');
        header=header.replaceAll('@Html.AntiForgeryToken()','');
        for (const match of [...header.matchAll(/src="~\/([^\"]+)"/g)]) {
            const image = await fs.readFile(path.join('LummaSimulator.Web/wwwroot',match[1]));
            header=header.replace(match[0],`src="data:image/svg+xml;base64,${image.toString('base64')}"`);
        }
        const pages=process.argv.includes('--visitor-title')?['Index']:['Index','Demo','Complete','SignedIn','ClickFixComplete'];
        for(const page of pages) {
            let body=await fs.readFile(`LummaSimulator.Web/Pages/${page}.cshtml`,'utf8');
            if(page==='Index')body=body.match(/<section class="hero">[\s\S]*?<\/section>/)[0];
            if(page==='Demo')body=body.replace(/<h1 id="demo-welcome"[^>]*>[\s\S]*?<\/h1>/,'<h1><local-text key="WelcomeStandalone"></local-text></h1>');
            body=body.replace('<partial name="_ClickFixExercise" />',await fs.readFile('LummaSimulator.Web/Pages/Shared/_ClickFixExercise.cshtml','utf8'));
            body=body.replace(/^@.*$/gm,'').replace(/@section Scripts[^\n]*/g,'').replace(/<link[^>]*>/g,'').replace(/@Html.AntiForgeryToken\(\)/g,'').replace(/data-attempt="[^"]*"/,'data-attempt=""');
            body=body.replace(/<local-text key="@[^\n]+?><\/local-text>/g,'<span>Demo</span>');
            body=body.replaceAll('@Model.Account.Name','Visitor-SYNTHETIC1234567890').replace(/disabled="@\([^\n]*?\)"/g,'');
            const html=`<!doctype html><html lang="${language}"><head><meta name="viewport" content="width=device-width, initial-scale=1"><style>${css}</style></head><body>${translate(header)}<main id="main" class="page-shell">${translate(body)}</main><template id="localization-data">${JSON.stringify({catalogs:{[language]:catalog}})}</template></body></html>`;
            const {frameTree}=await command('Page.getFrameTree');
            await command('Page.setDocumentContent',{frameId:frameTree.frame.id,html});
            // Static visual states only: never execute timers, completion or authenticated endpoints.
            const states=page==='SignedIn'?['complete','warning','run']:page==='Demo'?['register','authenticated']:['complete'];
            for(const state of states) {
                if(page==='SignedIn' && state!=='complete') await evaluate(`document.getElementById('clickfix-exercise').hidden=false;document.getElementById('clickfix-warning').hidden=${state!=='warning'};document.getElementById('clickfix-run').hidden=${state==='warning'}`);
                if(page==='Demo') await evaluate("document.getElementById('auth-confirmation').hidden="+(state!=='authenticated')+";document.getElementById('passkey-options').hidden="+(state==='authenticated'));




                for(const width of (page==='Index'?[320,360,390,1280]:[320,375,390,768,1024,1920])) for(const zoom of (page==='Index'?[1]:[1,2])) {
                    // CSS reflow equivalent only. This does NOT drive browser UI zoom or Windows DPI.
                    const viewport=Math.max(160,Math.round(width/zoom));
                    await command('Emulation.setDeviceMetricsOverride',{width:viewport,height:844,deviceScaleFactor:1,mobile:false});
                    const result=await evaluate(`({width:innerWidth,scroll:document.documentElement.scrollWidth,smallTargets:[...document.querySelectorAll('button,.button')].filter(e=>e.getClientRects().length && getComputedStyle(e).visibility!=='hidden').filter(e=>e.getBoundingClientRect().height<44).map(e=>e.id||e.textContent.trim())})`);
                    if(result.scroll>result.width+1 || result.smallTargets.length) throw new Error(JSON.stringify({page,state,language,width,zoom,...result}));
                    if(page==='Index'){
                        const title=await evaluate(`(()=>{const h=document.querySelector('h1');const r=document.createRange();r.selectNodeContents(h);const box=h.getBoundingClientRect();const next=document.querySelector('.hero-subtitle').getBoundingClientRect().top;const lines=[...r.getClientRects()];return {text:h.textContent.trim(),one:document.querySelectorAll('h1').length===1,fits:lines.every(line=>line.left>=box.left-1&&line.right<=box.right+1),clear:lines.every(line=>line.bottom<=next),box:box.toJSON(),lines:lines.map(line=>line.toJSON())};})()`);
                        const expected=language==='nl-NL'?'LummaSimulator: De kracht van passkeys':'LummaSimulator: The power of passkeys';
                        if(title.text!==expected||!title.one||!title.fits||!title.clear)throw new Error(JSON.stringify({page,language,width,title}));
                        console.log(`PASS: visitor heading ${language} ${width}px`);
                    }
                    checks++;
                    if((page==='Index'||language==='nl-NL'&&[320,768,1920].includes(width)) && zoom===1) {
                        const shot=await command('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
                        await fs.writeFile(path.join(evidence,`${page}-${state}-${language}-${width}.png`),Buffer.from(shot.data,'base64'));
                    }
                }
            }
        }
    }
    console.log(`PASS: ${checks} static-source browser layout cases; widths 320/375/390/768/1024/1920, NL/EN, 100% and 200%-equivalent CSS reflow. No actual browser zoom, Windows DPI, hardware or authenticated portal tested.`);
    await command('Browser.close').catch(()=>{});
} finally {
    socket?.close();
    if(browser.exitCode===null && !launchError) {await Promise.race([new Promise(r=>browser.once('exit',r)),new Promise(r=>setTimeout(r,2000))]);if(browser.exitCode===null)browser.kill();}
}

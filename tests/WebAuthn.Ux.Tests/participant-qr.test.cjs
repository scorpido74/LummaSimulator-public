'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../LummaSimulator.Web/wwwroot/js/participant-qr.js'),'utf8');
class Element{
  constructor(){this.dataset={};this.hidden=false;this.disabled=false;this.textContent='';this.attributes={};this.events={};}
  addEventListener(name,handler){this.events[name]=handler;}
  removeAttribute(name){delete this.attributes[name];}
  getAttribute(name){return this.attributes[name];}
  set src(value){this.attributes.src=value;}get src(){return this.attributes.src;}
  set href(value){this.attributes.href=value;}get href(){return this.attributes.href;}
}
async function flush(){for(let i=0;i<16;i++)await Promise.resolve();}
function harness(kind="prize"){
 const nodes=Object.fromEntries(['participant-qr','image','link','copy','status','name','local'].map(name=>[name==='participant-qr'?name:'participant-qr-'+name,new Element()]));
 const root=nodes['participant-qr'];root.hidden=true;root.dataset={eventKind:kind,eventId:'00000000-0000-0000-0000-000000000401',stateUrl:'/state',qrUrl:'/qr'};
 const url='https://portal.example.test/Index?eventKind='+kind+'&eventId='+root.dataset.eventId;
 nodes['participant-qr-link'].href=url;nodes['participant-qr-image'].src='/qr';
 const config={ok:true,entry:{eventId:root.dataset.eventId,eventName:'Synthetic event',url,laptopOnly:false}};
 const events={},documentEvents={},copied=[],requests=[];let poll;
 const context=vm.createContext({document:{hidden:false,getElementById:id=>nodes[id],addEventListener:(name,handler)=>documentEvents[name]=handler},
 window:{LummaI18n:{t:key=>key},addEventListener:(name,handler)=>events[name]=handler},
 navigator:{clipboard:{writeText:async value=>copied.push(value)}},URL,AbortController,setTimeout,clearTimeout,setInterval:handler=>{poll=handler;},
 fetch:async(url,options)=>{requests.push({url,options});if(config.pending)return await new Promise(resolve=>{config.resolve=resolve;});return {ok:config.ok,json:async()=>config.entry};}});
 vm.runInContext(source,context);
 return {nodes,root,url,config,events,documentEvents,copied,requests,context,poll:()=>poll(),attach:()=>vm.runInContext(source,context)};
}
test('dashboard QR preserves the exact public link and copies only on explicit click',async()=>{
 const h=harness();await flush();assert.equal(h.nodes['participant-qr-name'].textContent,'Synthetic event');assert.equal(h.root.hidden,false);
 assert.equal(h.copied.length,0);await h.nodes['participant-qr-copy'].events.click();assert.deepEqual(h.copied,[h.url]);
 assert.equal(h.requests[0].options.credentials,'same-origin');h.attach();assert.equal(h.requests.length,1);
});
test('closing event, lost authorization and failed requests remove QR, name and copyable link',async()=>{
 const h=harness();await flush();h.config.ok=false;h.poll();await flush();
 assert.equal(h.root.hidden,true);assert.equal(h.nodes['participant-qr-image'].src,undefined);assert.equal(h.nodes['participant-qr-link'].href,undefined);
 assert.equal(h.nodes['participant-qr-name'].textContent,'');assert.equal(h.nodes['participant-qr-copy'].disabled,true);
 await h.nodes['participant-qr-copy'].events.click();assert.equal(h.copied.length,0);
});
test('mismatched event payload cannot replace the selected event invitation',async()=>{
 const h=harness();await flush();h.config.entry={...h.config.entry,eventId:'00000000-0000-0000-0000-000000000402'};h.poll();await flush();assert.equal(h.root.hidden,true);
});
test('laptop-only warning follows server URL classification',async()=>{
 const h=harness();await flush();assert.equal(h.nodes['participant-qr-local'].hidden,true);
 h.config.entry={...h.config.entry,url:h.url.replace('portal.example.test','localhost:7243'),laptopOnly:true};h.poll();await flush();
 assert.equal(h.nodes['participant-qr-local'].hidden,false);assert.match(h.nodes['participant-qr-link'].href,/localhost/);
});
test('navigation clears the QR and an old in-flight response cannot restore it',async()=>{
 const h=harness();await flush();h.config.pending=true;h.poll();await flush();h.events.pagehide();
 h.config.resolve({ok:true,json:async()=>h.config.entry});await flush();assert.equal(h.root.hidden,true);assert.equal(h.nodes['participant-qr-copy'].disabled,true);
});
test('QR invitation and laptop-only warning are localized in Dutch and English',()=>{
 for(const language of ['nl-NL','en-GB']){
   const xml=fs.readFileSync(path.join(__dirname,`../../LummaSimulator.Web/Resources/Localization/UiText.${language}.resx`),'utf8');
   for(const key of ['ParticipantQrAlt','ParticipantQrScan','ParticipantQrCopy','ParticipantQrCopied','ParticipantQrCopyFailed','ParticipantQrLocal'])assert.match(xml,new RegExp(`name="${key}"[^>]*><value>[^<]+</value>`));
   assert.match(xml,/<data name="ParticipantQrLocal"[^>]*><value>[^<]*localhost/);
 }
});

test('non-Prize event card keeps its own route and refuses a different kind',async()=>{
 const h=harness('experience');await flush();assert.equal(h.root.hidden,false);assert.equal(h.nodes['participant-qr-link'].href,h.url);
 h.config.entry={...h.config.entry,url:h.url.replace('eventKind=experience','eventKind=prize')};h.poll();await flush();assert.equal(h.root.hidden,true);
});

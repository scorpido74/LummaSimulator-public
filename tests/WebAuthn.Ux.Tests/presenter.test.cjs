const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const script=fs.readFileSync(path.join(__dirname,'../../LummaSimulator.Web/wwwroot/js/presenter.js'),'utf8');
function harness(kind='login',config={}) {
    const queried=[];
    const inputs={ keyName:{value:'Reserve'},code:{value:kind==='enroll'?'test-only-code':''} };
    const button={disabled:false};
    const onboardingPath=config.visitor?'/Presenter/Events?handler=VisitorDefault':'/Presenter/Onboarding';
    const form={action:{value:'finish'},getAttribute:()=>onboardingPath,hasAttribute:name=>name==='data-onboarding-submit'?!!config.onboarding:name==='data-visitor-default-submit'&&!!config.visitor,dataset:{presenterAction:kind,useInvitation:String(kind==='enroll'),bootstrap:String(!!config.bootstrap),appReturnUrl:config.appReturnUrl},querySelectorAll:()=>[button,...Object.values(inputs)],
        querySelector:selector=>{queried.push(selector);return inputs[selector.match(/name="([^"]+)"/)?.[1]];},addEventListener:(_,f)=>form.submit=f};
    const status={dataset:{},textContent:''};const cancel={hidden:true,addEventListener:(_,f)=>cancel.click=f};
    const calls=[],signals=[],events={},locks=[],browserOptions=[];let browserCalls=0;let resolveBrowser;let submissions=0;
    const bytes=()=>new Uint8Array([1,2,3]).buffer;
    const credential={id:'AQID',rawId:bytes(),type:'public-key',getClientExtensionResults:()=>({}),response:{clientDataJSON:bytes(),attestationObject:bytes(),authenticatorData:bytes(),signature:bytes(),userHandle:bytes()}};
    async function browser(options) {
        browserCalls++;signals.push(options.signal);browserOptions.push(options.publicKey);
        if(config.pending)return new Promise((resolve,reject)=>{resolveBrowser=()=>resolve(credential);options.signal.addEventListener('abort',()=>reject(new DOMException('Aborted','AbortError')),{once:true});});
        if(config.expired)throw new DOMException('Timed out','NotAllowedError');
        return credential;
    }
    const redirects=[];
    const window={LummaI18n:{t:key=>'translated:'+key},addEventListener:(name,f)=>events[name]=f,location:{pathname:config.currentPath,search:'',assign:href=>{redirects.push(href);events.pagehide?.();},reload:()=>redirects.push('reload')}};
    vm.runInNewContext(script,{document:{querySelectorAll:()=>[form],getElementById:id=>config.visitor&&id.startsWith('presenter-')?null:id.endsWith('status')?status:cancel,querySelector:()=>({value:'test-csrf'})},
        window,navigator:{credentials:{get:browser,create:browser},locks:{request:async(name,__,f)=>{locks.push(name);return f(config.otherTab?null:{});}}},crypto:{randomUUID},AbortController,AbortSignal,DOMException,atob,btoa,
        FormData:class { constructor(){this.values=config.visitor?{selection:inputs.keyName.value,useAsDefault:'true'}:{action:'finish',confirmed:'true',name:inputs.keyName.value};} },setTimeout,clearTimeout,fetch:async(url,options)=>{
            calls.push({url,options});
            if(url===onboardingPath){
                submissions++;
                if(config.invalid)return {status:400,headers:{get:()=> 'application/json'},json:async()=>({errorKey:'VisitorDefaultConfirm'})};
                if(!config.fresh&&(submissions===1||config.expiredAfterPrompt))return {status:409,headers:{get:()=> 'application/json'},json:async()=>({errorKey:'PresenterReauthenticate'})};
                if(config.visitor)return {ok:true,status:200,headers:{get:()=> 'application/json'},json:async()=>({saved:true,redirectUrl:'/Presenter/Events#visitor-default'})};
                return {status:200,redirected:true,url:'/Presenter/Events'};
            }
            let result=url.endsWith('/options')?{challenge:'AQID',user:{id:'AQID'},allowCredentials:kind==='login'?[]:[{id:'AQID'}],userVerification:'required',excludeCredentials:[]}:
                url.endsWith('/verify')?{verified:!config.reject,kind,redirect:kind==='login'?'/Presenter':null}:{};
            if(config.reject&&url.endsWith('/verify'))result={errorKey:'PresenterDenied'};
            return {ok:!(config.reject&&url.endsWith('/verify')),status:200,json:async()=>result};
        }});
    return {queried,browserOptions,submit:()=>form.submit({preventDefault(){}}),calls,signals,redirects,status,cancel,button,inputs,config,events,locks,finish:()=>resolveBrowser(),count:()=>browserCalls};
}
async function until(f){for(let i=0;i<40;i++){if(f())return;await new Promise(r=>setImmediate(r));}throw new Error('Expected asynchronous state');}

test('visitor default reuses the original selection once and blocks double-click during verification and navigation',async()=>{
    const h=harness('reauth',{visitor:true,pending:true});
    const submitting=h.submit();await until(()=>h.count()===1);await h.submit();h.finish();await submitting;await h.submit();
    assert.deepEqual(h.calls.map(c=>c.url),['/Presenter/Events?handler=VisitorDefault','/api/presenter/reauth/options','/api/presenter/reauth/verify','/Presenter/Events?handler=VisitorDefault']);
    assert.equal(h.calls[0].options.headers['X-Lumma-Visitor-Default'],'1');
    assert.equal(h.calls[0].options.body,h.calls[3].options.body);
    assert.equal(h.calls[3].options.body.values.useAsDefault,'true');
    assert.deepEqual(h.redirects,['/Presenter/Events#visitor-default']);
    const fresh=harness('reauth',{visitor:true,fresh:true});await fresh.submit();await fresh.submit();
    assert.equal(fresh.count(),0);assert.equal(fresh.calls.length,1);
    const samePage=harness('reauth',{visitor:true,fresh:true,currentPath:'/Presenter/Events'});await samePage.submit();
    assert.deepEqual(samePage.redirects,['reload']);
});

test('visitor default preserves form values when invalid, cancelled, rejected or expired',async()=>{
    const cancelled=harness('reauth',{visitor:true,pending:true});const pending=cancelled.submit();
    await until(()=>cancelled.count()===1);cancelled.cancel.click();await pending;
    assert.equal(cancelled.status.dataset.i18n,'OnboardingVerificationCancelled');
    for(const run of [cancelled,...['invalid','reject','expired','expiredAfterPrompt'].map(key=>harness('reauth',{visitor:true,[key]:true}))]){
        if(run!==cancelled)await run.submit();
        assert.equal(run.calls.filter(c=>c.url.includes('?handler=VisitorDefault')).length,run.config.expiredAfterPrompt?2:1);
        assert.equal(run.inputs.keyName.value,'Reserve');
        assert.equal(run.calls[0].options.body.values.useAsDefault,'true');
        assert.equal(run.button.disabled,false);
        assert.deepEqual(run.redirects,[]);
        if(run.config.invalid)assert.equal(run.count(),0);
        if(run.config.expiredAfterPrompt)assert.equal(run.status.dataset.i18n,'VisitorDefaultRetry');
    }
});

test('onboarding reuses submitted values once after verified passkey and skips a redundant prompt',async()=>{
    const h=harness('reauth',{onboarding:true,pending:true});
    const submit=h.submit();await until(()=>h.count()===1);await h.submit();h.finish();await submit;
    assert.deepEqual(h.calls.map(call=>call.url),['/Presenter/Onboarding','/api/presenter/reauth/options','/api/presenter/reauth/verify','/Presenter/Onboarding']);
    assert.equal(h.calls[0].options.body,h.calls[3].options.body);
    assert.equal(h.calls[3].options.body.values.confirmed,'true');
    assert.deepEqual(h.redirects,['/Presenter/Events']);
    const ready=harness('reauth',{onboarding:true,fresh:true});await ready.submit();
    assert.equal(ready.count(),0);assert.equal(ready.calls.length,1);
});

test('cancelled and expired onboarding proof never resubmits and leaves fields intact',async()=>{
    const cancelled=harness('reauth',{onboarding:true,pending:true});
    const pending=cancelled.submit();await until(()=>cancelled.count()===1);cancelled.cancel.click();await pending;
    const expired=harness('reauth',{onboarding:true,expired:true});await expired.submit();
    for(const run of [cancelled,expired]){
        assert.equal(run.calls.filter(call=>call.url==='/Presenter/Onboarding').length,1);
        assert.equal(run.status.dataset.i18n,'OnboardingVerificationCancelled');
        assert.equal(run.inputs.keyName.value,'Reserve');assert.equal(run.button.disabled,false);
        assert.deepEqual(run.redirects,[]);
    }
});

test('presenter login verifies before navigation; pagehide after success sends no cancel',async()=>{
    const h=harness();await h.submit();
    assert.deepEqual(h.calls.map(c=>c.url),['/api/presenter/login/options','/api/presenter/login/verify']);
    assert.deepEqual(h.redirects,['/Presenter']);assert.equal(h.signals[0].aborted,true);
    assert.equal(h.calls[0].options.headers['X-Lumma-Operation'],h.calls[1].options.headers['X-Lumma-Operation']);
    assert.equal(h.calls[0].options.headers['X-CSRF-TOKEN'],'test-csrf');
});
test('verified presenter login returns only to a fixed app approval route with one UUID',async()=>{
    const appReturnUrl='/Presenter/AppConnection?requestId='+randomUUID();
    const h=harness('login',{appReturnUrl});await h.submit();assert.deepEqual(h.redirects,[appReturnUrl]);
    for(const value of ['https://other.invalid/Presenter/AppConnection','//other.invalid','/Presenter/AppConnection?requestId='+randomUUID()+'&next=elsewhere','/Presenter/AppConnection?requestId=invalid']) {
        const denied=harness('login',{appReturnUrl:value});await denied.submit();assert.deepEqual(denied.redirects,['/Presenter']);
    }
    const failed=harness('login',{appReturnUrl,reject:true});await failed.submit();assert.equal(failed.redirects.length,0);
});
test('helper approval reuses verified presenter login and rejects arbitrary callback destinations',async()=>{
    const appReturnUrl='/Presenter/HelperConnection?requestId='+randomUUID();
    const h=harness('login',{appReturnUrl});await h.submit();assert.deepEqual(h.redirects,[appReturnUrl]);
    for(const value of ['/Presenter/HelperConnection?requestId=invalid','/Presenter/HelperConnection?requestId='+randomUUID()+'&next=https://other.invalid','https://other.invalid/Presenter/HelperConnection']) {
        const denied=harness('login',{appReturnUrl:value});await denied.submit();assert.deepEqual(denied.redirects,['/Presenter']);
    }
});
test('reauthentication can repeat in the same page with new operation identities',async()=>{
    const h=harness('reauth');await h.submit();await h.submit();assert.equal(h.count(),2);
    assert.notEqual(h.calls[0].options.headers['X-Lumma-Operation'],h.calls[2].options.headers['X-Lumma-Operation']);
    assert.equal(h.redirects.length,0);assert.equal(h.status.dataset.i18n,'PresenterReauthenticated');
});
test('invitation enrollment consumes its code before WebAuthn and uses explicit invitation mode',async()=>{
    const h=harness('enroll');await h.submit();assert.equal(h.calls[0].url,'/api/presenter/enrollment/open');
    assert.equal(h.inputs.code.value,'');assert.equal(h.inputs.code.required,false);
    assert.equal(JSON.parse(h.calls[1].options.body).useInvitation,true);
    assert.equal(h.calls.some(c=>c.url.endsWith('/cancel')),false);
});
test('cancel pending ceremony then retry; no concurrent presenter requests or automatic retries',async()=>{
    const h=harness('reauth',{pending:true});const first=h.submit();await until(()=>h.count()===1);
    await h.submit();assert.equal(h.count(),1);assert.equal(h.button.disabled,true);
    h.cancel.click();await first;assert.equal(h.calls.at(-1).url,'/api/presenter/reauth/cancel');
    assert.equal(h.calls.at(-1).options.signal.aborted,false);assert.equal(h.button.disabled,false);
    h.config.pending=false;await h.submit();assert.equal(h.count(),2);
});
test('server rejection never navigates and only releases its own operation',async()=>{
    const h=harness('login',{reject:true});await h.submit();assert.equal(h.redirects.length,0);
    assert.equal(h.status.dataset.i18n,'PresenterDenied');assert.equal(h.calls.at(-1).options.headers['X-Lumma-Operation'],h.calls[0].options.headers['X-Lumma-Operation']);
});
test('presenter uses the existing visitor Web Lock; an active visitor ceremony prevents a second native request',async()=>{
    const h=harness('login',{otherTab:true});await h.submit();
    assert.deepEqual(h.locks,['lumma-passkey-operation']);assert.equal(h.calls.length,0);assert.equal(h.count(),0);
    assert.equal(h.status.dataset.i18n,'PresenterPending');assert.equal(h.button.disabled,false);
});

test('ordinary presenter login never reads or sends a username or account identity',async()=>{
    const h=harness();await h.submit();
    assert.equal(h.queried.some(selector=>/name=["'](?:login|username|account|email)["']/i.test(selector)),false);
    for(const call of h.calls)for(const field of ['login','username','account','email'])assert.equal(field in JSON.parse(call.options.body),false);
    assert.equal(h.calls.some(c=>c.url.includes('/setup/')||c.url.includes('/enrollment/')||c.url.includes('/enroll/')),false);
});
test('ordinary presenter login passes an empty credential allowlist to WebAuthn',async()=>{
    const h=harness();await h.submit();
    assert.equal(h.browserOptions[0].allowCredentials.length,0);
    for(const field of ['authenticatorAttachment','hints','transports'])assert.equal(field in h.browserOptions[0],false);
});
test('ordinary presenter login preserves mandatory user verification',async()=>{
    const h=harness();await h.submit();assert.equal(h.browserOptions[0].userVerification,'required');
});
test('Razor login has no account/password fields and remains separate from setup identity creation',()=>{
    const login=fs.readFileSync(path.join(__dirname,'../../LummaSimulator.Web/Pages/Presenter/Login.cshtml'),'utf8');
    const setup=fs.readFileSync(path.join(__dirname,'../../LummaSimulator.Web/Pages/Presenter/Setup.cshtml'),'utf8');
    assert.match(login,/data-presenter-action="login"/);
    assert.doesNotMatch(login,/<input\b[^>]*\bname\s*=\s*["'](?:login|username|account|email|password)["']/i);
    assert.doesNotMatch(login,/<input\b[^>]*\btype\s*=\s*["']password["']/i);
    assert.doesNotMatch(login,/data-presenter-action="enroll"|asp-page-handler="Begin"/);
    assert.match(setup,/name="login"/);assert.match(setup,/asp-page-handler="Begin"/);assert.match(setup,/data-presenter-action="enroll"/);
    assert.doesNotMatch(setup,/data-presenter-action="login"/);
});
test('reauthentication keeps the account-bound allowlist from the server',async()=>{
    const h=harness('reauth');await h.submit();
    assert.equal(h.browserOptions[0].allowCredentials.length,1);
    assert.deepEqual(Array.from(h.browserOptions[0].allowCredentials[0].id),[1,2,3]);
    assert.equal(h.browserOptions[0].userVerification,'required');
});

 test('setup consumes only the local bootstrap endpoint and keeps registration separate from login',async()=>{
    const h=harness('enroll',{bootstrap:true});await h.submit();
    assert.deepEqual(h.calls.map(c=>c.url),['/api/presenter/setup/open','/api/presenter/enroll/options','/api/presenter/enroll/verify']);
    assert.equal(h.inputs.code.value,'');
    assert.equal(h.calls.some(c=>c.url.includes('/login/')),false);
 });

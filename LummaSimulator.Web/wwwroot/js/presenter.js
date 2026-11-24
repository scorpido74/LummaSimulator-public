"use strict";
(() => {
    const forms = [...document.querySelectorAll("form[data-presenter-action]")];
    if (!forms.length) return;
    const sharedStatus = document.getElementById("presenter-status");
    const sharedCancel = document.getElementById("presenter-cancel");
    const visitorStatus = forms.some(form=>form.hasAttribute("data-visitor-default-submit"))?document.getElementById("visitor-default-status"):null;
    const visitorCancel = visitorStatus?document.getElementById("visitor-default-cancel"):null;
    const t = key => window.LummaI18n?.t(key) ?? key;
    const decode = value => Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
    const encode = value => btoa(String.fromCharCode(...new Uint8Array(value))).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/,"");
    let busy = false, active = null, navigating=false, status=sharedStatus, cancel=sharedCancel;
    function message(key) { status.dataset.i18n=key; status.textContent=t(key); }
    function render() { forms.forEach(form => form.querySelectorAll("button,input,select").forEach(node => node.disabled=busy)); if(cancel)cancel.hidden=!busy || (cancel===visitorCancel&&!active); }
    async function post(path, operation, data, signal) {
        const response = await fetch("/api/presenter/"+path, { method:"POST", credentials:"same-origin", cache:"no-store", signal,
            headers:{"Content-Type":"application/json","X-CSRF-TOKEN":document.querySelector('#language-preference-form input[name="__RequestVerificationToken"]').value,"X-Lumma-Operation":operation.id}, body:JSON.stringify(data) });
        if(response.status===429) throw new Error("Te veel aanvragen. Wacht een minuut en probeer opnieuw.");
        const result=await response.json(); if(!response.ok)throw new Error(result.errorKey || "PresenterDenied");return result;
    }
    function release(operation) { return operation.cleanup ??= post(operation.kind+"/cancel",operation,{},AbortSignal.timeout(5000)).catch(()=>null); }
    async function run(form) {
        const kind=form.dataset.presenterAction;
        const operation={id:crypto.randomUUID(),kind,controller:new AbortController()};active=operation;render();
        const timer=setTimeout(()=>operation.controller.abort(),125000);let verified=false;
        try {
            const useInvitation=form.dataset.useInvitation==="true";
            const code=form.querySelector('[name="code"]');
            const keyName=form.querySelector('[name="keyName"]')?.value || "";
            if(code?.value) { await post(form.dataset.bootstrap === "true" ? "setup/open" : "enrollment/open",operation,{code:code.value},operation.controller.signal);code.value="";code.required=false; }
            const options=await post(kind+"/options",operation,{useInvitation},operation.controller.signal);
            options.challenge=decode(options.challenge);
            let credential;
            if(kind==="enroll") { options.user.id=decode(options.user.id);options.excludeCredentials=(options.excludeCredentials||[]).map(c=>({...c,id:decode(c.id)}));credential=await navigator.credentials.create({publicKey:options,signal:operation.controller.signal}); }
            else { options.allowCredentials=(options.allowCredentials||[]).map(c=>({...c,id:decode(c.id)}));credential=await navigator.credentials.get({publicKey:options,signal:operation.controller.signal}); }
            if(!credential)throw new Error("PresenterDenied");
            const response={clientDataJSON:encode(credential.response.clientDataJSON)};
            if(kind==="enroll") { response.attestationObject=encode(credential.response.attestationObject);response.transports=credential.response.getTransports?.()||[]; }
            else { response.authenticatorData=encode(credential.response.authenticatorData);response.signature=encode(credential.response.signature);response.userHandle=credential.response.userHandle?encode(credential.response.userHandle):null; }
            const result=await post(kind+"/verify",operation,{keyName,useInvitation,response:{id:credential.id,rawId:encode(credential.rawId),type:credential.type,response,extensions:credential.getClientExtensionResults()}},operation.controller.signal);
            if(result.verified!==true || result.kind!==kind)throw new Error("PresenterDenied");verified=true;operation.done=true;
            message(kind==="reauth"?"PresenterReauthenticated":"PresenterVerified");
            const appReturn = form.dataset.appReturnUrl;
            if(kind === "login" && /^\/Presenter\/(?:AppConnection|HelperConnection)\?requestId=[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(appReturn || ""))window.location.assign(appReturn);
            else if(status!==visitorStatus && result.redirect && ["/Presenter","/Presenter/Events","/Presenter/Security","/Presenter/Login"].includes(result.redirect))window.location.assign(result.redirect);
            return true;
        } catch(error) {
            if((status===visitorStatus||form.hasAttribute?.("data-onboarding-submit"))&&(error.name==="AbortError"||error.name==="NotAllowedError"))
                message("OnboardingVerificationCancelled");
            else message(error.name==="AbortError"?"De aanvraag is geannuleerd. Je kunt opnieuw proberen.":/already pending|pending request/i.test(error.message||"")?"PresenterPending":error.message && t(error.message)!==error.message ? error.message:"PresenterDenied");
            return false;
        }
        finally { clearTimeout(timer);operation.controller.abort();if(!verified)await release(operation);if(active===operation)active=null;render(); }
    }
    async function submitProtected(form, data, visitor) {
        const retry=visitor?"VisitorDefaultRetry":"OnboardingRetry";
        async function submit() {
            const header=visitor?"X-Lumma-Visitor-Default":"X-Lumma-Onboarding";
            const response=await fetch(form.getAttribute("action"),{method:"POST",body:data,credentials:"same-origin",headers:{[header]:"1"}});
            if(response.redirected&&!visitor){window.location.assign(response.url);return null;}
            if(response.redirected||response.status===401||response.status===403)return visitor?"PresenterDenied":"OnboardingAdminRequired";
            if(response.status===429)return "PresenterPending";
            if(!response.headers.get("content-type")?.includes("application/json"))return retry;
            const result=await response.json();
            if(visitor&&response.ok&&result.saved===true){
                const page=result.redirectUrl.split("#",1)[0];
                if(page===window.location.pathname+window.location.search)window.location.reload();
                else window.location.assign(result.redirectUrl);
                navigating=true;return null;
            }
            return result.errorKey||retry;
        }
        try {
            let error=await submit();
            if(error==="PresenterReauthenticate") {
                message("OnboardingVerifyAction");
                if(!await run(form))return;
                error=await submit();
                if(visitor&&error==="PresenterReauthenticate")error="VisitorDefaultRetry";
            }
            if(error)message(error);
        } catch { message(retry); }
    }
    forms.forEach(form=>form.addEventListener("submit",async event=>{
        event.preventDefault();if(busy)return;
        const visitor=!!visitorStatus&&form.hasAttribute("data-visitor-default-submit");
        const protectedSave=visitor||form.hasAttribute?.("data-onboarding-submit");
        const data=protectedSave?new FormData(form):null;
        status=visitor?visitorStatus:sharedStatus;cancel=visitor?visitorCancel:sharedCancel;
        busy=true;render();message("PresenterWorking");
        const attempt=()=>protectedSave?submitProtected(form,data,visitor):run(form);
        try { if(navigator.locks) await navigator.locks.request("lumma-passkey-operation",{ifAvailable:true},async lock=>{if(lock)await attempt();else message("PresenterPending");});else await attempt(); }
        finally {if(!navigating){busy=false;render();}}
    }));
    for(const button of [sharedCancel,visitorCancel])button?.addEventListener("click",()=>active?.controller.abort());
    window.addEventListener("pagehide",()=>{
        forms.forEach(form=>{const code=form.querySelector('[name="code"]');if(code){code.value="";code.removeAttribute?.("value");}});
        const operation=active;if(operation && !operation.done){operation.controller.abort();void release(operation);}
    });
})();

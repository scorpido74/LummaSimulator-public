(() => {
    'use strict';
    const root=document.getElementById('clickfix-exercise');if(!root||root.dataset.attached==='true')return;root.dataset.attached='true';
    const byId=id=>document.getElementById('clickfix-'+id),button=document.getElementById('start-clickfix');
    const csrf=document.querySelector('#clickfix-antiforgery input[name="__RequestVerificationToken"]').value;
    const surface=document.getElementById('signed-in-surface'),green=document.getElementById('auth-confirmation');
    const t=key=>window.LummaI18n?.t(key)||key;
    let generation=0,starting=false,active=false,completing=false,durable=false,runState=null,copiedExample='';
    function label(id,key){const node=byId(id);node.replaceChildren();const span=document.createElement('span');span.dataset.i18n=key;span.textContent=t(key);node.append(span);}
    function markCompleted(){durable=true;root.dataset.completed='true';const key=button.dataset.startKey||'ExerciseStartNeutral';button.dataset.i18n=key;button.textContent=t(key);const debrief=document.getElementById('exercise-debrief');if(debrief)debrief.hidden=false;}
    function close(){
        generation++;active=false;starting=false;completing=false;root.hidden=true;
        copiedExample='';runState=null;root.dataset.attempt='';byId('command').value='';byId('paste').disabled=true;byId('ok').disabled=true;label('copy','ExerciseCopy');
        surface?.classList.toggle('exercise-open',false);if(green){green.inert=false;green.removeAttribute?.('aria-hidden');}
        if(durable)markCompleted();button.disabled=false;button.focus({preventScroll:true});
    }
    async function post(handler,body){const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),8000);try{
        const endpoint=handler==='BeginExercise'?'/SignedIn':'/ClickFix';const response=await fetch(endpoint+'?handler='+handler,{method:'POST',credentials:'same-origin',signal:controller.signal,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf},body:JSON.stringify(body)});
        if(response.status===401||response.status===403)window.dispatchEvent?.(new Event('lumma-authentication-expired'));
        if(!response.ok)throw new Error('Exercise request failed');return await response.json();
    }finally{clearTimeout(timeout);}}
    async function finish(state){
        if(!active||completing||state.finished||state.generation!==generation)return;completing=true;byId('retry').hidden=true;byId('warning').hidden=true;byId('run').hidden=true;byId('debrief').hidden=false;byId('debrief').focus({preventScroll:true});
        try{
            if(!state.replay){
                if(state.attempt&&!state.delivered){try{const result=await post('Choice',{attempt:state.attempt,actionType:'ClickFixFollowSimulated'});state.delivered=result.actionType==='ClickFixFollowSimulated'&&result.sequence===1;}catch{}}
                if(!active||state.generation!==generation)return;
                const result=await post('Complete',{});if(result.completed!==true)throw new Error('Completion not confirmed');
            }
            state.finished=true;markCompleted();if(state.generation===generation){byId('warning').hidden=true;byId('run').hidden=true;const debrief=byId('debrief');if(debrief){const stage=byId('stage');if(stage)stage.hidden=true;debrief.hidden=false;debrief.focus({preventScroll:true});}else close();}
        }catch{if(state.generation===generation){label('delivery','ClickFixDeliveryFailed');byId('retry').hidden=false;}}
        finally{if(state.generation===generation)completing=false;}
    }
    button.addEventListener('click',async()=>{
        if(active||starting)return;starting=true;button.disabled=true;const epoch=++generation;
        try{const receipt=await post('BeginExercise',{});if(epoch!==generation)return;if(typeof receipt.completed!=='boolean')throw new Error('Invalid exercise start');
            durable=durable||receipt.completed;runState={generation:epoch,replay:receipt.completed,attempt:receipt.completed?'':receipt.attempt||'',delivered:false};root.dataset.attempt=runState.attempt;
            const debrief=byId('debrief');if(debrief)debrief.hidden=true;const stage=byId('stage');if(stage)stage.hidden=false;byId('warning').hidden=false;byId('run').hidden=true;copiedExample='';byId('command').value='';byId('paste').disabled=true;label('copy','ExerciseCopy');byId('ok').disabled=true;byId('delivery').textContent='';byId('retry').hidden=true;
            root.hidden=false;active=true;surface?.classList.toggle('exercise-open',true);byId('close')?.focus({preventScroll:true});
        }catch{if(epoch===generation){document.getElementById('exercise-start-status').textContent=t('ExerciseStartFailed');button.disabled=false;}}
        finally{if(epoch===generation)starting=false;}
    });
    byId('copy').addEventListener('click',()=>{if(!active)return;copiedExample=root.dataset.exampleCommand;label('copy','ExerciseCopied');});
    byId('open').addEventListener('click',()=>{if(!active)return;byId('warning').hidden=true;byId('run').hidden=false;byId('command').value='';byId('ok').disabled=true;byId('paste').disabled=!copiedExample;byId('command').focus({preventScroll:true});});
    byId('paste').addEventListener('click',()=>{if(!active||!copiedExample||byId('run').hidden)return;byId('command').value=copiedExample;byId('ok').disabled=false;byId('ok').focus({preventScroll:true});});
    byId('run-close').addEventListener('click',close);
    byId('close')?.addEventListener('click',close);
    byId('back')?.addEventListener('click',close);
    byId('cancel')?.addEventListener('click',close);
    byId('ok')?.addEventListener('click',()=>{if(!byId('ok').disabled&&runState)void finish(runState);});
    byId('retry').addEventListener('click',()=>runState?finish(runState):undefined);
    window.addEventListener('keydown',event=>{if(event.key==='Escape'&&active){event.preventDefault();close();}});
    window.addEventListener('lumma-authentication-expired',close);
    window.addEventListener('pagehide',()=>{close();});
    if(typeof performance!=='undefined'&&performance.getEntriesByType('navigation')[0]?.type==='reload'&&(!green||!green.hidden))button.focus({preventScroll:true});
    window.addEventListener('lumma-language-changed',()=>{if(durable)markCompleted();});
})();

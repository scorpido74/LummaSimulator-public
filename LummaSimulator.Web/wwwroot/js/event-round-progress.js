(() => {
    'use strict';
    const select=document.getElementById('active-event');
    select?.addEventListener('change',()=>{document.getElementById('active-event-kind').value=select.selectedOptions[0]?.dataset.kind||'';select.form.requestSubmit();});
    document.getElementById('round-select')?.addEventListener('change',event=>event.target.form.requestSubmit());
    const root=document.getElementById('round-progress');if(!root)return;
    const byId=id=>document.getElementById('round-'+id),t=key=>window.LummaI18n?.t(key)||key;
    let stopped=false,busy=false,closed=root.dataset.status!=='Open';
    async function refresh(){
        if(stopped||closed||busy||document.hidden)return;busy=true;
        const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),3000);
        try{
            const response=await fetch(root.dataset.url,{credentials:'same-origin',cache:'no-store',signal:controller.signal});
            if(!response.ok)throw new Error();const data=await response.json();if(stopped)return;
            byId('occupied').textContent=`${data.round.occupied} / ${data.round.capacity}`;
            byId('completed').textContent=data.completed;byId('eligible').textContent=data.eligible;byId('zero').hidden=data.round.status!=='Open'||data.eligible!==0;
            const eligibleLabel=byId('eligible-label');if(eligibleLabel){eligibleLabel.dataset.i18n=data.prizesEnabled===false?'RoundActiveEntries':data.round.status==='Open'?'RoundEligible':'RoundRemainingActive';eligibleLabel.textContent=t(eligibleLabel.dataset.i18n);}
            const eligible=byId('confirm-eligible');if(eligible)eligible.textContent=data.eligible;
            const rows=byId('participants');if(rows){rows.replaceChildren();for(const person of data.participants||[]){const tr=document.createElement('tr');for(const value of [person.alias,t(person.registered?'Yes':'No'),t(person.signedIn?'Yes':'No'),t(person.completed?'Yes':'No'),t(person.status)+(person.prizeName?' · '+person.prizeName:'')+(person.reason?' · '+t(person.reason):'')]){const td=document.createElement('td');td.textContent=value;tr.append(td);}rows.append(tr);}}
            const price=byId('price-status');if(price){price.textContent=t(data.prizeReason);price.hidden=false;}
            const badge=byId('prizes-enabled');if(badge)badge.textContent=t(data.prizesEnabled?'EventPrizesOn':'EventPrizesOff');
            const config=byId('prize-config');if(config)config.textContent=data.prizeNames?.length?t(data.prizeSelection==='Fixed'?'PrizeFixed':'PrizeRandom')+': '+data.prizeNames.join(', '):t('RoundNoPrizeConfigured');
            const disabled=byId('draw-disabled');if(disabled)disabled.hidden=data.prizesEnabled!==false;
            const reason=byId('close-reason');if(reason){reason.hidden=data.round.status!=='Open'||data.closeReason===data.prizeReason;reason.textContent=t(data.closeReason);}
            const close=document.getElementById('close-round');if(close&&data.round.status==='Open'){close.disabled=!data.canClose;close.textContent=t(data.prizeAvailable&&data.eligible>0&&data.canClose?'RoundCloseDraw':'RoundCloseOnly');}
            const winners=byId('winners');winners.replaceChildren();
            for(const winner of data.round.winners){const p=document.createElement('p');p.textContent=winner.alias+' · '+winner.prizeName;winners.append(p);}
            if(data.round.status!=='Open'){
                closed=true;
                if(!data.round.winnerCount)winners.textContent=t('RoundNoWinner');
                const button=document.getElementById('close-round');if(button)button.disabled=true;
            }
            byId('progress-status').textContent='';
        }catch{byId('progress-status').textContent=t('RoundProgressFailed');}finally{clearTimeout(timeout);busy=false;}
    }
    document.getElementById('close-round-form')?.addEventListener('submit',()=>{document.getElementById('close-round').disabled=true;});
    window.addEventListener('pagehide',()=>{stopped=true;});window.addEventListener('pageshow',()=>{stopped=false;void refresh();});
    setInterval(refresh,4000);
})();

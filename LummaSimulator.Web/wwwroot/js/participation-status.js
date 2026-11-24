(() => {
    const link=document.getElementById('participation-link');if(!link)return;
    const card=document.getElementById('prize-round-status');let round=null;
    const keys=['VisitorPrizeWon','VisitorPrizeLost','VisitorPrizeNotEntered','VisitorPrizeUnknown','VisitorNoPrize','VisitorPrizesOff','VisitorPrizePending','VisitorCompleteToEnter'];
    const t=key=>window.LummaI18n?.t(key)||key;
    function render(value){
        if(!value||!card||card===link)return;round=value;
        card.hidden=false;card.classList.toggle('draw-performed',value.drawPerformed===true);
        const heading=document.getElementById('visitor-round-heading'),outcome=document.getElementById('visitor-round-outcome'),prize=document.getElementById('visitor-round-prize'),claim=document.getElementById('visitor-round-claim');
        heading.textContent=value.eventName+' · '+t('PrizeRoundLabel')+' '+value.number;
        const key=keys.includes(value.prizeMessageKey)?value.prizeMessageKey:'VisitorPrizeUnknown';outcome.dataset.i18n=key;outcome.textContent=t(key);
        prize.hidden=!value.isWinner;prize.textContent=value.isWinner?value.prizeName||'':'';claim.hidden=!value.isWinner;
    }
    window.LummaRoundStatus=render;
    window.addEventListener('lumma-language-changed',()=>{if(round)render(round);});
    window.addEventListener('lumma-authentication-expired',()=>{if(card&&card!==link)card.hidden=true;round=null;});
    let stopped=false,busy=false;
    async function update(){
        if(stopped||busy||document.hidden||link.hidden)return;busy=true;
        const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),3000);
        try{const response=await fetch(link.href+'?handler=State',{credentials:'same-origin',cache:'no-store',signal:controller.signal});if(response.status===401||response.status===403){link.hidden=true;window.dispatchEvent?.(new Event('lumma-authentication-expired'));return;}if(!response.ok)return;const value=await response.json();if(stopped)return;render(value.round);
            if(['YubiKeyResultLink','YubiKeyParticipationLink','RoundResultLink','RoundParticipationLink'].includes(value.key)){link.dataset.i18n=value.key;link.textContent=window.LummaI18n?.t(value.key)||value.key;}
        }catch{}finally{clearTimeout(timeout);busy=false;}
    }
    window.addEventListener('pagehide',()=>{stopped=true;});window.addEventListener('pageshow',()=>{stopped=false;void update();});setInterval(update,4000);
})();

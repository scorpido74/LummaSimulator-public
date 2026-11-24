(() => {
    const result=document.getElementById('exercise-prize-result');if(!result)return;
    let stopped=false,busy=false;
    async function refresh(){
        if(stopped||busy||document.hidden)return;busy=true;
        const abort=new AbortController(),timeout=setTimeout(()=>abort.abort(),3000);
        try{
            const response=await fetch('/ClickFixComplete?handler=Result',{credentials:'same-origin',cache:'no-store',signal:abort.signal});
            if(response.status===401||response.status===403){window.location.assign('/');return;}
            if(!response.ok)return;const value=await response.json();if(stopped)return;
            if(!['ExerciseNoPrizeAction','ExercisePrizePending','PrizeRoundWon','PrizeRoundNoPrize','RoundRowWon','RoundRowNoPrize','RoundRowNotDrawn','RoundRowUnknown','VisitorPrizeWon','VisitorPrizeLost','VisitorPrizeNotEntered','VisitorPrizeUnknown','VisitorNoPrize','VisitorPrizesOff'].includes(value.key))return;
            result.textContent=(window.LummaI18n?.t(value.key)??value.key).replace('{0}',value.prizeName??'');
        }catch{}finally{clearTimeout(timeout);busy=false;}
    }
    window.addEventListener('pagehide',()=>{stopped=true;result.textContent='';});
    window.addEventListener('pageshow',()=>{stopped=false;void refresh();});
    window.addEventListener('lumma-language-changed',()=>void refresh());
    setInterval(refresh,4000);
})();

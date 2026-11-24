(() => {
    'use strict';
    const clear = () => { const value = document.getElementById('monitor-issued-key'); if (value) value.textContent = ''; const panel = document.getElementById('monitor-issued'); if (panel) panel.hidden = true; };
    window.addEventListener('pagehide', clear);
    window.addEventListener('pageshow', event => { if (event.persisted) clear(); });
})();

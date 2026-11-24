(() => {
    'use strict';
    const field = document.getElementById('enrollment-code');
    if (!field) return;
    const params = new URLSearchParams(window.location.hash.slice(1));
    const code = params.get('invite');
    if (window.location.hash) history.replaceState(null, '', window.location.pathname + window.location.search);
    if (code && /^[A-Za-z0-9_-]{43}$/.test(code)) field.value = code;
    window.addEventListener('pagehide', () => { field.value = ''; });
})();

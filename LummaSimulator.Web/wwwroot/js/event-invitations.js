(() => {
    'use strict';
    const button = document.getElementById('copy-invitation');
    const value = document.getElementById('monitor-issued-key');
    const status = document.getElementById('copy-status');
    if (!button || !value || !status) return;
    button.addEventListener('click', async () => {
        if (!value.textContent.trim()) return;
        try { await navigator.clipboard.writeText(value.textContent.trim()); status.textContent = button.dataset.success; }
        catch { status.textContent = button.dataset.failed; }
    });
})();

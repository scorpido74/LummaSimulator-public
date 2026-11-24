(() => {
    const button = document.querySelector('[data-helper-download]');
    if (!button) return;
    const status = document.getElementById('helper-copy-status');
    button.addEventListener('click', async () => {
        try {
            await navigator.clipboard.writeText(button.dataset.helperDownload);
            status.textContent = 'Downloadlink gekopieerd.';
        } catch {
            status.textContent = button.dataset.helperDownload;
        }
    });
})();

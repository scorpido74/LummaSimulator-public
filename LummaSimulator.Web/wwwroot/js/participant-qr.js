(() => {
    'use strict';
    const root = document.getElementById('participant-qr');
    if (!root || root.dataset.attached) return;
    root.dataset.attached = 'true';
    const image = document.getElementById('participant-qr-image'), link = document.getElementById('participant-qr-link');
    const copy = document.getElementById('participant-qr-copy'), status = document.getElementById('participant-qr-status');
    let url = link.href, busy = false, ended = false, epoch = 0;
    const t = key => window.LummaI18n?.t(key) ?? key;
    function clear() {
        url = ''; root.hidden = true; copy.disabled = true; image.removeAttribute('src'); link.removeAttribute('href'); link.textContent = '';
        document.getElementById('participant-qr-name').textContent = '';
    }
    async function refresh() {
        if (busy || ended) return;
        busy = true;
        const generation = epoch;
        const abort = new AbortController(), timeout = setTimeout(() => abort.abort(), 1500);
        try {
            const response = await fetch(root.dataset.stateUrl, { credentials: 'same-origin', cache: 'no-store', signal: abort.signal });
            if (!response.ok) throw new Error();
            const entry = await response.json();
            if (ended || generation !== epoch) return;
            const uri = new URL(entry.url);
            if (entry.eventId !== root.dataset.eventId || uri.protocol !== 'https:' || uri.username || uri.password
                || uri.searchParams.get('eventId') !== entry.eventId || uri.searchParams.get('eventKind') !== (root.dataset.eventKind || 'prize')) throw new Error();
            if (url !== entry.url || !image.getAttribute('src')) image.src = root.dataset.qrUrl;
            url = entry.url; link.href = url; link.textContent = url;
            document.getElementById('participant-qr-name').textContent = entry.eventName;
            document.getElementById('participant-qr-local').hidden = !entry.laptopOnly;
            copy.disabled = false; root.hidden = false;
        } catch { clear(); } finally { clearTimeout(timeout); busy = false; }
    }
    copy.addEventListener('click', async () => {
        const value = url; if (!value || copy.disabled) return;
        try { await navigator.clipboard.writeText(value); status.textContent = t('ParticipantQrCopied'); }
        catch { status.textContent = t('ParticipantQrCopyFailed'); }
    });
    image.addEventListener('error', clear);
    window.addEventListener('pagehide', () => { ended = true; epoch++; clear(); });
    window.addEventListener('pageshow', () => { ended = false; void refresh(); });
    document.addEventListener('visibilitychange', () => { if (document.hidden) { epoch++; clear(); } else void refresh(); });
    window.addEventListener('lumma-language-changed', () => { image.alt = t('ParticipantQrAlt'); });
    setInterval(() => { if (!document.hidden) void refresh(); }, 2000);
    void refresh();
})();

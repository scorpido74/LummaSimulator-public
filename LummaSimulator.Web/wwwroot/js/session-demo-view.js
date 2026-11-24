"use strict";
(() => {
    const root = document.getElementById("synthetic-session-view");
    if (!root) return;
    const data = document.getElementById("synthetic-session-data");
    const status = document.getElementById("synthetic-session-status");
    const t = key => window.LummaI18n?.t(key) ?? key;
    const fragment = new URLSearchParams(location.hash.slice(1));
    let token = fragment.get("open");
    history.replaceState(null, "", location.pathname);
    const path = "/api/session-demo/views/" + encodeURIComponent(root.dataset.viewId);
    let busy = false, stopped = false, current = null, epoch = 0;
    function render() {
        data.hidden = !current;
        status.textContent = t(current ? "SessionDemoActive" : "SessionDemoUnavailable");
        document.getElementById("synthetic-session-alias").textContent = current?.alias || "";
        document.getElementById("synthetic-session-event").textContent = current ? `${current.eventName} · ${t("PrizeRoundLabel")} ${current.round}` : "";
    }
    async function refresh() {
        if (busy || stopped) return;
        busy = true; const version = epoch;
        try {
            const response = await fetch(path + (token ? "/redeem" : ""), token ? {
                method: "POST", credentials: "same-origin", cache: "no-store",
                headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": document.querySelector('#session-view-antiforgery input[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ token })
            } : { credentials: "same-origin", cache: "no-store" });
            token = null;
            if (!response.ok) throw new Error();
            const next = await response.json();
            if (version === epoch && !stopped) current = next;
        } catch { token = null; current = null; stopped = true; }
        finally { busy = false; render(); }
    }
    window.addEventListener("pagehide", () => { stopped = true; epoch++; token = null; current = null; render(); });
    window.addEventListener("pageshow", event => { if (event.persisted) { stopped = false; void refresh(); } });
    document.addEventListener("visibilitychange", () => {
        if (document.hidden) { epoch++; current = null; render(); }
        else void refresh();
    });
    window.addEventListener("lumma-language-changed", render);
    setInterval(() => { if (!document.hidden) void refresh(); }, 2000);
    void refresh();
})();

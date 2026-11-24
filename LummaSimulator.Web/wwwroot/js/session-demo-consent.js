"use strict";
(() => {
    const root = document.getElementById("session-demo-consent");
    if (!root) return;
    const t = key => window.LummaI18n?.t(key) ?? key;
    const check = document.getElementById("session-demo-confirm");
    const allow = document.getElementById("session-demo-allow");
    const deny = document.getElementById("session-demo-deny");
    let authenticated = root.dataset.authenticated === "true", current = null, busy = false, ended = false, epoch = 0;
    function render() {
        const pending = current?.status === "Pending", granted = current?.status === "Granted";
        root.hidden = !pending && !granted;
        document.getElementById("session-demo-context").textContent = current ? `${current.eventName} · ${t("PrizeRoundLabel")} ${current.round} · ${current.alias}` : "";
        document.getElementById("session-demo-confirmation").hidden = !pending;
        allow.hidden = !pending;allow.disabled = busy || !check.checked;
        deny.textContent = t(granted ? "SessionDemoWithdraw" : "SessionDemoDecline");deny.disabled = busy;
        document.getElementById("session-demo-consent-status").textContent = t(granted ? "SessionDemoConsentGranted" : "SessionDemoConsentInfo");
    }
    async function update(decision) {
        if (!authenticated || busy || ended) return;
        busy = true; const version = epoch;
        try {
            const response = await fetch("/api/session-demo/consent", decision === undefined ? { credentials: "same-origin", cache: "no-store" } : {
                method: "POST", credentials: "same-origin", cache: "no-store",
                headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ requestId: current.requestId, allow: decision, confirmed: decision ? check.checked : true })
            });
            if (!response.ok) throw new Error();
            const next = await response.json();
            if (version !== epoch || ended) return;
            if (next.requestId !== current?.requestId) check.checked = false;
            current = next;
        } catch { current = null; check.checked = false; }
        finally { busy = false; render(); }
    }
    check.addEventListener("change", render);
    allow.addEventListener("click", () => { if (current?.status === "Pending" && check.checked) void update(true); });
    deny.addEventListener("click", () => { if (current) void update(false); });
    window.addEventListener("lumma-authenticated", () => { authenticated = true; void update(); });
    window.addEventListener("lumma-language-changed", render);
    window.addEventListener("pagehide", () => { ended = true; epoch++; current = null; check.checked = false; render(); });
    window.addEventListener("pageshow", event => { if (event.persisted) { ended = false; void update(); } });
    document.addEventListener("visibilitychange", () => { if (!document.hidden) void update(); });
    setInterval(() => { if (!document.hidden) void update(); }, 2000);
    if (authenticated) void update();
})();

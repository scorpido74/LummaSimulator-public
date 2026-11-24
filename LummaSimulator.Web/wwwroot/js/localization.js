"use strict";
(() => {
    const data = document.getElementById("localization-data");
    if (!data || window.LummaI18n) return;
    const settings = JSON.parse(data.content.textContent);
    let language = settings.language;
    let saving = false;
    const t = key => settings.catalogs[language]?.[key] ?? key;
    window.LummaI18n = { t, language: () => language };
    const form = document.getElementById("language-preference-form");
    const notice = document.getElementById("language-notice");
    document.querySelectorAll("[data-localize-errors] li").forEach(node => {
        const key = Object.keys(settings.catalogs[language]).find(key => settings.catalogs[language][key] === node.textContent.trim());
        if (key) node.dataset.i18n = key;
    });
    function render() {
        document.documentElement.lang = language;
        document.querySelectorAll("[data-i18n]").forEach(node => { node.textContent = t(node.dataset.i18n); });
        document.querySelectorAll("[data-i18n-title]").forEach(node => { node.textContent = t(node.dataset.i18nTitle) + (node.dataset.titleAlias ? (node.dataset.titleSeparator ?? " · ") + node.dataset.titleAlias : "") + node.dataset.titleSuffix; });
        document.querySelectorAll("[data-i18n-aria-label]").forEach(node => { node.setAttribute("aria-label", t(node.dataset.i18nAriaLabel)); });
        form.querySelectorAll("[data-language]").forEach(button => {
            button.disabled = saving;
            button.setAttribute("aria-pressed", String(button.dataset.language === language));
        });
    }
    form.addEventListener("submit", event => event.preventDefault());
    form.querySelectorAll("[data-language]").forEach(button => button.addEventListener("click", async () => {
        const selected = button.dataset.language;
        if (saving || selected === language || !settings.catalogs[selected]) return;
        saving = true;
        notice.textContent = "";
        render();
        try {
            const response = await fetch("/api/language", {
                method: "POST", credentials: "same-origin", cache: "no-store",
                headers: { "X-CSRF-TOKEN": form.querySelector('input[name="__RequestVerificationToken"]').value },
                body: new URLSearchParams({ language: selected, audience: settings.audience })
            });
            if (!response.ok) throw new Error("LanguageSaveFailed");
            language = selected;
            render();
            window.dispatchEvent(new Event("lumma-language-changed"));
        } catch {
            notice.textContent = t("LanguageSaveFailed");
        } finally { saving = false; render(); }
    }));
    render();
})();

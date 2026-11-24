"use strict";
(() => {
    const root = document.getElementById("passkey-demo");
    // Protect against repeated script loading / event attachment, including restored pages.
    if (!root || root.dataset.webauthnInitialized === "true") return;
    root.dataset.webauthnInitialized = "true";
    const register = document.getElementById("register-passkey");
    const authenticate = document.getElementById("authenticate-passkey");
    const cancel = document.getElementById("cancel-passkey");
    const end = document.getElementById("end-demo");
    const again = document.getElementById("again-authenticate");
    const confirmationEnd = document.getElementById("confirmation-end-demo");
    const newVisitor = document.getElementById("new-visitor");
    const visitorMessage = document.getElementById("visitor-number");
    const roundStatus = document.getElementById("prize-round-status");
    let currentRound = null;
    let visitorReceipt = null;
    const status = document.getElementById("webauthn-status");
    const count = document.getElementById("credential-count");
    const success = document.getElementById("success-count");
    const registrationStep = document.getElementById("registration-step");
    const confirmation = document.getElementById("auth-confirmation");
    const csrf = document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]').value;
    const decode = value => Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
    const encode = buffer => btoa(String.fromCharCode(...new Uint8Array(buffer))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    let busy = false;
    let active = null;
    let supported = Boolean(window.isSecureContext && window.PublicKeyCredential && navigator.credentials);
    const t = key => window.LummaI18n?.t(key) ?? key;
    let lastMessage = null;
    let retryKey=null;
    function render() {
        const registered = Number(count.textContent) > 0;
        root.dataset.authenticated=String(!confirmation.hidden);
        const options=document.getElementById('passkey-options');if(options)options.hidden=!confirmation.hidden;
        if(again)again.hidden=true;
        register.textContent = t(registered ? "Nog een passkey toevoegen" : "VisitorCreatePasskey");
        register.classList.toggle("button-primary",!registered);register.classList.toggle("button-outline",registered);
        registrationStep.hidden=registered;
        const loginStep=document.getElementById("authentication-step");if(loginStep)loginStep.hidden=!registered;
        authenticate.hidden=!registered;
        const intro=document.getElementById("visitor-intro");if(intro)intro.hidden=!confirmation.hidden;
        const sessionDetails=document.getElementById("visitor-session-details");if(sessionDetails)sessionDetails.hidden=!confirmation.hidden;
        registrationStep.classList.toggle("completed", registered);
        root.dataset.registered=String(registered);
        document.getElementById("registration-state").textContent = t(registered ? "Registratie voltooid" : "Nog niet geregistreerd");
        register.disabled = busy || !supported || Number(count.textContent) >= 5;
        authenticate.disabled = busy || !supported || !registered;
        end.disabled = busy;
        if (again) again.disabled = busy || !supported || !registered;
        if (confirmationEnd) confirmationEnd.disabled = busy;
        if (newVisitor) newVisitor.disabled = busy;
        cancel.hidden = !busy;
        cancel.disabled = !busy || !active || Boolean(active.controller.signal.aborted);
        root.setAttribute("aria-busy", String(busy));
    }
    function message(text, kind = "info", prefix = "", suffix = "") {
        lastMessage = { text, kind, prefix, suffix };
        status.textContent = (prefix ? t(prefix) + ": " : "") + t(text) + (suffix ? " " + t("Annulering") + ": " + t(suffix) : "");
        if(retryKey){const retry=document.createElement("span");retry.dataset.i18n=retryKey;retry.textContent=" "+t(retryKey);status.append(retry);}
        status.classList.toggle("error", kind === "error");
        status.classList.toggle("success", kind === "success");
    }
    async function post(operation, action, operationId, data, signal, keepalive = false) {
        const response = await fetch(`/api/webauthn/${operation}/${action}`, {
            method: "POST", credentials: "same-origin", cache: "no-store", signal, keepalive,
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf, "X-Lumma-Operation": operationId },
            body: JSON.stringify(data ?? {})
        });
        if (response.status === 429) throw new Error("Te veel aanvragen. Wacht een minuut en probeer opnieuw.");
        if (response.status >= 500) throw new Error("De server is tijdelijk niet beschikbaar. Vraag de presentator om hulp.");
        let result;
        try { result = await response.json(); }
        catch { throw new Error("Onverwacht serverantwoord. Vernieuw de pagina en controleer je demosessie."); }
        if (!response.ok) throw new Error(result.errorKey || result.error || "De demonstratie is verlopen. Start opnieuw.");
        return result;
    }
    function release(operation) {
        // Only this operation may be cancelled, never a newer request from another tab.
        // pagehide and finally may both request cleanup; send it only once.
        return operation.releasePromise ??= (async () => {
            try { await post(operation.kind, "cancel", operation.id, {}, AbortSignal.timeout(5000), true); return true; }
            catch { return false; }
        })();
    }
    async function run(registration) {
        const operation = { id: crypto.randomUUID(), kind: registration ? "register" : "authenticate", controller: new AbortController(), reason: "" };
        active = operation;retryKey=null;
        delete status.dataset.webauthnErrorStage;
        confirmation.hidden = true;
        render();
        message("Volg de aanwijzingen van je browser. Kies de provider waar jouw passkey is opgeslagen.");
        const timeout = setTimeout(() => { operation.reason = "timeout"; operation.controller.abort(); }, 125000);
        let verified = false;
        let stage = "options";
        try {
            const options = await post(operation.kind, "options", operation.id, {}, operation.controller.signal);
            options.challenge = decode(options.challenge);
            // Do not force hints or attachment: leave Windows Hello, keys, phones and extensions to the browser.
            let credential;
            if (registration) {
                options.user.id = decode(options.user.id);
                options.excludeCredentials = (options.excludeCredentials || []).map(c => ({ ...c, id: decode(c.id) }));
                stage = "browser";
                credential = await navigator.credentials.create({ publicKey: options, signal: operation.controller.signal });
            } else {
                options.allowCredentials = (options.allowCredentials || []).map(c => ({ ...c, id: decode(c.id) }));
                stage = "browser";
                credential = await navigator.credentials.get({ publicKey: options, signal: operation.controller.signal });
            }
            if (!credential) throw new Error("De browser heeft geen credential teruggegeven.");
            stage = "verify";
            const response = { clientDataJSON: encode(credential.response.clientDataJSON) };
            if (registration) {
                response.attestationObject = encode(credential.response.attestationObject);
                response.transports = credential.response.getTransports?.() || [];
            } else {
                response.authenticatorData = encode(credential.response.authenticatorData);
                response.signature = encode(credential.response.signature);
                response.userHandle = credential.response.userHandle ? encode(credential.response.userHandle) : null;
            }
            const result = await post(operation.kind, "verify", operation.id, {
                id: credential.id, rawId: encode(credential.rawId), type: credential.type,
                response, extensions: credential.getClientExtensionResults()
            }, operation.controller.signal);
            if (result.verified !== true || result.operation !== operation.kind || result.verificationStatus !== "Verified"
                || !Number.isInteger(result.credentialCount) || !Number.isInteger(result.successfulAuthentications))
                throw new Error("De server heeft de verificatie niet bevestigd.");
            verified = true;
            // Authoritative counts, never optimistic increments after the browser dialog alone.
            count.textContent = String(result.credentialCount);
            success.textContent = String(result.successfulAuthentications);
            if (registration) message("Registratie voltooid. Je passkey is opgeslagen voor dit tijdelijke account. Test nu de afzonderlijke authenticatie.", "success");
            else {
                visitorReceipt = Number.isSafeInteger(result.visitorNumber) && result.visitorNumber > 0
                    ? { number: result.visitorNumber, event: result.eventName || "" } : null;
                renderVisitorNumber();
                currentRound = result.round || null;
                renderRound(currentRound);
                const participation=document.getElementById('participation-link');
                if(participation){const key=result.prizeLinkKey;participation.hidden=!['YubiKeyResultLink','YubiKeyParticipationLink','RoundResultLink','RoundParticipationLink'].includes(key);if(!participation.hidden){participation.dataset.i18n=key;participation.textContent=t(key);}}
                const exerciseStart=document.getElementById('start-clickfix'),warningTitle=document.getElementById('clickfix-warning-title');
                if(exerciseStart){const key=result.prizesEnabled===true?'ExerciseStartPrize':'ExerciseStartNeutral';exerciseStart.dataset.startKey=key;exerciseStart.dataset.i18n=key;exerciseStart.textContent=t(key);}
                if(warningTitle){const key=result.prizesEnabled===true?'ExercisePrizeFailure':'ExerciseResultFailure';warningTitle.dataset.i18n=key;warningTitle.textContent=t(key);}
                const displayName = currentRound?.alias || result.username;
                document.querySelectorAll("[data-visitor-display-name]").forEach(node => { node.textContent = displayName; });
                document.getElementById("verified-username").textContent = displayName;
                const welcome=document.getElementById("demo-welcome");
                if(welcome){
                    const prefix=document.createElement("span");prefix.dataset.i18n="WelcomeAliasPrefix";prefix.textContent=t("WelcomeAliasPrefix");
                    const alias=document.createElement("span");alias.setAttribute("data-visitor-display-name","");alias.textContent=displayName;
                    welcome.replaceChildren();welcome.append(prefix," ",alias);
                }
                const title = document.querySelector("title[data-i18n-title]");
                if (title) {
                    title.dataset.i18nTitle="WelcomeAliasPrefix";
                    title.dataset.titleSeparator=" ";
                    title.dataset.titleAlias = displayName;
                    title.textContent = t(title.dataset.i18nTitle) + (title.dataset.titleAlias ? (title.dataset.titleSeparator ?? " · ") + title.dataset.titleAlias : "") + title.dataset.titleSuffix;
                }
                const credentialLabel = document.getElementById("verified-credential");
                if (credentialLabel) credentialLabel.textContent = result.credentialAlias || t("Geverifieerde passkeyregistratie");
                const time = document.getElementById("verified-time");
                time.textContent = new Date(result.verifiedAt).toLocaleString(window.LummaI18n?.language() || "nl-NL");
                time.setAttribute("datetime", result.verifiedAt);
                document.getElementById("verification-result").textContent = t("Server-side geverifieerd · gebruikersverificatie bevestigd");
                confirmation.hidden = false;
                document.getElementById("auth-confirmation-title").focus();
                message("Authenticatie geslaagd. De server heeft de passkey-handtekening en gebruikersverificatie gecontroleerd.", "success");
                window.dispatchEvent?.(new Event("lumma-authenticated"));
                // Stay on /Demo. Only the explicit exercise button can start the next step.
            }
        } catch (error) {
            let text;
            if (operation.reason === "timeout" || error.name === "TimeoutError") text = "De aanvraag is verlopen. Je kunt opnieuw proberen.";
            else if (error.name === "AbortError") text = "De aanvraag is geannuleerd. Je kunt opnieuw proberen.";
            else if (stage === "browser" && error.name === "NotAllowedError") text = "VisitorBrowserInterrupted";
            else if (stage === "browser" && /already pending|pending request/i.test(error.message || "")) text = "Er staat nog een browseraanvraag open. Rond die af of annuleer hem, ook in andere tabs, en probeer daarna opnieuw.";
            else if (stage === "browser" && error.name === "InvalidStateError") text = "Deze authenticator heeft mogelijk al een passkey voor dit demoaccount. Probeer in te loggen of kies een andere provider.";
            else if (stage === "browser" && error.name === "SecurityError") text = "Controleer de HTTPS-origin en RP ID met de presentator. Localhost en het publieke domein gebruiken verschillende passkeys.";
            else text = error.message || "De actie kon niet worden voltooid.";
            const cancelled = error.name === "AbortError" || operation.reason === "timeout" || error.name === "TimeoutError";
            status.dataset.webauthnErrorStage = cancelled ? "cancel" : stage;
            const label = cancelled ? "Annulering" : stage === "options" ? "Opties ophalen" : stage === "browser" ? "Browserceremonie" : "Serververificatie";
            retryKey=registration?"VisitorRetryRegistration":"VisitorRetrySignIn";
            message(text, "error", label);
        } finally {
            clearTimeout(timeout);
            // Controlled compatibility trial: all awaited work has settled (including
            // reading the verification response). This controller belongs only to this
            // operation; abort cannot undo a completed verification or affect a new one.
            operation.controller.abort();
            if (!verified && !await release(operation))
                message(lastMessage?.text || "De actie kon niet worden voltooid.", "error", lastMessage?.prefix || "", "De server kon de annulering niet bevestigen; een achtergebleven aanvraag vervalt binnen twee minuten.");
            if (active === operation) active = null;
        }
    }
    async function ceremony(registration) {
        if (busy || !supported) return;
        busy = true; // Set before the first await; double-clicks cannot queue another request.
        render();
        try {
            if (navigator.locks) await navigator.locks.request("lumma-passkey-operation", { ifAvailable: true }, async lock => {
                if (!lock) { message("Er is een passkey-aanvraag actief in een andere tab. Rond die eerst af of annuleer hem.", "error"); return; }
                await run(registration);
            });
            else await run(registration); // The server also enforces one pending operation per demo.
        } catch {
            message("De browser kon de aanvraag niet starten. Controleer andere tabs en probeer opnieuw.", "error");
        } finally { busy = false; active = null; render();if(registration&&Number(count.textContent)>0){authenticate.focus({preventScroll:true});authenticate.scrollIntoView?.({block:"nearest",behavior:"smooth"});} }
    }
    function renderVisitorNumber() {
        if (!visitorMessage) return;
        visitorMessage.hidden = !visitorReceipt;
        visitorMessage.textContent = visitorReceipt
            ? t("VisitorCongratulations").replace("{0}", String(visitorReceipt.number)) + " · " + visitorReceipt.event : "";
    }
    function renderRound(round) {
        if (!roundStatus || !round) return;
        if(window.LummaRoundStatus){window.LummaRoundStatus(round);return;}
        roundStatus.hidden = false;
        const title = `${round.eventName} · ${t("PrizeRoundLabel")} ${round.number}`;
        const outcome = round.status === "Closed"
            ? t(round.drawResultKey || (round.isWinner ? "RoundRowWon" : "RoundRowNotDrawn")) + (round.prizeName ? " · " + round.prizeName : "")
            : round.status === "Full" ? t("PrizeRoundFull") : t("PrizeRoundWaiting");
        roundStatus.replaceChildren();
        const heading = document.createElement("strong");
        heading.textContent = title;
        const detail = document.createElement("p");
        detail.textContent = outcome;
        roundStatus.append(heading, detail);
    }
    register.addEventListener("click", () => ceremony(true));
    authenticate.addEventListener("click", () => ceremony(false));
    again?.addEventListener("click", () => ceremony(false));
    cancel.addEventListener("click", () => { active?.controller.abort(); render(); });
    window.addEventListener("lumma-authentication-expired",()=>{
        currentRound=null;visitorReceipt=null;renderVisitorNumber();confirmation.hidden=true;const participation=document.getElementById('participation-link');if(participation)participation.hidden=true;
        const welcome=document.getElementById('demo-welcome'),eventName=welcome?.dataset.eventName||'';
        if(welcome){const prefix=document.createElement('span');prefix.dataset.i18n=eventName?'WelcomeEventPrefix':'WelcomeStandalone';prefix.textContent=t(prefix.dataset.i18n);welcome.replaceChildren();welcome.append(prefix,eventName?' '+eventName:'');}
        const title=document.querySelector('title[data-i18n-title]');if(title){title.dataset.i18nTitle=eventName?'WelcomeEventPrefix':'WelcomeStandalone';title.dataset.titleAlias=eventName;title.textContent=t(title.dataset.i18nTitle)+(eventName?' '+eventName:'')+title.dataset.titleSuffix;}
        render();authenticate.focus({preventScroll:true});
    });
    window.addEventListener("pagehide", () => { const operation = active; if (operation) { operation.controller.abort(); void release(operation); } });
    window.addEventListener("lumma-language-changed", () => {
        render();
        renderVisitorNumber();
        renderRound(currentRound);
        if (lastMessage) message(lastMessage.text, lastMessage.kind, lastMessage.prefix, lastMessage.suffix);
        if (!confirmation.hidden) {
            document.getElementById("verification-result").textContent = t("Server-side geverifieerd · gebruikersverificatie bevestigd");
            const time = document.getElementById("verified-time");
            time.textContent = new Date(time.getAttribute("datetime")).toLocaleString(window.LummaI18n?.language() || "nl-NL");
        }
    });
    if (!supported) message("Deze browser biedt hier geen WebAuthn aan. Gebruik een ondersteunde browser via de juiste HTTPS-origin.", "error");
    render();
})();

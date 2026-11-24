# LummaSimulator v0.5 - phase 4 implemented, phase 5.1 storage foundation implemented

Status: phase 4 adds personal presenter WebAuthn accounts, authorization, event management and audit. The phase-2/3 sections below are historical checkpoints; the phase-4 section supersedes their presenter-login and closed-management descriptions. This is not a completed v0.5 release. The web assembly remains 0.4.1 until the remaining phases and validation are complete. Physical YubiKey acceptance of the phase-2 changes remains required.

## Recovery and scope

The verified v0.4.1 source baseline is stored outside the project at `C:/Example/Baselines\v0.4.1-20261030-080759`, with source ZIP, SHA-256 manifest and baseline validation results. No public repository was created.

Phase 2 does not change the SQLite schema or migrate/delete live visitor data. Existing user handles, credential IDs, keys and counters are retained. Tests use separate databases. The running demonstration server is not restarted automatically.

## Central configuration

The `Experience` section of `LummaSimulator.Web/appsettings.json` contains:

| Key | Default | Meaning |
| --- | --- | --- |
| OrganizationName | Infinigate | Display organization and display name for new WebAuthn registrations; does not change RP ID. Existing logos remain unchanged. |
| VisitorPrefix | Visitor | ASCII letters/digits, 1–32 characters. Applies only to newly created accounts; does not rename existing accounts. |
| DefaultLanguage | nl-NL | Initial language for a new visitor and a browser without a valid preference. |
| AvailableLanguages | nl-NL, en-GB | Implemented offline languages. An explicit array replaces defaults. |
| AccountIdleMinutes | 29 | Temporary account inactivity timeout, 1–29 minutes. The cleanup worker still runs every 30 seconds. |
| Hostname | empty | Optional explicit canonical DNS hostname. Empty retains the existing environment-specific policy. A value must match WebAuthn:RpId exactly. |
| StartCodesRequired | false | Reserved for the admission phase. Public demo access remains available by default. Enabling this before admission is implemented fails startup. |
| StartCodeLifetimeMinutes | 10 | Prepared admission lifetime, 1–30 minutes; no codes are issued in phase 2. |
| PrizeEnabledByDefault | false | Reserved for Prize Experience. Enabling this before prize transactions are implemented fails startup. |

`WebAuthn:RpId` and `WebAuthn:Origins` remain the single source of truth for WebAuthn. They are not inferred or changed automatically. With an explicit Hostname, custom hosting requires deliberate matching RP ID and canonical HTTPS origins. No wildcard, path, query, credentials or fragment is allowed; production retains default-port enforcement. Existing Development remains `localhost` / `https://localhost:7243`. Changing an RP ID can make existing passkeys unusable; this phase never changes it automatically.

No certificate private keys are added to configuration. Remote HTTPS hosting additionally requires a trusted certificate, explicit listener, DNS/firewall and presenter authorization. Those host changes are not performed by phase 2. The separate presenter WebAuthn login is a later phase; the existing protected presenter password login is preserved for now.

## Localization

ASP.NET Core request localization uses `Resources/Localization/UiText.resx`, `.nl-NL.resx` and `.en-GB.resx`. Existing source phrases are resource keys so Razor, server errors and JavaScript share exactly the same catalog; new generic UI uses named keys. Resource values are always rendered as text, not trusted HTML. All translations and scripts ship locally, with no runtime translation service or CDN.

`local-text` renders a translated span with a resource key. The shared layout embeds both configured catalogs as safely JSON-escaped data inside a template. `localization.js` changes text, accessibility labels, page title and document language in place. `passkeys.js` redraws its dynamic status and confirmation labels on the language-change event. It retains its active operation, AbortController, options, Web Lock and operation ID. No page navigation, arbitrary delay or automatic WebAuthn retry is introduced.

Preferences are separate Secure/HttpOnly/SameSite=Strict cookies:

- `__Host-Lumma.VisitorLanguage`: visitor pages and WebAuthn API.
- `__Host-Lumma.PresenterLanguage`: `/Presenter` including login.

No shared localStorage/sessionStorage is used. The two cookies can coexist in the same browser. A new visitor resets only the visitor preference to the configured default, leaving presenter preference intact. Preferences contain only a validated culture identifier, expire after 180 days, and convey no authentication or authorization. Accept-Language and arbitrary query strings do not override the configured default.

The language preference endpoint requires POST, exact configured Origin and antiforgery validation. It changes neither authentication cookies nor database state. Concurrent language clicks are serialized independently from WebAuthn operations. If saving fails, the current language is retained and the switch can be retried. A live server is needed to persist a preference, but no internet access is needed.

To add a language later: add the matching resource catalog, update the implemented-language validation and language labels, then run completeness and interaction tests. Official product names, credential aliases and MITRE IDs are not translated. WPF localization is not changed in this scoped phase.

## Confirmed rules for subsequent phases — not yet implemented

Public demonstrations remain the default. Startcodes will be optional and configurable. A code is an event-bound cryptographically random admission capability, consumed atomically once, with a limited configurable expiry and at most one prize chance. Aborted demonstrations do not automatically restore the code. A code is not proof of a unique natural person. A future QR link may carry an admission capability, but no QR feature is implemented now. Whether code-free Prize Experience is acceptable must be explicitly resolved before enabling a public prize endpoint: unlimited anonymous accounts cannot provide person-level uniqueness.

Visitor-counter reset and starting a new event are distinct actions. Only a new event creates a new prize pool. Disabling/re-enabling prizes, deleting accounts, resetting a visitor counter or changing the limit never frees already awarded prizes within the same event. A limit below the awarded count is rejected. Historical counts, awards and one-time handovers are preserved. Presenter authorization and explicit confirmation are required for a new event.

## Validation and manual acceptance

Use separate build output while the current server holds its executable open:

```powershell
dotnet build tests/LummaSimulator.Web.Tests/LummaSimulator.Web.Tests.csproj --no-restore --output verification/bin/phase2-tests
$env:LUMMA_TEST_WEB_DLL = 'C:/src/LummaSimulator\verification\bin\phase2-tests\LummaSimulator.Web.dll'
dotnet verification/bin/phase2-tests/LummaSimulator.Web.Tests.dll
node --test tests/WebAuthn.Ux.Tests/passkeys.test.cjs tests/WebAuthn.Ux.Tests/localization.test.cjs
```

HTTP tests cover actual localized Razor rendering, separate audience cookies in the same browser, invalid language/CSRF rejection, new-visitor default and preservation of an already registered demo across a language change. JavaScript tests cover in-place language selection, failure recovery and language change during a pending WebAuthn ceremony. Existing repeated authentication, two-credential, challenge, signature, cancellation and ownership checks remain enabled. Final phase-2 counts are recorded below after validation.

Manual acceptance on the updated server:

1. Open a new visitor: Dutch is default. Select English without navigation; inspect page title, instructions, buttons, errors and privacy page.
2. Select a different presenter language in the same browser. Confirm that visitor and presenter choices remain independent after navigation and login.
3. Register with the physical YubiKey 5C NFC, then authenticate repeatedly without refreshing. Switch language while the WebAuthn dialog is open; confirm that it is not cancelled and that successful server verification still increments exactly once.
4. Cancel an operation, switch language, retry and inspect the localized error stage. Check offline operation on the prepared local HTTPS network.
5. Check responsive layout and screen-reader labels in a real browser. Node mocks and HTTP rendering do not establish visual or physical-device acceptance.

The phase-2 checkpoint was validated before phase 3 began. Current development stops after phase 3. No v0.5 release is complete before all later-phase tests and physical acceptance.

## Phase-2 validation results

- Isolated web and web-test build: succeeded, 0 warnings, 0 errors.
- JavaScript: 33 tests passed, 0 failed, including language changes during WebAuthn and separate preference audiences.
- .NET web suite: 248 checks passed, including real HTTPS, localized Razor, configuration, two credentials, signature checks and session isolation.
- Simulator smoke: passed, including 2778 ATT&CK/status/export assertions and monitor/client checks.
- WPF integration: passed; no WPF production source was modified.
- Full existing solution compiled and its regression runner passed before phase-2 implementation. The only initial build warning was a locked running web executable; final web builds used separate output.
- Logs: `verification/bin/phase2-js-results.log`, `phase2-web-results.log`, `phase2-smoke-results.log`, `phase2-wpf-results.log`.

Physical-device testing and visual browser acceptance have not been performed for phase 2. The current demonstration process was not restarted. At the phase-2 checkpoint the version remained 0.4.1 and phase 3 had not started. See the phase-3 section below for subsequent work.

For custom hosting, also explicitly align ASP.NET Core `AllowedHosts`, listeners and certificate configuration; Hostname never changes these automatically. The language switch does not configure remote access or presenter WebAuthn.

## Phase-2 changed files relative to the v0.4.1 baseline

- `LummaSimulator.Web/Configuration/ExperienceSettings.cs` (new)
- `LummaSimulator.Web/Configuration/WebAuthnSettings.cs`
- `LummaSimulator.Web/Localization/LocalTextTagHelper.cs` (new)
- `LummaSimulator.Web/Localization/UiText.cs` (new)
- `LummaSimulator.Web/Pages/Complete.cshtml`
- `LummaSimulator.Web/Pages/Demo.cshtml`
- `LummaSimulator.Web/Pages/Error.cshtml`
- `LummaSimulator.Web/Pages/Index.cshtml`
- `LummaSimulator.Web/Pages/Index.cshtml.cs`
- `LummaSimulator.Web/Pages/Presenter/Index.cshtml`
- `LummaSimulator.Web/Pages/Presenter/Login.cshtml`
- `LummaSimulator.Web/Pages/Presenter/Login.cshtml.cs`
- `LummaSimulator.Web/Pages/Privacy.cshtml`
- `LummaSimulator.Web/Pages/Shared/_Layout.cshtml`
- `LummaSimulator.Web/Pages/_ViewImports.cshtml`
- `LummaSimulator.Web/Program.cs`
- `LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx` (new)
- `LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx` (new)
- `LummaSimulator.Web/Resources/Localization/UiText.resx` (new)
- `LummaSimulator.Web/Services/DemoService.cs`
- `LummaSimulator.Web/Services/SessionStatusService.cs`
- `LummaSimulator.Web/appsettings.json`
- `LummaSimulator.Web/wwwroot/css/site.css`
- `LummaSimulator.Web/wwwroot/js/localization.js` (new)
- `LummaSimulator.Web/wwwroot/js/passkeys.js`
- `README.md`
- `docs/LummaSimulator-v0.5.md` (new)
- `tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj`
- `tests/LummaSimulator.Web.Tests/HttpChecks.cs`
- `tests/LummaSimulator.Web.Tests/LocalizationChecks.cs` (new)
- `tests/LummaSimulator.Web.Tests/Program.cs`
- `tests/WebAuthn.Ux.Tests/localization.test.cjs` (new)
- `tests/WebAuthn.Ux.Tests/passkeys.test.cjs`


## Phase 3 — visitor management and persistent statistics

Status: implemented; assembly version remains **0.4.1**. No phase-4 prize drawing or prize handover is implemented. The original recovery baseline has not been overwritten. The running live server and its database have not been restarted or migrated by the development/test runs; migration is executed automatically on a deliberate start of the updated build. All test data is separate.

### Public new visitor

`POST /Demo?handler=NewVisitor` uses Razor antiforgery, the existing visitor cookie and the demo rate limit. It requires no presenter or startcode. The previous account is closed (`ClosedAt`) and its session hash is replaced with an unguessable, unissued value. Outstanding challenges and monitor grants are removed. A new account, random 32-byte user handle, random identity and session secret are created in the **same immediate SQLite transaction**. Failure rolls back the replacement, retaining the original session.

The new cookie replaces the browser session without a browser restart. The previous token grants access to neither the previous nor the new account. Previous credentials remain in SQLite until the normal inactivity cleanup; closed accounts are excluded from active-account lists and statistics. Their original LastSeen is not extended. The existing explicit End demonstration action still deletes the selected account as previously documented; it is distinct from New visitor.

The switch is available in Dutch and English and is disabled during the local WebAuthn ceremony. New visitors begin in the configured default language; the separate presenter preference is retained. Multiple tabs in one browser share the visitor cookie, so visitor handover should use one demo tab. This is session isolation, not proof of distinct natural people. The existing cap of 200 retained accounts and request rate limit remain in effect; public repeated starts can reach that cap until cleanup frees expired accounts.

### Preserving database migration

The existing Demos table receives a nullable `ClosedAt` column. Existing rows remain open; user handles, credential IDs, keys, counters and session hashes are not reset by migration. Additional tables:

| Table | Purpose and lifetime |
| --- | --- |
| ExperienceMigrations | Idempotent phase-3 data migration marker. |
| ExperienceEvents | UUID, name, start/end UTC timestamps, Active/Closed/Imported status, visitor and authentication totals. Permanent history. |
| VisitorCounts | One nullable DemoId link per retained account, event ID and visitor number. UNIQUE(DemoId) and UNIQUE(EventId,VisitorNumber). Account cleanup sets DemoId to NULL while preserving anonymous history. No username, user handle, credential data, cookie or per-visitor timestamp is copied. |
| DailyExperienceStats | UTC date, aggregate first-success visitor count and successful-authentication count. Permanent aggregate history. |
| EventPrizePolicies | One separate future prize pool per event, disabled, zero awards. CHECK constraints prevent enabling prizes or storing awards in this phase. |

On the first migration only, retained v0.4.1 accounts with Successes greater than zero are imported into a separate historical `Imported v0.4.1` event, along with their retained success counts. This prevents those already successful accounts from being counted again. Their unknown first-success day is not fabricated, so they are excluded from daily visitor counts. Accounts deleted before this migration cannot be reconstructed from aggregate-free v0.4.1 storage; historical totals therefore cover retained imported data and subsequent successful demonstrations, not unknowable earlier visitors. Migration can be rerun without duplicate imports.

### Atomic first-success counting

Only a cryptographically verified WebAuthn assertion calls the internal statistics commit. Credential counter/backup update, account success increment and authentication time, first visitor allocation, event authentication total and daily aggregate are committed together using BEGIN IMMEDIATE. The transaction checks the expected credential counter and current session hash/open-account state; it cannot resurrect a closed session. The existing atomic single-use challenge consumption remains in place before verification.

A visitor gets one number for its account lifetime, even with multiple credentials, reloads or repeat sign-in. Numbers are consecutive and unique **within their event**. The server response includes the number and assigned event; the confirmation translates “Gefeliciteerd, jij bent bezoeker #100!” / “Congratulations, you're visitor #100!” and displays the event name. Language changes re-render the same assigned number without contacting the authentication endpoint again.

An account's original visitor number/event never changes when a new event starts. Further successful authentications are credited to the event active at authentication time, without creating another visitor count for that account. New accounts first authenticated in the new event start at number 1. Total visitors, active-event visitors, UTC daily visitors, successful authentications and open/unexpired demo accounts are distinct backend statistics. “Visitor” means successful temporary demoaccount, not unique person.

### Event management remains closed

There is at most one active event, enforced by a partial unique SQLite index. Initialization creates the first `Passkey Experience` event if none exists. `ExperienceStatistics.SummaryAsync` provides the dashboard backend snapshot and history from one read transaction; it has no public HTTP endpoint.

`StartEventAsync` is a prepared backend operation with **no HTTP route or UI action**. It requires an authenticated Presenter principal carrying the server-established `amr=webauthn` claim, explicit confirmation, and the expected active event ID. No current production login issues that WebAuthn claim, so the existing password presenter login cannot start events. Tests use a synthetic server principal solely to exercise this future backend. A future presenter-WebAuthn implementation must validate the principal before invoking this operation.

An immediate transaction closes the expected active event and inserts the new event/pool. Concurrent actions based on the same old event use compare-and-swap: one succeeds, the other must reload. New events do not delete accounts, credentials, counts or history. No visitor-counter reset, prize reset or pool editing endpoint is present. Multiple presenters on one installation share the same central SQLite state; this is not a claim of supported distributed multi-host SQLite deployment.

### Method cards and acceptance

The DOM order is YubiKey, Windows Hello, phone in both languages. YubiKey has an orange accent and localized Recommended label; Windows Hello has an embedded Windows mark and phone retains its embedded smartphone icon. All assets work offline. Desktop CSS uses three columns and mobile CSS one column, with no CSS order override. No WebAuthn attachment, transport or hint is forced by choosing a card.

HTTP regression tests exercise registration and two successful authentications independently for each of the three routes using a software authenticator. They do not establish physical Windows Hello, phone/QR or YubiKey operation. Actual desktop/mobile visual inspection remains manual: the Computer Use native pipe was unavailable in this session. Keep the same card order, badge, icons and provider-neutral explanatory text during later interface work.

### Phase-3 files and verification

Production changes for phase 3 (including the card-order change requested immediately before this phase):

- `LummaSimulator.Web/Services/ExperienceStatistics.cs` (new): migration, transactions, event history, dashboard backend, closed event-management preparation.
- `LummaSimulator.Web/Services/DemoStore.cs`: nullable closure marker and statistics initialization; excludes closed accounts from active queries.
- `LummaSimulator.Web/Services/DemoService.cs`: transactional visitor rotation and authenticated statistics commit.
- `LummaSimulator.Web/Models/DemoModels.cs`, `Program.cs`: server receipt includes visitor number and event identity.
- `LummaSimulator.Web/Pages/Demo.cshtml`, `Demo.cshtml.cs`, `wwwroot/js/passkeys.js`: New visitor, localized receipt and busy-state protection.
- `LummaSimulator.Web/Pages/Index.cshtml`, `wwwroot/css/site.css`: method order, local icons and YubiKey emphasis.
- `LummaSimulator.Web/Resources/Localization/UiText*.resx`: Dutch/English new-visitor, visitor-number and privacy messages.
- `tests/LummaSimulator.Web.Tests/ExperienceChecks.cs` (new), `Program.cs`, `HttpChecks.cs`, `tests/WebAuthn.Ux.Tests/passkeys.test.cjs`: migration, retention, concurrency, session isolation, route and UI regression checks.
- `docs/LummaSimulator-v0.5.md`, `README.md`: phase status and operational/privacy documentation.

Build the entire regression dependency graph into a separate directory while the live executable is locked:

```powershell
dotnet build tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj --no-restore --output verification/bin/phase3-suite
$env:LUMMA_TEST_WEB_DLL = 'C:/src/LummaSimulator\verification\bin\phase3-suite\LummaSimulator.Web.dll'
dotnet verification/bin/phase3-suite/LummaSimulator.Web.Tests.dll
node --test tests/WebAuthn.Ux.Tests/passkeys.test.cjs tests/WebAuthn.Ux.Tests/localization.test.cjs
# Set LUMMA_DATA_DIRECTORY to a separate test scope before the Windows runners.
dotnet verification/bin/phase3-suite/LummaSimulator.SmokeTests.dll
dotnet verification/bin/phase3-suite/Verification.dll
```

These are all four runners used by the solution's regression target, including its WPF integration and ATT&CK/monitor checks. Results are recorded in `verification/bin/phase3-*-results.log`. Physical acceptance: start the updated build deliberately, register/authenticate with the YubiKey, repeat without refresh, switch language and verify the number is unchanged. Start New visitor, verify counters are zero and the old account is closed but its credential is retained until timeout. Repeat with Windows Hello and phone on the correctly trusted HTTPS origin. Remote phone support is not established by localhost software-authenticator tests.

Stop after phase 3. No definitive v0.5 release is delivered before physical acceptance and all subsequent approved phases.


### Phase-3 final test results

| Runner | Result |
| --- | --- |
| Entire regression dependency graph, isolated build | Passed; 0 warnings, 0 errors. |
| .NET web/service/HTTPS checks | 307 passed, exit code 0. |
| JavaScript WebAuthn/localization checks | 35 passed, 0 failed, exit code 0. |
| Windows simulator smoke | Passed, including 2778 ATT&CK/status/export assertions and monitor/client checks; exit code 0. |
| WPF integration | Passed; exit code 0. |

After the closed-account filter was added, the affected web build and all 307 web checks were rerun successfully. WPF and simulator source remained unchanged. All four regression runners were executed; the live demonstration process was not stopped or used for test accounts. No physical device result or real-browser screenshot is fabricated by these tests.


## Fase 4 — beveiligde presentatoromgeving

Fase 4 vervangt de gedeelde wachtwoordaanmelding door persoonlijke accounts met WebAuthn. Er is geen wachtwoordfallback, standaardaccount of openbare accountaanmaak. De versie blijft **0.4.1** totdat de volledige v0.5 en fysieke acceptatie zijn afgerond. Fase 5 (trekking/prijsuitreiking) is niet geïmplementeerd.

### Rollen en identiteit

- **Administrator:** accounts aanmaken, rollen/activering beheren, sleuteluitnodigingen uitgeven en sleutels verwijderen; evenementen starten/afsluiten/hernoemen; statistieken en audit bekijken.
- **Presentator:** dashboard, eigen sleutels, extra eigen sleutel na herauthenticatie, evenementen starten/afsluiten en statistieken bekijken. Geen account-/rolbeheer, auditinzage of beveiligingsinstellingen.

Elke beveiligde pagina heeft een server-side policy. Beheeracties controleren bovendien de actuele accountrol, accountstatus en server-side sessie binnen de schrijftransactie. Alleen knoppen verbergen is nooit de controle. Rollen wijzigen en accounts uitschakelen trekken alle sessies van het doelaccount in. Acties voor andere accounts trekken de eigen of andere onafhankelijke presentatorsessies niet algemeen in.

De laatste actieve administrator met een geregistreerde sleutel kan niet worden gedegradeerd of uitgeschakeld. Accounts zonder sleutel kunnen niet handmatig als actieve administrator worden gebruikt om deze controle te omzeilen. De laatste sleutel van een account kan niet worden verwijderd. Deze controles staan in onmiddellijke SQLite-transacties, ook bij gelijktijdig beheer door twee administrators.

Presentatoraccounts, hun 32-byte user handles, credentials, sessies, uitnodigingen en challenges gebruiken afzonderlijke tabellen en cookies. Bezoekerscredentials zijn niet bruikbaar voor een presentatoraccount, ook niet op dezelfde fysieke YubiKey of RP ID. Bezoekers- en presentator-taalvoorkeuren blijven apart.

### Eerste administrator instellen

Gebruik een onderhoudsvenster. Stop een eventuele oude webversie; laat oude en nieuwe versies niet tegelijk dezelfde database beheren. Maak vooraf met de normale beheerprocedure een consistente backup van de database en bewaar de bestaande Data Protection-sleutels bij dezelfde installatie. De oorspronkelijke v0.4.1-bronbaseline is niet overschreven.

1. Bouw de nieuwe webversie in een aparte uitvoermap. Controleer welke **bestaande** database en datamap de server gebruikt; kies niet per ongeluk een nieuw leeg bestand. Er worden geen oude bezoekersaccounts omgezet in presentatoren.
2. Open een verhoogde PowerShell-terminal met dezelfde Windows-gebruiker/profielcontext als de webapp. De CLI controleert het verhoogde administratortoken. Op Linux is root vereist; zorg dat de runtimegebruiker daarna toegang houdt tot de database/datamap (0700/0600) en zijn bestaande Data Protection-sleutels. De opdracht is niet publiek via HTTP aanroepbaar.
3. Gebruik dezelfde configuratie, RP ID en HTTPS-origin als de uiteindelijke installatie. Wissel niet tijdelijk van RP ID om bootstrap te doen: zo'n wijziging kan zowel bezoekers- als presentatorpasskeys onbruikbaar maken.

Voor de bestaande lokale Development-installatie, met het eerder vertrouwde localhost-certificaat:

```powershell
# Alleen een voorbeeld voor de bestaande standaarddatamap; controleer het werkelijke pad.
$env:Database__Path = Join-Path $env:LOCALAPPDATA 'LummaSimulator\PasskeyPortal\portal.db'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet C:/src/LummaSimulator\verification\bin\phase4-suite\LummaSimulator.Web.dll --bootstrap-administrator
```

De opdracht vraagt een persoonlijke accountnaam (3–64 ASCII-letters/cijfers/punt/underscore/streepje), weergavenaam en de expliciete bevestiging `BOOTSTRAP`. Ze bindt geen webpoort. Ze maakt uitsluitend een nog niet geactiveerd administratoraccount en geeft één cryptografisch willekeurige autorisatiecode. Alleen de SHA-256-hash wordt opgeslagen. Kopieer de code niet naar een URL, script, configuratie, screenshot of applicatielog.

4. Start de bijgewerkte webserver in de normale runtimecontext met de gecontroleerde database en bestaande HTTPS-configuratie. Bijvoorbeeld voor localhost:

```powershell
$env:ASPNETCORE_URLS = 'https://localhost:7243'
dotnet C:/src/LummaSimulator\verification\bin\phase4-suite\LummaSimulator.Web.dll
```

5. Open op **dezelfde computer**, rechtstreeks via de geconfigureerde vertrouwde HTTPS-origin, `/Presenter/Enroll`. Voer daar de code en een herkenbare sleutelnaam in en registreer de primaire sleutel. Bootstrap vraagt een loopbackverbinding zonder forwarded headers, naast de geheime autorisatie. Maak bootstrap niet via een publieke reverse proxy bereikbaar; IP-detectie vervangt de autorisatiecode niet. Bij een productiehostnaam moet dezelfde naam lokaal via loopback bereikbaar zijn en het bestaande certificaat daarvoor geldig en vertrouwd zijn.
6. De code en browserregistratieautorisatie verlopen uiterlijk tien minuten na uitgifte; inwisselen verlengt die termijn niet. De code kan eenmaal worden ingewisseld. Een browserregistratie kan binnen de resterende termijn na annulering worden hervat. Na succesvolle registratie verdwijnen de code/autorisatie definitief. Registratie maakt nog geen aangemelde sessie: meld daarna aan op `/Presenter/Login` door de geregistreerde passkey te kiezen, zonder accountnaam in te voeren.
7. Als de eerste bootstrap verloopt of wordt afgebroken vóór registratie, kan de lokale verhoogde opdracht opnieuw worden uitgevoerd. Dit maakt eerdere bootstrapcodes/grants ongeldig. Zodra ooit een eerste account succesvol is geactiveerd, is deze bootstraproute permanent gesloten; er is geen permanente noodcode.
8. Registreer en test direct een reservesleutel via `/Presenter/Security`. Bewaar deze afzonderlijk. Als alle administrators hun sleutels verliezen, is er in deze fase geen automatische herstel- of wachtwoordroute. Gecontroleerd installatieherstel uit backup of een afzonderlijk ontworpen lokale herstelprocedure is dan nodig. Dit is bewust geen verborgen achterdeur.

De ontwikkeltests gebruiken uitsluitend een interne testtoegang tot de uitgiftemethode voor hun eigen tijdelijke databases; de productie-HTTP-laag kan deze methode niet aanroepen. De echte OS-bootstrap en fysieke sleutelinteractie moeten handmatig worden geaccepteerd.

### Persoonlijke accounts en veilig sleutelbeheer

Een administrator bevestigt eerst opnieuw zijn identiteit en maakt daarna een account met rol aan op `/Presenter/Accounts`. De korte code wordt één keer in het beveiligde antwoord getoond en veilig aan de bedoelde persoon overgedragen. Deze opent `/Presenter/Enroll` op de geldige HTTPS-origin; gewone uitnodigingen werken ook vanaf een andere computer. Een code maakt maximaal één registratieautorisatie. Er is geen publiek endpoint om zonder zo'n autorisatie een account aan te maken.

Iedere account kan maximaal tien credentials hebben, met eigen naam, registratie- en laatste-gebruiksdatum. Een eigen extra sleutel vereist expliciete WebAuthn-herauthenticatie in dezelfde sessie. Deze autorisatie geldt maximaal vijf minuten voor het starten van één beveiligingswijziging; de registratiechallenge zelf duurt maximaal twee minuten. Annuleren herstelt geen verbruikte herauthenticatieautorisatie.

Administratieve account-/rolwijzigingen, sleuteluitnodigingen en sleutelverwijdering vereisen eveneens zo'n eenmalige verse herauthenticatie. Verwijdering trekt doelaccountsessies in en bewaart minimaal één sleutel. Veilige vervanging bij verlies/compromittering: schakel het doelaccount uit, geef na herauthenticatie een hersteluitnodiging uit, registreer de nieuwe sleutel, verwijder de oude sleutel en schakel daarna het account expliciet weer in. Een nieuwe sleutelregistratie heractiveert een eerder uitgeschakeld account **niet** automatisch.

De UI gebruikt beheerreferenties voor sleutelverwijdering, niet de volledige WebAuthn-credential-ID. Namen zijn labels; geen bewijs dat een sleutel van een bepaald merk is.

### WebAuthn en sessiebeveiliging

- De bestaande Fido2 4.3.1-verifier controleert onder meer challenge, origin, RP ID, credential-ID, publieke sleutel, handtekening en gebruikersverificatie. User Verification en resident credentials zijn verplicht. Er worden geen privésleutels ontvangen.
- Attestation is `none`; certificaatattestation wordt niet geaccepteerd. Daardoor zijn er geen externe metadatadiensten nodig, maar het merk YubiKey of fysiek/hardwaregebonden karakter wordt niet cryptografisch vastgesteld. Er wordt geen authenticatorAttachment, transport of hint geforceerd. Browser-/authenticatorondersteuning bepaalt de werkelijk beschikbare keuze.
- Signature counters worden door de verifier behandeld: constante nul is toegestaan bij authenticators zonder teller; verdachte niet-oplopende niet-nultellers worden afgewezen. De permanente backup-eligible flag wordt aanvullend vergeleken met registratie. Zie de [WebAuthn-specificatie](https://www.w3.org/TR/webauthn-3/).
- Challenges zijn browsergebonden; registratie en herauthenticatie en bestaande oudere loginchallenges zijn ook accountgebonden. Nieuwe normale loginchallenges ontdekken het account via de credential. Ze zijn maximaal twee minuten geldig en worden vóór verificatie atomair eenmalig verbruikt. De operatie-ID beschermt cancel tegen het wissen van een nieuwere aanvraag. Web Locks en busy-guards voorkomen concurrerende aanvragen vanuit dezelfde browsercontext. Afgeronde operatiecontrollers worden expliciet afgesloten, zonder afgeronde verificatie terug te draaien.
- `__Host-Lumma.Presenter` is Secure, HttpOnly, SameSite=Strict. De cookie bevat een vers, door de server gegenereerd sessiegeheim in het beveiligde ticket; SQLite bewaart alleen de hash. `Presenter:SessionMinutes` is standaard 30 en wordt begrensd tot 5–30 minuten, zonder sliding expiration. De database-expiratie is leidend.
- Op iedere aanvraag wordt de actuele sessie/accountstatus gecontroleerd volgens het [ASP.NET Core ValidatePrincipal-model](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0#react-to-back-end-changes). Uitloggen trekt alleen de eigen sessie in. Rollen wijzigen, uitschakelen en sleutels verwijderen trekken gericht doelsessies in.
- Presentatoridentiteit wordt niet toegepast op bezoekersroutes. Zo blijft een al geopende bezoekerspagina met zijn bestaande CSRF-token werken als dezelfde browser daarna op het presentatordashboard inlogt. `/api/language` gebruikt expliciet de CSRF-identiteit van de gekozen doelgroep.
- Alle wijzigingen vereisen POST en antiforgery. WebAuthn-API's controleren de exacte Origin. Authenticatie-/registratieaanvragen zijn beperkt tot 40 per minuut, beheer tot 60 per minuut; de bestaande bezoekerslimieten blijven. Dit zijn gedeelde installatielimieten, geen vervanging voor infrastructuurbeveiliging bij publieke distributie.
- Vervallen presentatorsessies, uitnodigingen, registratiegrants en challenges worden opgeruimd. Bezoekerscleanup verwijdert nooit persoonlijke presentatoraccounts, keys of auditregels.

### Dashboard en evenementen

`/Presenter` toont actieve evenementstatus, unieke succesvolle **demoaccounts**, totale/eventauthenticaties, actieve tijdelijke accounts, geregistreerde bezoekerspasskeys, UTC-dagtellingen en historie. Een demoaccount is geen bewezen unieke natuurlijke persoon.

Beide rollen mogen met expliciete bevestiging een evenement starten of afsluiten. Alleen administrators mogen een evenement hernoemen. Vergelijking met het verwachte actieve evenement en een onmiddellijke SQLite-transactie voorkomen dat twee gelijktijdige acties elkaar stilzwijgend overschrijven. Een nieuw evenement behoudt alle historie en krijgt alleen zijn eigen nieuwe, nog uitgeschakelde prijzenpot.

Na afsluiten kan er nul actief evenement zijn. De openbare demo en WebAuthn blijven werken: nieuwe successen komen dan in het herkenbare historische statistiekvak `Open demo`, zonder een nieuw beheerd evenement of prijzenpot aan te maken. Een herstart heropent het afgesloten evenement niet. Eerder getelde accounts behouden hun eerste bezoekersnummer en evenement; herhaalde authenticaties verhogen geen bezoekerstelling. Prize Experience blijft uit; er is geen trekking, uitreiking of vrijgave van reeds toegekende prijzen.

### Opslag en audit

Fase 4 voegt behoudend toe: `PresenterAccounts`, `PresenterKeys`, `PresenterAuthSessions`, `PresenterInvitations`, `PresenterEnrollmentGrants`, `PresenterCeremonies`, `PresenterAudit` en migratiemarkering 4. Alle opslag blijft centraal in dezelfde SQLite-installatie. Er worden geen oude bezoeker- of presentatorgegevens verwijderd of als persoonlijk account geïmporteerd. Oude gedeelde wachtwoordtickets worden afgewezen; `Presenter__PasswordHash` en de oude hash-CLI geven geen toegang meer.

Auditregels bevatten UTC-tijd, handelende interne account-ID, actie en doelaccount/evenement of gevalideerde DemoRunId. Ze registreren bootstrap, registratie, geslaagde/afgewezen ceremonies, aan-/afmelding, herauthenticatie, account-/rol-/statuswijzigingen, uitnodigingen, sleutelverwijdering en evenementacties. Demo-/monitoracties worden als aanvraag vastgelegd, zodat een mislukte uitvoering niet als bewezen succes wordt gepresenteerd. Er worden geen codes, cookies, sessiegeheimen, volledige credential-ID's, challengewaarden, privésleutels of browserfingerprints gekopieerd.

Alleen administrators kunnen de gepagineerde auditpagina lezen. Er is geen verwijderactie; SQLite-triggers weigeren normale UPDATE/DELETE-opdrachten op auditregels. Dit is applicatiegebonden append-only opslag, **geen** cryptografisch onveranderbare/WORM-opslag: een beheerder met volledige database-/bestandssysteemrechten kan triggers of bestanden wijzigen. Audit en persoonlijke presentatoraccounts blijven bewaard tot een expliciete toekomstige beheer-/archiveringsprocedure; geen automatische bezoekersretentie wordt erop toegepast. Bescherm datamap, backups, machineaccount en Data Protection-sleutels overeenkomstig hun gevoeligheid.

### HTTPS vanaf een andere computer

Alleen bootstrap is lokaal beperkt. Normale persoonlijke login, dashboard en geautoriseerde uitnodigingsregistratie hebben geen loopbackbeperking. Gebruik voor andere computers een expliciete bereikbare hostnaam, passende RP ID en exacte HTTPS-origin, een vertrouwd certificaat met die DNS-naam, een geschikte listener en een bewust geopende firewallroute. Voor de bestaande productieconfiguratie is de origin `https://passkey.example.invalid` op poort 443. De andere computer moet die naam naar de server kunnen oplossen. `localhost` verwijst op iedere computer naar zichzelf en is daarvoor geen vervanging.

Configureer listener/certificaat buiten de publieke bronconfiguratie, bijvoorbeeld via Kestrel of een expliciet vertrouwde reverse proxy. Bewaar PFX-bestanden/privésleutels en wachtwoorden buiten de repository. Wijzig geen bestaande RP ID automatisch. Bij een proxy blijven de bestaande exacte KnownProxies-controles gelden. SQLite ondersteunt hier meerdere gelijktijdige presentatoren op één installatie; gedistribueerde hosts met losse lokale databases worden niet als gedeelde installatie ondersteund.

### Fase-4-bestanden en acceptatie

De kern staat in `Services/PresenterAuthentication.cs`, `PresenterManagement.cs`, `PresenterEndpoints.cs` en `LocalMaintenance.cs`. Gewijzigd zijn ook `Program.cs`, `DemoStore.cs`, `PresenterService.cs` (housekeeping), de evenementservice, presentatorpagina's, `wwwroot/js/presenter.js`, lokale resources, CSS en tests. `Properties/TestAccess.cs` geeft alleen de testassembly toegang tot interne test-/onderhoudsmethoden; er is geen HTTP-testachterdeur. README verwijst naar deze actuele bootstrapprocedure.

Automatische tests omvatten echte Fido2-verificatie, twee credentials, nul-tellers, afwijzing van bezoekerscredentials, capability/challenge-expiratie en replay, verse herauthenticatie, onafhankelijke sessies, onmiddellijke accountintrekking, sleutelherstel terwijl het account uitgeschakeld blijft, bescherming tegen gelijktijdig verwijderen van de laatste administrator, auditbescherming, echte HTTPS-login en rolgrenzen. JavaScript-tests controleren de afzonderlijke presentatorcoördinator inclusief annulering, opnieuw proberen en veilige navigatie na verify. Alle bestaande bezoekers-, statistiek-, WPF- en simulatortests blijven onderdeel van de regressieronde.

Handmatige acceptatie blijft nodig:

1. Voer de verhoogde bootstrap uit op de juiste database/origin; registreer de fysieke primaire YubiKey en meld aan. Test dat de code niet herbruikbaar is.
2. Herauthenticeer, registreer een reservesleutel en test beide afzonderlijk met herhaald aanmelden zonder refresh.
3. Maak een persoonlijk presentatoraccount aan en test gelijktijdige toegang vanaf een tweede browser/computer via vertrouwd HTTPS. Controleer rolgrenzen, accountuitschakeling en onafhankelijke logout.
4. Test starten/afsluiten van een evenement door twee presentatoren, statistieken/historie, naamwijziging door administrator en de openbare demo zonder actief evenement.
5. Test verlies/herstel van een sleutel zonder het uitgeschakelde account tussentijds toegankelijk te maken; controleer de auditregels.
6. Controleer Nederlands/Engels, mobiele layout en bezoeker plus presentator in dezelfde browser. Nieuwe fysieke en visuele acceptatie is niet bewezen door software-authenticator- of Node-tests.

Er zijn geen live accounts gebootstrapt, certificaten/RP ID's gewijzigd of live gegevens gemigreerd door deze ontwikkeltests. Die gebruiken uitsluitend eigen testdatabases en tijdelijke HTTPS-certificaten. Geen definitieve v0.5-release en geen fase 5 zonder verdere acceptatie.


### Fase-4-eindvalidatie en bestandslijst

| Controle | Resultaat |
| --- | --- |
| Volledige regressieprojectstructuur, aparte build-output | Geslaagd, 0 waarschuwingen en 0 fouten. |
| .NET-services en echte HTTPS-integratie | 358 controles geslaagd, exitcode 0. |
| JavaScript, bezoekers en presentatoren | 41 tests geslaagd, 0 mislukt. |
| Simulator-smoke-tests | Geslaagd, inclusief 2778 ATT&CK/status/exportasserties en monitor/clientcontroles. |
| WPF-integratie | Geslaagd, exitcode 0. |

Alle vier regressierunners zijn uitgevoerd. De laatste gerichte webtests omvatten de volledige nieuwe rollen-/sessieflow en de correctie voor bezoekers-CSRF na presentatorlogin in dezelfde browser. De presentatorcoördinator deelt bewust de bestaande `lumma-passkey-operation` Web Lock met bezoekers: op dezelfde browser/origin starten geen twee native WebAuthn-dialogen tegelijk. Identiteiten, cookies, credentialtabellen en autorisatie blijven gescheiden.

Logs staan onder `verification/bin/phase4-web-results.log`, `phase4-js-results.log`, `phase4-smoke-results.log` en `phase4-wpf-results.log`. Bouw en voer de webtests uit met `LUMMA_TEST_WEB_DLL` gericht op `verification/bin/phase4-suite/LummaSimulator.Web.dll`. De aparte buildmap is ontwikkeluitvoer, geen distributiepakket; publiceer later uitsluitend het webproject, niet de testassemblies.

Nieuw in fase 4:

- `LummaSimulator.Web/Services/PresenterAuthentication.cs`, `PresenterManagement.cs`, `PresenterEndpoints.cs`, `LocalMaintenance.cs`.
- `LummaSimulator.Web/Properties/TestAccess.cs`.
- `LummaSimulator.Web/Pages/Presenter/Enroll.cshtml` en `.cshtml.cs`.
- `LummaSimulator.Web/Pages/Presenter/Security.cshtml` en `.cshtml.cs`.
- `LummaSimulator.Web/Pages/Presenter/Accounts.cshtml` en `.cshtml.cs`.
- `LummaSimulator.Web/Pages/Presenter/Audit.cshtml` en `.cshtml.cs`.
- `LummaSimulator.Web/Pages/Presenter/Denied.cshtml`.
- `LummaSimulator.Web/wwwroot/js/presenter.js`.
- `tests/LummaSimulator.Web.Tests/PresenterChecks.cs`.
- `tests/WebAuthn.Ux.Tests/presenter.test.cjs`.

Gewijzigd in fase 4:

- `LummaSimulator.Web/Program.cs`, `appsettings.json`.
- `LummaSimulator.Web/Services/DemoStore.cs`, `PresenterService.cs`, `ExperienceStatistics.cs`.
- `LummaSimulator.Web/Localization/UiText.cs` en de drie `Resources/Localization/UiText*.resx`-catalogi.
- `LummaSimulator.Web/Pages/Presenter/Index.cshtml`, `Index.cshtml.cs`, `Login.cshtml`, `Login.cshtml.cs`.
- `LummaSimulator.Web/wwwroot/css/site.css`.
- `tests/LummaSimulator.Web.Tests/Program.cs`, `HttpChecks.cs`, `ExperienceChecks.cs`.
- `tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj`.
- `README.md` en deze handleiding.

De bezoekers-WebAuthn-JavaScriptcode, bezoekerscredentialverificatie, RP ID/origins en WPF-productiecode zijn in fase 4 niet gewijzigd. De noodzakelijke wijziging aan de HTTP-authenticatiescope beschermt juist de bestaande bezoeker-CSRF/sessie tegen een gelijktijdige presentatorlogin. Fysieke bootstrap/primary-reserve-YubiKey, echte toegang vanaf een tweede computer en visuele desktop/mobielacceptatie blijven open. De ontwikkelwerkzaamheden eindigen na fase 4; er is geen definitieve v0.5-release of fase-5-implementatie opgeleverd.


## Gebruikersnaamloze presentatorlogin

De versie blijft **0.4.1**. Deze wijziging betreft uitsluitend de normale presentatorlogin; fase 5 is niet gestart. De bezoekerflow, bootstrap, uitnodigingen, herstel en accountgebonden herauthenticatie blijven behouden.

### Gedrag en verificatie

`/Presenter/Login` vraagt geen accountnaam. De browser krijgt assertion-options met `allowCredentials: []`, de bestaande RP ID en `userVerification: required`. De JavaScript-coördinator geeft de lege lijst ongewijzigd door aan `navigator.credentials.get()`. Er worden geen attachment, transport, provider of hints geforceerd. Alle teksten blijven in de gezamenlijke offline NL/EN-resourcecatalogus.

De server verbruikt de browser-/operatiegebonden challenge vóór verificatie in een afzonderlijk gecommitte SQLite-transactie. Hij zoekt de ontvangen credential-ID uitsluitend op in `PresenterKeys`. Een aangeleverde accountnaam wordt voor normale login genegeerd, evenals de identiteit van een eventuele bestaande sessie bij het kiezen van het doelaccount. Een andere geldige presentatorcredential meldt aan bij het **eigen** account; deze kan nooit als een vooraf opgegeven ander account worden gebruikt. Voor oudere accountgebonden loginchallenges en herauthenticatie wordt een credential van een ander account afgewezen.

Na het opzoeken volgen de bestaande Fido2-controles voor challenge, origin, RP ID, credential-ID, publieke sleutel, handtekening en verplichte user verification. Een normale login vereist bovendien een aanwezige, niet-lege user handle die bytegewijs overeenkomt met de opgeslagen handle van de credentialeigenaar. De verifier controleert de signature counter; constante nul blijft ondersteund. De permanente backup-eligibility wordt vergeleken met registratie. Alleen een actief account krijgt een nieuwe sessie. Opnieuw inloggen vervangt uitsluitend de meegezonden browsersessie, nooit alle sessies van het account of van andere presentatoren.

Herauthenticatie gebruikt nog steeds uitsluitend de credentials van de actuele, server-side gecontroleerde accountsessie. De bestaande operatie-ID's, Web Lock, busy-guard, expiratie, replaybescherming, exacte Origin-controle, CSRF, rate limiting en cookiebeveiliging zijn behouden. Er zijn geen nieuwe gevoelige logvelden toegevoegd.

### Transactionele databasemigratie

Nieuwe marker: `ExperienceMigrations.Version = 5`. Dit is een **schemamarkering**, geen implementatie van ontwikkelfase 5 of Prize Experience.

Binnen de bestaande `BEGIN IMMEDIATE`-initialisatietransactie wordt alleen `PresenterCeremonies` opnieuw opgebouwd. `AccountId` wordt nullable, met `CHECK(AccountId IS NOT NULL OR Kind='login')`. De foreign key naar `PresenterAccounts(Id)` blijft bestaan; foreign keys worden nergens uitgeschakeld. Alle zeven velden van iedere bestaande ceremonie worden gekopieerd, inclusief opties, challenge-expiratie, oorspronkelijke accountkoppeling en sessie-/browserbinding. De tabelvervanging en marker worden samen gecommit. Bij een fout wordt de volledige omzetting teruggedraaid. Herhaald initialiseren voert de gemarkeerde migratie niet opnieuw uit.

De migratie controleert vooraf de kolommen en bestaande uitgaande foreign key, onverwachte inkomende foreign keys en eigen indexen/triggers. Een afwijking stopt de migratie voor handmatige beoordeling in plaats van gegevens of schema-uitbreidingen stilzwijgend te verwijderen. `PRAGMA foreign_key_check` wordt vóór en na de omzetting uitgevoerd. De migratie wijzigt geen accounts, credentials, sessies, uitnodigingen, auditregels of bezoekers/statistieken. Ook actieve oudere login-, registratie- en herauthenticatieceremonies blijven bruikbaar met hun oorspronkelijke beveiligingsbinding en uiterste geldigheidsduur; de upgrade verlengt die niet.

De ontwikkeltests hebben uitsluitend afzonderlijke tijdelijke testdatabases gebruikt. **De draaiende live applicatie is niet herstart en de live database is niet gemigreerd.** Pas de upgrade toe in een onderhoudsvenster met een consistente databasebackup en de bestaande Data Protection-sleutels. Stop de oude server voordat de nieuwe build dezelfde database opent. Start geen oude build naast de nieuwe: oude code verwacht een niet-lege accountkoppeling voor iedere loginchallenge. Terugzetten vereist de consistente pre-upgrade databasebackup met bijbehorende applicatie; verwijder nooit passkeys of Data Protection-sleutels als workaround.

### Regressievalidatie

Alle runners zijn uitgevoerd met aparte build-output `verification/bin/discoverable-suite`; bestaande bestanden van het draaiende webprogramma zijn niet overschreven. Resultaten van deze wijziging staan onder `verification/bin/discoverable-*-results.log`.

| Controle | Werkelijk uitgevoerd resultaat |
| --- | --- |
| Build volledige regressieprojectstructuur | Geslaagd, 0 waarschuwingen, 0 fouten. |
| .NET-services en echte HTTPS-integratie | 411 controles geslaagd, exitcode 0. |
| JavaScript bezoekers/lokalisatie/presentatoren | 43 tests geslaagd, 0 mislukt, 0 overgeslagen. |
| Simulator-smoke | Geslaagd, exitcode 0; inclusief 2778 ATT&CK/status/exportasserties en monitorcontroles. |
| WPF-integratie | 17 controles geslaagd, exitcode 0. |

Logbestanden: `verification/bin/discoverable-build-results.log`, `discoverable-web-results.log`, `discoverable-js-results.log`, `discoverable-smoke-results.log`, `discoverable-wpf-results.log`.

De vier runners uit het bestaande regressieproject zijn rechtstreeks vanuit de aparte build-output gestart; er wordt geen aanvullende `dotnet test`-run geclaimd. De laatste webbuild en webrun omvatten ook de aanvullende schemavalidatie en migratie-rollbacktests.

De nieuwe tests verifiëren login zonder naam met twee credentials, onbekende credential, onjuiste/lege/ontbrekende handle, verkeerde signature/origin/RP ID/UV/challenge/backup-eligibility/counter/ID-combinatie, replay inclusief concurrente replay, verlopen challenges, verkeerde browser, onafhankelijke gelijktijdige presentatoren en gerichte sessievervanging. Bestaande tests bevestigen accountuitschakeling, accountgebonden herauthenticatie en bezoekers-WebAuthn. Echte HTTPS-tests bevestigen naamloze opties/login en scheiding van bezoekers-/presentator-CSRF. Migratietests vergelijken alle synthetische bestaande tabelrijen vóór en na de upgrade, verifiëren actieve ceremonies en foreign keys, herhaalbaarheid, terugdraaien van een geïnjecteerde fout na tabelvervanging en veilig stoppen bij onbekende afhankelijkheden/kolommen.

### Resterende acceptatie en beperkingen

1. Meld op de bestaande geldige HTTPS-origin af en open `/Presenter/Login`. Controleer dat er geen accountnaamveld is. Kies de bestaande primaire fysieke YubiKey, voer de PIN in en controleer de juiste accountnaam en rol in het dashboard. Log uit en herhaal met de reservesleutel; herhaal beide meerdere keren.
2. Test een ondersteunde passkeymanager met een reeds geregistreerde discoverable presentatorcredential. De browser bepaalt de beschikbare selectie. Software-authenticators en Node-mocks bewijzen geen compatibiliteit met een specifieke fysieke sleutel of provider.
3. Gebruik twee verschillende presentatoraccounts tegelijk in twee browsers/computers. Controleer juiste identiteit, onafhankelijke sessies en logout. Test dat uitschakelen toegang intrekt en dat een bezoekerspasskey geen presentatortoegang geeft.
4. Controleer herauthenticatie: uitsluitend eigen accountcredentials mogen een beheeractie vrijgeven. Controleer ook de bestaande uitnodigings-/herstelprocedure en NL/EN-taalwisseling tijdens de login.
5. Test de upgrade eerst op een afgeschermde, consistente kopie van de eigen installatie met ongewijzigde RP/origin en beschermd sleutelbeheer. Een testdatabase met software-authenticators vervangt deze installatieacceptatie niet.

Niet-discoverable credentials verschijnen niet bij lege `allowCredentials`; er is bewust geen gebruikersnaamfallback toegevoegd. Bestaande presentatorregistratie vereist al resident credentials, maar de daadwerkelijke beschikbaarheid in de browser/passkeymanager moet fysiek worden geaccepteerd. Een passkey van een andere applicatie kan door de browser worden aangeboden als die dezelfde RP ID gebruikt; de server accepteert uitsluitend opgeslagen presentatorcredentials en een overeenkomende handle. Er wordt geen YubiKey-merk of fysiek karakter geclaimd.

### Bestanden van deze wijziging

Productie:

- `LummaSimulator.Web/Services/PresenterAuthentication.cs`.
- `LummaSimulator.Web/Services/PresenterLoginMigration.cs` (nieuw).
- `LummaSimulator.Web/Pages/Presenter/Login.cshtml`.
- `LummaSimulator.Web/wwwroot/js/presenter.js`.
- `LummaSimulator.Web/Resources/Localization/UiText.resx`, `UiText.nl-NL.resx`, `UiText.en-GB.resx`.

Tests:

- `tests/LummaSimulator.Web.Tests/DiscoverablePresenterChecks.cs` (nieuw).
- `tests/LummaSimulator.Web.Tests/PresenterMigrationChecks.cs` (nieuw).
- `tests/LummaSimulator.Web.Tests/PresenterChecks.cs`.
- `tests/LummaSimulator.Web.Tests/HttpChecks.cs`.
- `tests/WebAuthn.Ux.Tests/presenter.test.cjs`.

Documentatie: `README.md` en dit document. Geen configuratie-, certificaat-, RP ID-, live account- of Data Protection-wijzigingen. Gegenereerde build-/testuitvoer is geen distributiepakket.


## Fase 5 - Prize Experience: 5.1 en evenementbeheer 5.2a gereed

Datum: 30 oktober 2026. Deze sectie is de actuele fase-5-roadmap en vervangt uitsluitend eerdere toekomstverwijzingen naar Prize Experience. Historische fase-2/3/4-resultaten blijven checkpoints, geen bewijs van fase-5-implementatie. De huidige webversie blijft 0.4.1; schemamarker 5 hoort bij presentatorlogin en is geen Prize Experience.

- [Functionele specificatie en besluitregister](phase5-prize-experience-functional.md)
- [Technische architectuur, gecontroleerde baseline en twaalf verplichte controles](phase5-prize-experience-architecture.md)
- [Werkpakketten, testpoorten en resultaten 5.1/5.2a](phase5-prize-experience-implementation.md)

De read-only bronanalyse bevestigt: bestaande bezoekersaccounts zijn tijdelijk en cookiegebonden; maximaal vijf passkeys per account zijn al mogelijk; de statistieken hebben één actief evenement; SQLite gebruikt directe transacties; SignalR is nog niet aangesloten. Het ontwerp behoudt bestaande statistieken/authenticatie en voegt afzonderlijke gelijktijdige PrizeEvents, retentiebindingen voor deelnemende accounts en aanvullende bezoekersherstellogin toe. Alle kritieke acties worden transactioneel vastgelegd; realtimepresentatie is een projectie daarvan.

Belangrijkste functionele besluiten: Cybertruck is standaard besloten (openbaar verplicht startcodes); twee minuten is reactietermijn, geen automatische hertrekking; ooit gewonnen deelnemers blijven uitgesloten, ook na verval; claims/voorraad blijven bij uitschakelen bewaard; heropening verlengt de oorspronkelijke bewaarbovengrens niet automatisch. Profielen zijn snapshots, laatst gebruikt installatiebreed.

| Pakket | Status |
| --- | --- |
| 5.1 Architectuur en opslagfundament | Geïmplementeerd en getest; vaste migratie 6, geen publieke Prize-functionaliteit |
| 5.2 Evenementen, profielen en deelnemers | 5.2a evenementbeheer/profielen geïmplementeerd en getest; 5.2b niet gestart |
| 5.3 Trekking, prijzen en voorraad | Gespecificeerd |
| 5.4 Realtime presentatie | Gespecificeerd; lokale SignalR-client/QR-keuze nog vastzetten |
| 5.5 Claims, persoonsgegevens en bewaartermijnen | Gespecificeerd; productiegebruik geblokkeerd tot volledige keten gereed |
| 5.6 Rapportage, exports en acceptatietests | Gespecificeerd; fysieke/operationele acceptatie nog uit te voeren |

Werkpakket 5.1 is na expliciete opdracht uitgevoerd. Vaste migratie 6 is uitsluitend op synthetische databases getest; bestaande ontwikkeldatabase en draaiende server zijn niet geüpgraded. Resultaten: 467 web-/WebAuthn-checks, 43 JavaScripttests, smoke en WPF geslaagd; builds zonder waarschuwingen/fouten. Geen publieke deelname/trekking/claims/SignalR/QR/export/purge. Herstelbaseline: C:/Example/Baselines/phase5-51-20261030-210247. Zie het implementatieplan voor bestanden, objecten, herstelprocedure, logs en risico's. Alle accountverwijderpaden inclusief SessionStatusService.GetAsync aanpassen is een verplichte acceptatiepoort voor 5.2. Dit is het historische 5.1-checkpoint; de actuele uitvoering van 5.2a staat hieronder.


### Actueel resultaat 5.2a

Beschermd evenementbeheer en profielen via /Presenter/Prizes zijn gereed: overzicht, snelle start, instellingen, sluiten/heropenen, profielkopie en installatiebreed laatst gestart profiel met Cybertruckterugval. Alle beheeracties gebruiken bestaande passkeyautorisatie, servervalidatie, idempotentie, transacties en revisies. Migratie 6 en bestaande WebAuthn/lifecycle/statistiekcode zijn ongewijzigd. PrizeEnabled blijft 0, ook na beheerstart; geen bezoekersdeelname of publieke Prize-routes.

547 webchecks, 43 JavaScript-tests, volledige smoke en 20 WPF-controles geslaagd. Definitieve regressiebouw: 0 fouten/waarschuwingen. Herstelbaseline: C:/Example/Baselines/phase5-52a-20261030-221240. Ontwikkeldatabase niet gemigreerd, server niet herstart. Zie [uitvoeringsrapport](codex-report.md), functioneel F12, architectuur A12 en het implementatieplan voor bewijs en risico's.

5.2b is niet gestart. Alle accountverwijderpaden inclusief SessionStatusService.GetAsync retentiebewust maken blijft verplicht vóór deelnemersregistratie; publieke activering wacht op de gehele retentie-/herstel-/claimketen. Geen implementatie buiten 5.2a.

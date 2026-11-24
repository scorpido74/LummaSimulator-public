# Fase 6.3 — interactieve ClickFix en adaptieve interfaces

Datum: 4 november 2026. Functionele wijziging op basis van `ffe8780`; geen deployment, databasehandeling, certificaatwijziging, commit of push.

## Hergebruik en grenzen

De bestaande ClickFix Razor-pagina, passkeycoördinator, EventEditionService, beschermde manifestopslag, runpairing, lokale HTTPS-polling en expliciete WPF-presentatorbevestiging zijn hergebruikt. Het bestaande choice-/snapshotcontract, endpointautorisatie, WebAuthn-verificatie, CSRF/Origin-controles en loopbackgrenzen zijn ongewijzigd. De identity-auditexporter en Prize-functionaliteit zijn niet aangepast. Het standaard uitgeschakelde integratiemodel en zelfstandige offline WPF blijven bestaan.

De oude ClickFix-oefening bood onmiddellijk volgen/stoppen en uitleg. WPF had een minimale venstermaat van 1100×900 DIP, vaste drie fasekolommen, een vaste beoordelingskolom van 280 DIP en een monitorpaneel van 340 DIP. De webportal had al mobiele CSS, maar onder meer intrinsieke gridbreedtes, marge naast passkeyknoppen, logobreedtes en kleine taalknoppen konden reflow hinderen.

## Bezoekersflow

Na een **server-side geslaagde authenticatie**, nooit alleen na registratie, opent een Windows-geïnspireerd herstelvenster boven de bestaande bevestiging. De bezoeker kan dit sluiten/Escape gebruiken of het herstelvenster openen. De bestaande link naar `/ClickFix` blijft beschikbaar. De ClickFix-pagina blijft zelf de bestaande bezoekers-/aanmeldcontroles toepassen.

De oefening heeft opeenvolgende toestanden: herstelmelding → nagebootst Uitvoeren → fictief herstel → onthulling → afronding. Kopiëren en plakken gebruiken uitsluitend een lokale variabele in de pagina. De tekst `LUMMA-DEMO-ONLY · REPAIR-041 · NOT-AN-OS-COMMAND` is geen OS-opdracht, script, URL of uitvoerbaar bestand. Het echte klembord wordt niet gelezen of gewijzigd. Het readonly oefenveld accepteert geen echte paste-invoer. Er is geen keybinding voor Windows+R; de desktoptekst verwijst expliciet naar de knop voor het nagebootste venster. Onder 768 CSS-pixels vervangt een tikinstructie de Windows-handeling.

Pas **OK in het nagebootste venster** meldt de bestaande `ClickFixFollowSimulated`-keuze. Kopiëren, openen en plakken melden niets. Stoppen vóór OK meldt `ClickFixStopSelected`. Stoppen ná OK toont uitleg en trekt een reeds vastgelegde volgkeuze niet achteraf in. De veilige simulatieaanduiding en stopknop blijven beschikbaar tijdens de oefening. Er is geen download, installatie, shell, echte credential-/cookieverzameling of endpointtest.

Het bestaande contract bewaart één eerste keuze met sequence 1. Retry houdt dezelfde inhoud; reset/herladen wordt geen nieuwe run of nieuwe eventreeks. Een nieuwe gekoppelde demonstratie vereist een nieuwe bezoeker en WPF-run. De browserverklaring bewijst geen menselijke aandacht of daadwerkelijke endpointuitvoering.

## WPF-flow

Bereid de bestaande Event Edition-run voor en koppel die in het presentatorportaal. Laat de bezoeker de ClickFix-pagina pas daarna openen, zodat die een actueel meldticket krijgt. Plaats de tijdelijke leescode in WPF en start de begeleide run.

Event Edition schakelt LIVE uit; LIVE kan tijdens deze modus niet worden ingeschakeld. De bestaande endpointtelemetrie blijft uitsluitend via afzonderlijke, handmatige standalonebediening beschikbaar. Een ontvangen keuze begint geen fase: de presentator moet nog steeds expliciet bevestigen. Bij een bevestigde stopkeuze eindigt de aanvalstak zonder installatie of fases. Bij volgen verschijnt een kort zichtbare **fictieve Lumma Stealer-installatie** in de WPF-status en als `SIM`/`Simulation`/`Simulated`-logregels, gevolgd door de bestaande zes fases. De onthulling wordt eveneens fictief gelogd. Handmatige fallback blijft expliciet gelabeld zonder claim van ontvangen bezoekersactie. Er wordt geen MDR-detectie afgeleid.

Na de WPF-presentatie kiest de bezoeker **Na de presentatie: ontdek wat er gebeurde**. Dit is bewuste bezoekersbediening, geen automatisch door WPF teruggestuurd voltooiingsbericht. De uitleg maakt onderscheid tussen passkeybescherming bij authenticatie en endpointcompromittering. De afsluitende link keert terug naar de bestaande demo; accountbeëindiging blijft de bestaande afzonderlijke expliciete handeling.

## Responsive layout

WPF gebruikt vrije resize vanaf 480×320 DIP, layout rounding, normale WPF-tekst-/DIP-layout en geen algemene Viewbox. De beginmaat wordt op het werkgebied begrensd. Het werkvlak is verticaal scrollbaar; tabs, beoordeling, monitor en log houden eigen begrensde scrollgebieden. Bediening loopt om. Fasekaarten gebruiken één, twee of drie kolommen; beoordelings- en monitorpanelen komen bij beperkte breedte onder elkaar. Teksten, knopinhoud en tabkoppen lopen om; knoppen hebben minimaal 44 DIP hoogte en zichtbare toetsenbordfocus. Bestaande schermen en presentatorcommando's blijven bereikbaar.

Web-CSS gebruikt expliciete mobiele (<768), tablet- (768–1023) en desktopbreedtes, `minmax(0,…)`, afbreekbare lange teksten, passende knopmarges en grotere taalknoppen. Windows-dialogen blijven binnen het document en zijn geen OS-vensters. Resize/rotatie verandert geen JavaScript-toestand en doet geen nieuwe melding. Het aanmelddialoogvenster gebruikt native `dialog` voor focusbeheer en Escape.

## Tests en bewijsstatus

- De vier Node UX-bestanden: **55 tests geslaagd**, inclusief nieuwe copy/paste-/OK-grenzen, externe invoer weigeren, stop tijdens Uitvoeren en herstelvenster uitsluitend na geverifieerde authenticatie.
  Definitieve uitvoer: `artifacts/phase63-8bb08001985c4043b106580440bcdae9/node-acceptance.log`. Een eerdere rerun na een tekstwijziging faalde op hoofdlettergevoelig `review` versus `Review`; de taalassertion is hoofdletterongevoelig gemaakt. De eerdere uitvoer in `node-final.log` blijft behouden.
- XAML/App.xaml en de drie resourcebestanden parsen als XML; geen dubbele resourcesleutels. `git diff --check` geslaagd. JavaScript-browserscript syntaxcontrole geslaagd.
- WPF-smoketests uitgebreid met LIVE-blokkering, fictieve installatie-/onthullingslogging en nul fases bij bevestigde stop. WPF Verification uitgebreid met effectieve DIP-venstermaten voor 1366×768, 1920×1080 en 2560×1440 gedeeld door 1/1,25/1,5/2. **Niet uitgevoerd:** geen nieuwe .NET-build beschikbaar. Deze controles zouden bovendien geen echte Windows-DPI-wijziging bewijzen.
- De bestaande geauthenticeerde ClickFix-browserfixture volgt nu copy/open/paste/OK/reveal en bevat breedtecontroles voor 320/375/390/768/1024/1920, plus halve CSS-viewports als zoom-reflowbenadering. **Niet uitgevoerd:** afhankelijk van de nog niet gebouwde testportal. Dit is geen echte browserzoomtest.
- Nieuwe zelfstandige `tests/WebAuthn.Ux.Tests/responsive-browser.mjs` kan statische Razor-bronpreviews voor registratie, bevestiging, ClickFix-toestanden en afsluiting testen met NL/EN en dezelfde breedtematrix, zonder portal of database. Twee uitvoeringen zijn vóór de eerste layoutmeting vastgelopen op CDP-timeout (`Page.getFrameTree`, vervolgens `Page.enable`). Bewijsdirectories: `artifacts/phase63-responsive-01` en `-02`. **Geen geslaagde browserlayout- of visuele acceptatie geclaimd.** De fixture vervangt geen Razor-rendering of WebAuthn-test.
- Locked restore van alle zeven projecten in de eigen bronkopie `artifacts/phase63-8bb08001985c4043b106580440bcdae9/source` is geblokkeerd door toegang geweigerd tot de bestaande gebruikers-NuGet.Config. Zie de naastliggende `restore.log`. Geen permissie-/configuratieomweg gebruikt. Daardoor zijn solutionbuild, Razor-compilatie, .NET-regressie en WPF-runtime hier **niet bevestigd**.

Geen bestaande portal, gebruikersdatabase, passkey, certificaat of endpoint-/MDR-test is voor deze controles gebruikt. De eerdere ontwikkellauncherwijzigingen blijven afzonderlijk aanwezig; `codex-start.bat` is ongemoeid.

## Nog handmatig accepteren

Na een geslaagde geïsoleerde build: fysieke passkey/YubiKey en beide talen; volledige gekoppelde volgen-/stopflow met twee bezoekers; storing/retry en reset zonder dubbele events; zelfstandige WPF. Controleer echte vensters op 1366×768, 1920×1080 en 2560×1440 bij Windows 100/125/150/200%, inclusief verplaatsen tussen monitoren. Controleer web op de zes breedtes bij **echte** 100/200% browserzoom, toetsenbord/Tab/Escape, zichtbare focus, lange teksten, telefoonrotatie en touchbediening. Controleer dat stop, pairingvelden, beoordeling, monitor, log, exports en afronding bereikbaar blijven. Er zijn in deze sessie geen fysieke apparaat-, Windows-DPI- of visuele schermafbeeldingstests voltooid.

De volledige bestaande regressieorchestrator blijft de integratiegate met expliciet `LUMMA_TEST_WEB_DLL` naar een eigen testbuild. Die suite bevat ook synthetische lokale telemetrie-/sidecarchecks; voer geen operationele endpoint-/MDR-acceptatie uit als bijeffect. Geen publish-/sidecarcontractwijziging in deze fase. Voor deployment of een actieve database-upgrade is afzonderlijke toestemming nodig.

## Gewijzigde bestanden in deze fase

| Onderdeel | Bestanden |
|---|---|
| WPF-flow en layout | `LummaSimulator.App/MainViewModel.cs`, `EventEditionViewModel.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs` |
| Bezoekerspagina's | `LummaSimulator.Web/Pages/ClickFix.cshtml`, `Demo.cshtml` |
| Browsergedrag en styling | `LummaSimulator.Web/wwwroot/js/clickfix.js`, `passkeys.js`, `wwwroot/css/site.css`; nieuw `wwwroot/css/clickfix.css` |
| Talen | `LummaSimulator.Web/Resources/Localization/UiText.resx`, `UiText.nl-NL.resx`, `UiText.en-GB.resx` |
| Tests | `tests/WebAuthn.Ux.Tests/clickfix.test.cjs`, `passkeys.test.cjs`; nieuw `responsive-browser.mjs`; `tests/LummaSimulator.Web.Tests/clickfix-browser.mjs`; `tests/LummaSimulator.SmokeTests/EventEditionChecks.cs`; `verification/Program.cs` |
| Documentatie | `README.md`, dit verslag |

De eerder toegevoegde `Start-LummaDev.bat`, `packaging/Start-LummaDev.ps1`, `verification/Test-LummaDevLauncher.ps1` en `docs/development-start.md` horen bij de voorafgaande launcheropdracht en zijn in deze fase niet aangepast. De staginglijst is leeg; `codex-start.bat` blijft ongetrackt en ongewijzigd.

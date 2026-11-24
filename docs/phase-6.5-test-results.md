# Fase 6.5 — testresultaten en hervatcommando's

Bijgewerkt op 6 november 2026. De gebruikersreferentie van een geslaagde lokale Release-build/volledige regressie, 849 webchecks en eerdere Node-tests is uitsluitend baseline. Die resultaten bewijzen de nieuwe wijzigingen niet.

## Werkelijk uitgevoerd in deze vervolgstap

| Controle | Resultaat |
|---|---|
| Node: passkeys, localization, presenter, clickfix, branding, monitor-keys, presenter-shell | 73 checks: 71 PASS, 0 FAIL, 2 bestaande PE/publish-icoon-SKIP |
| Nieuwe Node SQLite-DDL-test | PASS; 14 exacte schemaobjecten in :memory:, herhaalde DDL, rollback, installatie-resettriggers en owner-deactivatietrigger |
| Nieuwe Node invitationchecks | PASS; alleen expliciete clipboardklik, fragment lokaal verwerken/uit history verwijderen, invoer wissen bij pagehide, geen browseropslag |
| Resource-XML en unieke keys | PASS; drie UiText- en twee DesktopText-catalogi |
| Presenter-literalkeys | Twee onbestaande PresenterPersonalName-referenties gevonden en naar bestaande PresenterDisplayName gecorrigeerd |
| Bron-conflictmarkers | Geen gevonden in gecontroleerde C#/Razor/XAML/JS/resources |
| git diff --check | PASS; alleen bestaande LF/CRLF-conversiewaarschuwingen, geen whitespacefout |
| Opgeslagen bron versus geïsoleerde kopie | SHA-256-vergelijking PASS voor de 293 gekopieerde bronbestanden; daarna bijgewerkte documentatie/testbestand opnieuw gekopieerd |
| Locked .NET restore solution | FAIL, exit 1: unauthorized access tot gebruikers-NuGet.Config, alle zeven projecten |
| C#/Razor/WPF-build | NIET uitgevoerd na geblokkeerde restore; geen geslaagde compilatie geclaimd |
| Nieuwe .NET-service/HTTP/WPF-tests | Geschreven/aangepast, NIET uitgevoerd |
| Volledige regressieorchestrator / smoke / WPF verification | NIET uitgevoerd wegens restoreblokkade |
| Fysieke YubiKey, multi-presenterbrowser/WPF, DPI/zoom/responsive | NIET uitgevoerd |
| Publish / installer / portable / sidecar | NIET uitgevoerd |
| Migratie op actieve of bestaande installatie | NIET uitgevoerd; actieve portal niet gestart |

De eerste Node-run vond een verouderde brandingassertie op @Experience.OrganizationName. De layout gebruikt nu het opgeslagen, door Razor gecodeerde @Presentation.Current.Organization. Alleen die assertion is aangepast; de rest van de lokale-fallback/responsivecontroles bleef bestaan. De volledige Node-run is daarna tweemaal geslaagd, ook na het wissen van enrollmentvelden bij pagehide.

Een extra poging om SQLite-DDL via python te controleren kon niet starten: python is niet beschikbaar. Daarna is de DDL daadwerkelijk met de reeds beschikbare Node 24 node:sqlite in een geheugen-DB getest; dit is geen uitvoering van de C#-migratieservice en geen workaround voor een compilerblokkade.

## Exacte restoreblokkade

Uitgevoerd:

```powershell
dotnet restore C:/src/LummaSimulator\artifacts\phase65-completion-6a5df5ef1d2d465d8271530a80000f1e\source\LummaSimulator.slnx --locked-mode
```

.NET SDK 10.0.403 rapporteerde:

```text
Failed to read NuGet.Config due to unauthorized access.
Path: C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config
Access to the path is denied.
```

Geen alternatieve NuGet-config, ACL-aanpassing, compilerroute, identiteit of sandboxomzeiling gebruikt. Er is niet geprobeerd de actieve portal als buildcontrole te starten.

## Nieuw geschreven, nog lokaal uit te voeren .NET-coverage

EventAccessChecks gebruikt nieuwe synthetische databases en software-WebAuthn-signatures: admin/A/B, twee afzonderlijke events en één gedeeld event, lijstisolatie, onbekende legacyowner, pending/accept/replay/decline/expiry/revoke, nieuwe-accountenrollment zonder overname, vreemde demo-ID, eigen actor/session/run-capabilities, memberrevocation, transfer met verse proof, eigenaar-deactivatieguard, eigen sessierefs, configuratie/hostingvalidatie, first-runfinish, herstart/parallelle eerste-eventrequests en immutable installatiestatus.

Migratiechecks gebruiken een synthetische legacyfixture: foutinjectie vóór commit met volledige rollback, herhaalbaarheid, behoud van visitor/key/grantdata en rejection van een onbekende extra schematabel. Een afzonderlijke nieuwe DB test concurrente GUI-bootstrapuitgifte en sluiting na passkeyactivatie. Deze tests zijn nog niet uitgevoerd.

HTTP-fixtures zijn aangepast voor echte onboardingstappen, expliciete Edition-admission vóór WebAuthn, extra monitorcapabilityheader, 404 voor ongerelateerde Prize-detail-URL, uitnodigen/accepteren via bestaande auth, legacy globaal-eventverbod en aparte sessies. De bestaande negatieve Origin/antiforgery/diagnostiekchecks blijven aangesloten. De nieuwe extra HTTP-verzoeken kunnen limiter-/fixtureproblemen zichtbaar maken; lokaal controleren en oplossen zonder security te versoepelen.

Prize-tests gebruiken expliciete membership waar de oude suite algemene toegang aannam. Gewone presentatoren mogen geen globale profielvoorkeur veranderen; de assertions zijn daarop aangepast. Servicefixtures met synthetische public-keybytes zijn alleen opslag/autorisatietests en bewijzen geen cryptografische WebAuthn-verificatie. Die laatste gebeurt in de softwareauthenticatorfixtures.

WPF MonitorIntegrationChecks controleert de capabilityheader uitsluitend voor het juiste portalrun-ID, geen forwarding naar een ander ID en verwijderen uit geheugen/header. Bestaande DPAPI/ACL/HEAD/TLS-foutchecks blijven bestaan. Nog niet gecompileerd of uitgevoerd.

## Lokale verificatie — exact

Gebruik een gewoon lokaal PowerShell-venster met toegang tot de bestaande normale NuGet-config, .NET 10 en Node. Gebruik de geïsoleerde bronkopie hieronder, niet de actieve projectinstallatie. De kopie bevat geen databases, keyrings, certificaten, secrets-state, buildoutput of node_modules. Zij is een buildbron, geen runtimebackup.

```powershell
$verificationSource = 'C:/src/LummaSimulator\artifacts\phase65-completion-6a5df5ef1d2d465d8271530a80000f1e\source'
Push-Location -LiteralPath $verificationSource
try {
    # Alleen dit shellproces; bestaande configuratiebestanden worden niet aangepast.
    $env:LUMMA_MONITOR_KEY = $null
    $env:Monitor__KeyHash = $null
    $env:LUMMA_PORTAL_STOP_FILE = $null
    $env:LUMMA_DATA_DIRECTORY = Join-Path $verificationSource 'artifacts/local-test-data'
    $env:LUMMA_LOG_DIRECTORY = 'Logs'
    $env:LUMMA_EXPORT_DIRECTORY = 'Exports'
    $env:SecurityAudit__WindowsEventLog__Enabled = 'false'
    $env:LUMMA_TEST_WEB_DLL = Join-Path $verificationSource 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.dll'

    dotnet restore LummaSimulator.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt; geen verdere build/tests.' }
    dotnet build LummaSimulator.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build mislukt; los compilatie eerst op.' }

    node --test tests/WebAuthn.Ux.Tests/passkeys.test.cjs tests/WebAuthn.Ux.Tests/localization.test.cjs tests/WebAuthn.Ux.Tests/presenter.test.cjs tests/WebAuthn.Ux.Tests/clickfix.test.cjs tests/WebAuthn.Ux.Tests/branding.test.cjs tests/WebAuthn.Ux.Tests/monitor-keys.test.cjs tests/WebAuthn.Ux.Tests/presenter-shell.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Node-regressie mislukt.' }

    # Gericht: Web/FIDO2/service/migratie/HTTP/HTTPS-fixtures met nieuwe tijdelijke data.
    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Web-regressie mislukt.' }

    # Bestaande orchestrator: Web + Node + Windows smoke + WPF Verification.
    dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Volledige regressie mislukt.' }
} finally { Pop-Location }
```

Sluit het aparte verificatievenster na afloop zodat tijdelijke env-overrides vervallen. De orchestrator omvat alle bestaande regressies; een groene run is nog geen fysieke-key/DPI/MDR/publishacceptatie. Bouw alle zes directe solutionprojecten plus transitieve LocalTestServer; geen losse portal-only build als vervanging.

Publicatie is niet uitgevoerd. Bij latere packagingwijzigingen App inclusief LocalTestServer in een eigen publishmap publiceren en verification/Test-LocalHttpPublished.ps1 met expliciete geïsoleerde PublishDirectory uitvoeren. Dat is geen toestemming om een actieve installatie te overschrijven.

## Hervatpunt volgende sessie

Lees eerst de vier fase-6.5-documenten en AGENTS.md, controleer git status en behoud alle bestaande wijzigingen. Begin bij geïsoleerde .NET-restore/build en EventAccessChecks/HttpChecks, niet bij nieuwe UI. Controleer vooral pagefilter-handlernamen/policyvolgorde, nieuwe HTTP-redirects/CSRF-identiteiten, Prize-schemaallowlist/herstart en monitorcapabilityisolatie. Los compiler-/testfouten op zonder bypasses. Rond daarna de open punten in phase-6.5-implementation.md af. De taak is nog geen volledig bewezen fase-6.5-oplevering.

## Gerichte reparatie migratiebehoudtest (6 november 2026)

De lokaal gemelde failure op PrizeMigrationChecks.cs:78 is herleid tot de legacyOnly-snapshot die nieuwe PresenterEventScopes/PresenterDemoContexts/PresenterEventMigrations/PresenterInstallation-rijen meerekende. De bron-SQL-reproductie toont vijf legitieme toegevoegde rijen en geen wijzigingen aan de oude tabelwaarden. De test sluit uitsluitend de exact bekende additive tabellen uit en controleert oude tabellen/kolommen/bytes/counters/sessies/ceremonies/statistieken nu per categorie, inclusief fase-6.5-rollback, herhaling en integriteit/FK's.

Alleen testcode/testwiring en documentatie aangepast; productiecode en runtimegegevens ongemoeid. Actuele Node-run: 72 PASS, 2 bestaande skips (74 totaal), inclusief nieuwe in-memory SQL-reproductie. Geïsoleerde solution-restore opnieuw FAIL wegens ontoegankelijke gebruikers-NuGet.Config; geen nieuwe C#-build/.NET-test geclaimd. De eerdere status hierboven blijft historische context. Exacte oorzaak, bestanden en lokale commando's voor de actuele bronkopie: [migratieherstelrapport](phase-6.5-migration-preservation-fix.md).

## Gerichte reparatie InviteAsync-fixture (6 november 2026)

De lokaal gemelde PresenterDenied na de negatieve leesassertie is veroorzaakt door een fixture-ontvanger met impliciet Pending=1, ondanks Active=1/Activated=1. De fixture zet nu expliciet Pending=0 en geeft administratorprincipals de bestaande beide rolclaims. Productieautorisatie is ongewijzigd. Exacte denialchecks en een autorisatiematrix zijn toegevoegd, inclusief bestaande administrator vóór migratie, gewone eigenaar, acceptatie, zelfuitnodiging en eventisolatie.

Actuele Node-run: 73 PASS, 2 bestaande skips (75 totaal). Nieuwe in-memory bron-SQL-check PASS; geen C#-serviceacceptatie geclaimd. Geïsoleerde locked restore opnieuw geblokkeerd door ontoegankelijke gebruikers-NuGet.Config. Nieuw gericht lokaal testargument: --prize-event-checks. Bestanden, betekenis van de testresultaten en exacte lokale commando's: [uitnodigingsfixtureherstel](phase-6.5-invitation-fixture-fix.md).

## Gerichte reparatie bezoekersnummeringfixture (6 november 2026)

ExperienceChecks.cs:90 gebruikte een next-demo die al vóór de eventwissel was aangemaakt. Zijn fase-6.5-context bleef terecht het oude event; bron-SQL reproduceert nummer 7 in het oude event in plaats van verwacht nummer 1 in het nieuwe. De fixture gebruikt nu het huidige managementpad en selecteert het nieuwe event expliciet vóór credentialregistratie. Productiecode blijft ongewijzigd. Nieuwe C#-checks dekken twee events met onafhankelijke 1–4-reeksen, parallelle registraties/authenticaties, herhaalde initialisatie met vervolgnummer 5, stabiele repeat-auth en SQLite-integriteit/FK's.

Actuele Node-run: 74 PASS, 2 bestaande skips (76 totaal), inclusief nieuwe in-memory bron-SQL-reproductie. Geïsoleerde locked restore opnieuw geblokkeerd door gebruikers-NuGet.Config; geen nieuwe .NET-build/concurrentierun geclaimd. Diagnose, gewijzigde bestanden, testgrenzen en exacte lokale commando's: [bezoekersnummeringherstel](phase-6.5-experience-numbering-fix.md).

## Gerichte reparatie HTTPS-loginassertie (6 november 2026)

De gecombineerde loginassertie gebruikte HTML van /Presenter/Setup uit de variabele login. Die pagina bevat sinds de wizard terecht name=login; de gewone /Presenter/Login bevat geen accountveld. HttpChecks gebruikt nu aparte setupHtml/loginHtml en afzonderlijke controles voor geen accountveld/-requestdata, lege allowCredentials en userVerification=required, plus scheiding van setup/login en geen passwordfallback. Productiecode blijft ongewijzigd.

Actuele gerichte Node-presentersuite: 12 PASS. Volledige Node-run: 77 PASS, 2 bestaande skips (79 totaal). JS-syntax/diffcontroles PASS. Geïsoleerde locked restore opnieuw geblokkeerd door gebruikers-NuGet.Config; geen nieuwe HTTPS/.NET-pass geclaimd. Exacte diagnose, bestanden en lokale commando's: [presenter-loginassertieherstel](phase-6.5-presenter-login-test-fix.md).

# Fase 6.5 — herstel bezoekersnummeringfixture

6 november 2026. Alleen de regressie in ExperienceChecks onderzocht/hersteld. Geen productiecode, beveiligingsregels, bestaande credentials of runtimegegevens gewijzigd; geen bestaande portal gestart, geen commit/push.

## Oorzaak

ExperienceChecks maakt `next` aan vóór de evenementwisseling, om de bestaande bezoekersrotatie en sessie-intrekking te testen. DemoService.CreateAsync schrijft sinds fase 6.5 bij creatie een PresenterDemoContexts-rij. Op dat moment verwijst deze naar het eerste event. De fixture wisselt vervolgens het actieve event en registreert/authenticeert dezelfde reeds aangemaakte demo zonder een nieuwe eventselectie.

ExperienceStatistics.CommitAuthenticationAsync leest de expliciete eventcontext. De teller is niet globaal: UPDATE ExperienceEvents SET Visitors=Visitors+1 WHERE Id=$id RETURNING Visitors verhoogt alleen het geselecteerde event; VisitorCounts bewaart EventId/VisitorNumber met UNIQUE(EventId,VisitorNumber) en één koppeling per demo. De oude fixture verwachtte nog impliciete toewijzing aan het event dat bij eerste authenticatie globaal actief is.

De bron-SQL-reproductie geeft exact:

| Gegeven | Oude fixture |
|---|---|
| Reeds getelde bezoekers in eerste event | 6 |
| Eventcontext van next vóór en na globale wissel | Eerste event |
| Verwachting van assertion | Nummer 1, tweede event |
| Werkelijke toekenning | Nummer 7, eerste event |
| Teller eerste event na toekenning | 7 |
| Teller tweede event | 0 |

De synthetische Node-reproductie gebruikt herkenbare vaste IDs: eerste event ...0201, tweede event ...0202. De C#-fixture gebruikt willekeurige event-GUIDs; de nieuwe diagnostiek toont de concrete verwachte/werkelijke GUIDs tijdens die lokale run.

Dit is geen globale teller of verlies van per-eventnummering. De testverwachting voor een NIEUW event blijft 1. Alleen de impliciete aanname dat een voor een ander event aangemaakte demo automatisch verhuist, past niet bij de nieuwe expliciete fase-6.5-context.

## Eisen en isolatie

LummaSimulator-v0.5.md beschrijft nummers als uniek/consecutief binnen het event en stabiel gedurende het demoaccount. De historische beschrijving kende nog één globaal actief event. De fase-6.5-documentatie beschrijft expliciete typed eventcontext en geen stilzwijgende verplaatsing van bestaande bezoekers. Het doel blijft dus per-eventnummering, niet globale nummering.

ExperienceChecks gebruikt experience.db in de nieuw aangemaakte willekeurige testsuitemap. Andere testgroepen gebruiken andere databases; er is geen aanwijzing voor gegevensvervuiling uit een andere groep. De nieuwe onafhankelijke-eventsfixture gebruikt opnieuw een eigen database experience-independent-events.db. Er wordt geen actieve installatie geopend.

## Reparatie

- De bestaande compare-and-swap-eventwisseltest gebruikt nu PresenterService.EventAsync met een via software-WebAuthn geauthenticeerde synthetische administrator en twee service/storeinstanties. Daardoor ontstaat ook de bijbehorende autorisatiescope. Het oude interne statistics-only testpad maakte die scope niet aan.
- Na de wissel selecteert de nog niet geregistreerde next-demo expliciet het nieuwe event via de bestaande EventAccess.BindVisitorAsync. Dit gebeurt vóór passkeyregistratie, zoals de bestaande bezoekersadmission. Geen rebinding van geregistreerde accounts of versoepeling van checks.
- De test bevestigt eerst dat de oorspronkelijke context werkelijk bij het oude event lag. De nieuwe receipt moet daarna nummer 1 én het nieuwe event hebben; VisitorCounts en PresenterDemoContexts moeten hetzelfde bevestigen.
- NumberCheck toont uitsluitend synthetische publieke event-ID's, verwacht/werkelijk nummer, opgeslagen VisitorNumber en de eventteller. Geen tokens, handles, credentials of sessionhashes.
- Nieuwe onafhankelijke-eventsfixture: twee tegelijk actieve events, acht bezoekers, parallelle WebAuthn-registraties en parallelle authenticaties over twee stores; elk event moet 1–4 krijgen. Na herhaalde volledige initialisatie krijgt elk event nummer 5. Herhaalde authenticatie behoudt het oorspronkelijke nummer en voegt geen VisitorCounts-rij toe. Integriteit en foreign keys worden gecontroleerd.
- Nieuw testargument --experience-checks voert uitsluitend deze servicefixtures uit in een nieuwe tijdelijke map, zonder portalproces.

## Gewijzigde bestanden

1. tests/LummaSimulator.Web.Tests/ExperienceChecks.cs
2. tests/LummaSimulator.Web.Tests/Program.cs
3. tests/WebAuthn.Ux.Tests/experience-numbering.test.cjs
4. tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj
5. Dit rapport.
6. Toevoeging in docs/phase-6.5-test-results.md.

DemoService.cs, ExperienceStatistics.cs en EventAccess-productiecode zijn voor deze reparatie niet gewijzigd. De reeds bestaande niet-gecommitte wijzigingen zijn behouden.

## Werkelijke verificatie

- Nieuwe Node bron-SQL-test PASS: oude 7/oud-event-uitkomst gereproduceerd; expliciet gekozen events onafhankelijk 1–4, na herhaalde schema-DDL elk 5, bestaand nummer stabiel, SQLite-integriteit/FK's PASS.
- De Node-test voert SQLite-DDL/nummeringspredicates in aparte in-memorydatabases uit. Hij voert geen C#-initializer, HTTP, echte WebAuthn of gelijktijdige transacties uit.
- Volledige beschikbare Node-run (10 bestanden): 76 totaal, 74 PASS, 0 FAIL, 2 bestaande PE/publish-icoonskips.
- JS-syntax, regressieproject-XML en git diff --check PASS.
- Geïsoleerde locked solution-restore opnieuw FAIL, exit 1, SDK 10.0.403: toegang geweigerd tot C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config.
- Nieuwe C#-build/ExperienceChecks/concurrentietests en volledige .NET-regressie NIET uitgevoerd wegens de restoreblokkade. De door gebruiker gemelde eerdere Release-build en gerichte eventtests zijn baseline, geen bewijs voor deze wijzigingen.

## Exacte lokale verificatie

Gebruik de bijgewerkte geïsoleerde bronkopie en een apart normaal PowerShell-venster:

```powershell
$verificationSource = 'C:/src/LummaSimulator\artifacts\experience-numbering-d979077e16054941ba72b47d734a50fd\source'
Push-Location -LiteralPath $verificationSource
try {
    $env:LUMMA_MONITOR_KEY = $null
    $env:Monitor__KeyHash = $null
    $env:LUMMA_PORTAL_STOP_FILE = $null
    $env:LUMMA_DATA_DIRECTORY = Join-Path $verificationSource 'artifacts/local-test-data'
    $env:LUMMA_LOG_DIRECTORY = 'Logs'
    $env:LUMMA_EXPORT_DIRECTORY = 'Exports'
    $env:SecurityAudit__WindowsEventLog__Enabled = 'false'
    $env:LUMMA_TEST_WEB_DLL = Join-Path $verificationSource 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.dll'

    dotnet restore LummaSimulator.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt.' }
    dotnet build LummaSimulator.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build mislukt.' }

    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --experience-checks
    if ($LASTEXITCODE -ne 0) { throw 'Gerichte bezoekersnummeringtests mislukt.' }
    node --test tests/WebAuthn.Ux.Tests/experience-numbering.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'SQL-nummeringreproductie mislukt.' }

    dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Volledige regressie mislukt.' }
} finally { Pop-Location }
```

De gerichte C#-test start geen portal. De volledige orchestrator gebruikt eigen synthetische HTTP-fixtures en een eigen testportal-DLL. Niet de bestaande portal starten om de fix te controleren. Geen NuGet-, ACL-, TLS- of autorisatiebypass. Sluit het aparte shellvenster na afloop om tijdelijke env-overrides te laten vervallen. Eventuele verdere .NET-regressies zijn pas na uitvoering vast te stellen.

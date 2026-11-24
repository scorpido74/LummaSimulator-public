# Fase 6.2 — optionele Windows Event Log-export

Basis: commit `30fe814`, fase 6.1. Implementatiedatum: 4 november 2026. Geen wijziging aan de gepubliceerde Event Edition v0.1 RC1, WPF, authenticatieregels of SQLite-schema's. Geen dependency-upgrades of nieuwe NuGet-packages.

## Architectuur en rechten

SecurityAuditService blijft de enige bron van identity-auditrecords. Authenticatie schrijft uitsluitend naar de bestaande primaire SQLite-audit, transactioneel met de bestaande authenticatiemutaties. De optionele BackgroundService leest daarna kleine batches via een eigen read-only SQLite-verbinding. De verbinding wordt gesloten voordat externe I/O begint. Er is geen exporter-aanroep, await, Windows API-call of checkpointschrijfactie in het authenticatiepad.

De dispatcher gebruikt afzonderlijke IIdentityAuditExporter-instanties met een vaste naam, IdentityAuditExportBatch en een eigen checkpoint/stream-ID. Iedere IdentityAuditExporterRegistration heeft eigen activering en batchlimieten; de achtergrondlus gebruikt het kortste pollinterval van de ingeschakelde exporters. Windows-export uitzetten kan een toekomstige andere exporter niet uitschakelen. Er is nu uitsluitend een WindowsEventLog-exporter. Latere HTTPS-/API-/SIEM-exporters kunnen dezelfde dispatchgrens gebruiken, maar zijn niet geïmplementeerd. Die mogen geen gewone gebruikers-API tot globale audittoegang promoveren.

Windows Event Log-export is standaard **uit**. Uitgeschakelde export leest de audit niet en maakt geen checkpoint-/lockbestand of eventsource aan. De runtime vraagt nooit om UAC/elevatie, registreert geen source en wijzigt geen ACL, firewall of groepslidmaatschap. Normale WPF-, bezoekers- en presentatorbediening gebeurt onder het gewone Windows-account. De bestaande eenmalige eerste-administratorbootstrap blijft een afzonderlijke, lokaal geautoriseerde onderhoudsprocedure; fase 6.2 verandert die trust boundary niet.

Event source: `LummaSimulator.IdentityAudit`; lokaal log: `Application`. De writer leest uitsluitend de bijbehorende HKLM-registratiesleutel en gebruikt RegisterEventSourceW/ReportEventW met één JSON-insertionstring. Hij gebruikt geen remote host, SID of raw-data-buffer. Er is bewust geen EventLog.WriteEntry: die .NET-API kan een ontbrekende source automatisch registreren. De gekozen native writer bevat geen provisioning-/privilegeverhogingscode. Bij ontbrekende source of schrijfrechten blijft de backlog in SQLite en wordt een foutstatus zichtbaar.

Het standaard Application-log kan normaal door een regulier account worden beschreven nadat de source is geprovisioneerd. Organisatiebeleid kan dit beperken. Dan blijft de portal functioneren, terwijl beheer een afzonderlijke minimale schrijfrechtentoekenning moet beoordelen. Start de portal niet verhoogd als oplossing. Dit ontwerp verandert geen beveiligingsbeleid automatisch.

## Configuratie

De huidige bron bevat uitsluitend deze uitgeschakelde standaardinstelling:

```json
{
  "SecurityAudit": {
    "WindowsEventLog": {
      "Enabled": false,
      "BatchSize": 50,
      "PollSeconds": 10
    }
  }
}
```

Voor een afzonderlijke, daarvoor bestemde installatie kan beheer Enabled=true configureren, bijvoorbeeld via `SecurityAudit__WindowsEventLog__Enabled=true` in de portalprocesomgeving. BatchSize: 1–200; PollSeconds: 1–300. Instellingen worden bij processtart ingelezen; geen runtime-configuratie-editor of automatische herstart toegevoegd. Een ongeldige optionele exportinstelling maakt de exporter inactief met zichtbare InvalidConfiguration-status; ze veroorzaakt geen startup-validatiefout in de portal.

Gebruik de feitelijk geconfigureerde portalinstallatie en database, niet een verondersteld standaardpad. De checkpointmap is de directory van DemoStore.DatabasePath, dezelfde bestaande opslaggrens als de audit. Er is geen aparte exportkopie van de database en geen overdracht van gebruikersdata naar een andere laptop.

## Exportcontract

Windows Event ID blijft gelijk aan het bestaande IdentityEvent-ID. Eventnaam wordt uit die vaste enum afgeleid, niet overgenomen uit een willekeurige opgeslagen string. 6104/6107 zijn Warning; de overige events zijn Information.

| Event-ID | Naam |
|---|---|
| 6101 | IDENTITY_CREATED |
| 6102 | PASSKEY_REGISTRATION_STARTED |
| 6103 | PASSKEY_REGISTERED |
| 6104 | PASSKEY_REGISTRATION_FAILED |
| 6105 | AUTHENTICATION_STARTED |
| 6106 | AUTHENTICATION_SUCCEEDED |
| 6107 | AUTHENTICATION_FAILED |
| 6108 | SESSION_CREATED |
| 6109 | SESSION_ACTIVITY |
| 6110 | SESSION_REVOKED |
| 6111 | USER_LOGGED_OUT |

Het JSON-schema bevat: schemaVersion=1, streamId, sequence, at (oorspronkelijke UTC-gebeurtenistijd), eventId, eventName, demoRunId, correlationId, authenticationMethod, result, sourceComponent en simulation. Onbekende schema's/event-ID's worden niet geëxporteerd. Run-/correlatiereferenties worden uitsluitend als geldige GUID's opgenomen. Methode/resultaat zijn begrensde waarden; sourceComponent is vast LummaSimulator.Web. De oorspronkelijke simulation-markering blijft behouden.

Deze strengere projectie bevat **geen cookies, tokens, credentials, credentialhashes, gebruikersnamen, gebruikers-/sessie-ID's, IP-adressen, private keys, volledige credential-ID's, ruwe WebAuthn-responses of exceptionberichten**. Ook als IP-auditing voor SQLite expliciet aan staat, exporteert deze Windows-exporter dat IP niet. Arbitrary velden uit opgeslagen JSON worden niet doorgestuurd.

SQLite-tijd `at` en Windows TimeCreated hebben verschillende betekenissen: bij een backlog is at het oorspronkelijke identitymoment en TimeCreated het latere exportmoment. Correlatie en `(streamId, sequence)` verbinden records zonder authenticatiegeheimen.

## Aflevering, checkpoint en fouten

Per exporter staan naast de werkelijke database `identity-audit-export-<naam>.checkpoint.json` en een `.lock`. Het checkpoint bevat alleen versie, exporteridentificatie, een hash die het geconfigureerde databasepad bindt, een willekeurige stream-ID en de laatst bevestigde sequence. Het is geen extra auditbron. De bestanden erven de bestaande maprechten en zijn uitgesloten van Git; neem ze niet mee als schone partnerdistributie.

De stream-ID wordt vóór de eerste externe write opgeslagen. Een exclusieve OS-filelock serializeert concurrerende dispatchers van dezelfde exporter. Alleen na een volledig geslaagde batch wordt de sequence via een geflushte tijdelijke file en vervangende move bijgewerkt. Bij gedeeltelijke aflevering of een checkpointschrijffout kan een volgende poging reeds geschreven events dupliceren: **at-least-once**, geen exactly-once-garantie. Dedupliceer op streamId/sequence; de Windows-record-ID is per Windows-logrecord anders.

Een nieuw checkpoint begint op nul en exporteert ook bestaande fase-6.1-records. Beschadigde/verkeerd gebonden checkpoints worden niet gereset. Een bron die vóór het checkpoint eindigt (bijvoorbeeld na een losse database-restore) veroorzaakt een exportfout. Beheer moet database, checkpoint en historie onderzoeken; kopieer of herstel geen actieve database/keyring als automatische reparatie. Handmatige checkpointverwijdering kan alle oude records opnieuw afleveren en hoort bij een afzonderlijk goedgekeurde herstelhandeling.

Fouten blijven bij de betreffende exporter en worden elke poll opnieuw geprobeerd. Een falende toekomstige sink verhindert de andere sinks niet. De presenterstatus toont uitsluitend veilige foutcodes, geen payloads, paden of exceptionteksten. Geen SQLite-auditrecord wordt verwijderd of als afgeleverd gemarkeerd bij een sinkfout.

## Status voor presentatoren

Open **Presentatordashboard → Audit-exportstatus** (`/Presenter/AuditExport`). De bestaande Presenter-policy en cookievalidatie gelden ook voor deze pagina; een gewone bezoeker krijgt geen toegang. De pagina toont per exporter Uitgeschakeld, Wacht op export, Actief of Exportfout, de bevestigde sequence, de laatste poging en de laatste geslaagde batch sinds deze processtart. Vernieuwen is handmatig. Dit is statusmetadata, geen globale bezoekersaudit-API.

De exporter controleert platform en source-registratie ook wanneer de audit nog leeg is, zonder een proefevent te schrijven. SourceMissing is dus direct zichtbaar. Bij een lege backlog kan Actief zonder laatste geslaagde batch verschijnen; dit bewijst nog geen schrijfrecht. Dat recht wordt pas daadwerkelijk gebruikt bij een event/batch of de expliciete standaardgebruikerschrijftest. Een latere schrijfweigering wordt AccessDenied.

Actief is geen garantie dat een grote backlog al volledig is ingehaald. De bevestigde sequence toont de feitelijke voortgang; de dispatcher levert per poll hoogstens de ingestelde batchlimiet.

Belangrijke codes: SourceMissing (aparte provisioning ontbreekt), AccessDenied (lees-/schrijfrechten geweigerd), UnsupportedPlatform (geen Windows), InvalidConfiguration, InvalidCheckpoint, InvalidAuditEvent, WindowsWriteFailed en ExportFailed. Een volle/onbeschikbare Event Log-service, checkpointschrijffout of corrupte auditbatch houdt de export tegen, maar verandert de aanmeldflow niet. SQLite-opslagfouten in de primaire authenticatietransactie blijven uiteraard de bestaande afzonderlijke foutgrens.

## Afzonderlijke provisioning door beheer

Voer dit uitsluitend uit na goedkeuring voor de concrete Windows-host. Het is **geen** onderdeel van Setup-Demo, de portalstartup of een normale demo. Onderstaande commando's zijn voor een aparte verhoogde **Windows PowerShell 5.1**-sessie van de beheerder. Ze worden door deze implementatie niet uitgevoerd:

```powershell
$identitySource = 'LummaSimulator.IdentityAudit'
$identityLog = 'Application'
if ([System.Diagnostics.EventLog]::SourceExists($identitySource)) {
    $identityExistingLog = [System.Diagnostics.EventLog]::LogNameFromSourceName($identitySource, '.')
    if ($identityExistingLog -ne $identityLog) {
        throw 'Source bestaat onder een ander log. Stop en laat beheer dit beoordelen.'
    }
} else {
    New-EventLog -LogName $identityLog -Source $identitySource
}
```

Laat Windows de registratie verversen voordat de eerste write wordt getest. Een ontbrekende message-resource kan bij een klassieke source een beschrijvingswaarschuwing geven; inspecteer dan de insertionstring via Properties/ToXml. De JSON-inhoud en event-ID zijn de verificatiebasis. Voor een volledig eigen gelokaliseerde manifestprovider is een afzonderlijke toekomstige provisioning nodig.

ACL-aanpassingen zijn geen standaardstap. Als het echte gewone runtimeaccount geen schrijfrecht heeft, laat de beheerder de huidige Application-log-/source-/maprechten en het organisatiebeleid controleren. Regel alleen het minimaal benodigde recht via de goedgekeurde beheermethode, met vastgelegde bestaande ACL en rollback. Geef de runtime geen lokaal administratorlidmaatschap, verwijder geen beveiligingsinstellingen en vervang geen volledige bestaande ACL door een voorbeeld-SDDL. Deze fase voert geen dergelijke handeling uit.

## Partners met en zonder lokale administratorrechten

- **Zonder lokale administratorrechten:** gebruik de gewone demo met export uit. SQLite-audit, bestaande bezoekersaanmelding en geprovisioneerde presentatorbediening blijven beschikbaar. Een verse installatie moet de bestaande eerste-admin-/TLS-provisioning vooraf door bevoegd IT-beheer laten regelen in de juiste Windows-/DPAPI-context. De exporter introduceert geen bypass daarvoor. Wil de partner Windows-events, dan vraagt hij de IT-beheerder de bovenstaande source-/eventuele rechtenstap uit te voeren. Ontbreekt die, dan mag export aan een statusfout geven; de applicatie blijft functioneren.
- **Met lokale administratorrechten voor installatie:** registreer de source eenmalig in de aparte beheersessie en controleer eventuele beleidstoegang. Sluit die sessie en start de portal daarna onder het gewone, niet-verhoogde runtimeaccount. Alleen het bezitten van beheerdersrechten tijdens installatie is geen reden om de portal verhoogd te draaien.

Verdeel uitsluitend de schone, daarvoor gebouwde applicatie; geen gebruikersdatabase, keyring, checkpoint, geregistreerde passkeys of certificaatprivésleutels. Deze fase wijzigt de bestaande RC1-ZIP/release niet. Activering op een bestaande partnerinstallatie blijft een expliciete deployment-/beheerhandeling. Activering voegt Windows-opslag toe met eigen retentie en leesrechten; het beëindigen van een portal-demo wist geen Windows-events. Application-loglezers kunnen de geëxporteerde tijden en pseudonieme run-/correlatiegegevens zien. Gebruik uitsluitend fictieve demo's en stem logretentie vooraf af.

## Get-WinEvent-verificatie

Na provisioning en activering: voer een fictieve registratie en afzonderlijke WebAuthn-aanmelding uit, wacht minstens één poll en controleer de presenterstatus. Lees vervolgens het lokale Application-log onder het gewone account (of laat IT de leesrechten beoordelen):

```powershell
$identityWindowsEvents = Get-WinEvent -FilterHashtable @{
    LogName = 'Application'
    ProviderName = 'LummaSimulator.IdentityAudit'
    Id = (6101..6111)
    StartTime = (Get-Date).AddMinutes(-15)
} -MaxEvents 100

$identityWindowsEvents | ForEach-Object {
    $identityData = $_.Properties[0].Value | ConvertFrom-Json
    [pscustomobject]@{
        WindowsRecordId = $_.RecordId
        WindowsEventId = $_.Id
        OriginalUtc = $identityData.at
        Name = $identityData.eventName
        DemoRunId = $identityData.demoRunId
        CorrelationId = $identityData.correlationId
        StreamId = $identityData.streamId
        Sequence = $identityData.sequence
        Simulation = $identityData.simulation
    }
} | Format-Table -AutoSize
```

Een bestaand event of geslaagde write bewijst alleen aflevering aan Windows Event Log. **Er wordt geen automatische WithSecure-detectie, alert, MDR-respons of SIEM-ingestie geclaimd.** Daarvoor zijn afzonderlijke collector-/sensorconfiguratie en feitelijke operationele acceptatie nodig.

Voor een portal en WPF op verschillende machines schrijft de exporter uitsluitend op de Windows-portalhost. Een later door IT ingerichte Windows Event Forwarding-subscription kan die events naar een Windows Event Collector sturen met passende authenticatie, TLS/Kerberos, leesrechten en firewallbeleid. WPF krijgt geen toegang tot de portaldatabase. Deze fase installeert geen collector/subscription en maakt geen externe verbindingen.

## Geautomatiseerde tests en gewone gebruikersacceptatie

AuditExportChecks zit in de bestaande Web.Tests-runner. Tests gebruiken nieuwe synthetische SQLite-opslag en een geïnjecteerde writer, **geen echte Windows Event Log-writes of source-/ACL-provisioning**. Ze dekken standaard uit/geen I/O, stabiele IDs, JSON/redactie, bronbatch, ontbrekende source, geweigerde schrijftoegang, authenticatie tijdens sinkfalen, hervatten/checkpoint/herstart, gedeeltelijke aflevering/deduplicatiesleutel, gelijktijdige lock, corrupt checkpoint, onafhankelijke sinks, ongeldige optionele configuratie en DI. HTTPS-tests controleren de presentatorautorisatie en statuspagina.

Er is daarnaast een expliciete opt-in echte schrijftest. Voer die na een geslaagde isolated Release-build en voorafgaande provisioning uit in een **niet-verhoogde** Windows-terminal, vanuit de geïsoleerde bronkopie:

```powershell
dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --windows-eventlog-standard-user-smoke
```

De test weigert een verhoogd administrator-token, schrijft één synthetisch IDENTITY_CREATED-event met simulation=true via de productie-exporter en toont uitsluitend de veilige stream-/run-/correlatie-GUID's. Hij registreert niets, verhoogt geen rechten, leest geen database en start geen portal. Verifieer het met Get-WinEvent. Een geweigerde write is een concrete provisioning-/beleidsuitkomst; het is geen reden om normale uitvoering te verhogen.

Test ook met Enabled=true zonder source of met door IT geweigerde schrijftoegang: authenticatie blijft werken, SQLite groeit normaal en de presentator ziet SourceMissing/AccessDenied. Herstel de toegang uitsluitend via de aparte beheerprocedure; na de volgende poll wordt de backlog afgeleverd. Verwijder voor zo'n test nooit een bestaande actieve source of bestaande ACL. Gebruik een daarvoor ingerichte test-VM.

Technische bronnen: [EventLog.WriteEntry en automatische source-registratie](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.eventlog.writeentry?view=windowsdesktop-10.0), [RegisterEventSourceW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-registereventsourcew), [klassieke event sources en message resources](https://learn.microsoft.com/en-us/windows/win32/eventlog/event-sources).

## Werkelijke verificatie in deze sessie

Gewijzigde bestanden: `.gitignore`, `LummaSimulator.Web/Program.cs`, `appsettings.json`, `Services/SecurityAuditService.cs`, de drie nieuwe bestanden onder `Services/AuditExport/`, de twee nieuwe `Pages/Presenter/AuditExport.cshtml*`-bestanden, `Pages/Presenter/Index.cshtml`, de drie UiText-resources, `tests/LummaSimulator.Web.Tests/AuditExportChecks.cs`, test-Program.cs en HttpChecks.cs, en dit document. Bestaande .NET-project-/lockfiles en migraties zijn ongewijzigd. De HTTP-testserver forceert export uit, ook als de ouderomgeving een operator-opt-in bevat.

- Basiscommit gecontroleerd: 30fe814; alleen codex-start.bat was vooraf ongetrackt en is ongemoeid gelaten.
- Huidig shelltoken gecontroleerd: geen verhoogd administrator-token. Er zijn geen sources geregistreerd, ACL's gewijzigd of echte Windows-events geschreven.
- 52 bestaande Node-regressietests geslaagd, 0 mislukt.
- Resource-XML en dubbele-sleutelcontrole geslaagd; git diff --check geslaagd. Checkpoints/locks worden door de specifieke ignore-regel uitgesloten.
- Geen wijzigingen onder packaging/ of LummaSimulator.App; geen RC1-releasehandeling, bestaande databasehandeling, commit of push.
- Locked restore van alle zeven projecten geprobeerd in een eigen actuele bronkopie. Geblokkeerd: `Failed to read NuGet.Config due to unauthorized access`, pad `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. Geen configuratie-/identiteitsomweg gebruikt.
- Daarom zijn compilatie, nieuwe .NET-tests, volledige regressie, Razor-rendering en daadwerkelijke gewone-gebruikersacceptatie van Windows Event Log **nog niet bevestigd**. De tests voor AccessDenied/SourceMissing gebruiken mocks; dit is geen claim van een geslaagde write naar het Windows-log.

De actuele geïsoleerde bronkopie staat in `artifacts/phase62-b4f9180003f14493b6568ae35022b245/source`; restore.log staat één niveau hoger. Werk buiten de sandbox met toegestane NuGet-toegang, zonder de actieve portal of RC1 te starten:

```powershell
Set-Location 'C:/src/LummaSimulator\artifacts\phase62-b4f9180003f14493b6568ae35022b245\source'
dotnet restore LummaSimulator.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt' }
dotnet build LummaSimulator.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build mislukt' }
$env:LUMMA_TEST_WEB_DLL = Join-Path (Get-Location) 'LummaSimulator.Web\bin\Release\net10.0\LummaSimulator.Web.dll'
dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Regressie mislukt' }
```

Na die controles blijft de opt-in standaardgebruikerschrijftest/Get-WinEvent-verificatie op een daarvoor geprovisioneerde testhost open, evenals presenterstatus bij een echte beleidsweigering. Een SQLite-backlog mag daarbij niet worden gewist of hersteld om de test te laten slagen. De bestaande fase-6.1-wizard-/authenticatie-/sessie- en Event Edition-tests blijven in de orchestrator staan.

## Correctie HTTP-429 in de integratiefixture

De gebruiker heeft buiten de sandbox een geslaagde Release-build en geslaagde AuditExportChecks gemeld. De latere HTTP-fixture liep vervolgens vast op HttpChecks.cs:359: `colleague.GetStringAsync("/Presenter")`, na de gedeelde Event Edition-/Prize-/browsercontroles. De tweede stacklocatie rond regel 442 is de afronding/async-propagatie van RunAsync, geen afzonderlijk bewijs van een tweede falende request.

Deze GET valt onder de bestaande gedeelde presenter-management-quota van 60 requests per minuut, QueueLimit=0. De fase-6.1-fixture had alleen een specifieke 429-afhandeling voor de **volgende POST** naar `/Presenter?handler=Event`: eenmaal zestig seconden wachten. Een 429 op de voorafgaande GetStringAsync werd niet afgehandeld en veroorzaakte direct HttpRequestException. De twee nieuwe fase-6.2-GETs naar `/Presenter/AuditExport` (bezoeker en ingelogde presentator) gebruiken eveneens die limiter en kunnen het quotamoment naar de eerdere GET verschuiven. AuditExportChecks zelf gebruikt geen HTTP; hij voegt geen requests aan deze Kestrel-fixture toe.

Alleen testinfrastructuur is voor deze correctie aangepast: HttpChecks.cs, test-Program.cs, nieuw HttpRateLimitFixture.cs en deze toelichting. Productiequota, middleware, autorisatie, TLS, Origin/CSRF en bestaande assertions zijn ongewijzigd. De helper wordt expliciet gebruikt voor het afsluitende beheerblok; er is geen globale HttpClient-retryhandler.

De helper herhaalt uitsluitend een ontvangen HTTP 429, maximaal één keer. Retry-After delta-seconden en HTTP-datums worden gerespecteerd. Zonder header wordt het bestaande éénminuutvenster afgewacht. Een vereist interval boven 120 seconden veroorzaakt een zichtbare 429-testfout; het interval wordt niet ingekort om te vroeg opnieuw te proberen. Een tweede 429 faalt altijd expliciet, ook bij negatieve autorisatieassertions. Overige 4xx-/redirectstatussen blijven ongewijzigd beschikbaar voor de oorspronkelijke assertions/EnsureSuccessStatusCode; 5xx-responses falen direct zichtbaar, zodat een negatieve autorisatieassertion een serverfout niet als geslaagde weigering kan behandelen. Netwerk-/timeoutfouten worden niet opgevangen of herhaald. POST-pogingen maken telkens nieuwe formuliercontent met dezelfde oorspronkelijke velden; een 429 is door de bestaande middleware afgewezen vóór de handler.

De gerichte checks gebruiken synthetische HttpResponseMessage-objecten en een geïnjecteerde klok/wachtfunctie: delta, HTTP-datum, ontbrekende/verstreken Retry-After, niet-429-statussen, blijvende 429, te lange serverwachttijd en netwerkfouten. Ze wachten zelf geen echte minuut. Ze draaien mee in Web.Tests en kunnen na build afzonderlijk worden gestart:

```powershell
dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --http-rate-limit-fixture-checks
```

Voer daarna de volledige bestaande regressieorchestrator uit met LUMMA_TEST_WEB_DLL naar de eigen geïsoleerde Release-build, zodat ook de werkelijke gedeelde limiter en de bestaande assertions worden geverifieerd. Er is geen applicatieherstart, databasehandeling, commit of push uitgevoerd voor deze correctie.

Werkelijke controle voor deze correctie: git diff --check geslaagd; de vaste productie-limiterdefinities zijn gelijk aan 30fe814. Nieuwe .NET-fixturechecks en de werkelijke HTTP-regressie zijn nog niet uitgevoerd in de sandbox: locked restore faalt opnieuw op `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config` met toegang geweigerd. De eigen actuele kopie staat in `artifacts/http429-fixture-9a7082e280384179804947e9ea1ca371/source`; restore.log staat één niveau hoger. De eerdere fase-6.2-snapshot en gebruikersbuild zijn hiervoor niet aangepast.

Na herstel van toegestane NuGet-toegang buiten de sandbox:

```powershell
Set-Location 'C:/src/LummaSimulator\artifacts\http429-fixture-9a7082e280384179804947e9ea1ca371\source'
dotnet restore LummaSimulator.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt' }
dotnet build LummaSimulator.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build mislukt' }
dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --http-rate-limit-fixture-checks
if ($LASTEXITCODE -ne 0) { throw 'HTTP-fixturechecks mislukt' }
$env:LUMMA_TEST_WEB_DLL = Join-Path (Get-Location) 'LummaSimulator.Web\bin\Release\net10.0\LummaSimulator.Web.dll'
dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
```

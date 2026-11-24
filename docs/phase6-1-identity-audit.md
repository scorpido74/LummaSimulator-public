# Fase 6.1 — identity audit en sessieadministratie

Datum: 3 november 2026. Branch: feature/phase5-domain-foundation. **Bronimplementatie; .NET-build en nieuwe integratietests nog niet bevestigd wegens sandboxblokkade.** Geen wijzigingen aan de gepubliceerde Event Edition v0.1 RC1, WPF, productiegegevens, dependencyversies of lockfiles. Geen commit/push.

## Architectuur en scope

De bestaande bezoekerscookie `__Host-Lumma.Visitor` is een Secure/HttpOnly/SameSite=Strict eigendomsgeheim voor een tijdelijk demoaccount. Dit is geen ASP.NET Core Presenter-cookie en registratie is geen aanmelding. Alleen een door IFido2 geverifieerde assertion zet LastAuthenticatedAt en de bestaande statistieken. Presentatoren hebben de afzonderlijke Presenter-authenticatiescheme, server-side sessietabel, rolcontroles en bestaande PresenterAudit. Deze grenzen blijven behouden. Fase 6.1 voegt audit en administratie toe aan de **bezoekersidentityflow**; bestaande presenterlogging wordt niet vervangen en bezoekers-API's geven geen toegang op grond van administratorrechten.

Geen tweede loginmechanisme, replayticket, browsersessie-uitlezing of WPF-interface toegevoegd. De bestaande monitor blijft een beperkte loopback/monitor-key/grant-consument. ClickFix, Event Edition en de monitor toetsen nu ook de sessieadministratie; een accounttijdstip zonder geldige onderliggende sessie is niet voldoende.

## Auditcontract

SecurityAuditService is de centrale schrijver voor de bezoekersflow. IdentityAudit bevat append-only records met een oplopende sequence, UTC-milliseconden, interne gebruikers-/runreferentie, eventnaam en een JSON-envelope met camelCase-velden:

`schemaVersion`, `at`, `eventId`, `eventName`, `demoRunId`, `userId`, `credentialHash`, `sessionId`, `authenticationMethod`, `result`, `sourceComponent`, `correlationId`, `ipAddress`, `simulation`.

| ID | Naam |
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

Sleutelregistratie en geslaagde authenticatie worden samen met hun audit-/sessiemutaties gecommit. Challenges blijven vóór verificatie eenmalig verbruikt; er worden geen eigen cryptografische checks ingevoerd. Mislukte verificaties krijgen alleen een veilig vast resultaat, geen exceptiontekst of onbetrouwbaar responseobject. De bestaande Origin-/CSRF-/rate-limitafwijzingen vóór de WebAuthn-service blijven bestaan; die zijn geen nieuwe WebAuthn-verificatie-events.

Credentialreferenties worden SHA-256-gehasht. Geen volledige credential-ID, wachtwoord, PIN, private key, public-keyblob, cookie, bindinghash, assertion, uitnodigingscode of ander authenticatiegeheim komt in het envelope. Een GUID-correlatieheader wordt genormaliseerd; ontbrekende/ongeldige invoer valt terug op een geldige operatie-GUID of een nieuw gegenereerde GUID. De ongeldige invoer wordt niet bewaard. Deze correlatie is diagnostiek, geen autorisatie of bewijs van herkomst.

IP-logging is standaard uit. Alleen bij expliciete `SecurityAudit:IncludeIpAddress=true` wordt het beschikbare server-side RemoteIpAddress vastgelegd, binnen de bestaande proxytrustconfiguratie. Er wordt geen ruwe forwardingheader gelogd. Beperk deze optie tot een passend fictief demo-/privacybeleid. Huidige events hebben simulation=false; toekomstige simulatie-export moet expliciet simulation=true gebruiken en een herkenbaar afzonderlijk eventcontract krijgen.

## Sessies en afdwinging

IdentitySessions bewaart een niet-geheime interne ID, gebruiker, run, cookiebindinghash (uitsluitend intern), eerste aanmaaktijd, laatste activiteit, absolute authenticatie-eindtijd, methode, gehashte credentialreferentie, type Normal en eventuele intrekking. Backfill gebruikt bestaande LastAuthenticatedAt/LastSeen/SessionHash; er wordt geen historische credential of auditgebeurtenis verzonnen.

Actief vereist tegelijk: bestaand open demoaccount, passende werkelijke cookiebinding, recente account- én sessieactiviteit, niet-ingetrokken sessie en nog geldige WebAuthn-aanmelding. De absolute grens blijft dertig minuten; de bestaande account-idlegrens is configureerbaar, standaard 29 minuten. Browsen verlengt die absolute authenticatiegrens niet. Bezoeken aan de demopagina en sessiepagina werken activiteit bij. SESSION_ACTIVITY wordt per sessie hoogstens eenmaal per minuut geschreven; de activiteitstijd zelf wordt wel bijgewerkt. API-/monitorreads verlengen de sessie niet.

Intrekking is een transactie die de eigen sessie markeert, de werkelijke cookiebinding ongeldig maakt door rotatie naar een niet-uitgegeven waarde, pending challenges verwijdert en audit schrijft. Het historische authenticatietijdstip en de passkeys blijven behouden. De oude cookie kan geen API, ClickFix of nieuwe assertion meer gebruiken. De huidige tijdelijke identityflow heeft na deze afmelding geen herstelticket: men start een nieuwe demo. Er wordt geen herlogin/replaymechanisme voor die oude identiteit geïntroduceerd.

De tabel en eigenaarfilters ondersteunen meerdere records per identiteit. **Fase 6.1 geeft nog geen tweede werkende browsersessie uit.** Het bestaande model heeft één daadwerkelijke bezoekerscookiebinding per identity. Een tweede administratierecord zonder werkelijke authenticatiebinding wordt Expired, nooit Active. Het uitgeven van twee onafhankelijk geldige cookies na authenticatorverificatie en een eventuele read-only replaybinding hoort bij een volgende expliciete fase. De UI bevat daarvoor alleen uitleg en een toekomstige typenaam; de huidige database accepteert uitsluitend Normal. Geen replay geïmplementeerd.

## Gebruikerspagina en API

`/Sessions`, bereikbaar via de demopagina, vereist een actuele WebAuthn-aanmelding via de bestaande bezoekerscookie. De pagina toont UTC-aanmaaktijd, activiteit, methode, type, status en huidige sessie. Intrekking gebeurt uitsluitend via een Razor-antiforgery-POST met exacte Origin-controle en hercontrole van de eigenaar. Voor deze native formuliernavigatie gebruikt alleen deze pagina dezelfde same-origin-referrerinstelling als het bestaande Prize-beheer. Andere pagina's houden hun bestaande beleid.

Read-only endpoints:

- GET `/api/identity/sessions`
- GET `/api/identity/audit`

Beide ontlenen de eigenaar server-side aan de bezoekerscookie en huidige aanmelding. Geen user-ID-/tenantparameter kan de scope uitbreiden. Een presenter/admincookie is geen alternatief. De applicatie heeft geen algemeen multi-tenantidentiteitsmodel; bestaande demo-/gebruikersgrenzen zijn de isolatiegrens en er wordt geen nieuwe tenantclaim verzonnen. Er is in deze fase geen globale SOC-/administrator-audit-API.

Filters: `from` en `until` als ISO-8601-tijdstippen, `demoRunId` als GUID; audit ondersteunt daarnaast `eventType` met de exacte eventnaam. Sessies filteren op aanmaaktijd; audit op gebeurtenistijd. `offset` wordt begrensd op 0–100000 en `limit` op 1–100 (standaard 50). Responses bevatten offset, limit en items, geen geheimen. Er is geen CORS-vrijgave en de bestaande HTTPS-/no-store-/rate-limitmiddleware blijft gelden.

## SQLite-upgrade, herstel en retentie

De historische ExperienceMigrations 3/4/5/6 en het Prize-v6-fingerprint blijven ongewijzigd. IdentitySchemaVersion=1 is een afzonderlijk additief contract met IdentitySessions, IdentityAudit, indexen en append-only triggers. Prize-preflight erkent uitsluitend de exacte aanvullende schemaobjecten met de juiste marker, nadat Prize-v6 aanwezig is; onbekende of gedeeltelijke schema's falen vóór reparatie. De additive migratie en backfill zijn transactioneel. Er worden geen bestaande accounts, passkeys, PresenterAuthSessions, challenges of cookies door de nieuwe migratie verwijderd of herschreven.

De nieuwe intrekking verwijdert evenmin accounts/passkeys. De reeds bestaande expliciete demo-eind-/idlecleanup blijft functioneel bestaan; fase 6.1 vervangt dat bewaarbeleid niet. Administratieve sessies worden na verwijdering van hun onderliggende demo afgeleid als verlopen en verlenen geen toegang. De nieuwe audit-/sessierecords bevatten geen FK-cascade die bewijs stilzwijgend wist. Dat verlengt wel de bewaartijd van pseudonieme referenties: er is **nog geen automatische audit-/sessieretentie of purge**. Volume, bewaartermijn en eventuele IP-logging moeten vóór operationele inzet worden vastgesteld. Append-only is geen cryptografische auditintegriteitsgarantie.

Rollback: een migratiefout rolt nieuwe tabellen/rows/marker terug. Na een geslaagde upgrade weigert een oude RC1-binary de nieuwe onbekende tabellen; een binaire downgrade over dezelfde actieve database is dus geen veilige rollback. Gebruik pas na expliciete toestemming een consistente vooraf gemaakte backup met database, keyring en oorspronkelijke DPAPI-context, via een afzonderlijke herstelprocedure. Geen automatische restore, dataverplaatsing of aanpassing van RC1-runtimepaden.

## Voorbereiding Windows Event Log en SOC

IIdentityAuditExporter definieert uitsluitend de toekomstige grens voor batches van veilige versioned envelopes plus sequence. Er is geen concrete exporter, netwerkverbinding, Windows-providerinstallatie of Event Log-write toegevoegd. Een toekomstige dispatcher kan sequence gebruiken als checkpoint en herhaalde aflevering dedupliceren; een transportfailure mag de SQLite-bron niet wissen. Autorisatie voor een service-exporter moet apart worden ingericht en mag geen gewone gebruikers-API tot globale datatoegang promoveren.

Bij verschillende WPF-/portalmachines horen identity-events bij de **portalbron**. Een toekomstige Windows-exporter schrijft op die Windows-portalhost naar een expliciet geïnstalleerde provider/channel; Windows Event Forwarding kan ze vervolgens via een door beheer ingerichte, geauthenticeerde subscription naar een Windows Event Collector sturen. Providerregistratie, subscriptions, Kerberos/certificaatvertrouwen, HTTPS/WinRM en firewallbeleid zijn afzonderlijke beheerhandelingen, hier niet uitgevoerd. Een Linux-portal vereist een toekomstige TLS-SIEM-exporter of gecontroleerde bridge; geen directe Windows Event Log-aanname.

WPF verzamelt later alleen zijn eigen toegestane demo-events op zijn eigen bronmachine. Correlatie tussen hosts gebruikt expliciete Demo Run-/Event Edition-runkoppelingen, UTC en de correlatie-ID, nooit cookies of gedeelde databasetoegang. Een collector heeft geen recht op portalcredentials of private keys. Test klokafwijkingen, deduplicatie, buffering, toegangscontrole en uitval voordat operationele SOC-integratie wordt geclaimd.

## Gewijzigde bronbestanden

- Nieuw: Services/IdentityMigration.cs, SecurityAuditService.cs, IdentityEndpoints.cs; Pages/Sessions.cshtml en Sessions.cshtml.cs.
- Integratie: DemoStore.cs, DemoService.cs, ExperienceStatistics.cs, SessionStatusService.cs, EventEditionService.cs, Prizes/PrizeMigration.cs, Program.cs, Pages/Demo.cshtml en drie UiText-resources. Privacy.cshtml en Complete.cshtml vermelden dat auditmetadata/sessiehistorie na demo-einde bewaard blijven en dat IP-auditing standaard uit staat.
- Tests: nieuw IdentityChecks.cs; runner Program.cs, HttpChecks.cs en de legacy-only snapshotfilter in PrizeMigrationChecks.cs. De historische migratieasserties blijven bestaan; de filter vergelijkt uitsluitend de oorspronkelijke tabellen, nieuwe backfill krijgt eigen asserts.

## Verificatie en beperkingen

Toegevoegde .NET-regressies: geslaagde/mislukte registratie en authenticatie, registratie is geen aanmelding, sessieaanmaak/activiteit, absolute expiry ondanks accountactiviteit, meerdere administratierecords zonder verzonnen actieve sessie, intrekking/ongeldige oude cookie/challenges, behoud van credentials, gebruikersisolatie, correlatie-/secretredactie, filters/paginering, idempotente herstart, upgrade/backfill, migratierollback en fail-closed schemacontrole. HTTPS-checks dekken eigenaarschap, ontbrekende aanmelding, administratorisolatie, pagina, filters en werkelijke server-side revocation. De bestaande wizard-, Presenter-, Event Edition-, monitor- en WebAuthn-suites blijven onderdeel van de regressieorchestrator.

Werkelijke controles: **52 Node-tests geslaagd**; resource-XML/dubbele-sleutelcontrole en git diff --check geslaagd. De nieuwe .NET-tests, volledige solutionbuild en Razor-compilatie zijn **niet bevestigd**. Locked restore in een eigen bronkopie faalt op alle zeven projecten met:

`Failed to read NuGet.Config due to unauthorized access. Path: %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`.

Log: `artifacts/phase61-2829ad7ff263434babb0bd6c61fd9baf/restore.log`; Node-uitvoer staat er in node.log. Geen permissie-/configuratieomweg gebruikt. Geen actieve portal gestart, database geopend of authenticatoracceptatie geclaimd. De nieuwe migratie is dus niet op een gebruikersdatabase toegepast.

### Buiten de sandbox opnieuw testen

Voer met toegestane SDK/NuGet-toegang onderstaande Windows PowerShell-commando's uit. Gebruik een **nieuwe bronkopie met de huidige niet-gecommitteerde wijzigingen**, geen actieve installatie en geen RC1-publicatiemap:

```powershell
Set-Location 'C:/src/LummaSimulator'
$phase61Root = Join-Path (Get-Location) ('artifacts\phase61-external-' + [guid]::NewGuid().ToString('N'))
$phase61Source = Join-Path $phase61Root 'source'
New-Item -ItemType Directory -Path $phase61Source -Force | Out-Null
$phase61Files = git ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Git-inventarisatie mislukt' }
foreach ($relative in $phase61Files) {
    if ($relative -notmatch '^(LummaSimulator\.(App|Web)/|shared/|tools/|tests/|verification/|docs/|LummaSimulator\.slnx$|Directory\.[^/]+$|NuGet\.Config$|global\.json$|README\.md$)') { continue }
    if ($relative -match '(^|/)(bin|obj|artifacts|node_modules)/|\.(db|sqlite|sqlite3|pfx|pem|key)(\.|$)|event-edition\.manifest|\.setup-verification') { throw "Beschermd bestand in bronselectie: $relative" }
    $destination = Join-Path $phase61Source $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $relative -Destination $destination
}
Set-Location $phase61Source
dotnet restore LummaSimulator.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt' }
dotnet build LummaSimulator.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build mislukt' }
$env:LUMMA_TEST_WEB_DLL = Join-Path $phase61Source 'LummaSimulator.Web\bin\Release\net10.0\LummaSimulator.Web.dll'
dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Regressie mislukt' }
```

De bestaande runner maakt eigen synthetische databases/loopback-TLS en voert Web.Tests, Node, SmokeTests en Windows WPF-verificatie uit. Hij start geen bestaande portalinstallatie. Publiceer of migreer geen RC1-runtime als onderdeel van deze controle. Voer daarna nog fysieke/browseracceptatie uit voor de sessiepagina, afmelding, verlopen aanmelding, reservesleutel en privacy-instellingen. Windows Event Log-/mobiele-/SIEM-acceptatie blijft buiten deze fase.

## Correctie activiteitstest: tijdprecisie

De gebruiker meldde een geslaagde volledige Release-build buiten de sandbox, gevolgd door de falende activiteitassertion in IdentityChecks. Die assertion vergeleek een uit SQLite gereconstrueerde DateTimeOffset rechtstreeks met TestClock.GetUtcNow(). De eerste heeft gehele Unix-milliseconden; de tweede behoudt de fracties van milliseconden van DateTimeOffset.UtcNow. Exacte tickgelijkheid is daardoor geen correcte opslagassertion. Dit is geen reden om beveiligings- of vervalregels te versoepelen.

Broncontrole bevestigt: DemoService.GetAsync(touch:true) zoekt de bestaande gehashte cookiebinding op en roept TouchAsync aan. Die controleert de daadwerkelijke WebAuthn-aanmelding, verval en intrekking; de UPDATE verandert uitsluitend LastActivity, nooit ExpiresAt. SessionsAsync leest dezelfde sessiereferentie en gebruikt FromUnixTimeMilliseconds. De test verwacht nu exact dezelfde millisecondeconversie, zonder tolerantie. Een lokale deterministische IdentityClock begint op UTC met 1234 extra ticks (123,4 microseconden), zodat de oorspronkelijke precisiefout gegarandeerd wordt gereproduceerd.

Alleen IdentityChecks.cs en deze documentatie zijn voor deze correctie aangepast. Productiecode en beveiligingsregels zijn ongewijzigd. De afzonderlijke assertions controleren nu: activiteit neemt toe tot exact de opgeslagen tijdprecisie; ExpiresAt en CreatedAt blijven gelijk; sessie-ID/current-status komen overeen met de bestaande cookiebinding; een ander administratierecord met een andere binding blijft onaangeraakt; gewone activiteit kan verlopen of ingetrokken sessies niet bijwerken/heractiveren, ook niet met een oude in-memory accountreferentie. Alleen de bestaande nieuwe WebAuthn-assertion kan na normaal verval opnieuw authenticeren.

Werkelijk uitgevoerd: een directe .NET DateTimeOffset-reproductie in PowerShell bevestigt dat exacte gelijkheid 1234 ticks verschilt, terwijl de Unix-milliseconden gelijk zijn. git diff --check geslaagd. De nieuwe .NET-regressies zijn niet uitgevoerd: locked restore in de eigen bronkopie faalt opnieuw met toegang geweigerd tot %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. Log: artifacts/identity-activity-fix-846fc3956278489f85516a4e45f97f9a/restore.log. Geen workaround, gebruikersdatabasehandeling, commit of push. De door de gebruiker gemelde Release-build is externe evidence van vóór deze testcorrectie, geen hier uitgevoerde nieuwe test.

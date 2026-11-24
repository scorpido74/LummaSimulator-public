# Prize HTTPS-regressie: opaque Origin

6 november 2026. Alleen HTTP-fixture/testcode gewijzigd; geen productiebeveiligingsinstelling aangepast.

## Request en afwijzingspaden

De oorspronkelijke regel 37 maakt via `Post` een `HttpRequestMessage(HttpMethod.Post, "/Presenter/Prizes/New")`. Hij zet letterlijk `Origin: null` (vier ASCII-tekens), gebruikt de bestaande synthetische administratorsessie en stuurt URL-encoded ProfileId, OperationKey, Name, start, Input.Mode en de verborgen antiforgery-token mee. De HttpClient uit `HttpChecks` heeft `AllowAutoRedirect=false` en vertrouwt uitsluitend het eigen tijdelijke fixturecertificaat. De assertion verwacht exact HTTP 400.

Een ontbrekende header is iets anders: de oude helper kon die situatie niet uitdrukken, omdat hij altijd `Headers.Add("Origin", origin)` uitvoerde. De aangepaste helper gebruikt een nullable string: C# null betekent de header weglaten; de string `"null"` verstuurt daadwerkelijk `Origin: null`. Diagnostiek noemt die gevallen respectievelijk `missing` en `opaque`.

`PrizePageModel.TrustedOrigin` vergelijkt `Request.Headers.Origin.ToString()` exact, case-sensitive, met de gevalideerde HTTPS-origins in `WebAuthnSettings`. Ontbrekend geeft een lege string, letterlijk null geeft `"null"`; beide worden geweigerd. `NewModel.OnPostAsync` doet deze controle vóór profiel/servicegebruik en retourneert een lege 400 met log-event 511. Ongeldige antiforgery wordt reeds door Razor vóór de handler geweigerd. De admin-/presentatorautorisatie en HTTPS-controle gelden ongewijzigd.

Het referrerbeleid kan browser-gegenereerde Origin beïnvloeden, maar deze HttpClient-test stuurt de header expliciet. Een responseheader Referrer-Policy verandert de expliciete header van de volgende HttpClient-request niet. De recente referrercorrecties voor dashboard en monitorbeheer wijzigen de Prize-Origin-check niet. De Prize-handler, TrustedOrigin en hun reject-logteksten zijn identiek aan HEAD.

## Aangetroffen fixturefout en onzekerheid

De route heeft daarnaast de bestaande `presenter-management`-limiter: 60 requests per minuut, zonder queue; overschrijding retourneert **429 vóór de handler**. De oude test nam aan dat iedere request de Origin-controle bereikt en gebruikte directe SendAsync/GetStringAsync-calls. Daardoor kan een begrensde afwijzing als een falende securityassertie worden gerapporteerd, terwijl de aanvraag de handler nooit bereikt. De vooraf geslaagde externe-Origin-test bewijst niet dat ook de direct volgende request nog binnen hetzelfde budget valt. De recente monitorregressies voegen extra beheerrequests vóór Prize toe en kunnen deze bestaande timing-/budgetafhankelijkheid blootleggen.

De exacte oorspronkelijke response is uit alleen de assertionmelding **niet vast te stellen**. Een 429 is een onderbouwde verklaring vanuit de bron, geen hier gemeten HTTP-uitkomst. Zonder werkende .NET-restore is niet bewezen of de gemelde run 429, een authredirect of iets anders ontving. Er is geen bewijs gevonden dat de server null als vertrouwd accepteert; de statusverwachting 400 blijft behouden. Ook een 302/403/500 wordt niet als geslaagde Origin-test geaccepteerd.

## Minimale correctie en nieuwe regressieassertions

- De Prize-test hergebruikt nu de reeds bestaande `HttpRateLimitFixture` voor GET en POST. Alleen 429 wordt maximaal eenmaal opnieuw geprobeerd, na bestaande Retry-After of de vaste-windowfallback. Elke POST-poging krijgt een nieuwe request/content met dezelfde operatie en velden. Er worden geen credentials vernieuwd of Origin/CSRF aangepast om door te gaan. Herhaalde 429 en alle 5xx blijven expliciete failures; 400/401/403 worden nooit herhaald. De productiepermitlimiet blijft 60.
- Iedere POST logt uitsluitend de classificatie missing/opaque/present, de numerieke **werkelijke** status en een redirectcategorie none/login/access-denied/other-relative/absolute. Geen ruwe Location, query, cookies, tokens, formulierwaarden, body of secrets. Ook de eerste 429 wordt zichtbaar vóór de retry. De negatieve assertion noemt bovendien haar scenario en finale status/redirect-aanwezigheid.
- Requests met correcte Origin maar ontbrekende/ongeldige CSRF, externe Origin, letterlijk null en ontbrekende Origin moeten exact 400 zonder redirect geven.
- Vóór en na iedere negatieve case wordt de volledige relevante Prize-bedrijfstoestand vergeleken: events, prijstypen, creationoperations, overige operations, audit, outbox, installatievoorkeur en profielen. De vergelijking gebruikt intern een hash van deterministisch geordende rijen, zonder waarden of hash te publiceren. Ook bestaande synthetische event-/profielwijzigingen zouden hierdoor worden opgemerkt, niet alleen nieuwe rijen.
- Die query opent **alleen read-only** `Path.Combine(temporaryRoot,"http.db")`: exact het bestand dat de eigen HttpChecks-child expliciet via `Database__Path` configureert. Geen DemoStore-initialisatie/migratie, geen actieve database, geen runtimepadgok. Authenticatie-/beheerlogs buiten de Prize-tabellen worden niet als onveranderlijke bedrijfstoestand geclaimd.
- Geldige same-origin-creatie moet nog steeds 302 geven en aantoonbaar Prize-toestand opslaan. Bestaande modelvalidatie-, idempotentie- en conflictchecks blijven behouden. Sec-Fetch-Site wordt nergens als autorisatievoorwaarde gebruikt.

Gewijzigd: `tests/LummaSimulator.Web.Tests/PrizeEventHttpChecks.cs`, de aanroep met de eigen tijdelijke root in `tests/LummaSimulator.Web.Tests/HttpChecks.cs`, en dit document. Geen productiecodewijziging.

## Werkelijke resultaten

- Node passkeys/localization/presenter: **45 geslaagd**, geen failures/skips.
- Diff-whitespacecontrole geslaagd; productie-Prize-handler en TrustedOrigin gecontroleerd op ongewijzigde inhoud.
- Locked restore van alle zeven projecten geprobeerd in `artifacts/prize-origin-verify-c07e3d62e0504e3e86b78f921e0622ea/source`: **exitcode 1**, toegang geweigerd tot `C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config`.
- Geen bypass toegepast. C#-build, fixture-unitchecks en HTTPS-/databaseassertions zijn **niet uitgevoerd**. Dus geen gemeten oorspronkelijke status of bevestigde runtimeacceptatie; ook geen claim dat de nieuwe assertions reeds geslaagd zijn.

## Exacte lokale verificatie

Gebruik een afzonderlijk normaal PowerShell-venster met .NET 10, Node, de bestaande browservereisten en leesrechten op de normale NuGet-configuratie. Geen beveiligingsrechten/configuratie aanpassen om de sandbox te omzeilen. Onderstaande bronkopie bevat de correctie en geen bestaande runtimegegevens:

```powershell
$source = 'C:/src/LummaSimulator\artifacts\prize-origin-verify-c07e3d62e0504e3e86b78f921e0622ea\source'
Push-Location -LiteralPath $source
try {
    $env:LUMMA_MONITOR_KEY = $null
    $env:Monitor__KeyHash = $null
    $env:LUMMA_PORTAL_STOP_FILE = $null
    $env:LUMMA_DATA_DIRECTORY = Join-Path $source 'artifacts/local-test-data'
    $env:LUMMA_LOG_DIRECTORY = 'Logs'
    $env:LUMMA_EXPORT_DIRECTORY = 'Exports'
    $env:SecurityAudit__WindowsEventLog__Enabled = 'false'
    $env:LUMMA_TEST_WEB_DLL = Join-Path $source 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.dll'

    dotnet restore LummaSimulator.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt.' }
    dotnet build LummaSimulator.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build mislukt.' }

    # Synthetische helpertests: 429, één begrensde retry, geen retry bij securityafwijzingen.
    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --http-rate-limit-fixture-checks
    if ($LASTEXITCODE -ne 0) { throw 'Rate-limitfixturetests mislukt.' }

    # HTTPS-tests: eigen portal, tijdelijke data en certificaat; geen bestaande installatie.
    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Web-tests mislukt; zie veilige Prize HTTPS POST-diagnostiek.' }

    dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Regressiesuite mislukt.' }
} finally { Pop-Location }
```

Verwacht voor de finale negatieve case: `Prize HTTPS POST: origin=opaque; status=400; redirect=none`. Een voorafgaande `status=429` gevolgd door de fixture-wachtmelding bevestigt het budgetprobleem. Een andere status blijft een fout en moet op basis van die veilige diagnostiek nader worden onderzocht. De helper wacht normaal tot de productielimiet weer ruimte geeft; hij schakelt die niet uit. Sluit het aparte PowerShell-venster na afloop zodat tijdelijke env-overrides verdwijnen.

Geen bestaande evenementen, databases, monitorsleutels, certificaten of configuratie gewijzigd. Geen commit of push. Eerdere niet-gecommitteerde wijzigingen blijven behouden.

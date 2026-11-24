# HTTP-assertion: onafhankelijke laptop-/iPad-demo — 13 november 2026

De actieve werkboom is gecontroleerd met `git rev-parse --show-toplevel`: `C:/src/LummaSimulator`. Het bestaande lokale werk is behouden. Alleen `tests/LummaSimulator.Web.Tests/HttpChecks.cs` is voor deze correctie aangepast. Geen bestaande demodatabase, portal of installatie is gestart of gebruikt.

## Exacte oorspronkelijke voorwaarden

De assertion `Independent browser accounts and iPad route` combineerde:

1. `firstName.Length > 0`, waarbij `firstName` werd gezocht met `Visitor-[A-Z2-9]{8}`.
2. `secondHtml.Contains("IPAD-DEMONSTRATIE")`.
3. `!secondHtml.Contains(firstName)`.

De eerste verwachting past niet meer bij de bedoelde naamgeneratie. `DemoService.CreateCoreAsync` gebruikt nu `PrizeRoundsService.AllocateVisitorAliasAsync` voor `DemoAccount.Name`. `DemoModel.DisplayName` toont de rondealias of deze accountnaam. De naam heeft dus geen verplicht `Visitor-XXXXXXXX`-formaat. Bij een lege regexmatch is bovendien `Contains("")` altijd true; daardoor faalt ook de laatste voorwaarde. Dat is geen bewijs van gedeelde accounts.

De bedoelde route is ongewijzigd: `IndexModel.OnPostAsync` slaat de gekozen route op en verwijst naar `/Demo`. `DemoModel.RouteTitle` gebruikt voor `ipad` de sleutel `DE IPAD-DEMONSTRATIE`. Er is geen productiecode aangepast: de bron verklaart een verouderde naamverwachting, geen bedoelde wijziging van de iPad-route. Of er daarnaast een runtimeprobleem bestaat, kon door de restoreblokkade niet worden gemeten.

## Gesplitste controles

- De naam wordt nu uit het gerenderde `verified-username`-veld gelezen en HTML-gedecodeerd, zonder vast aliaspatroon. Laptop en iPad hebben ieder een eigen assertion voor een aanwezige naam.
- De iPad-kop, de redirect naar `/Demo` en de opgeslagen route `ipad` hebben afzonderlijke assertions. De laptoproute wordt eveneens afzonderlijk gecontroleerd.
- Accountscheiding wordt niet langer afgeleid uit een substringzoekactie in de hele HTML-pagina. Beide geldige unieke demo-identiteiten moeten ongelijk zijn; de twee weergegeven namen moeten ook ongelijk zijn. DemoRunId is in het bestaande schema uniek per account.
- De laptoprespons wordt vóór het aanmaken van de iPad-demo vastgelegd. Na het aanmaken van beide demo's worden beide clients opnieuw opgevraagd. Elke client moet dezelfde eigen identiteit behouden; hun identiteiten moeten onderling ongelijk blijven. Beide routes worden ook na herladen gecontroleerd.
- De bestaande HTTP-fixture gebruikt twee afzonderlijke `HttpClientHandler`-/`CookieContainer`-instanties, eigen loopback-HTTPS, een nieuwe synthetische database en een eigen testportal-DLL. Er wordt geen live portal aangeroepen.
- Accountdiagnostiek toont alleen verwachte en werkelijke gelijkheidsbooleans. Namen, cookies, tokens en demo-/sessie-ID's worden niet toegevoegd aan de uitvoer. Routes worden zonder querystring of fragment gelezen; diagnostiek staat uitsluitend bekende statische paden/routenamen toe en redigeert onverwachte dynamische waarden.

## Verwacht en werkelijk

| Controle | Verwacht | Werkelijk gemeten via HTTP |
| --- | --- | --- |
| Laptop redirectpad | `/Demo` | Niet gemeten: fixture niet bereikt |
| iPad redirectpad | `/Demo` | Niet gemeten: fixture niet bereikt |
| Laptop demoroute | `laptop` | Niet gemeten |
| iPad demoroute | `ipad` | Niet gemeten |
| Laptopaccount gelijk aan iPad-account | `False` | Niet gemeten |
| Laptopaccount vóór/na aanmaken iPad gelijk | `True` | Niet gemeten |
| iPad-account vóór/na laptopreload gelijk | `True` | Niet gemeten |

## Nieuwe bronkopie en controles

Nieuwe verificatiekopie: `artifacts/dev-start-619e75fe64eb49a590ef853250fd5f9f/source`. Deze bevat de actuele lokale/ongetrackte bronnen en benodigde packaging-bronnen. De laatste correctie is naar diezelfde kopie gesynchroniseerd en de build-/webtestrunnerpoging is daarna opnieuw uitgevoerd. `LUMMA_TEST_WEB_DLL` verwijst expliciet naar de portal-DLL in deze kopie; runtimepaden wijzen naar de eigen testmap.

- Locked restore: exit 1, ontbrekend leesrecht op `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. Log: `restore.log` naast de bronkopie.
- Release-build `--no-restore`: exit 1, NETSDK1004 wegens ontbrekende `project.assets.json`. Log: `build.log`.
- Eerst de rechtstreekse webtestrunner via `dotnet run --project tests/LummaSimulator.Web.Tests/LummaSimulator.Web.Tests.csproj -c Release --no-restore`: exit 1, NETSDK1004 voor Web.Tests/obj/project.assets.json, vóór uitvoering van de HTTP-fixture. Log: `web-direct.log`.
- Volledige regressie: **niet gestart**, omdat de webtestrunner niet slaagde. Vastgelegd in `regression-not-run.txt`.
- `git diff --check`: geslaagd.

Geen succesvolle compilatie, HTTP-acceptatie of accountscheidingsmeting geclaimd. Geen configuratie-, rechten- of identiteitsomweg gebruikt.

## Exacte opdrachten voor gewone Windows PowerShell

Voer deze uit met normale NuGet.Config-leestoegang. Ze gebruiken de nieuwe kopie en starten uitsluitend de eigen testfixtures. De volledige regressie wordt alleen bij een geslaagde rechtstreekse webtestrunner uitgevoerd.

```powershell
$source = 'C:/src/LummaSimulator\artifacts\dev-start-619e75fe64eb49a590ef853250fd5f9f\source'
$logs = Split-Path $source -Parent
Set-Location $source

dotnet restore LummaSimulator.slnx --configfile "$source\NuGet.Config" --locked-mode -p:RuntimeIdentifiers=win-x64 2>&1 | Tee-Object "$logs\restore-windows.log"
if ($LASTEXITCODE -ne 0) { throw 'Locked restore mislukt; zie restore-windows.log' }
dotnet build LummaSimulator.slnx -c Release --no-restore 2>&1 | Tee-Object "$logs\build-windows.log"
if ($LASTEXITCODE -ne 0) { throw 'Release-build mislukt; zie build-windows.log' }

$env:LUMMA_TEST_WEB_DLL = "$source\LummaSimulator.Web\bin\Release\net10.0\LummaSimulator.Web.dll"
$env:LUMMA_DATA_DIRECTORY = "$logs\synthetic-runtime-windows"
$env:LUMMA_LOG_DIRECTORY = 'Logs'
$env:LUMMA_EXPORT_DIRECTORY = 'Exports'
$env:LUMMA_MONITOR_KEY = ''

dotnet "$source\tests\LummaSimulator.Web.Tests\bin\Release\net10.0\LummaSimulator.Web.Tests.dll" 2>&1 | Tee-Object "$logs\web-direct-windows.log"
if ($LASTEXITCODE -ne 0) { throw 'Webtestrunner mislukt; volledige regressie niet gestart' }

dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore 2>&1 | Tee-Object "$logs\regression-windows.log"
if ($LASTEXITCODE -ne 0) { throw 'Volledige regressie mislukt; zie regression-windows.log' }
```

De gesplitste assertionmeldingen leggen de gemeten routes en alleen accountgelijkheid vast zodra de HTTP-fixture kan worden uitgevoerd. Deze fixture bewijst geen werking van een fysieke iPad of native passkeydialoog.

# Session-demo schema-assertion — 13 november 2026

De actieve werkboom is gecontroleerd met `git rev-parse --show-toplevel`: `C:/src/LummaSimulator`. Bestaand lokaal werk is behouden. Alleen de betrokken test en een aanvullende SQLite-regressie zijn aangepast; geen productiecode, migratie of bestaande demodatabase is gewijzigd of aangeroepen.

## Exacte vergelijking en oorzaak

De oorspronkelijke assertion vergelijkt `Equals(schema, await store.ScalarAsync(...))`. `schema` wordt direct na de eerste `store.InitializeAsync()` vastgelegd. De tweede waarde wordt na de volledige sessiedemoflow gelezen. Beide gebruiken:

```sql
SELECT group_concat(name||':'||sql,';')
FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY name
```

`ORDER BY name` buiten het aggregaat sorteert niet de invoerrijen van `group_concat`. De test vergelijkt daardoor ook de aanmaakvolgorde van schemaobjecten. Die verandert door zijn eigen upgradefixture, **vóór de eerste `SessionDemoService.BeginAsync`**:

```sql
DROP TABLE PrizeRoundHistory;
DROP TABLE PrizeRoundHistoryContract;
```

Daarna roept de fixture `store.InitializeAsync()` opnieuw aan. `PrizeRoundHistoryMigration` maakt de twee tabellen en de automatisch mee verwijderde index `PrizeRoundHistoryGroup` opnieuw aan. In de eerste initialisatie maakt `DemoStore` de historie vóór `VisitorFlowMigration` aan; bij deze herinitialisatie bestaan `EventLoginRoutes` en `VisitorExercises` al. Hierdoor staan die twee objecten in de oorspronkelijke string nu vóór de historieobjecten.

Een nieuwe, geïsoleerde SQLite-database met de vijf ongewijzigde CREATE-statements uit beide migratiebronnen reproduceert dit zonder enige toestemmingsactie. De exacte volledige verwachte en werkelijke strings, de oorspronkelijke SQL en objectvolgorden staan in [comparison.json](../artifacts/dev-start-865858488add427ab05b6e9170925bed/schema-reproduction/comparison.json). Deze reproduceert het relevante DDL-deel van de fixture; het is geen uitvoering van de volledige .NET-initialisatie of toestemmingsflow.

| Waarde | Volgorde van de vijf schemaobjecten |
| --- | --- |
| Verwacht: na eerste initialisatie | PrizeRoundHistory → PrizeRoundHistoryGroup → PrizeRoundHistoryContract → EventLoginRoutes → VisitorExercises |
| Werkelijk: na DROP/herinitialisatie, vóór consent | EventLoginRoutes → VisitorExercises → PrizeRoundHistory → PrizeRoundHistoryGroup → PrizeRoundHistoryContract |

Oorspronkelijke vergelijking: **false**. Vergelijking met invoerrijen gesorteerd op naam: **true**. De objectnamen en CREATE-definities zijn gelijk; het verschil is de volgorde. Broninspectie van `SessionDemoService` en `SessionDemoState` vindt geen CREATE/ALTER/DROP of migratieaanroep. De volledige flow kon hier door onderstaande buildblokkade niet dynamisch worden uitgesloten als bron van een aanvullende wijziging.

## Herstel zonder verzwakking

`SessionDemoChecks` sorteert nu de rijen binnen een subquery vóór `group_concat`. Hij legt drie snapshots vast: na initialisatie, direct na de upgradefixture en na de toestemmingsflow. De eerste twee moeten gelijk zijn, en de laatste moet gelijk zijn aan de baseline vóór de flow. Een schemawijziging tijdens de fixture wordt dus ook niet stilzwijgend geaccepteerd.

Een snapshot bevat daarnaast de geordende rijen van ExperienceMigrations, PrizeSchemaContract, IdentitySchemaVersion, PresenterEventMigrations, PrizeRoundsContract en PrizeRoundHistoryContract, plus `PRAGMA user_version`. De assertion controleert daarmee nu werkelijk ook migratiemarkeringen en fingerprints; de oude assertion las alleen schema-DDL. De interne `schema_version`-teller wordt niet als identiteitsbewijs gebruikt: die verandert terecht bij DROP/CREATE en transactionele DDL-probes.

Tijdens de C#-test wordt een onverwachte tabel in een transactie toegevoegd, de afwijking gecontroleerd en de transactie teruggedraaid. De baseline moet daarna exact gelijk zijn. De aanvullende Node-regressie voert de schemaquery uit op een nieuwe in-memory SQLite-database en controleert ook onverwachte indexen, triggers en kolommen. Sorteren verbergt zulke verschillen niet.

Bij een geslaagde toekomstige C#-uitvoering schrijft de test `session-demo-schema-comparison.json` in zijn eigen synthetische testroot. Dit bevat de volledige drie snapshots en de oorspronkelijke verwachte/tussenliggende/werkelijke strings, zonder accounts, sessies of tokens. Het pad wordt gelogd. Die volledige .NET-waarden zijn hier nog niet gemeten.

## Nieuwe bronkopie en resultaten

Na correctie is een nieuwe geïsoleerde bronkopie gemaakt: `artifacts/dev-start-aca2edb1192740a7b86d4e990a811d5f/source`. Ook het reeds aanwezige lokale/ongetrackte werk is meegenomen. `LUMMA_TEST_WEB_DLL` verwijst expliciet naar de portal-DLL in die kopie; runtimepaden verwijzen naar de eigen testmap.

| Controle, in uitvoervolgorde | Resultaat |
| --- | --- |
| Locked restore | Exit 1: ontbrekend leesrecht op `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`; `restore.log` |
| Release-build `--no-restore` | Exit 1: NETSDK1004, ontbrekende project.assets.json; `build.log` |
| Rechtstreekse webtestrunner, `dotnet run --project ...Web.Tests... -c Release --no-restore` | Exit 1 vóór testuitvoering: NETSDK1004 voor Web.Tests/obj/project.assets.json; `web-direct.log` |
| Daarna volledige `dotnet test ...RegressionTests... -c Release --no-restore` | Exit 1 vóór testuitvoering: NETSDK1004 voor RegressionTests/obj/project.assets.json; `regression.log` |
| Onafhankelijke volledige expliciete Node-set van de orchestrator | **118 tests, 116 geslaagd, 2 bestaande PE-iconcontroles overgeslagen, 0 fouten**; `node.log` |
| Gericht `node --test tests/WebAuthn.Ux.Tests/session-demo.test.cjs` | **7 geslaagd, 0 fouten**, inclusief de nieuwe SQLite-regressie |
| `git diff --check` | Geslaagd |

Er is geen geslaagde C#-compilatie of volledige regressie geclaimd. Geen configuratie-, rechten- of identiteitsomweg is gebruikt. Er is geen runtime-exception/stacktrace van de herstelde assertion, omdat de webtestrunner de test niet bereikte.

## Opdrachten voor gewone Windows PowerShell

Gebruik normale toegang tot NuGet.Config. Dit bouwt uitsluitend de nieuwe kopie en start de testfixtures, geen bestaande portal:

```powershell
$source = 'C:/src/LummaSimulator\artifacts\dev-start-aca2edb1192740a7b86d4e990a811d5f\source'
Set-Location $source
dotnet restore LummaSimulator.slnx --configfile "$source\NuGet.Config" --locked-mode -p:RuntimeIdentifiers=win-x64
if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt' }
dotnet build LummaSimulator.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build mislukt' }
$env:LUMMA_TEST_WEB_DLL = "$source\LummaSimulator.Web\bin\Release\net10.0\LummaSimulator.Web.dll"
$env:LUMMA_DATA_DIRECTORY = "$source\artifacts\synthetic-runtime"
$env:LUMMA_LOG_DIRECTORY = 'Logs'
$env:LUMMA_EXPORT_DIRECTORY = 'Exports'
$env:LUMMA_MONITOR_KEY = ''
dotnet "$source\tests\LummaSimulator.Web.Tests\bin\Release\net10.0\LummaSimulator.Web.Tests.dll" 2>&1 | Tee-Object "$source\web-direct.log"
$webExit = $LASTEXITCODE
dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore 2>&1 | Tee-Object "$source\regression.log"
$regressionExit = $LASTEXITCODE
"Webtestrunner: $webExit; volledige regressie: $regressionExit"
```

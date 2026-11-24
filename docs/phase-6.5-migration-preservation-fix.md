# Fase 6.5 — herstel migratiebehoudtest

6 november 2026. Scope uitsluitend de regressie in PrizeMigrationChecks (voormalige regel 78). Geen productiecode gewijzigd, geen bestaande portal gestart, geen echte database geopend/gemigreerd, geen accounts/credentials/secrets/certificaten gewijzigd, geen commit/push.

## Oorzaak en vergelijking

Snapshot(v5, true) selecteerde alle niet-interne tabellen behalve namen beginnend met Prize/Identity en ExperienceMigrations. De helper nam SELECT * ORDER BY rowid van elke geselecteerde tabel en serialiseerde iedere niet-lege rij als {name,row}. Hij vergeleek dus alle oude kolomwaarden, maar óók gegevens uit nieuwe fase-6.5-tabellen. Schemaobjecten werden alleen bij Snapshot(..., false) opgenomen; dit was geen vergelijking van uitsluitend oude accounts.

DemoStore.InitializeAsync voert inmiddels achtereenvolgens PrizeMigration, IdentityMigration en EventAccessMigration uit. De laatste voegt legitiem nieuwe autorisatie-/installatiegegevens toe. De fase-6.5-tabellen beginnen met Presenter en werden daardoor ten onrechte als bestaande gegevens meegeteld.

De synthetische fixture geeft precies deze extra rijen:

| Nieuwe tabel | Rijen | Betrokken kolommen / betekenis |
|---|---:|---|
| PresenterEventScopes | 2 | Kind/SourceId/Name afgeleid van beide bestaande ExperienceEvents; OwnerId NULL |
| PresenterDemoContexts | 1 | DemoId/Kind/EventId gekoppeld aan de bestaande VisitorCounts-rij |
| PresenterEventMigrations | 1 | Version=1 |
| PresenterInstallation | 1 | Id=1, Existing=1, HostingConfirmed=0, FirstEvent/CompletedAt NULL |

Samen vijf extra rijen in vier tabellen. De overige nieuwe fase-6.5-tabellen zijn in deze fixture leeg en verschenen daardoor niet in de oude rijensnapshot. ExperienceMigrations krijgt bovendien versie 6; Identity krijgt de bestaande bedoelde backfill. Die laatste twee categorieën werden door de oude helper al apart uitgesloten.

De bron-SQL-reproductie in Node/in-memory SQLite heeft de oude ongelijke vergelijking daadwerkelijk gereproduceerd en alle oorspronkelijke tabelkolommen exact gelijk bevonden. Dit is geen uitvoering van de volledige C#-initializer of bewijs over een gebruikersdatabase.

## Correctie

- Alleen bij legacyOnly worden exact de bekende additive EventAccessMigration-tabellen uitgesloten. Geen algemene Presenter%-uitsluiting: PresenterAccounts, PresenterKeys, PresenterAuthSessions, PresenterCeremonies en alle andere bestaande tabellen blijven gecontroleerd.
- De volledige snapshot inclusief schema blijft ongewijzigd voor rollback en herhaalde initialisatie.
- Een aanvullende behoudcontrole legt vóór upgrade alle oude tabellen, kolomnamen en celwaarden vast, inclusief lege tabellen. Na Prize/Identity, na onderbroken fase 6.5, na voltooide upgrade en na herhaling worden dezelfde tabellen opnieuw vergeleken.
- Diagnostiek vermeldt per tabel de gegevenscategorie, kolomcontract, rijenaantallen en eventuele gewijzigde kolomnamen. Geen credentialbytes, cookies, tokens of sessionhashes worden gelogd.
- De fixture is aangevuld met synthetische legacy-presentersessie, uitnodiging, enrollmentgrant en enrollmentceremonie, zodat ook die categorieën daadwerkelijk niet-lege rijen bevatten.
- Nieuwe fase-6.5-markers, bestaande-installatiestatus, onbekend eigenaarschap en democontext worden afzonderlijk beoordeeld.
- Foutinjectie in zowel Prize-schema/seeds/marker als de aparte fase-6.5-transactie moet volledige rollback bewijzen. PRAGMA integrity_check en foreign_key_check worden na de upgrade uitgevoerd.
- Nieuw testargument --prize-migration-checks voert uitsluitend de migratiechecks in een nieuwe willekeurige tijdelijke map uit, zonder portalproces.

## Gewijzigde bestanden binnen deze reparatie

1. tests/LummaSimulator.Web.Tests/PrizeMigrationChecks.cs — exacte legacyselectie, behoud-/rollback-/additiondiagnostiek en fixturedekking.
2. tests/LummaSimulator.Web.Tests/Program.cs — gericht testargument; bestaande eerder aanwezige wijzigingen behouden.
3. tests/WebAuthn.Ux.Tests/migration-preservation.test.cjs — nieuwe bron-SQL-reproductie in :memory:; geen externe dependencies. Slaat expliciet over als node:sqlite ontbreekt.
4. tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj — nieuwe Node-check aangesloten op de bestaande orchestrator.
5. Dit rapport en een toevoeging in docs/phase-6.5-test-results.md.

Geen wijziging aan DemoStore, EventAccessMigration, PrizeMigration, IdentityMigration of andere productiefunctionaliteit nodig.

## Werkelijk uitgevoerde controles

- Node 24: acht testbestanden, 74 checks: 72 PASS, 0 FAIL, 2 bestaande PE/publish-icoonskips. De nieuwe migratiereproductie is PASS, niet geskipt.
- In-memory bron-SQL-test: oude failure gereproduceerd, alle bestaande waarden behouden, fase-6.5-DDL/data/indexrollback, herhaalde DDL, SQLite-integriteit en foreign keys PASS.
- node --check nieuw testbestand: PASS.
- Regressieproject-XML: PASS; nieuwe Node-test is geregistreerd.
- git diff --check: PASS (gebruikelijke LF/CRLF-waarschuwingen zijn geen whitespacefouten).
- Geïsoleerde locked restore solution opnieuw geprobeerd: FAIL, exit 1, SDK 10.0.403. Access denied op C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config, alle zeven projecten. Geen workaround toegepast.
- Nieuwe C#-build, gerichte .NET-tests en volledige .NET-regressie: NIET uitgevoerd door die blokkade. De door gebruiker gemelde lokale Release-build is baseline en geen resultaat voor deze testaanpassing.

## Exacte lokale PowerShell-verificatie

De bijgewerkte geïsoleerde bron staat hier; geen runtimebackup of gebruikersgegevens:

```powershell
$verificationSource = 'C:/src/LummaSimulator\artifacts\migration-regression-621c6822d0e74fefb1048cd65f6c3e0f\source'
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

    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --prize-migration-checks
    if ($LASTEXITCODE -ne 0) { throw 'Gerichte migratietests mislukt.' }

    node --test tests/WebAuthn.Ux.Tests/passkeys.test.cjs tests/WebAuthn.Ux.Tests/localization.test.cjs tests/WebAuthn.Ux.Tests/presenter.test.cjs tests/WebAuthn.Ux.Tests/clickfix.test.cjs tests/WebAuthn.Ux.Tests/branding.test.cjs tests/WebAuthn.Ux.Tests/monitor-keys.test.cjs tests/WebAuthn.Ux.Tests/presenter-shell.test.cjs tests/WebAuthn.Ux.Tests/migration-preservation.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Node-tests mislukt.' }

    dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Volledige regressie mislukt.' }
} finally { Pop-Location }
```

Voer uit in een apart gewoon lokaal PowerShell-venster en sluit dat na afloop, zodat tijdelijke environment-overrides vervallen. Niet de actieve portal starten om een migratie te testen. Geen NuGet-, ACL-, authenticatie- of TLS-bypass gebruiken.

## Resterende beperkingen

De werkelijke C#-serviceflow en alle overige regressies moeten lokaal nog slagen. De Node-check voert bron-DDL/backfill-SQL op de synthetische fixture uit, niet FIDO2-verificatie, volledige Prize-profileseeding of de C#-schema-preflight. Daarom is geen geslaagde .NET-run of algemene upgradeacceptatie geclaimd. De reparatie verandert geen runtimegedrag en geeft geen toestemming voor migratie van een actieve installatie.

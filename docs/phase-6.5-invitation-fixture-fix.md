# Fase 6.5 — PresenterDenied bij Prize-eventuitnodiging

6 november 2026. Uitsluitend herstel en regressiedekking van de falende InviteAsync-aanroep in PrizeEventChecks. Geen productiecode, bestaande credentials/databases, instellingen of processen gewijzigd. Geen portalstart, commit of push.

## Exacte oorzaak

De lokale Actor-fixture vulde PresenterAccounts.Active=1 en Activated=1, maar liet Pending weg. De bestaande kolom heeft DEFAULT 1. Daardoor hadden zowel beheerder als ontvanger een intern inconsistente teststatus: actief/geactiveerd, maar enrollment nog pending.

Het event Draft is correct aangemaakt door de databaseaccount admin (Role=Administrator); PresenterEventScopes.OwnerId is admin. EventAccess.Require controleert de actuele rol in PresenterAccounts en de geldige PresenterAuthSessions-rij, en staat de beheerder dus terecht toe. De daaropvolgende InviteAsync-doelcontrole vereist Active=1 AND Pending=0. De ontvanger presenter had Pending=1: COUNT(*)=0, waarna PresenterDenied wordt gegooid. De vorige negatieve leesassertie slaagt onafhankelijk hiervan omdat die gebruiker ook geen eventlid/eigenaar is.

De fixture maakte bovendien voor iedere rol alleen ClaimTypes.Role=Presenter. Een echte administratorlogin voegt ook ClaimTypes.Role=Administrator toe. Dat was niet de oorzaak van deze InviteAsync-exception: de service leest de rol uit de database. Het maakte de fixture wel ongeschikt voor PresenterService.ValidateAsync/policies; ook die inconsistentie is gecorrigeerd.

Dit is een fixtureprobleem, geen aangetoonde fout in de productieautorisatie. De Pending-controle of scope-/rolcontrole versoepelen zou onjuist zijn.

## Herstel en nieuwe checks

- De fixture stelt Pending=0 expliciet in voor reeds geactiveerde synthetische accounts.
- Administratorprincipals bevatten zowel Presenter als Administrator, zoals bestaande login-code; accounts.ValidateAsync controleert beide fixtureprincipals.
- Een exact Denied-testhulpmiddel vangt uitsluitend DemoRequestException met Message=PresenterDenied. Geen exception of een andere fout laat de negatieve test falen; SQLite-/programmeeraanslagen worden niet als autorisatiesucces gemaskeerd.
- De vroegere Pending=1-inconsistentie wordt nu expliciet als negatieve test gereproduceerd in uitsluitend de nieuw aangemaakte testdatabase.
- Positief: administrator nodigt uit, bedoelde ontvanger accepteert en mag presenteren; gewone eventeigenaar nodigt een eigen gast uit.
- Negatief: ongerelateerde presentator, zelfuitnodiging (ook eigenaar), pending target, verkeerde acceptant, replay, toegang vóór acceptatie, uitnodigen door gekoppeld lid en cross-eventgebruik.
- Een eigenaar krijgt geen globaal accountbeheer; zijn nieuwe enrollmentinvite maakt uitsluitend een Presenter-account aan.
- Een afzonderlijke synthetische legacydatabase krijgt een administrator met sessie/key vóór de upgrade. Na volledige initialisatie moet die bestaande identiteit toegang behouden tot een historisch event met onbekende eigenaar. De eigenaar blijft NULL; er wordt geen onbewezen ownership toegekend.
- Herhaalde migratie van de gewone fixture moet eveneens de globale administrator behouden, inclusief toegang tot events van andere eigenaren.

De fixture gebruikt synthetische keybytes voor opslag-/autorisatiecontrole. Dit bewijst geen WebAuthn-ceremonie of fysieke YubiKey. De bestaande softwareauthenticatorchecks blijven aangesloten.

## Gewijzigde bestanden in deze reparatie

1. tests/LummaSimulator.Web.Tests/PrizeEventChecks.cs — fixturecorrectie, exacte denialchecks en positieve/negatieve autorisatiematrix.
2. tests/LummaSimulator.Web.Tests/Program.cs — nieuw argument --prize-event-checks, uitsluitend voor het testexecutable, met een eigen willekeurige tijdelijke map.
3. tests/WebAuthn.Ux.Tests/event-invitation-authorization.test.cjs — bron-SQL-reproductie in een in-memorydatabase.
4. tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj — nieuwe Node-check aangesloten.
5. Dit rapport en een toevoeging in docs/phase-6.5-test-results.md.

De werkboom bevat al eerdere niet-gecommitte wijzigingen; die zijn behouden. EventAccess.cs, PresenterAuthentication.cs en migratieproductiecode zijn voor deze reparatie niet aangepast.

## Werkelijke verificatie

| Controle | Resultaat |
|---|---|
| Nieuwe Node SQLite-reproductie | PASS, niet overgeslagen |
| Originele Active=1/Pending=1/Activated=1-target | Bronpredicate weigert terecht |
| Gecorrigeerde Active=1/Pending=0-target | Bronpredicate accepteert als uitnodigbare ontvanger |
| Werkelijke Allowed-SQL | Owner manage toegestaan; outsider/pending invite geweigerd; member alleen present; A geeft geen B; owner B geeft geen A |
| In-memory SQLite-integriteit/FK's | PASS |
| Volledige beschikbare Node-run (9 bestanden) | 75 totaal: 73 PASS, 0 FAIL, 2 bestaande PE/publish-icoonskips |
| Nieuwe JS-syntax, regressieproject-XML en git diff --check | PASS |
| Locked .NET restore solution in geïsoleerde bron | FAIL, exit 1; SDK 10.0.403; toegang geweigerd tot C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config |
| Nieuwe C#-build/service-/migratie-/HTTP-tests | NIET uitgevoerd door restoreblokkade |

De Node-test gebruikt de werkelijke bron-SQL-predicates en het fixture-INSERT, maar voert niet de C#-services, claimsvalidatie, acceptatietransacties of cookiepolicies uit. De C#-checks hiervoor zijn geschreven en moeten lokaal nog slagen. Geen volledige autorisatieacceptatie geclaimd.

## Exacte lokale PowerShell-commando's

Gebruik de actuele geïsoleerde bronkopie, niet de bestaande installatie. Geen actieve portal starten als verificatie.

```powershell
$verificationSource = 'C:/src/LummaSimulator\artifacts\invitation-regression-7185aa8551454bdfaff78da70362a90f\source'
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

    # Alleen servicefixtures in nieuwe tijdelijke databases; start geen portal.
    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --prize-event-checks
    if ($LASTEXITCODE -ne 0) { throw 'Prize-eventautorisatiechecks mislukt.' }

    node --test tests/WebAuthn.Ux.Tests/event-invitation-authorization.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Gerichte Node-check mislukt.' }

    # Bestaande volledige orchestrator; HTTP-onderdelen gebruiken eigen testportal en synthetische data.
    dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Volledige regressie mislukt.' }
} finally { Pop-Location }
```

Gebruik een apart PowerShell-venster en sluit dat na afloop om de tijdelijke env-overrides te laten vervallen. Geen NuGet/ACL/TLS/autorisatiebypass. Overige lokale regressiefouten kunnen pas na de daadwerkelijke .NET-run worden vastgesteld; ze zijn hiermee niet uitgesloten.

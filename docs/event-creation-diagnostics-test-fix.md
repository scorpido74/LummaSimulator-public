# Event creation diagnostics: falende logassertie

6 november 2026. Alleen testcode aangepast; geen runtime-, database- of configuratiewijziging.

## Verwachting en huidige implementatie

De gemelde assertie in `tests/LummaSimulator.Web.Tests/HttpChecks.cs` combineerde drie substringcontroles op de stdout/stderr van de geïsoleerde testportal. De scenario's staan in `PrizeEventHttpChecks.cs`. De werkelijke productiecode staat in `Pages/Presenter/Prizes/New.cshtml.cs` en `PrizePageModel.cs`.

| Request / scenario | HTTP en pagina | Verwachte bestaande diagnostiek |
|---|---|---|
| Ontbrekende antiforgery | 400 door Razor, vóór de handler | Geen Prize-create-handlerlog vereist |
| Externe of letterlijke null-Origin, met geldige antiforgery | Lege 400 via `BadRequest()` | Event 511: `Prize create rejected at Origin check` |
| Geldige Origin, `Input.GroupSize=0` | 400, formulier met `role="alert"`, `PrizeInvalidSettings` en behouden OperationKey | Event 512: `Prize create rejected at model validation` |
| Geldige create/start | 302 naar het aangemaakte event | Geen rejection-log |
| Zelfde operatie en dezelfde payload opnieuw | 302 naar hetzelfde event | Geen rejection-log |
| Zelfde operatie met gewijzigde Name | 409, formulier met `PrizeIntentChanged` | Event 513: `Prize create rejected at service/storage`, exceptiontype zonder payload |

De invalid-settings-test verwacht daarnaast letterlijk `Controleer de instellingen`, het formulier met `class="panel prize-event-form"` en dezelfde verborgen OperationKey. De conflict-test verwacht status 409. De overkoepelende diagnostiekassertie controleerde niet de HTTP-body maar alleen de drie logteksten. Logging bevat uitsluitend beperkte metadata zoals foutaantal, exceptiontype, SQLite-codes en TraceId; geen ingevoerde namen of secrets.

## Aangetoonde fout en bewijsgrens

De logasserties stonden in `finally`. Iedere eerdere exception in de uitgebreide HTTP-test voert dat blok uit, ook wanneer `PrizeEventHttpChecks.RunAsync` nooit is bereikt. De nog niet uitgevoerde scenario's hebben dan vanzelfsprekend geen logs geproduceerd. Een assertion-exception uit finally vervangt de oorspronkelijke exception. De gemelde gecombineerde logassertie kan dus een eerdere HTTP-, browser- of andere assertionfout maskeren en vermeldt bovendien niet welke categorie ontbreekt.

Dit structurele defect is aantoonbaar in de broncode. Het is zonder de oorspronkelijke uitvoer vóór deze eindmelding niet mogelijk vast te stellen welke eerdere fout in de gemelde run optrad, óf dat alle scenario's waren doorlopen maar een logcategorie ontbrak. Een succesvolle runtime-reproductie wordt niet geclaimd. De fix zorgt dat een eerdere fout intact blijft en anders de ontbrekende logcategorie afzonderlijk wordt benoemd.

Git toont dit patroon, de drie logteksten en de negatieve Prize-requests al in `803659e` van 31 oktober 2026. De Prize-handler en `PrizeEventHttpChecks.cs` zijn ongewijzigd ten opzichte van HEAD (`ffe8780`); er is geen nieuwe afwijking in hun responses of logteksten gevonden. De recente monitorwijzigingen voegen wel extra assertions vóór de Prize-scenario's toe, zodat een eerdere fout daar de bestaande maskering kan blootleggen. Dit bewijst niet welke van die assertions operationeel faalde.

## Minimale correctie

In `HttpChecks.cs` blijft finally verantwoordelijk voor het stoppen/afwachten van uitsluitend de eigen synthetische portal, het verzamelen van diens output en het verwijderen van het eigen tijdelijke testcertificaat. De logasserties staan nu **na** try/finally en worden dus uitsluitend uitgevoerd als de HTTP-scenario's volledig zijn doorlopen. Alle logdekking en controles op gevoelige waarden blijven verplicht voor een geslaagde test. De gecombineerde assertion is gesplitst in drie controles met een herkenbare fasenaam; er worden geen ruwe logs of payloads aan foutmeldingen toegevoegd.

Geen productiecode, Origin-check, antiforgery, autorisatie, verwachte HTTP-status of fouttekst is versoepeld. Gewijzigd voor deze opdracht: `tests/LummaSimulator.Web.Tests/HttpChecks.cs` en dit document. Eerdere niet-gecommitteerde wijzigingen blijven behouden.

## Uitgevoerde controles

- `node --test tests/WebAuthn.Ux.Tests/passkeys.test.cjs tests/WebAuthn.Ux.Tests/localization.test.cjs tests/WebAuthn.Ux.Tests/presenter.test.cjs`: **45 geslaagd**, geen failures/skips. Dit voert geen C#-HTTP-test uit.
- `git diff --check`: geslaagd. Staging is leeg.
- Locked restore in nieuwe geïsoleerde bronkopie `artifacts/event-diagnostics-verify-48ba0d95b7724d618f07ff19054979ec/source`: **exitcode 1**, toegang tot `C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config` geweigerd voor alle zeven projecten. Geen workaround gebruikt.
- Build en .NET-regressie konden daardoor niet worden uitgevoerd. Geen geslaagde runtimeacceptatie geclaimd en geen actieve portal gestart.

## Exacte lokale PowerShell-commando's

Een tweede, actuele bronkopie is klaargezet voor lokale uitvoering. Zij bevat geen bestaande buildoutput, databases, keyrings, monitorstatus of certificaten. Voer de onderstaande opdrachten uit in een normale lokale PowerShell die de bestaande NuGet-configuratie mag lezen; wijzig geen ACL of NuGet-configuratie om de sandbox te omzeilen. .NET 10, Node en de bestaande Chrome/browsertestvereisten zijn nodig.

```powershell
$source = 'C:/src/LummaSimulator\artifacts\event-diagnostics-local-c1ec4e73413f49fe8b7c9dbfb3d179c5\source'
Push-Location -LiteralPath $source
try {
    # Alleen de omgeving van deze testsessie; geen blijvende configuratiewijziging.
    $env:LUMMA_MONITOR_KEY = $null
    $env:Monitor__KeyHash = $null
    $env:LUMMA_PORTAL_STOP_FILE = $null
    $env:LUMMA_DATA_DIRECTORY = Join-Path $source 'artifacts/local-test-data'
    $env:LUMMA_LOG_DIRECTORY = 'Logs'
    $env:LUMMA_EXPORT_DIRECTORY = 'Exports'
    $env:SecurityAudit__WindowsEventLog__Enabled = 'false'
    $env:LUMMA_TEST_WEB_DLL = Join-Path $source 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.dll'

    dotnet restore LummaSimulator.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore mislukt; tests niet gestart.' }
    dotnet build LummaSimulator.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build mislukt; tests niet gestart.' }

    # Gerichte suite: bevat HttpChecks en PrizeEventHttpChecks, met eigen tijdelijke data.
    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Web-tests mislukt; bewaar de eerste foutmelding.' }

    # Vervolgens de bestaande volledige regressieorchestrator.
    dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Regressiesuite mislukt.' }
} finally {
    Pop-Location
}
```

Gebruik hiervoor een afzonderlijk PowerShell-venster; sluit het na afloop om de tijdelijke omgevingsvariabelen te laten vervallen. De bestaande tests starten uitsluitend hun eigen testportal en synthetische processen/browserprofielen. Er wordt geen bestaande portal gebruikt en geen fysieke passkey of MDR-acceptatie geclaimd. Als nu een eerdere fout zichtbaar wordt, is dat de fout die voorheen door finally werd overschreven; dat is geen bewijs van een nieuwe productieregressie door deze correctie. Als uitsluitend een logcontrole faalt, staat voortaan de specifieke ontbrekende categorie in de melding.

Geen commit, push of wijziging van bestaande databases, monitorsleutels of configuratie uitgevoerd.

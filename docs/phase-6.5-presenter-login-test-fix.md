# Fase 6.5 — HTTPS presenter-loginassertie

6 november 2026. Alleen testcode en documentatie aangepast. Geen wijziging aan login, JavaScript-productiecode, WebAuthn, databases, credentials, certificaten of configuratie. Geen bestaande portal gestart, geen commit/push.

## Falende voorwaarde en oorzaak

De samengestelde assertion controleerde achtereenvolgens allowCredentials.Length==0, userVerification==required en !login.Contains("name=\"login\""). De variabele login was echter eerder gevuld met GET /Presenter/Setup. De response van de latere GET /Presenter/Login werd alleen kort op data-presenter-action=login gecontroleerd en vervolgens weggegooid; login werd niet bijgewerkt.

Setup bevat sinds de first-runwizard terecht name=login voor het aanmaken van de eerste beheeridentiteit. Daardoor is de derde operand gegarandeerd false, ook wanneer de normale login en de options volledig correct zijn. De assertion keek dus naar de verkeerde pagina. Deze bronvaststelling verklaart de gemelde failure; de endpointwaarden zijn in deze sessie niet opnieuw via live HTTPS gemeten.

## Gecontroleerde productiebron

- Pages/Presenter/Login.cshtml bevat alleen een data-presenter-action=login-formulier met knop, geen gebruikersnaam/account/password-invoer.
- Pages/Presenter/Setup.cshtml bevat het first-run-accountformulier met asp-page-handler=Begin en de afzonderlijke enrollmentflow. Dat formulier hoort niet bij normale aanmelding.
- LoginModel stuurt een installatie zonder geactiveerde beheerder naar Setup; Setup sluit na activatie. Registratie en login blijven afzonderlijke handelingen.
- Services/PresenterAuthentication.cs: normale login maakt assertions met AllowedCredentials=[] en UserVerificationRequirement.Required. AccountId blijft NULL in de loginchallenge tot de assertion wordt geverifieerd. Het legacy loginargument wordt bewust genegeerd. Enrollment vereist ResidentKeyRequirement.Required en user verification.
- wwwroot/js/presenter.js leest/verstuurt geen login/accountidentiteit bij gewone aanmelding en geeft de serveropties aan navigator.credentials.get door. Geen wachtwoordpad.
- PresenterEndpoints behoudt exacte Origin, antiforgery en bestaande serviceverificatie. RP-ID-, challenge-, handtekening-, user-verification- en sessiecontroles zijn niet gewijzigd.

Geen productiebeveiligingsregressie aangetoond; de fout zit in de fixturevariabele en de samengestelde diagnostiek.

## Testherstel

HttpChecks gebruikt nu setupHtml uitsluitend voor Setup. Na enrollment wordt de echte /Presenter/Login-response opgeslagen als loginHtml, met expliciete HTTP-200-controle.

Afzonderlijke HTTPS-checks beoordelen:

1. Geen account-/gebruikersnaamveld op de daadwerkelijke loginpagina.
2. Geen accountidentiteit in de login/options-requestbody.
3. Discoverable login: allowCredentials is een lege array; ontbrekende/onjuiste waarde meldt actual=-1.
4. User verification is required, met afzonderlijke verwachte/werkelijke waarde.
5. Geen password-invoer of fallback, en geen enrollment/Begin-formulier op gewone login.

Er worden geen challenges, tokens, cookies, credentials of complete HTML/JSON-responses gelogd. De bestaande negatieve password-POST, Origin/antiforgerytests en scheiding tussen enrollment, login en onboarding blijven behouden.

De Node-presentersuite heeft nu afzonderlijke tests voor geen lezen/versturen van accountidentiteit, lege WebAuthn-allowlist en verplichte UV. Een extra markupcheck vergelijkt gewone Login en Setup. De echte productie-JavaScript wordt in de bestaande VM-harness uitgevoerd; serveroptions zijn daar gemodelleerd. Het is dus geen vervanging voor de HTTPS-endpointtest.

## Gewijzigde bestanden binnen deze reparatie

- tests/LummaSimulator.Web.Tests/HttpChecks.cs
- tests/WebAuthn.Ux.Tests/presenter.test.cjs
- docs/phase-6.5-presenter-login-test-fix.md
- Toevoeging in docs/phase-6.5-test-results.md

Eerdere niet-gecommitte werkboomwijzigingen zijn behouden. Geen productiecode gewijzigd voor deze reparatie.

## Werkelijke verificatie

- Gerichte Node-presentersuite: 12 PASS, 0 FAIL, geen skips.
- Volledige beschikbare Node-run (10 bestanden): 79 totaal, 77 PASS, 0 FAIL, 2 bestaande PE/publish-icoonskips.
- JS-syntax en git diff --check: PASS.
- Broninspectie: gewone login, Setup, JavaScript, options/verify-service en endpointfilters gecontroleerd zoals hierboven.
- Geïsoleerde locked restore opnieuw FAIL, exit 1, SDK 10.0.403: toegang geweigerd tot C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config. Geen bypass toegepast.
- Nieuwe C#-build/Razorcompilatie en volledige HTTPS/.NET-regressie: NIET uitgevoerd door die blokkade. Geen nieuwe geslaagde HTTPS-run geclaimd.

## Exacte lokale verificatie

Gebruik de bijgewerkte geïsoleerde bronkopie in een apart normaal PowerShell-venster:

```powershell
$verificationSource = 'C:/src/LummaSimulator\artifacts\presenter-login-regression-d6f5d670b8c34723b8de347560589cea\source'
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
    node --test tests/WebAuthn.Ux.Tests/presenter.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Presenter Node-tests mislukt.' }
    dotnet tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Web-/HTTPS-regressie mislukt.' }
    dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Volledige regressie mislukt.' }
} finally { Pop-Location }
```

De HTTP-fixture start uitsluitend haar eigen testportal-DLL met eigen synthetische database en testcertificaat. Niet de bestaande portal starten als verificatie. Sluit het aparte shellvenster om tijdelijke env-overrides te laten vervallen. Eventuele resterende HTTP-/policy-/renderingfouten zijn pas na de lokale .NET-run uit te sluiten; fysieke authenticators zijn hiermee niet getest.

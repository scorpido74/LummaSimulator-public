# Automatisch beheer lokale HTTP-testserver

De bestaande WPF-versieaanduiding blijft behouden. Deze wijziging raakt uitsluitend lokale synthetische telemetrie, de bestaande testserver en bijbehorende tests/bouwstappen. Webapplicatie, presentatorauthenticatie, databases, passkeys, certificaten en Data Protection-sleutels zijn niet aangepast.

## Bediening

1. Selecteer LIVE TELEMETRY. De lokale HTTP-functie staat standaard uit.
2. Kies de poort (standaard **8080**) en vink **Local HTTP POST** aan. De poortwijziging wordt verwerkt bij focusverlies.
3. **Starten...** betekent dat de functie is aangevraagd maar nog niet operationeel is. Een live run kan pas beginnen als de status **Actief – 127.0.0.1:8080** toont (met de werkelijk gekozen poort).
4. Bij **Fout – server niet bereikbaar** wordt een zichtbare toelichting getoond, bijvoorbeeld dat een onbekende service de poort bezet of dat serverbestanden ontbreken. Schakel uit en weer in om opnieuw te proberen; er is geen automatische herstartlus.
5. Uitvinken of LIVE TELEMETRY uitschakelen stopt alleen de server van deze appinstantie. Een externe herkenbare server blijft draaien. Sluiten van het WPF-venster wacht ook in rust op correcte afhandeling en opruimen van het eigen serverproces.

Nieuwe status- en foutteksten staan in lokale NL/EN-resourcebestanden. Nederlands is de neutrale fallback; een Engelse `CurrentUICulture` kiest en-GB. Er is in deze wijziging geen nieuwe WPF-taalschakelaar gebouwd. De bestaande statische WPF-teksten zijn behouden.

## Identiteit en bereikbaarheid

De receiver bindt uitsluitend aan **IPv4-loopback**, nooit aan een wildcard, LAN- of publieke interface. De client gebruikt alleen `127.0.0.1`, geen proxy, cookies, redirects, DNS-hostnamen of browserauthenticatie.

`GET /lumma-health?nonce=<willekeurige probe-ID>` retourneert productnaam, protocolversie, dezelfde probe-ID, proces-ID, poort en een per serverstart nieuwe instantie-ID. De client controleert alle velden en de bijbehorende `X-Lumma-TestServer`-header, met een maximale antwoordgrootte van 2 KB. Een generieke HTTP 200 is onvoldoende. De probe-ID is geen sessietoken en wordt niet gelogd. Een oude receiver zonder dit protocol geldt als onbekende service en wordt niet overgenomen of gestopt.

Dit protocol voorkomt het per ongeluk gebruiken van een andere lokale service; het is **geen cryptografische authenticatie of binary-attestation tegen kwaadwillende lokale code** die bewust het protocol nabootst. De machine en installatiebestanden moeten worden vertrouwd. Een netwerkidentiteit verleent nooit toestemming een extern proces te stoppen.

Vóór iedere POST wordt opnieuw gecontroleerd of de ontvanger het juiste protocol spreekt. De POST-response moet dezelfde instantie-identiteit hebben. De server accepteert uitsluitend de vaste `DummyBrowserData`-waarden en een corresponderende DemoRunId; verzoeken en antwoorden zijn begrensd. De receiver slaat geen payloadbestanden op en logt geen ontvangen inhoud, cookies, tokens of credentials. Er is geen proxyroute of HTTP-shutdownendpoint.

## Proces- en lifecyclebeheer

- De app start de vooraf gebouwde `LocalTestServer/LummaSimulator.LocalTestServer.exe` via een absoluut pad naast de WPF-uitvoer, zonder zichtbaar consolevenster of shell.
- Een seriële gate, revisienummers en annulering zorgen dat oude opstart-/monitoractiviteiten een nieuwere keuze niet kunnen overschrijven. Snel aan/uit/aan convergeert naar de laatste keuze. Een gestarte maar geannuleerde eigen server wordt eerst opgeruimd.
- Een nieuwe server krijgt maximaal tien seconden om daadwerkelijk gereed te worden. De opstartprobes gebruiken een interval van 100 ms. Dit is begrensde readiness-polling, geen vaste opstartwachttijd.
- Alleen de behouden `Process`-handle van het zelf gestarte kind verleent stoprecht. Er wordt niet op procesnaam, opgegeven PID of poort gezocht om een proces te beëindigen.
- De beheerde server heeft een private omgeleide stdin-pipe. Bij normaal stoppen ontvangt hij een stopregel en EOF. EOF bij het verdwijnen van de eigenaar beëindigt eveneens de server; er is geen publiek stopmechanisme.
- Normale afsluiting wacht op proces-exit. Na drie seconden is uitsluitend voor het eigen kind een kill-fallback aanwezig, gevolgd door wachten op exit. De proceshandle wordt opgeruimd. Gedeelde servers krijgen deze pipe of stopbevoegdheid niet.
- Eigen procesuitval wordt via het exit-event gemeld. Alle gebruikte servers worden daarnaast iedere 750 ms gecontroleerd, met maximaal twee seconden per healthrequest. Een mislukte HTTP-stap zet de status direct op fout, zonder de volgende probe af te wachten. Een geleende server kan daardoor bij uitval enkele seconden detectievertraging hebben.
- De netwerkfase registreert een mislukte of geweigerde POST als `HttpPost/Failed` en fase 6 faalt. Dit is geen bevestiging van EDR-detectie.
- Instellingen blijven zoals voorheen tijdens een run vergrendeld. Afsluiten annuleert eerst lopende werkzaamheden en ruimt daarna de eigen receiver op.

## Build en toekomstige Windows-installatie

`shared/LocalTestServer.Build.targets` bouwt de bestaande server als projectreferentie en plaatst de benodigde serverbestanden in `LocalTestServer` bij WPF en de twee relevante testuitvoeren. De publish-stap publiceert de server met dezelfde RID/self-contained-instelling in die submap. Er zijn geen nieuwe externe packages toegevoegd.

Voorbeeld vanuit de projectmap:

```powershell
dotnet build LummaSimulator.App/LummaSimulator.App.csproj
dotnet publish LummaSimulator.App/LummaSimulator.App.csproj -c Release -o <installatiemap>
```

De geïnstalleerde app gebruikt geen SDK of `dotnet run`. Een framework-dependent installatie heeft de passende .NET 10-runtime nodig; een zelfstandige Windows-publicatie moet de volledige door publish gegenereerde map behouden. Alleen de WPF-exe kopiëren is onvoldoende. Een clean-machine/self-contained/RID-installatietest is niet uitgevoerd.

Een handmatig gestarte externe receiver blijft mogelijk met de gebouwde server-exe en het poortargument, zonder `--managed`. Deze wordt met Ctrl+C door de eigenaar gestopt. Handmatige serverstart is voor normaal gebruik niet meer nodig.

## Uitgevoerde validatie

Alle bestaande regressierunners zijn rechtstreeks uitgevoerd vanuit aparte build-output; geen extra `dotnet test`-run wordt geclaimd.

| Controle | Resultaat |
| --- | --- |
| Volledige regressieprojectstructuur | Build geslaagd, 0 waarschuwingen, 0 fouten |
| .NET-webservices en echte HTTPS-integratie | 411 controles geslaagd |
| JavaScript bezoekers/presentatoren/lokalisatie | 43 tests geslaagd, 0 mislukt |
| Simulator-smoke | Geslaagd, inclusief 2778 ATT&CK/status/exportasserties, bestaande telemetrie- en monitorcontroles |
| Nieuwe lokale-serverchecks in smoke | 24 lifecyclecontroles geslaagd |
| WPF-integratie | 20 controles geslaagd, inclusief bedienen checkbox en sluiten van een inactief venster met eigen server |
| Framework-dependent Windows-publicatie | Geslaagd; receiver opgenomen in eigen submap |
| Gepubliceerde apphost/receiver | 4 controles geslaagd: readiness/identity, synthetische POST, ongeldige invoer weigeren, schoon stoppen |

De lifecyclechecks omvatten starten, stoppen, herhaald snel schakelen, een geleende server behouden, poortconflict, uitval van eigen én geleende server, een mislukte echte netwerkfase, NL/EN-status, normaal afsluiten, gelijktijdig afsluiten tijdens startup en EOF van de eigenaarpipe. De bestaande telemetrietest verstuurt de echte vaste synthetische payload naar de echte receiver. De redirecttest behoudt een gecontroleerde protocolfixture en bevestigt dat geen redirect wordt gevolgd.

Logs: `verification/bin/local-http-build-results.log`, `local-http-web-results.log`, `local-http-js-results.log`, `local-http-smoke-results.log`, `local-http-wpf-results.log`, `local-http-publish-results.log` en `local-http-published-check-results.log`.

Daarnaast is een build met gescheiden projectmappen onder `verification/bin/local-http-separated-artifacts` gecontroleerd. Ook de 24 gerichte lifecyclechecks en 20 WPF-controles slagen vanuit hun afzonderlijke projectuitvoeren. Logs: `local-http-separated-build-results.log`, `local-http-focused-separated-results.log` en `local-http-wpf-separated-results.log`. Na afronding zijn bij een read-only procescontrole geen `LummaSimulator.LocalTestServer.exe`-processen aangetroffen.

Herhaalbare controle van de gepubliceerde receiver:

```powershell
pwsh -NoProfile -File verification/Test-LocalHttpPublished.ps1 -PublishDirectory <publishmap>
```

Bij de testafsluiting wordt uitsluitend de door het testscript behouden kindproceshandle gebruikt. Geen onbekende of live services worden gestopt. De ontwikkeltests gebruiken willekeurige vrije loopbackpoorten en eigen uitvoer-/gegevensmappen; de bezette-poorttest gebruikt een eigen gecontroleerde TCP-listener. Ze beëindigen geen bestaande service op poort 8080.

## Bestanden

Productie en build:

- `LummaSimulator.App/LocalTestServerManager.cs` (nieuw).
- `LummaSimulator.App/MainViewModel.cs`.
- `LummaSimulator.App/MainWindow.xaml` en `MainWindow.xaml.cs`.
- `LummaSimulator.App/TelemetryService.cs`.
- `LummaSimulator.App/LocalHttpText.resx` en `LocalHttpText.en-GB.resx` (nieuw).
- `LummaSimulator.App/LummaSimulator.App.csproj`.
- `shared/LocalTestServerProtocol.cs` en `LocalTestServer.Build.targets` (nieuw).
- `tools/LummaSimulator.LocalTestServer/Program.cs` en `LummaSimulator.LocalTestServer.csproj`.

Tests:

- `tests/LummaSimulator.SmokeTests/LocalServerChecks.cs` (nieuw).
- `tests/LummaSimulator.SmokeTests/Program.cs`, `TelemetryChecks.cs`, `LummaSimulator.SmokeTests.csproj`.
- `verification/Program.cs`, `Verification.csproj`.
- `verification/Test-LocalHttpPublished.ps1` (nieuw).

Documentatie: `README.md` en dit document. Gegenereerde build-, publicatie- en testbestanden onder `verification/bin` zijn verificatie-uitvoer, geen definitieve distributierelease.

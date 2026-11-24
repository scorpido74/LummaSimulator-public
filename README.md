# LummaSimulator

Windows WPF-presentator en zelfstandig ASP.NET Core 10 Razor Pages-portaal met SQLite en FIDO2/WebAuthn voor veilige securitydemo's. Beide applicaties werken onafhankelijk; de optionele HTTPS-portalkoppeling staat standaard uit.

## Actuele status — v0.6.5-beta.1

Het huidige broncheckpoint bevat persoonlijke presentatoraccounts, eventgebonden autorisatie, uitnodigingen en eigendomsoverdracht, een begeleide first-runroute, Prize Experience en Event Edition. Dit broncheckpoint is geen bevestiging van een vrijgegeven installatiepakket.

De operator heeft een geslaagde lokale .NET-verificatie gemeld voor de bestaande applicatiecode. De daaropvolgende packagingwijzigingen en het uiteindelijke pakket vallen niet automatisch onder dat bewijs. Bij de laatste vastgelegde pakketcontrole ontbraken Windows-RID-assets; ZIP-, installatie- en sidecaracceptatie zijn nog niet voltooid. Zie de [pre-releasevoorbereiding](docs/public-prerelease-preparation.md) voor resultaten en resterende stappen. Oudere faseverslagen hieronder beschrijven hun eigen checkpoint; hun toenmalige blokkades zijn geen actuele conclusie over alle applicatiecode.

| Onderwerp | Actuele documentatie |
| --- | --- |
| Presentatorbeheer, eventworkspace en autorisatie | [Fase-6.5-operatorhandleiding](docs/phase-6.5-operator-guide.md) en [implementatie](docs/phase-6.5-implementation.md) |
| Nieuwe Event Edition-installatie, browserbootstrap en Onboarding | [Installatiehandleiding](docs/event-edition-installatie.md) |
| Prize Experience | [Functionele beschrijving](docs/phase5-prize-experience-functional.md) en [architectuur](docs/phase5-prize-experience-architecture.md) |
| Ontwikkeldemo starten en stoppen | [Ontwikkelstarter](docs/development-start.md) |
| Pakket bouwen en acceptatie | [Pakketdocumentatie](packaging/EventEdition/README.md) en [pre-releasevoorbereiding](docs/public-prerelease-preparation.md) |

De [functionele requirements](docs/functional-requirements.md) bevatten ook toekomstige eisen. Gebruik de implementatie- en operatorhandleiding om vast te stellen wat daadwerkelijk beschikbaar is.

## Achtergrond en eerdere ontwikkelfases

Afsluiten/portaldiagnostiek: zie [WPF-afsluitcrash en afzonderlijke portal-500](docs/shutdown-and-portal-500-fix.md) voor oorzaak, idempotente cleanup, null-webrootfallback en de nog geblokkeerde .NET-acceptatie.

Branding en talen: neutraal LummaSimulator-icoon, configureerbare lokale resellerlogo's en NL/EN-keuze in WPF en portal. Zie [instellingen, icoonacceptatie en testbeperkingen](docs/branding-and-languages.md). De actuele .NET-build blijft door NuGet-configuratietoegang onbevestigd.

Fase 6.3: interactieve ClickFix met een browserintern Uitvoeren-venster, fictief herstel, expliciet bevestigde SAFE-aanvalspresentatie en adaptieve WPF-/web-layout. Zie [werking, veiligheidsgrenzen en werkelijke teststatus](docs/phase6-3-clickfix-responsive.md); .NET-build en fysieke/DPI-acceptatie zijn nog niet bevestigd.

Ontwikkeldemo starten vanuit deze repository: dubbelklik **Start-LummaDev.bat**. De starter bouwt actuele broncode in een aparte werkmap, start portal en WPF en opent `https://localhost:7243`. Zie [vereisten, bestaande opslag, foutmeldingen en stoppen](docs/development-start.md). Normale portalstartup kan bestaande migraties en idlecleanup uitvoeren; de starter reset geen data of certificaten.

Event Edition, sprint 3: de optionele WPF-tab **Event Edition** koppelt een voorbereide WPF-run via het bestaande presentatorportaal expliciet aan een portalrun. De eerste ClickFix-keuze kan worden gemeld; de presentator bevestigt zelf vóór fase 1 of kiest handmatige fallback. Het minimale beursregister staat apart van SQLite. Zie [gebruik, beveiligingsgrenzen en acceptatie](docs/event-edition-sprint3-report.md). Geen automatische MDR-acties of remote portalintegratie. De onderstaande sprint-2-beschrijving is de eerdere baseline.

Event Edition, sprint 2: WPF opent met een scenario-introductie voor de presentator en gebruikt de bestaande zes fases en startbediening. Na een geslaagde passkeyaanmelding biedt de portal een veilige, interactieve ClickFix-oefening op `/ClickFix`. De keuzes blijven in de browser en voeren niets uit; opnieuw beginnen of herladen wist de oefenvoortgang. Er is nog geen melding naar WPF of het SOC. Zie het [sprintverslag](docs/event-edition-sprint2-report.md) en het [contractvoorstel voor sprint 3](docs/architecture/event-edition-sprint3-clickfix-contract.md). De uitgebreide v0.5-platformontwikkeling is voor deze evenementensprints bevroren.

Ontwikkeling v0.5: fase 4 voegt persoonlijke presentatoraccounts met WebAuthn, rollen, evenementbeheer en audit toe. Zie [de actuele fase-4-instructies](docs/LummaSimulator-v0.5.md#fase-4--beveiligde-presentatoromgeving) voor lokale bootstrap, uitnodigingen, reservesleutels en HTTPS-toegang. Deze eerdere fase kende geen wachtwoordfallback of winactie. Prize Experience is later toegevoegd; fysieke acceptatie blijft afzonderlijk vereist.

## Passkey Demo Portal en WPF-simulator v0.4

`LummaSimulator.Web` is een zelfstandige ASP.NET Core 10 Razor Pages-portal met echte WebAuthn, tijdelijke SQLite-accounts en een apart beveiligd presentatordashboard. `LummaSimulator.App` behoudt de zes interne fases, met ATT&CK-techniekkaarten en Navigator-export. Een optionele, beveiligde lokale metadata-API koppelt de Session Exposure Monitor via DemoRunId. Een actieve sessie wordt nooit automatisch als gestolen of gedetecteerd aangeduid; exposure/replay gebruiken uitsluitend fictieve objecten.

Beide applicaties draaien onafhankelijk, ook op twee schermen. Inmiddels zijn een ontwikkelstarter en Event Edition-pakketbouwer aanwezig; zie de actuele status hierboven voor de nog openstaande pakketacceptatie. De [volledige v0.4-handleiding](docs/LummaSimulator-v0.4.md) bevat configuratie, paden, offline HTTPS/DNS, monitorbeveiliging, logo-bronnen, ATT&CK-mapping, testresultaten en handmatige acceptatietests.

Na installatie van de .NET 10 SDK:

```powershell
dotnet restore LummaSimulator.slnx
dotnet build LummaSimulator.slnx
dotnet dev-certs https --trust
dotnet run --project LummaSimulator.Web --launch-profile https
```

Open **https://localhost:7243**. Kies een apparaatroute, registreer een passkey en test daarna de afzonderlijke authenticatie. De server ontvangt nooit de privésleutel. De localhost-origin werkt voor de browser op deze computer; voor afzonderlijke telefoon/iPad-toegang is een bereikbare, vertrouwde HTTPS-host nodig.

Het presentatordashboard op `/Presenter` gebruikt persoonlijke WebAuthn-accounts. Er bestaat geen standaardadministrator, openbare accountregistratie of wachtwoordfallback. De eerste administrator start een lokale onderhoudsprocedure vanuit een verhoogde terminal en registreert daarna een sleutel via de bestaande geldige HTTPS-origin. Gebruik de [uitvoerbare bootstrapprocedure](docs/LummaSimulator-v0.5.md#eerste-administrator-instellen) en registreer daarna een reservesleutel.

Normaal aanmelden op `/Presenter/Login` werkt zonder accountnaam: kies een geregistreerde discoverable passkey in de browser. Herauthenticatie blijft aan het aangemelde account gebonden. Zie [de loginmigratie en acceptatietest](docs/LummaSimulator-v0.5.md#gebruikersnaamloze-presentatorlogin) voor gegevensbehoud en browserbeperkingen.

De oude `Presenter__PasswordHash` en `--hash-presenter-password` geven geen toegang meer. Bestaande bezoekersgegevens, passkeys en statistieken blijven behouden; de oude gedeelde presentatoridentiteit wordt niet als persoonlijk account overgenomen.

De [v0.5-ontwikkeldocumentatie](docs/LummaSimulator-v0.5.md) is leidend voor de actuele ontwikkeling. De [v0.4-documentatie](docs/LummaSimulator-v0.4.md) beschrijft de eerdere versie. De [v0.3-handleiding](docs/Passkey-Portal-v0.3.md) en het [oorspronkelijke plan](docs/Passkey-Portal-v0.3-plan.md) blijven als historische achtergrond beschikbaar.

Goedgekeurde directe dependencies blijven `Fido2.AspNet` **4.3.1** en `Microsoft.Data.Sqlite` **10.0.14**, met vastgelegde transitieve dependencies. Officiële Infinigate/Yubico-SVG's, eigen CSS/JS en systeemfonts worden lokaal geladen; geen runtime-CDN of analytics. De kleuren en assetbronnen staan in de v0.4-handleiding.

Voor de sessiemonitor: genereer een afzonderlijke key met `dotnet run --project LummaSimulator.Web -- --generate-monitor-key`. Configureer de getoonde hash als `Monitor__KeyHash` in de webomgeving en de raw key als `LUMMA_MONITOR_KEY` in de WPF-omgeving. Geef vervolgens op `/Presenter` alleen de gewenste demo toestemming en gebruik die DemoRunId in WPF. De API geeft nooit echte bezoekerscookies of sessietokens terug.

Portable paden: `LUMMA_DATA_DIRECTORY`, `LUMMA_LOG_DIRECTORY`, `LUMMA_EXPORT_DIRECTORY` en het WPF-settingsbestand. Relatieve paden zijn niet afhankelijk van de startwerkmap. De standaardlocaties in de onderstaande simulatiebeschrijving blijven gelden zolang geen override is ingesteld.

Volledige regressiesuite uitvoeren (.NET 10 SDK en Node.js voor de JS-tests). Gebruik een geïsoleerde bronkopie, eigen tijdelijke testgegevens en de eigen gebouwde portal-DLL. Start de bestaande portal niet als controle. De orchestrator voert Web.Tests, Node UX-tests, SmokeTests en op Windows WPF Verification uit:

```powershell
# Voer uit vanuit een geïsoleerde bronkopie, na een Release-build.
$env:LUMMA_TEST_WEB_DLL = Join-Path $PWD 'LummaSimulator.Web/bin/Release/net10.0/LummaSimulator.Web.dll'
dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj --configuration Release
```

De tests controleren echte serververificatie met een test-only software-authenticator en echte lokale HTTPS-aanvragen. Dit bevestigt geen fysieke Windows Hello-, YubiKey-, iOS-, Android- of iPad-test. Browserkeuze, cross-device-ondersteuning en apparaat-PIN/biometrie blijven afhankelijk van de gebruikte combinatie.

## Bestaande simulatiefuncties in de WPF-app

WPF-dashboard met C#, .NET 10 en MVVM. De zes uitvoeringsfases, tijdmeting, voortgang, live activiteitenlog en start/stop/reset blijven behouden. De visuele kaarten zijn ATT&CK-georiënteerd. De WPF-app heeft geen externe NuGet-packages of administratorrechten nodig.

## Bouwen en starten

Windows met de .NET 10 SDK:

```powershell
dotnet build LummaSimulator.slnx
dotnet run --project LummaSimulator.App
```

De applicatie start standaard in **SAFE SIMULATION**. Vink **LIVE TELEMETRY** aan voordat je op Start klikt om gecontroleerde echte Windows-activiteiten uit te voeren. De modus en HTTP-instellingen kunnen tijdens uitvoering niet worden gewijzigd. Elke start krijgt een nieuwe GUID als **DemoRunId**, zichtbaar in het dashboard en opgenomen in alle sessielogregels.

## Werkelijke activiteiten tegenover simulatie

| Fase | SAFE SIMULATION | LIVE TELEMETRY |
| --- | --- | --- |
| 1. Initial execution | Teksten en wachttijden | Start de echte `whoami.exe` uit de Windows-systeemmap, zonder shell of verhoging van rechten. Registreert procesnaam, PID, starttijd, eindtijd en exitcode. |
| 2. System discovery | Vaste fictieve inventaris | Dezelfde vaste fictieve inventaris; geen echte hostinventarisatie. |
| 3. Browser discovery | Fictieve browserlijst | Maakt een eigen runmap, eigendomsmarker en `synthetic-browser.json` met vaste dummywaarden. Geen browser wordt gestart of doorzocht. |
| 4. Credential collection | Fictieve records in meldingen | Leest uitsluitend het eigen `synthetic-browser.json`. De inhoud moet exact overeenkomen met de verwachte dummywaarden en DemoRunId. |
| 5. Data staging | Denkbeeldig pakket | Schrijft `collected-data.json` met uitsluitend de fictieve browserrecords. |
| 6. Simulated exfiltration | Meldingen zonder verbinding | Standaard gesimuleerd. Alleen met Local HTTP POST ingeschakeld: een echte POST met dummy-JSON naar `http://127.0.0.1:<poort>/lumma-demo`. |

`whoami.exe` vraagt de huidige Windows-identiteit op en toont deze in een normale console; het venster kan kort zichtbaar zijn. De applicatie vangt die uitvoer niet op en slaat de echte accountnaam niet op. De DemoRunId staat in de eigen proceslogs en in de omgevingsvariabele `LUMMA_DEMO_RUN_ID` van het kindproces; het is geen whoami-commandlineargument.

In beide modi worden echte lokale JSON-sessielogs geschreven. **Clean demo files** voert echte bestandsverwijderingen uit, ook als de geselecteerde trainingsmodus SAFE SIMULATION is. Tijdens opruimen toont de modusindicator LIVE TELEMETRY.

## Locaties, logging en opruimen

- Sessielogs: `%LOCALAPPDATA%\LummaSimulator\Logs\<DemoRunId>.json`.
- Live-demobestanden: `%LOCALAPPDATA%\LummaSimulator\DemoRuns\<DemoRunId>\`.
- Logvelden: versie, modus, DemoRunId, sessiestatus, fasestatussen en uitvoeringstijden; per activiteit tijdstempel, type, uitkomst en relevante details zoals pad of procesgegevens.
- `Attempted` is een poging, `Succeeded` een bevestigde lokale handeling. `Simulated` en `Skipped` betekenen dat de betreffende handeling niet echt is uitgevoerd. Een HTTP-response bewijst alleen een lokale response, geen securitydetectie.
- JSON wordt bij start, na iedere fase en bij einde opgeslagen via een tijdelijk bestand. Het dashboard wordt direct bijgewerkt. Een geforceerde procesbeeindiging kan gebeurtenissen sinds het laatste checkpoint verliezen. Opslagfouten zijn zichtbaar; zonder schrijfbare logmap kan geen volledige audit worden gegarandeerd.
- **Stop** annuleert via CancellationToken. Een onderbroken fase wordt `failed`; volgende fases blijven `pending`. Een gestart kindproces wordt afgewacht en zo nodig beeindigd. Bestanden blijven beschikbaar voor onderzoek en opruimen.
- **Reset** wist alleen het dashboard. **Clean demo files** verwerkt herkende GUID-runmappen met een passende `demo-owner.txt` en uitsluitend de drie bekende demobestanden. Het verwijdert geen logs. Onbekende inhoud blijft staan; junctions/symlinks worden geweigerd. Er wordt niet recursief verwijderd.
- Sluiten tijdens uitvoering annuleert de actieve taak en wacht op de laatste logopslag.

## Optionele lokale HTTP-test

HTTP staat standaard uit. Er wordt geen externe hostnaam gebruikt, geen proxy ingesteld of gebruikt, geen redirect gevolgd en geen cookie of authenticatie meegestuurd. Alleen de vaste IPv4-loopbackhost `127.0.0.1` is mogelijk; de gebruiker kiest alleen de poort. Timeout: vijf seconden. Een onbereikbare server of foutresponse laat fase 6 falen en wordt gelogd.

De bestaande dependencyvrije .NET-testserver wordt automatisch beheerd. Kies de poort (standaard 8080) en vink **Local HTTP POST** aan. De app controleert eerst het identity-endpoint, start zo nodig de meegebouwde server zonder consolevenster en toont **Starten...**, **Actief**, **Fout** of **Uitgeschakeld**. Een live run kan pas starten als de server gereed is. Een onbekende service op dezelfde poort wordt geweigerd en blijft ongemoeid.

Uitvinken, LIVE TELEMETRY uitschakelen of het WPF-venster sluiten stopt uitsluitend de door deze appinstantie gestarte server. Een reeds draaiende herkenbare server wordt gebruikt maar niet gestopt. De receiver accepteert alleen de vaste synthetische payload en slaat deze niet op. Bij uitval wordt de status aangepast; een mislukte POST laat fase 6 falen.

Build en publish plaatsen de server in `LocalTestServer` naast de WPF-app. De runtime gebruikt de meegeleverde Windows-apphost, **geen `dotnet run` of SDK**. Een framework-dependent installatie heeft wel de passende .NET-runtime nodig, net als de WPF-app zelf. Zie [beheer, publicatie, tests en beperkingen](docs/Local-HTTP-testserver.md).

## Onderzoek met Rapid7 InsightIDR en WithSecure Elements EDR

De app genereert lokale activiteiten die je in je eigen onderzoeksomgeving kunt vergelijken met de beschikbare endpoint- en logtelemetrie. Begin bij de DemoRunId in het dashboard en JSON, de tijdstempels, het whoami-PID, de procesnaam en de unieke runmap. De optionele HTTP-request bevat `X-Lumma-DemoRunId`. Niet ieder platform of iedere configuratie verzamelt al deze velden of lokale loopbackverbindingen.

De app integreert niet met de platform-API's en verzendt geen logs naar deze diensten. Eventuele verzameling van de JSON-bestanden moet je in je eigen omgeving configureren. **Zichtbaarheid, ingestie, alerts en detecties zijn niet door deze app bevestigd.** Controleer dit afzonderlijk in het betreffende platform. `edrDetectionConfirmed` blijft daarom `false`; een geslaagde run of HTTP-status is geen bewijs van een EDR-detectie.

Geen echte browserprofielen, cookies, wachtwoorden of tokens worden benaderd. Geen externe netwerkverbindingen, persistence, privilege escalation of beveiligingsomzeiling. Alle opgeslagen browserwaarden zijn fictief, inclusief `example.invalid` en expliciete `FAKE-*`-waarden.

## Architectuur en controles

- `MainWindow.xaml` / code-behind: dashboard, bindingen, scrollen en sluiten.
- `MainViewModel.cs`: modus, commands, DemoRunId, fasecoordinatie, annulering en foutafhandeling.
- `Models.cs` / `Infrastructure.cs`: observeerbare fasemodellen, logrecords en commands.
- `Services.cs`: bestaande visuele simulatie en lokale JSON-opslag.
- `TelemetryService.cs`: afzonderlijke service voor het kindproces, eigen dummybestanden, loopback-HTTP en begrensd opruimen.

Dependencyvrije controles:

```powershell
dotnet run --project tests/LummaSimulator.SmokeTests
dotnet build verification/Verification.csproj
dotnet build tools/LummaSimulator.LocalTestServer
```

De smoke-tests voeren zowel de visuele sessiestromen als echte whoami-processen, dummybestandsacties en tijdelijke loopback-HTTP uit. Ze testen ook redirects, annulering, foutafhandeling en opruimen. Testbestanden staan in een eigen `DemoRuns\Verification-<guid>`-scope; opruimen van de tests raakt geen andere demoruns. De tests laten JSON-auditlogs en een lege verificatiescopemap achter.

De bestaande WPF-integratiecontrole kan apart worden gestart met `dotnet run --project verification/Verification.csproj`; deze opent het echte dashboard en controleert de visuele simulatiemodus. Visuele layout en bevestiging in de securityplatforms vereisen afzonderlijke controle.

Fase 6.4: [monitorbeheer, WPF-koppeling en verificatiestatus](docs/phase6-4-monitor-integration.md).

### Publieke Windows-prerelease

`portal.example.test` is een gereserveerd voorbeeld. Kies bij installatie een eigen HTTPS-hostnaam en een vertrouwd certificaat. Gebruik de gekozen hostnaam voor de WebAuthn-RP-ID en de exacte HTTPS-origin voor de toegestane origin. Gebruik de bestaande setup uit [de Windows-installatiehandleiding](docs/event-edition-installatie.md). Bij een upgrade behoud je de bestaande RP-ID, origin en certificaatverwijzing, zodat bestaande passkeys blijven werken. Bewaar installatieconfiguratie, database en keyring lokaal.

# Eerste Windows-release: verpakking, helpers en netwerk

## Testpakket Presenter Helper en Demonetwerk

`artifacts/helper-network-test-30fa3124eed14eb28f6be8a3561108fe/dist/LummaSimulator-PhysicalAcceptance-TEST-v0.7.0-beta.1-win-x64.zip` bevat Helper 0.1.1. De gecontroleerde SHA-256 is `3bfa7e7860cf5a1bb6d4208195273488915c3b70f123ea185d17376684c5cfcb`. De builder doorliep locked restore, Release-build (0 fouten, 3 waarschuwingen), vier publishes, beide sidecartests en ZIP-manifestcontrole. De generieke helperdownload bevat geen certificaat of beschermde installatiegegevens. De optionele installatie-export is gecontroleerd met een synthetische CA-onafhankelijke servercertificaatfixture en levert alleen publieke DER met trustinstructies en een nieuwe ZIP-hash op.

Zie [upgrade en herstel](helper-network-upgrade.md) voor de openbare URL's, certificaatexport, behoud van installatie-identiteit en fysieke acceptatie. Voor productie is de werkelijke certificaatketen, koppeling op de demoapparatuur en de volledige ronde-/herstartproef apart te verifiëren.

De volledige geïsoleerde regressie slaagde met exitcode 0 in `artifacts/helper-network-regression-ca3736e815304100a5c3cecfb9549dbc/regression.log`. De tests controleerden onder meer de anonieme helperdownload en checksum, de netwerkvalidatie, echte Chrome-weergave op 360 px met een onderschepte clipboard-aanroep, nul/één/meerdere actieve events, expliciete links en behouden demosessies. De vergelijking met de 420 bronhashes van het pakket vond alleen gewijzigde verificatiebestanden. De afzonderlijke sessiedemo-test `artifacts/helper-presence-95ec28d110dc469d8f3dc30679b6b8b8/presence.log` slaagde ook. Die test gebruikt een klokfixture voor twee gelijke apparaatnamen, veroudering, offline, gerapporteerde receiverweigering, reconnect en vervallen portalgoedkeuring na herstart.

## Testpakket voor de fysieke cybertruckacceptatie

Gebruik `Build-EventEdition.ps1 -PhysicalAcceptance` in een nieuwe outputmap als de volledige regressie de tijdige apparatuurtest blokkeert. De ZIP heet `LummaSimulator-PhysicalAcceptance-TEST-…-win-x64.zip`; ook de hoofdmap en `PHYSICAL-ACCEPTANCE-TEST.txt` in de ZIP zijn als test gemarkeerd. De volledige regressie staat in deze modus open. Locked restore, Release-build, vier publishes, de launcher- en netwerkplancontroles, de sidecartest vóór en na uitpakken, bestandsmanifest en externe checksum blijven vereist. De normale aanroep behoudt de volledige regressie als releasevoorwaarde.

De testmodus maakt een eigen bronkopie. `PHYSICAL-ACCEPTANCE-TEST.txt` vermeldt de volledige basiscommit, `SOURCE-SHA256SUMS.txt` bevat de SHA-256 van elk gekopieerd bronbestand uit de werkboom en `working-tree-status.txt` staat bij de interne builderlogs. Zo zijn ook ongetrackte en lokaal gewijzigde invoerbestanden te herleiden. Bewaar de corresponderende `source` en logs bij de acceptatieresultaten.

De eerste poging onder `restricted test account` strandde op leestoegang tot `NuGet.Config`. Onder `standard Windows user` slaagden restore, build en alle vier publishes. De merge stopte bij `WindowsBase.dll`, het enige overlappende bestand met een verschillende hash. De Helper publiceert deze assembly vanuit Windows Desktop, terwijl de console-Updater de variant uit de .NET-runtime meebracht. De Updater gebruikt nu dezelfde Windows Desktop-frameworkreferentie als de Helper. Bij samenvoegen blijven alle overlappende bestanden aan dezelfde hashcontrole onderworpen.

De herhaling onder `standard Windows user` leverde `artifacts/physical-acceptance-e67ea2b67e9949a9aed87934dbaa1927/dist/LummaSimulator-PhysicalAcceptance-TEST-v0.7.0-beta.1-win-x64.zip` op. SHA-256 `21D40989B561B2DE1CD0DC253C12C4B65F93420EAA112E366FAFF576822165A0` is opnieuw berekend en komt overeen met het bijbehorende checksum-bestand. Werkmap `artifacts/event-edition-package-3fa4d98a624b4392818e19489d7f453a` bevat de buildlogs, status van de werkboom en de uitgepakte testversie. De bronregistratie omvat basiscommit `d8da9ee2efebe9e21740507e5a58c347aca38352`, 413 bronhashes en 174 Git-statusregels. Locked restore, Release-build (0 fouten, 3 waarschuwingen), alle publishes, samenvoeging, manifestcontrole en beide gepubliceerde sidecartests slaagden. App-, Portal-, Helper- en Updater-executables zijn in de uitgepakte versie aanwezig. De fysieke proef staat open.

De volledige releasebuilder stopte met exitcode 1 in `artifacts/event-edition-package-f7a8254bc7ed47ce904e8f81eaeaf1db/regression.log`. De sessiedemo slaagde ook binnen deze regressie. De eerstvolgende fout is HTTP 404 op `GET /Presenter/Events?kind=prize&id=<ownerEventId>` met het beheerdersaccount (`PrizeEventHttpChecks.cs:232`), vóór de POST voor het uitnodigen van een nieuw account. Er is geen releasezip gemaakt. De broninventaris van het testpakket is vergeleken met de releasebronkopie. Alleen dit statusdocument verschilde.

### Beheeroverzicht in de Prize-fixture

De mislukte GET gebruikte de presentatiewerkruimte. `ownerEventId` hoort bij het door `http-event-owner` aangemaakte evenement `Owner scoped policy`, met status `Open`, modus `Instant` en nul rondes. De bootstrapbeheerder `http-admin` heeft een actieve WebAuthn-sessie met de rol `Administrator` en is geen eigenaar of lid van dit evenement. De actieve werkruimte vereist ook voor beheerders een eigenaars- of ledenrelatie. De beheerlink gebruikt `overview=true`; het beheeroverzicht controleert vervolgens de bestaande eventautorisatie. De fixture gebruikt nu die link, verifieert de verwachte 404 op de actieve route en 200 op de beheerroute en behoudt de positieve uitnodigingscontrole en de weigering voor gewone presentatoren.

De gerichte `--prize-http-checks` onder `standard Windows user` slaagde met exitcode 0. Voor synthetisch evenement `7151d96b-133a-4fd2-884f-e600a4edaf1c` logde de fixture `Open`, `Instant`, `rounds=0`, HTTP 404 voor de actieve route en HTTP 200 met `overview=true`. Ook de uitnodigingscontrole slaagde. De normale releasebuilder startte vervolgens zijn volledige regressie. Zie `artifacts/prize-overview-a029d91c863e41edbe8c7ac0c1c8a0a1`.

De daaropvolgende volledige regressie passeerde Prize, inclusief de browsertest voor evenementaanmaak. Ze stopte later in `HttpChecks.cs:560` omdat de test voor de gelokaliseerde startpagina drie kaarten verwachtte. Het bestaande Razor-scherm biedt op de standaardroute vier keuzes, met iPad als vierde. De fixture controleert nu alle vier in de bestaande volgorde en verifieert de taal voor beide weergaven. De uitvoer van deze eerste volledige regressie staat in `artifacts/event-edition-package-7d971aadfb374615bd90c2fedeeac024/regression.log`.

Sinds het fysieke testpakket zijn `tests/LummaSimulator.Web.Tests/PrizeEventHttpChecks.cs`, `tests/LummaSimulator.Web.Tests/HttpChecks.cs` en dit statusdocument gewijzigd. Voor de definitieve ZIP blijven de checksum en startcontrole van de drie uitgepakte onderdelen relevant. Geslaagde fysieke controles van pairing, YubiKey, twee rondes en herstart kunnen bij identieke runtime-invoer worden overgenomen.

De definitieve Windows x64-build staat in `artifacts/cybertruck-final-86a60a59ba0d49558c1586241fef88a7/dist/LummaSimulator-EventEdition-v0.7.0-beta.1-win-x64.zip`. De gecontroleerde SHA-256 is `EE02D062F72D1AF0B0A409B026F3BC99F4800712933B2EECFDECED8D93BF9C22`. Locked restore, volledige regressie (Node 122 geslaagd, 2 overgeslagen, 0 fouten), vier publishes, manifestcontrole en sidecartest uit de uitgepakte ZIP slaagden. Zie `artifacts/event-edition-package-10b3c806b5bb46acac202e9fe8772cdf`. De bronvergelijking bevestigt alleen de twee testbestanden en dit statusdocument als gewijzigd. De nieuwe ZIP bevat opnieuw gebouwde binaries met andere bestandshashes. Voer daarom een korte pakketcontrole uit vanuit een verse uitpakmap: start App en Portal, download en start de bijbehorende Helper en verifieer verbinding en evenementkeuze. Herhaal alleen openstaande of mislukte fysieke scenario's wanneer de demo-inrichting gelijk blijft.

```powershell
Set-Location 'C:/src/LummaSimulator'
$output = Join-Path (Get-Location).Path ('artifacts/physical-acceptance-' + [guid]::NewGuid().ToString('N'))
& .\packaging\Build-EventEdition.ps1 -PhysicalAcceptance -OutputDirectory $output
```

Controleer de ZIP en checksum en test vanuit het uitgepakte pakket op de echte apparatuur met nieuwe synthetische opslag. Volg de onderstaande proefstappen voor App, Portal en Helper-download en -pairing. Test daarna twee volledige rondes met toestemmingen, loting, zichtbare winnaar en aliashistorie, en herhaal bereikbaarheid, aanmelding en pairing na de herstart.

## Vervolg op de winnaar in de gesloten sessiedemoronde

De gerichte run `artifacts/cybertruck-release-a91eeb7b1f7c4b04bf46049bf9aedc29` onder de gewone Windows-account passeerde de QR-autorisatie. State en Image gaven voor de eigenaar en het geaccepteerde lid 200 met respectievelijk `application/json` en `image/svg+xml`. De outsider en de nog onbevestigde genodigde kregen voor beide 404 zonder mediatype. Alle acht gelogde verzoeken hadden geen redirect. De controle op weigering na sluiting van het QR-evenement slaagde ook.

De eerste fout zat bij de winnaarsalias vóór accountbeëindiging. Voor **Synthetic session B** schreef de fixture één deelnemer in ronde 1 in (`foreign`, alias `other.Alias`) en gaf die deelnemer toestemming voor de synthetische weergave. De fixture voltooide zijn oefening niet. `CloseRound` kan een ronde zonder lotingsgerechtigde deelnemers sluiten. Dat verklaart waarom de enige deelnemer geen winnaar werd. De resultaat-GET gebruikte `/Presenter/Prizes/Event/{b}` en slaagde volgens `GetStringAsync` met een 2xx-status. Het oude log bevat geen precieze status, evenement- of ronde-ID voor B, feitelijke alias of resultaatinhoud.

De fixture voltooit nu eerst de oefening via de bestaande HTTPS-handlers. Ze controleert voor sluiting exact één voltooide, lotingsgerechtigde deelnemer en een beschikbare prijs. Ze logt de URL, HTTP-status, ronde-ID, rondenummer, status, tellers en winnaarvelden van de specifieke ronde. Voor en na accountbeëindiging controleert ze dezelfde winnaar en prijs in het resultaat en in de HTML-winnaarslijst. De controle op intrekking van weergavetoegang en weigering van nieuwe toestemming na sluiting blijft actief. De overige assertions gebruiken geen lotingsuitkomst zonder voltooide oefening.

De geïsoleerde Release-build van Web.Tests en portal in `artifacts/round-winner-9468dd2215df45fd944919e9b9b37a55` slaagde met 0 fouten en 3 waarschuwingen. De aangepaste HTTPS-test wacht op uitvoering onder `standard Windows user`.

De daaropvolgende normale-accountlog `artifacts/cybertruck-release-86af0c3cac474f24902e24d7ddeb647b/session-demo.stdout.log` bevestigt ronde 1, `eligible=1` vóór sluiting, en één winnaar erna (`Stormhagedis`, `Cybertruck-demoprijs`). Resultaat-JSON en evenement-HTML gaven HTTP 200. De HTML-vergelijking zocht direct na een scheidingsteken het rondenummer, terwijl de Razor-pagina eerst het vertaalde rondelabel weergeeft. De vergelijking begint nu bij het rondenummer en controleert dezelfde alias en prijs in beide resultaatmomenten. De nieuwe Release-build in `artifacts/round-html-3341ea4c6ede42beb780f92a2e32e0f0` slaagde met 0 fouten en 3 waarschuwingen.

De gerichte herhaling onder `standard Windows user` gaf exitcode 0 (`artifacts/cybertruck-release-cb487b878f2e4b9e8678b35dd51c37dc`). Ronde 1 van `Synthetic session B` had na sluiting één winnaar (`Noorderbunzing`, `Cybertruck-demoprijs`). Dezelfde alias en prijs staan vóór en na accountbeëindiging in zowel de resultaat-JSON als de HTML-winnaarslijst. Beide GETs gaven telkens HTTP 200. Na accountbeëindiging kon de bezoeker de portal niet heropenen. De volledige releasebuilder is daarna gestart.

## Vervolg op de QR-autorisatie in de gerichte test

De eerdere selectie `--session-demo-http-checks` gaf bij de QR-controle de bootstrapbeheerder (`http-admin`, rol Administrator) door als outsider, terwijl de volledige regressie een afzonderlijke Presentator gebruikte. De bestaande QR-handler staat het lezen toe aan de beheerder voor een geregistreerde, open evenementscope. Beide selecties gebruiken nu dezelfde eigenaar en afzonderlijke outsider met rol Presentator. De reeds aangemelde derde presentator ontvangt een uitnodiging voor het synthetische QR-evenement. De test verwacht toegang voor de eigenaar, 404 voor de outsider en de nog onbevestigde uitnodiging, en toegang voor het lid na acceptatie.

De fixture controleert zowel `/Presenter/ParticipantQr?handler=State&id=<synthetisch-event-id>` als de handler `Image`. Ze logt de HTTP-status, het mediatype en een beperkte weergave van de Location-header met automatisch volgen van redirects uitgeschakeld. De eerdere normale-accountlog bevat voor de mislukte QR-assertion geen HTTP-status, Location of mediatype. De productieregels voor QR-autorisatie zijn ongemoeid.

## Vervolg op de rondesluiting in de sessiedemo

`event-edition-package-e7027786d00949f6a970577ec9603cb8/regression.log` bevestigt de eerdere browserfix en de anonieme sessiedemo-GET. De bronkopie bevat beide fixes. De GET geeft 200 zonder redirect, met het juiste evenement en de YubiKey-route. De weigeringstekst staat uitsluitend in de vertaalcatalogus. De nieuwe eerste fout is een 404 op het aanmaken van toestemming in `OpenView` na rondesluiting (`SessionDemoHttpChecks.cs:244,266` in de buildkopie).

De fixture opent de synthetische weergave nu vóór de rondesluiting, controleert dat ze actief is en controleert na sluiting dat zowel de bestaande weergave als een nieuw toestemmingsverzoek 404 geeft. De deelnemer kan nog steeds zijn account beëindigen en de historische alias blijft in de ronde-uitslag. De gerichte reproductie onder de runner stopte eerder bij de synthetische PFX-import. De volledige builder wacht op een geslaagde gerichte test.

Bij een mislukte dotnet-stap toont de releasebuilder nu de stap, exitcode, het volledige logpad en maximaal 24 geselecteerde fout- en stackregels. De bestaande build- en releasecontroles blijven actief. De foutselectie is gecontroleerd met synthetische restore-, regressie- en MSBuild-fouten.

## Vervolg op de sessiedemo-HTTP-regressie

De nieuwe normale-accountbuild `event-edition-package-bc7a87edd3b44fdbb400284de18a3aef` strandde bij de eerste anonieme evenement-GET in `SessionDemoHttpChecks.cs:46`. De fixture en de actuele bron waren gelijk. De bestaande log toont dat de evenement-POST een redirect gaf, maar bevat geen responsdetails van de GET. Die GET gebruikte `GetStringAsync` zonder automatische redirects en kwam tot de HTML-assertion, wat een succesvolle HTTP-status bevestigt. De eerder gelogde 429 hoorde bij presentatorbeheer.

De oude assertion zocht de Nederlandstalige weigeringstekst door de hele HTML. De layout levert die tekst ook standaard in de verborgen vertaalkenmerken van `<template id="localization-data">`. De fixture controleert nu de bezoekersinhoud binnen `<main>`, de status, evenementnaam en -scope, beschikbare YubiKey-route en registratie- en aanmeldknoppen na de routekeuze. Ze logt de synthetische publieke URL, status, aanwezigheid van een redirect en vaste HTML-kenmerken zonder cookiewaarden, formulierwaarden of redirects met gevoelige gegevens te dumpen. De testselectie is `--session-demo-http-checks`.

De aangepaste Web.Tests en portal zijn in `artifacts/sd-1a871cdac44c4bb8b5c72a71fc61a311` gebouwd met `-c Release --no-restore` (0 fouten, 3 waarschuwingen). De gerichte test stopte onder `restricted test account` vóór de GET bij Kestrels import van de synthetische PFX. Daardoor zijn de nieuwe responsdiagnose en testuitkomst onder `standard Windows user` nog nodig voordat de releasebuilder wordt hervat.

## Vervolg op de build onder standard Windows user

- `artifacts/event-edition-package-8246cc43a8d74fad8d060662ee1bd8aa/regression.log` bevestigt dat de HTTPS-testportal opstart en dat de browser de Windows Hello-kaart vindt, registratie voltooit en aanmelding bevestigt. De eerste fout is de timeout bij `DOM wait 3`, vóór `Page.reload`. De stack loopt via `clickfix-browser.mjs:223`, `ClickFixBrowserChecks.cs:43`, `HttpChecks.cs:273` en `Program.cs:249`; MSB3073 volgt door de exitcode van de testhost.
- De fixture wacht nu op de geopende oefening voordat zij op Kopiëren en Openen klikt. Die knoppen negeren klikken totdat de asynchrone start-POST succesvol is afgerond. De fixture wacht daarna afzonderlijk op het Run-venster en controleert nog steeds de toestand na herladen. Productiecode is ongemoeid.
- Gerichte reproductie met de laatst gebouwde test- en portalbinaries via `--clickfix-browser-checks` strandde op de runner bij het importeren van de synthetische PFX. Onder de gewone account kwam diezelfde eerdere build wel tot de browsertest. De release blijft afhankelijk van een geslaagde gerichte test en vervolgens de volledige builder.

## Cybertruck-checkpoint 23 november 2026

- **Geslaagd.** In deze checkout toont `artifacts/event-edition-package-2deb61ccad1a421aa3a4f7bccc36daab` een geslaagde locked restore en Release-solutionbuild (0 fouten, 3 waarschuwingen). De regressie kwam voorbij Phase65e en startte de synthetische HTTPS-portal en browser. De eerste fout was een timeout bij `DOM Index laptop action available`. De huidige Razor-kaart heeft `data-method="windows-hello"` en blijft bij de POST route `laptop` gebruiken. De browserfixture zoekt nu de submitknop van die kaart. JavaScript-syntax en diffcontrole slagen. De expliciete Node-set van de orchestrator gaf 122 geslaagd, 2 overgeslagen en 0 fouten.
- **Geblokkeerd.** De schone builderpoging op de huidige runner doorliep de packaging-, launcher-, proces- en netwerkplancontroles. Locked restore strandde op geweigerde leestoegang tot de gebruikersconfiguratie van NuGet. Zie `artifacts/event-edition-package-9130e8ca0a0e41fc88480060a49c6d04/restore.log`. Geen releasezip of checksum aangetroffen. Windows weigerde ook de reads voor actieve netwerkprofielen, listeners en firewallregels.
- **Gerichte browsertest geblokkeerd vóór de eerste UI-stap.** De laatst gebouwde test- en portalbinaries zijn voor diagnose gekopieerd naar de nieuwe geïsoleerde bronkopie, met de aangepaste browserfixture en expliciete `LUMMA_TEST_WEB_DLL`. Kestrel kon de nieuwe synthetische PFX onder de runneraccount niet importeren. Zie `clickfix-targeted.log` naast `restore.log`. De selectorcorrectie is nog niet dynamisch bevestigd.
- **Handmatig op de demoapparatuur.** Volledige UI-acceptatie, YubiKey, twee rondes, netwerkverbinding vanaf een tweede apparaat en herstart staan open. De matrix hieronder beschrijft bronverwachtingen.

### Release en proef voor morgen

1. Voer in gewone PowerShell onder het buildaccount met leestoegang tot zijn NuGet-configuratie de bestaande builder uit. Kies bij een volgende poging een nieuwe uitvoermap. Succes levert één hoofdzip, een apart SHA-256-bestand en de werkmap met logs en `unpacked` op.

   ```powershell
   Set-Location 'C:/src/LummaSimulator'
   .\packaging\Build-EventEdition.ps1 -OutputDirectory .\artifacts\cybertruck-final
   ```

2. Vergelijk op elke pc de uitvoer van `Get-FileHash -Algorithm SHA256 -LiteralPath <zip>` met de eerste kolom in het bijbehorende `.sha256`-bestand. Pak dezelfde gecontroleerde ZIP lokaal uit in een nieuwe map. Gebruik voor de proef synthetische accounts en een afzonderlijk ingericht runtimeprofiel. Laat de beheerder de bestaande origin/RP-ID en de bijbehorende TLS-inrichting verifiëren vóór start; ingebruikname met bestaande runtimegegevens vereist een expliciet goedgekeurde upgrade.
3. Laat de beheerder op de server en app-pc `.\Firewall.ps1 -Action Audit` uitvoeren vanuit de pakketmap, zo nodig in verhoogde PowerShell. Koppel de PIDs aan `Get-Process -Id <PID> | Select-Object Id,ProcessName,Path`. Controleer op de app-pc `netsh http show sslcert`, `netsh http show urlacl` en `netsh http show servicestate`. Verwacht de exacte app-IP:443-binding en actieve WPF-queue met prefix `/lumma-helper/` als de receiver is ingericht. Bevestig op de server het proces en adres van de HTTPS-front-end. Gebruik `Firewall.ps1 -Action Plan` met werkelijke rol, lokaal IP, profiel en bron-CIDR als een regel nodig blijkt. Voer de concrete wijziging pas na controle en toestemming uit. Test vanaf de helper-pc beide bestemmingen met `Test-NetConnection <DNS-naam> -Port <effectieve-poort>` en open de echte portal-origin vanaf een deelnemerstoestel. Verwacht `TcpTestSucceeded : True` en vertrouwd HTTPS onder de exacte portalnaam.
4. Start als standaardgebruiker op de server `Start-PasskeyPortal.cmd` en op de app-pc `Start-LummaSimulator.cmd`. Meld aan als presentator, open `/Presenter/Helper`, download de Windows-ZIP, pak die uit en start `Start-PresenterHelper.cmd`. Vul de ingerichte waarden lokaal in en keur in de browser goed. Kies het actieve evenement. Vergelijk de QR-bestemming op een tweede apparaat met de bezoekerslink van dat evenement, inclusief pad en query. Verwacht dat de helper kan selecteren en dat de app hetzelfde actieve evenement toont.
5. Registreer een fictieve deelnemer met de fysieke YubiKey, meld aan met dezelfde key, sluit de browser volledig, open opnieuw en herhaal de aanmelding. Rond de oefening via de portal af. Geef expliciet toestemming en vergelijk de getoonde alias met de juiste deelnemer in WPF. Test ook een deelnemer zonder toestemming. Doorloop beide volledige rondes met meerdere deelnemers, inclusief beschikbare Prizes-loting, bevestigde rondesluiting, resultaat, volgende ronde en de bestaande opschoning. Controleer dat de vorige ronde geen actieve deelnemersweergave meer geeft. Laat waar mogelijk een tweede bevoegde presentator dezelfde ronde in een apart browserprofiel openen. Verwacht een consistente uitkomst na verversen en geen dubbele verwerking bij een achterhaalde rondeactie.
6. Herstart na toestemming beide betrokken pc's en het presentatortoestel. Start dezelfde gecontroleerde uitpakmappen onder dezelfde gebruikersaccounts. Herhaal portalbereikbaarheid, helperverbinding, evenement/QR, YubiKey-aanmelding en deelnemerweergave. Controleer of de opgeslagen installatie-instellingen en pairing bruikbaar blijven en voldoe aan een eventuele normale aanmeld- of goedkeuringsvraag.

### Terugval bij portal- of helperuitval

1. Schakel tijdens de presentatie over op de bestaande WPF-simulatie. Start zo nodig alleen `Start-LummaSimulator.cmd`. Zet live-telemetrie uit wanneer de app stilstaat, controleer de simulatiemodus en gebruik **Scenario → Start scenario**. Presenteer de lokale simulatie en pauzeer QR, gekoppelde deelnemersacties en loting totdat de verbinding hersteld is.
2. Controleer de portal eerst via de bestaande origin vanaf de presentator-pc. Start zo nodig het eigen portalonderdeel via `Start-PasskeyPortal.cmd` onder hetzelfde ingerichte Windows-account. Behoud bestaande opslag en TLS-identiteit.
3. Controleer daarna of WPF de ingerichte receiver meldt. Open de helper opnieuw als standaardgebruiker en keur de portalverbinding opnieuw goed als daarom wordt gevraagd. Selecteer het actuele evenement en vergelijk de QR en rondestatus vóór hervatting.

## Laatste vervolg: regressiefout Phase65e (13 november 2026)

Dit checkpoint vervangt de actuele status hieronder; de eerdere checkpoints blijven historische informatie. De normale-accountpoging `artifacts/event-edition-package-797e1af3b624438ba032a1de0bedf677` heeft restore en Release-build wel doorlopen. De eerste concrete fout in zijn `regression.log` is een onbehandelde `LummaSimulator.Web.Models.DemoRequestException: PresenterReauthenticate`, niet een restore-, compilatie- of testprocesstartfout.

Stacktrace van die eerste fout (paden relatief aan de geïsoleerde `source`):

```text
PresenterService.Actor — LummaSimulator.Web/Services/PresenterAuthentication.cs:127
PresentationSettings.SaveAsync — LummaSimulator.Web/Services/PresentationSettings.cs:36
PresentationSettings.SaveAsync — LummaSimulator.Web/Services/PresentationSettings.cs:43
Phase65eChecks.RunAsync — tests/LummaSimulator.Web.Tests/Phase65eChecks.cs:34
TestProgram.Main — tests/LummaSimulator.Web.Tests/Program.cs:237
TestProgram.<Main>
```

`packaging/Build-EventEdition.ps1` schrijft het log met `dotnet test <source>/tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore`. De VSTest-target start vervolgens `dotnet <source>/tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll`. Dit laatste proces eindigt met `-532462766` (0xE0434352, onbehandelde CLR-exception); de orchestrator rapporteert MSB3073 op regel 25. Dit is de waarschijnlijke aanleiding voor de gemelde Windows-pop-up; een directe koppeling van het pop-upvenster aan een PID is niet waargenomen.

Oorzaak en fix: de eerste succesvolle configuratieopslag verbruikt het eenmalige herauthenticatiebewijs. De nieuwe netwerkplantest probeerde daarna nogmaals op te slaan zonder nieuw bewijs. Alleen `Phase65eChecks.cs` is aangepast: expliciet controleren dat hergebruik `PresenterReauthenticate` oplevert en revision/netwerkplan gelijk blijven, daarna opnieuw met de bestaande software-authenticator herauthenticeren en opslaan. Productiecode is ongemoeid.

### Werkelijke controles

- Exacte oorspronkelijke regressiestap opnieuw uitgevoerd vanaf de repositoryroot, met `C:/Program Files/dotnet/dotnet.exe`, SDK 10.0.404, dezelfde projectkopie, Release en `--no-restore`, en expliciete geïsoleerde `LUMMA_TEST_WEB_DLL`. De oorspronkelijke fout is gereproduceerd. De oorspronkelijke `regression.log` is niet gewijzigd.
- De testfix is ook in die bestaande geïsoleerde testkopie gezet; het oorspronkelijke testbestand is bewaard als `artifacts/regression-reauth-0ac629b041e54e109d2fb3eec841aa08/Phase65eChecks.before.cs`. Geen bestaande demodatabase gebruikt.
- Gerichte test: `dotnet <source>/tests/LummaSimulator.Web.Tests/bin/Release/net10.0/LummaSimulator.Web.Tests.dll --phase65e-checks`: **exit 0, 48 checks geslaagd**, inclusief weigering van hergebruik en succesvolle opslag met nieuw bewijs. Log: `artifacts/regression-reauth-0ac629b041e54e109d2fb3eec841aa08/phase65e-fixed.log`.
- Volledige regressie na fix: **exit 1**. Phase65e slaagt nu. Daarna kan de afzonderlijke HTTPS-testportal niet starten: `System.Security.Cryptography.CryptographicException: Het systeem kan het opgegeven bestand niet vinden`, via `X509CertificateLoader.ImportPfx` → `CertificateConfigLoader.LoadCertificate` → Kestrel → `Program.cs:341`; de webtestrunner meldt dit bij `HttpChecks.cs:86`, `TestProgram.Main:249`. Dit is een aparte certificaatimport/startblokkade onder `restricted test account`; werking onder de normale account is nog niet bevestigd. Geen certificaatbeveiliging of productiecode gewijzigd. Log: dezelfde map, `regression-fixed.log`.
- Daarna volledige builder opnieuw gestart zonder `VerifiedSourceDirectory`, dus met nieuwe lege staging: `artifacts/event-edition-package-b40ab34aa24642158dcdaa7f574ce8e6`. Packaging- en netwerkplanchecks doorlopen; locked restore stopt op `NuGet.targets(198,5)`, geweigerde gebruikersconfig `%USERPROFILE%/AppData/Roaming/NuGet/NuGet.Config`, ondanks expliciete repo-config. Log: die map, `restore.log`; algemene uitvoer in bovenstaande regressiemap, `builder-fixed.log`.
- Alle vier checkout-lockfiles zijn byte-identiek aan de oorspronkelijke 797-bronkopie. Locked restore, `--no-restore` en releasechecks zijn behouden. Geen profielrechten, actieve firewallregels of lokale installaties gewijzigd; niets gepubliceerd.

### Artefacten en resterende acceptatie

Beoogde uitvoer van de nieuwe poging, **niet aangemaakt**:

```text
C:/src/LummaSimulator\artifacts\regression-reauth-0ac629b041e54e109d2fb3eec841aa08\dist\LummaSimulator-EventEdition-v0.7.0-beta.1-win-x64.zip
C:/src/LummaSimulator\artifacts\regression-reauth-0ac629b041e54e109d2fb3eec841aa08\dist\LummaSimulator-EventEdition-v0.7.0-beta.1-win-x64.zip.sha256
```

Hashvergelijking en schone extractie zijn niet uitvoerbaar zonder ZIP. App-start, Portal-start, Presenter Helper-download en standaardgebruikerspairing met synthetische data vanuit diezelfde ZIP zijn daarom nog niet uitgevoerd. Er is geen oude build als vervanging gebruikt.

Volledige builder onder de gewone Windows-account, zonder elevatie (de gerichte Phase65e-test is hierboven al geslaagd):

```powershell
Set-Location 'C:/src/LummaSimulator'
& .\packaging\Build-EventEdition.ps1 -OutputDirectory '.\artifacts\regression-reauth-0ac629b041e54e109d2fb3eec841aa08\dist'
```

Dit maakt opnieuw een eigen lege staging en voert alle releasechecks uit. Gebruik geen `VerifiedSourceDirectory` om de geblokkeerde volledige verificatie over te slaan.

## Actueel vervolg: expliciete NuGet-configuratie en bouwacceptatie

13 november 2026, vervolg op onderstaand eerdere checkpoint. **Restore is nog niet hersteld; ZIP en checksum zijn niet aangemaakt.** De helper-, netwerk- en packagingwijzigingen zijn behouden. Er is niets naar GitHub gepubliceerd en geen actieve firewallregel gewijzigd.

### Vastgestelde restorecontext

- De builder logt nu zijn werkelijke Windows-account: `restricted test account`. De dotnet-childprocessen erven deze identiteit; de builder gebruikt geen RunAs of credentialoverdracht.
- USERPROFILE en APPDATA verwijzen naar het profiel van Windows user. Het geweigerde bestand is exact `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. De bevestigde fout is een toegangsfout door deze account-/profielcombinatie; het is geen bewezen feedauthenticatiefout.
- De bestaande, leesbare repo-`NuGet.Config` bevat `<clear />` en uitsluitend `https://api.nuget.org/v3/index.json`. De bestaande directe packages zijn System.Formats.Cbor 9.0.24, Fido2.AspNet 4.3.1 en Microsoft.Data.Sqlite 10.0.14; de SDK-framework/runtimepacks en transitieve dependencies gebruiken volgens de restoregraph dezelfde bron. Geen nieuwe feed nodig volgens de checkout. Deze openbare bron vraagt geen feedcredentials; er zijn geen NuGetPackageSourceCredentials-omgevingsentries ingesteld. Er zijn geen geheime waarden opgehaald, gekopieerd of gelogd en bestaande credentialproviders zijn niet aangepast.
- Build-EventEdition geeft nu expliciet `--configfile <gekopieerde bron>\NuGet.Config` mee, naast ongewijzigd `--locked-mode` en de bestaande runtimeparameters. Build, tests en alle vier publishes behouden `--no-restore`. De argumentregressie controleert dit exacte contract. Een geslaagde publish met hergebruikte assets kan pas na een daadwerkelijk geslaagde restore worden bevestigd.
- GenerateRestoreGraphFile slaagt voor **alle elf projecten**: configFilePaths bevat uitsluitend de expliciete bronconfig, sources uitsluitend nuget.org. Toch probeert de daaropvolgende RestoreTask (NuGet.targets:198) opnieuw de genoemde gebruikersconfig te lezen. De precieze interne NuGet-call die dit veroorzaakt is niet vastgesteld; de configuratie komt aantoonbaar wel in de restoregraph aan. De expliciete-config-aanpassing is dus correct doorgegeven, maar lost deze runnerblokkade niet op.
- Een herhaling met de tijdelijke diagnostische instelling NUGET_SHOW_STACK=true geeft dezelfde fout en geen aanvullende interne stack. De instelling is daarna hersteld. [Microsoft documenteert deze instelling als stackrapportage](https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-environment-variables); signature-, audit-, TLS- en restorecontroles zijn niet uitgeschakeld.
- Alle vier bestaande checkout-lockfiles én die in de eerste geïsoleerde restorekopie zijn na de pogingen byte-identiek aan de vooraf vastgelegde hashes. Geen lockfile of packageversie gewijzigd. Geen profielrechten, APPDATA/USERPROFILE, cache-identiteit of Windows-identiteit gewijzigd.

### Nieuwe bouwpoging en artefactstatus

De bestaande builder is daadwerkelijk uitgevoerd met nieuwe lege staging. Laatste poging:

`C:/src/LummaSimulator\artifacts\event-edition-package-b37be91f7d7142b48cbe42e911a442fb`

Daarin staan source/, de lege release-staging, build-context.json en restore.log. Algemene logs en configuratiebewijs:

`C:/src/LummaSimulator\artifacts\release-restore-1ef38178f7f0495aa2845236c0501685`

Onder meer builder-final.log, restore-graph.json, restore-config-summary.json, locks-before.json, packaging-checks.log en network-plan-checks.log. De laatste builderpoging stopt vóór build/publish op dezelfde leesblokkade.

Exacte beoogde ZIP- en checksum-paden voor deze poging, **beide niet bestaand**:

```text
C:/src/LummaSimulator\artifacts\release-restore-1ef38178f7f0495aa2845236c0501685\dist\LummaSimulator-EventEdition-v0.7.0-beta.1-win-x64.zip
C:/src/LummaSimulator\artifacts\release-restore-1ef38178f7f0495aa2845236c0501685\dist\LummaSimulator-EventEdition-v0.7.0-beta.1-win-x64.zip.sha256
```

De builder maakt de hoofdchecksum nu pas nadat de ZIP bestaat en de archive-/uitgepakte sidecarchecks zijn afgerond. Daarna leest hij het geschreven checksum-bestand terug en vergelijkt opnieuw met SHA-256 van de definitieve ZIP voordat hij succes meldt. Dat pad is hier niet bereikt: er is geen hashwaarde uitgegeven en geen oude build opnieuw verpakt.

### Acceptatie per onderdeel

| Test/controle | Resultaat van dit vervolg | Concrete ontbrekende stap/blokkade |
| --- | --- | --- |
| Packagingcontract, helpers en filters | 106 packagingchecks geslaagd, waaronder vier publishes, expliciete config, locked restore en no-restore-publicatie. 14 monitorstarterchecks en 45 synthetische proceschecks geslaagd | Dit controleert de bestaande scripts, geen nieuw gepubliceerde binaries |
| Netwerkplan | 9 synthetische planchecks geslaagd; geen regels gewijzigd | Geen bevestiging van effectieve live firewall |
| Locked restore | Exit 1, gebruikers-NuGet.Config Access denied, ondanks expliciete leesbare config | Runner moet toegang hebben onder de bedoelde normale accountcontext; geen verdere toegestane codefix vastgesteld |
| Release-build en .NET-regressie | Door de builder niet gestart na de restorefout | Geen actuele project.assets.json; een no-restore-herhaling levert geen aanvullende compileerinformatie |
| Self-contained App/Portal/Helper/Updater-publish | Niet uitgevoerd | Geen geslaagde restore/build/regressie |
| Eén zichtbaar helperstartpunt, dependencies en schoon pakket | Bestaand bron-/scriptcontract behouden en packagingchecks slagen | Geen ZIP om daadwerkelijke inhoud, dependencybytes en download te controleren |
| Definitieve ZIP en SHA-256-terugcontrole | Niet uitgevoerd | Geen publicatie-uitvoer om te archiveren; geen kunstmatig leeg of gedeeltelijk releasepakket gemaakt |
| Uitpakken in nieuwe map en App-start | Niet uitgevoerd | ZIP ontbreekt; geen bestaande installatie als vervanging gebruikt |
| Portal-start en helperdownload vanuit uitgepakte map | Niet uitgevoerd | ZIP/portalbinary ontbreken; geen actieve portal aangeroepen |
| Presenter Helper standaardgebruikerspairing, synthetische sessie | Niet uitgevoerd | Nieuwe helper/portal/app-uitvoer ontbreekt; bestaande pairing/DPAPI-/sessiegegevens blijven ongemoeid |
| Syntax en diff | PowerShell-parser/argumentchecks en git diff --check geslaagd | Geen claim van C#-/Razor-compilatie |

### Live hosts en TCP 443/7243

Op de huidige runnerhost `` zijn **afzonderlijk** Get-NetConnectionProfile, Get-NetTCPConnection -State Listen en de ActiveStore-firewallaudit geprobeerd: alle drie geven Access denied. Daardoor zijn voor 443 en 7243 geen luisterproces, luisteradres of effectieve regel/profiel vastgesteld. Geen alternatief provider-, elevatie- of identiteitspad gebruikt.

De feitelijke server en app-pc zijn niet als vaste host opgegeven; ze kunnen volgens de gebruiker per installatie wisselen. Er is dus geen geteste remote host en geen bewijs dat de runnerhost een van beide rollen vervult. TCP 7243 blijft uitsluitend als **ontwikkelpoort** geclassificeerd totdat de effectieve configuratie van een werkelijke host anders aantoont. De matrix verderop beschrijft code/ontwerp, geen waargenomen listeners.

| Host/rol | Wat ontbreekt | Eventuele beheeractie na read-only controle |
| --- | --- | --- |
| , rol niet vastgesteld | Profiel, listeners/processen en ActiveStore-regels door access denial | Audit onder een daartoe bevoegde gewone Windows-/beheercontext uitvoeren. Nog geen concrete firewallwijziging aantoonbaar nodig |
| Portalserver, installatieafhankelijk | Exacte host, TLS-front-end/proxy, bindingsadressen, effectieve inbound/outboundregels | Alleen indien nodig: proces-/IP-/CIDR-beperkte HTTPS-regel voor de werkelijk ingerichte portal; 7243 niet generiek openzetten |
| App-pc, installatieafhankelijk | HTTP.sys-prefix, WPF-eigenaar, certificaatbinding, URLACL en effectieve regels | Alleen indien receiver ingericht en nodig: intern begrensde System/HTTP.sys-443-regel plus bestaande URLACL/TLS-provisioning; geen internetforwarding |
| Presentator-pc | Bereikbaarheid en eventueel uitgaande block-policy | Alleen indien nodig: proces- en bestemminggebonden outboundtoegang naar portal en app-pc; geen helper-inboundregel |

### Uitvoeren buiten de geblokkeerde runner

In gewone Windows PowerShell, onder het bedoelde Windows-account met normale toegang tot zijn eigen NuGet-configuratie:

```powershell
Set-Location 'C:/src/LummaSimulator'
& .\packaging\Build-EventEdition.ps1 -OutputDirectory '.\artifacts\release-restore-1ef38178f7f0495aa2845236c0501685\dist'
```

Dit gebruikt dezelfde expliciete repo-config, locked restore, tests en no-restore-publishes. Gebruik geen VerifiedSourceDirectory om een niet-geslaagde verificatie over te slaan. Bij succes blijven de hierboven genoemde app-/portal-/download-/pairingchecks vanuit de nieuwe uitpakmap afzonderlijk uit te voeren en te registreren. De concrete resterende blokkade hier is de Windows-runnercontext, niet een toestemming om profielrechten te verruimen of controles te omzeilen.

## Eerder checkpoint (historisch)

13 november 2026. **Bouw geblokkeerd; geen vrijgegeven releasezip.** De actuele werkboom is bevestigd als `C:/src/LummaSimulator`; lokaal werk en bronmappen zijn behouden. Geen actieve database, keyring, browser, installatie, firewallregel of certificaatbinding is gewijzigd. Geen GitHub-release gepubliceerd.

## Onderdelen op basis van de actuele bron

| Brononderdeel | Rol en pc | Wie start / koppeling zichtbaar |
| --- | --- | --- |
| LummaSimulator.PresenterHelper | Eén WPF-bediening op de presentator-pc; spreekt de serverportal en de receiver op de app-pc aan | Gewone gebruiker via Start-PresenterHelper.cmd. Portalbrowserlogin plus expliciete goedkeuring. Daarna evenementkeuze, ingeschakelde selecteerknop, status en actuele QR. Een geopend venster bewijst geen koppeling. |
| LummaSimulator.Helper.Core | Interne library voor protocol 1, receiver/client, per-user DPAPI-instellingen, QR-lease en verificatie van ondertekende updates | Geen zelfstandig programma of service. Wordt gebruikt door WPF, Presenter Helper en updater. |
| LummaSimulator.HelperUpdater | Interne exe voor gecontroleerde update, recovery/healthcontrole en release-signingtooling | Door helper bij een gevalideerde update; niet door de presentator als tweede helper starten. Update-uitgever, ondertekend pakket en beheerde gebruikersinstallatie vereist. |
| LummaSimulator.App/HelperReceiver.cs | Optionele ontvanger binnen de WPF-app op de app-pc | WPF start deze uitsluitend bij expliciete HelperReceiver.settings.json-inrichting. WPF toont beschikbaar/niet beschikbaar; die tekst bewijst nog geen gepairde client. |
| tests/LummaSimulator.Helper.Tests en verification/PresenterHelperUiChecks.cs | Actieve synthetische ontwikkelverificatie | Testproces; niet meeleveren als runtime. |

Er zijn drie helperbronprojecten en één helpertestproject, geen twee uitwisselbare gebruikershelpers. De solution bevat inmiddels tien directe projecten plus de transitieve LocalTestServer; het oudere instructieaantal van zeven beschrijft de eerdere situatie. Geen projecten verplaatst of verwijderd.

De WPF-app bevat geen afzonderlijke helperdownloadknop: zij is de ontvanger en toont receiverstatus. De portalpagina `/Presenter/Helper` gebruikt de geconfigureerde `Helper.DownloadUrl`; het label heet nu **Presenter Helper voor Windows downloaden (ZIP)**. De pakketlauncher maakt die URL afhankelijk van zijn eigen ingerichte portal-origin, zodat een serverinstallatie niet naar de standaard localhost-download verwijst.

## Verpakking

De bestaande Build-EventEdition.ps1 blijft de release-ingang. Aanpassingen:

- Nieuwe lege staging; geen hergebruik of destructieve opschoning.
- Vier gerichte publishes: Web, App met LocalTestServer, Presenter Helper en HelperUpdater. HelperUpdater-uitvoer wordt intern met PresenterHelper samengevoegd; gedeelde bestanden moeten byte-identiek zijn. Conflicten stoppen de build. De Core-library en beide exe's zijn verplicht.
- Eén gebruikersstartpunt Start-PresenterHelper.cmd. Eén portable helperdownload, uit dezelfde runtime opgebouwd, onder Portal/wwwroot/downloads. Die ZIP bevat dezelfde starter en interne dependencies, plus README en beschikbare licentie. De hoofdzip behoudt ook de lokale PresenterHelper-runtime.
- Broncode, tests, buildmappen, logs, runtime-databases/sidecars, lokale helperinstellingen, DPAPI-bestanden, sleutels, certificaatmateriaal en oude archieven worden geweigerd. PDB's en oorspronkelijke appsettings worden niet gekopieerd; bekende niet-geheime defaults worden gegenereerd. Alleen de bewust gegenereerde helperdownload mag als geneste ZIP in de eindstaging staan.
- Lange installatie-/branding-/monitorhandleidingen zijn uit de beoogde ZIP-selectie gehaald; de korte README, release notes, versie, bestandschecksums en noodzakelijke licenties blijven. De bronhandleidingen blijven intact.
- Na ZIP/checksum worden de bestanden in een nieuwe map uitgepakt en tegen de manifesthashes gecontroleerd; de bestaande sidecaracceptatie wordt ook vanuit die uitgepakte map aangeroepen. Dit pad is door de restoreblokkade nog niet bereikt.
- Helper en updater declareren versie 0.1.0. Hun FileVersion is nu expliciet 0.1.0.0: de algemene app-FileVersion 0.7.0.0 mag de updatervergelijking niet onbedoeld sturen. Binarymetadata nog niet gemeten wegens bouwblokkade.

De portable eerste installatie omzeilt geen ondertekening. Zij kan niet automatisch als beheerde gebruikersinstallatie worden bijgewerkt. De onjuiste verwijzing naar een niet meegeleverd Install-Helper.ps1 is uit de statusmelding gehaald. Het publieke portable pakket is niet hetzelfde als het interne, ondertekende updatepakket op `/lumma-helper/updates/v1/…`. Er zijn geen signingkeys, trustpins of ondertekende updatebestanden gefabriceerd.

## Wisselende hosts bij installatie en in de webconfiguratie

Op verzoek zijn hostnamen/IP's geen vaste demo-keuze. De nieuwe Setup-parameters AppOrigin, PortalListenAddress, AppListenAddress, DemoSubnet, HelperSubnet en NetworkProfile vullen een volledig gecontroleerd **netwerkplan** in event-edition.local.json. PortalOrigin blijft de expliciete HTTPS-origin van die installatie. Component Portal/Both vereist het eigen portalcertificaat; Component App vraagt geen serverprivésleutel en kan geen portal starten. De bestaande standaard is Both voor de lokale demokit. De launcher ondersteunt actieve publieke HTTPS 443; een bestaande 7243-/reverse-proxyserver blijft aparte beheerinrichting.

De server krijgt de niet-geheime planvelden als DemoNetwork-configuratie. Configuratie → Demonetwerk laat een opnieuw geauthenticeerde administrator de hosts, luister-IP's, CIDRs en Private/Domain-keuze aanpassen. Het bestaande JSON-profiel bewaart deze velden met revisioncontrole en audit; gewone profielwijzigingen behouden het plan. Historische tabellen/migraties zijn niet gewijzigd. Nieuwe C#-tests controleren validatie, adminrechten en JSON-behoud, maar zijn niet uitgevoerd.

**Opslaan is geen actieve netwerkdeployment:** het verandert geen listener, DNS, URLACL, certificaat, firewall of RP-ID. De beheerder moet het plan op de betrokken hosts controleren en expliciet toepassen; een RP-ID/originwijziging kan bestaande passkeys raken en vraagt de bestaande migratieprocedure. Helperprofielen op client-pc's blijven expliciet beheerd. Er is geen remote shell of automatische herconfiguratie toegevoegd.

## Poortmatrix: bronverwachting, niet live bevestigd

| Host | Listener / adres volgens code of installatie | Client / richting | Benodigde scope |
| --- | --- | --- | --- |
| Server of lokale portal-pc | Kestrel LummaSimulator.Web.exe, HTTPS 443 bij pakketstarter; host komt uit de installatie-origin | Bezoekersbrowser, Presenter Helper en optionele WPF-portalclient openen uitgaand HTTPS | Inbound alleen werkelijk luister-IP, portalproces en benodigde demo-CIDRs, Private/Domain. Localhost behoeft geen LAN-regel. |
| Ontwikkelportal | LummaSimulator.Web, launchSettings: https://localhost:7243 | Lokale browser/WPF; DesktopSettings heeft deze ontwikkeldefault | Loopback, geen inbound LAN-regel. Runtime-overrides kunnen anders zijn; eerst effectief controleren. |
| Server met expliciet netwerk-7243 | Kestrel op ingericht serveradres:7243, indien werkelijk zo geconfigureerd | Alleen ingestelde interne clients of reverse proxy | Alleen indien niet-loopback: eigen portalproces, lokale IP's en expliciete bronhosts. Een reverse proxy op 443 moet afzonderlijk op zijn werkelijke proces worden beoordeeld. |
| App-pc | HTTP.sys/System (PID 4), HTTPS 443, prefix /lumma-helper/; WPF registreert de queue | Presenter Helper opent uitgaand naar app-origin:443 | Inbound System, exact app-IP en alleen interne helperhosts/CIDRs. Certificaatbinding, URLACL voor de WPF-gebruiker, certificaatpin en pairing blijven nodig. Geen internet-NAT/portforwarding. |
| Presentator-pc | Presenter Helper opent zelf geen listener | Uitgaand naar serverportal en app-pc | Geen inbound helperregel. Bij een uitgaande block-policy zijn proces- en bestemminggebonden outboundregels op de werkelijke poorten apart nodig. |
| App-pc | WPF-portalclient opent geen extra listener op 7243 | Uitgaand naar de geconfigureerde serverportal | Bij outbound blokkering een gerichte WPF-exe → server-IP:443/7243-regel; geen brede uitzondering. |

De pakketportal/Kestrel en app-receiver/HTTP.sys kunnen niet zonder aparte bindingsinrichting hetzelfde lokale IP:443 delen. Gebruik de afzonderlijke server/app-pc-rollen of expliciet ingerichte afzonderlijke bindingsadressen. Dit is geen bewezen configuratie van de huidige machines.

Firewall.ps1 ondersteunt afzonderlijke HostRole Portal en AppPc (en expliciet Combined), alleen concrete lokale IP's, expliciete CIDRs en Private/Domain. Plan voert geen wijziging uit. Install vereist administratorrechten, lokale adressen, overeenkomend effectief profiel, aantoonbare listener/proceseigendom en bij receiver de actieve HTTP.sys-prefix. Regels krijgen proces- en adresfilters en EdgeTraversal Block. Alleen eigen gemarkeerde regels kunnen worden gewijzigd/verwijderd; bestaande brede of blokkerende regels worden niet automatisch opgeruimd. De presentator voert deze beheerstap niet uit.

## Werkelijke controles en blokkades

- Windows-netwerkaudit: Get-NetConnectionProfile geeft **Access denied**. De daaropvolgende listener-/firewallreads zijn daardoor niet bereikt. Geen alternatief provider-/identiteits- of elevatiepad gebruikt. Werkelijke hostnamen, luisterprocessen, bindingsadressen en effectieve firewallregels/profielen zijn dus niet vastgesteld.
- De server en app-pc zijn bewust niet geraden uit brondefaults. De gebruiker geeft aan dat hosts per installatie wisselen. Voer op beide echte hosts de Audit-actie uit, controleer TCP 443/7243 met procespad, bepaal eventuele proxy, controleer HTTP.sys-URLACL/TLS-binding en test TCP/HTTPS vanaf de echte client. Bij outbound block ook beide clientprocessen controleren. Pas daarna Plan/Install uitvoeren met de werkelijke scope.
- Packaging/launchercontroles: **106 geslaagd**; monitorstartercontroles **14 geslaagd**; echte synthetische procescompatibiliteitschecks **45 geslaagd**. Dit is geen startacceptatie van de nieuw gepubliceerde app.
- Netwerkplan: **9 geslaagde synthetische Plan-controles**, waaronder aparte rollen, poorten/procesfilters en weigering van Any/loopback. Geen firewallregel gemuteerd.
- De daadwerkelijke DemoNetworkPlan-klasse is onafhankelijk in PowerShell gecompileerd en met een geldig en ongeldig synthetisch geval uitgevoerd. Dit is geen solution-/Razor-build of JSON-/autorisatie-integratietest.
- Volledige expliciete Node-set: **124 tests, 122 geslaagd, 2 bestaande PE-iconchecks overgeslagen, 0 fouten**. PowerShell-syntax, resource-XML en diff-check slagen.
- De bestaande releasebuilder bereikt locked restore en faalt op ontbrekend leesrecht voor %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. Geen restore-, ACL- of identiteitsomweg gebruikt. Helper- en volledige .NET-regressiepogingen zonder restore falen vóór testuitvoering op ontbrekende project.assets.json (NETSDK1004).

Werk- en controlelogs: `artifacts/first-release-2fc6c2c2a1074cd58efdb715f7110cb6`. De laatste buildpoging en precieze bronkopie staan in release-status.json en de gerelateerde restore.log onder de genoemde event-edition-package-map. Historische poginglogs blijven behouden.

**Niet uitgevoerd:** publish, daadwerkelijke ZIP/hashgeneratie, uitpakken/starten van nieuwe App/Portal-binaries, HTTP-downloadacceptatie, standaardgebruikerspairing met nieuwe synthetische sessie, daadwerkelijke signing-/updateacceptatie, fysieke TLS-/authenticator-/mobielchecks. Er is geen oude build als nieuwe release verpakt.

## Artefactstatus en vervolg

Verwacht ZIP-pad (nog niet bestaand):
`C:/src/LummaSimulator\artifacts\first-release-2fc6c2c2a1074cd58efdb715f7110cb6\dist\LummaSimulator-EventEdition-v0.7.0-beta.1-win-x64.zip`

Verwacht checksum-pad (nog niet bestaand): hetzelfde pad met `.sha256` erachter. Er is geen SHA-256-waarde zonder werkelijk artefact uitgegeven.

Voer in gewone Windows PowerShell met normale NuGet.Config-leestoegang het bestaande proces uit:

```powershell
Set-Location 'C:/src/LummaSimulator'
& .\packaging\Build-EventEdition.ps1 -OutputDirectory '.\artifacts\first-release-2fc6c2c2a1074cd58efdb715f7110cb6\dist'
```

Gebruik geen VerifiedSourceDirectory om ontbrekende verificatie te overslaan. De builder stopt bij elke mislukte controle en drukt alleen na de uitgevoerde archive-/sidecarchecks het ZIP-/checksum-pad af. Volledige app-/portal-/pairingacceptatie vanaf de uitgepakte map blijft nodig vóór verspreiding. Live netwerkbeheer blijft apart en vereist de concrete plancontrole; niets wordt naar GitHub gepubliceerd.

Windows-regelparameters: [Microsoft New-NetFirewallRule](https://learn.microsoft.com/en-us/powershell/module/netsecurity/new-netfirewallrule). HTTP.sys-inrichting: [Microsoft HTTP.sys](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/httpsys).

# Event Edition — sprint 1: stabilisatie

Datum: 1 november 2026. Geteste bron: `1c97072`, branch `feature/phase5-domain-foundation`. Bij aanvang was de werkboom schoon. Dit verslag bevat nieuwe verificatieresultaten, geen wijziging van de bevroren v0.5-doelarchitectuur.

## Uitkomst

De volledige bestaande regressiesuite is geslaagd. De gerichte broncontrole en uitgevoerde tests hebben geen reproduceerbare applicatiefout opgeleverd waarvoor een reparatie nodig was. Daarom zijn geen applicatiecode, tests, packages, configuratie of migraties gewijzigd. Alleen dit verslag wordt lokaal gecommit; niets wordt gepusht.

Dit geeft een technische regressiebaseline, geen volledige operationele evenementvrijgave. Fysieke authenticators, de demonstratienetwerken, productie-TLS en WithSecure-detectie/respons moeten nog op de beoogde apparaten worden geaccepteerd. Historische securityrapporten blijven van toepassing; deze sprint sluit de daarin beschreven operationele bewijs- en hardeninggaten niet automatisch.

## Uitvoering en isolatie

- AGENTS.md, requirements, README, relevante bestaande architectuur-, security- en Prize-documentatie en de implementatie zijn geraadpleegd. Toekomstige requirements zijn niet als geïmplementeerd beschouwd.
- Een nieuwe bronkopie is gemaakt van de 167 niet-genegeerde Git-kandidaten. Geen bestaande database, keyring, runtime-installatie of historisch archief is gekopieerd of geopend.
- Restore, build en regressies zijn uitsluitend in die kopie uitgevoerd. De tests kregen een expliciete `LUMMA_TEST_WEB_DLL` naar de nieuw gebouwde testportal en eigen tijdelijke/appdata-mappen. De orchestrator gebruikt daarnaast eigen testdatamappen binnen de kopie. Alle database- en HTTPS-tests gebruiken nieuw aangemaakte synthetische gegevens en loopback-origins.
- Alleen door tests gemaakte portal-/sidecarprocessen zijn gestart en afgesloten. De actieve portal is niet aangeroepen, gestart of gestopt. Geen productiegegevens, passkeys, deploymentinstellingen of certificaatvertrouwen zijn gewijzigd.
- Na afloop zijn de hashes van alle 167 gekopieerde bronbestanden met de oorspronkelijke bestanden vergeleken: geen verschillen. `main` en `origin/main` bleven op `a1d30fd584acfa5091cf4fe58a83611f66346086`.

Lokale, genegeerde bewijslocatie: `artifacts/event-edition-sprint1-b5fa8083ee074e9eac55f41de1d4af10/`. Daar staan `source-files.txt`, `restore.log`, `build.log`, `baseline-regression.log` en `work/`. Ruwe testlogs zijn geen publicatieartefact: LIVE-procesverificatie kan de lokale procesidentiteit tonen. De logs en testgegevens worden niet gecommit.

## Werkelijke testresultaten

Uitgevoerd vanuit de geïsoleerde `work/`-map:

```powershell
dotnet restore LummaSimulator.slnx --locked-mode
dotnet build LummaSimulator.slnx --no-restore
dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj --no-restore
```

| Controle | Resultaat |
|---|---|
| Locked restore | Geslaagd |
| Solutionbuild, zes directe projecten plus transitieve LocalTestServer | Geslaagd; 0 waarschuwingen, 0 fouten |
| Web services/configuratie/HTTPS-integratie | 566 checks geslaagd |
| Node passkeys/localization/presenter | 43 tests geslaagd; 0 mislukt, overgeslagen of geannuleerd |
| WPF smoke | Volgorde zes fases, timing, JSON, reset, stoppen en foutherstel geslaagd |
| LocalTestServer | 24 lifecyclechecks geslaagd |
| LIVE-telemetrie | Proces/PID, eigen dummybestanden, correlatie, echte loopback-HTTP, redirectweigering, annulering en begrensde cleanup geslaagd |
| MITRE ATT&CK/Navigator | 2778 assertions geslaagd tegen Enterprise 20.0 en vastgezet Navigator 4.5-formaat |
| Session Exposure Monitor | Bestaande status-/simulatiegrenzen en metadata-clientchecks geslaagd |
| Windows WPF-verificatie | Start/stop/reset, volledige simulatie, export, monitorweergave en sidecar-shutdown geslaagd |
| Gehele orchestrator | Exitcode 0; alle geconfigureerde suites geslaagd |

Geen publish-/sidecarintegratiewijziging gedaan; geen nieuwe publishacceptatieronde uitgevoerd. Na de tests is uitsluitend dit document toegevoegd, zodat een herhaling van runtime-tests geen gewijzigde runtime zou testen.

## Passkey Portal

Relevante implementatie: `LummaSimulator.Web/Services/DemoService.cs` (`OptionsAsync`, `VerifyAsync`, `CancelAsync`, `CreateAsync`, `EndOwnAsync`, `EndAsPresenterAsync`), `Program.cs`, `wwwroot/js/passkeys.js` en de presenter-services. Bewijs: `tests/LummaSimulator.Web.Tests/Program.cs`, `HttpChecks.cs`, presenterchecks en `tests/WebAuthn.Ux.Tests/passkeys.test.cjs`.

- De bestaande FIDO2-library verifieert registratie en ondertekende assertions. Verkeerde origin, RP, challenge, UV en hergebruik worden geweigerd. Verificatie consumeert de passende challenge; browser-/serveroperaties zijn gebonden aan de sessie en operation-ID.
- Registreren, aanmelden en opnieuw aanmelden zijn in de suite succesvol. Annuleren, timeout, dubbele acties, concurrerende tabbladen, netwerk-/serverfouten en opnieuw proberen zijn afgedekt. Alleen een bevestigde serverrespons verhoogt de zichtbare authenticatieteller.
- De UI onderscheidt fouten bij opties ophalen, browserceremonie, serververificatie en annulering. Een providerfout wordt niet opgelost door beveiligingscontroles of browserinstellingen uit te schakelen.
- Synthetische bezoekers- en presentatorsessies blijven gescheiden. Tests dekken bestaande credentials, herhaalde login, sessie-intrekking en migratiecompatibiliteit in nieuwe testdatabases. Dit is geen logincontrole op echte bestaande accounts.
- Nieuwe-bezoeker/reset vereist de bestaande sessie-/antiforgerycontroles, roteert de cookie en maakt de oude cookie onbruikbaar. De actie is geblokkeerd tijdens een lopende browserceremonie. Beëindigen en idle-cleanup kunnen volgens bestaand beleid het tijdelijke account en bijbehorende passkeys verwijderen; reset is geen manier om accounts te bewaren. Dit beleid is niet veranderd en niet op productiegegevens uitgevoerd.
- Er is geen fysieke YubiKey- of LastPass-fout gereproduceerd. Software-authenticator- en JS-tests bewijzen geen werking van een specifiek fysiek apparaat of provider. De bestaande authenticatorselectie is behouden; `attestation: none` bewijst geen hardwaretype.

## WPF en Session Exposure Monitor

Relevante bronnen: `LummaSimulator.App/MainViewModel.cs`, `LummaSimulator.App/Services.cs` (`SimulationService`), `LummaSimulator.App/NavigatorExportService.cs`, `LummaSimulator.App/SessionMetadataClient.cs`, `LummaSimulator.App/SessionMonitorViewModel.cs` en `LummaSimulator.Web/Services/SessionStatusService.cs`. Bewijs: `tests/LummaSimulator.SmokeTests/` en `verification/Program.cs`.

- SAFE doorloopt alle zes fases. Stop, reset en opnieuw starten herstellen de commandostatus; reset behoudt logs. Verwerkings-/opslagfouten worden getest zonder een geslaagde logopslag te verzinnen.
- LIVE gebruikt bestaande lokale, begrensde telemetrie en synthetische bestanden. Sidecar-readiness, poortconflict, eigenaarschap, uitval tijdens een run en procesopruiming zijn getest. Geen remote agent of nieuwe internetafhankelijkheid toegevoegd.
- Navigator-export en ATT&CK-status blijven gescheiden van bewijs van een productdetectie. Handmatige presentatorbevestiging is geen automatisch gemeten WithSecure-resultaat.
- Monitorcommunicatie blijft optioneel, met beperkte metadata en afzonderlijke autorisatie; geen bezoekerscookies of echte sessietokens worden gedeeld. De bestaande loopbackbeperking is behouden. Openen van de monitortab voert geen automatische exposure uit.
- WPF en de portal delen geen database. Standalone WPF is door de smoke-/WPF-suite getest; er is geen volledige offline netwerkcapture uitgevoerd. De geïsoleerde MDR-VM wordt door deze lokale telemetrie niet automatisch aangestuurd.

## HTTPS, RP ID en netwerk

Bronnen binnen `LummaSimulator.Web`: `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`, `Configuration/WebAuthnSettings.cs` en `Program.cs`; daarnaast `LummaSimulator.App/SessionMetadataClient.cs`.

- De bronconfiguratie gebruikt de bestaande productiehost als RP ID en dezelfde HTTPS-host als toegestane origin. Development gebruikt afzonderlijk localhost met poort 7243. Validatie weigert onveilige/afwijkende origins; HTTPS-, CSRF-, Origin- en FIDO2-controles zijn behouden.
- De bestaande hostnaam resolveert op deze Windows-machine naar `127.0.0.1` (read-only `Resolve-DnsName`, 1 november). Dit is een lokale resolverwaarneming, geen verklaring over publieke DNS of andere apparaten.
- Loopback op deze machine bewijst geen toegang vanaf een telefoon of ander LAN-apparaat. De localhost-launchconfiguratie is geen gedeeld netwerkprofiel. Operationele listenerbinding, DNS op slachtofferapparaten, firewall, certificaatketen/SAN/vervaldatum en proxyconfiguratie zijn niet live onderzocht of gewijzigd.
- De monitorclient laat alleen de bestaande HTTPS-doelen toe en begrenst de verbinding tot loopback; dit is geen algemene remote-monitorverbinding. Geen uitzondering toegevoegd voor externe adressen, HTTP, ongeldige certificaten of andere RP ID's.

## Resterende acceptatie en problemen

Er zijn geen nieuwe reproduceerbare softwaredefecten vastgesteld en dus geen reparaties geclaimd. De volgende operationele punten staan nog open:

| Acceptatie op de demonstratieopstelling | Vereist resultaat vóór vrijgave |
|---|---|
| Fysieke YubiKey, primaire browser en reservebrowser | Met een apart synthetisch account registreren; minstens driemaal aanmelden; annuleren en opnieuw proberen zonder refresh of reset van andere bezoekers |
| LastPass-passkey, indien deel van de demonstratie | Dezelfde flow op de werkelijk gebruikte combinatie; providerbeschikbaarheid en duidelijke foutmelding vastleggen |
| Presentator en administrator | Bestaande geautoriseerde operator voert zelf login/herlogin uit; geen wijziging aan bestaande sleutels; bezoekersrechten geven geen beheertoegang |
| HTTPS en slachtofferapparaat | Correcte hostnaam, vertrouwde geldige TLS-keten, bereikbaarheid vanaf werkelijk gebruikte wifi/mobiel en geslaagde WebAuthn-flow zonder beveiligingsuitzondering |
| Bezoekersreset | Alleen eigen afzonderlijk testaccount resetten; oude sessie ongeldig, andere bezoekers onaangetast; verlies van tijdelijke passkeys vooraf begrijpelijk |
| WPF SAFE/LIVE en export | Op presentatorapparaat zes fases, stop/herstart/reset, lokale sidecar en Navigator-import controleren; logs behouden; WPF bruikbaar bij uitval van portal/MDR-VM |
| Session Exposure Monitor | Met een apart testaccount bestaande metadata-autorisatie en handmatige simulatie controleren; geen echte tokenblootstelling of detectie suggereren |
| SOC/WithSecure | Afzonderlijk goedgekeurde veilige test op de MDR-VM, echte consolewaarneming en eventuele isolatie testen; presentatoromgeving blijft functioneren |

De bestaande Prize Experience blijft op de huidige implementatiegrens; geen publieke deelname/trekking/claims geactiveerd. De uitgebreide lifecycle-/capability-architectuur en de nog niet gebouwde ClickFix-flow zijn geen sprint-1-regressiedefecten.

## Overdracht naar sprint 2

Sprint 2 kan technisch beginnen op deze geslaagde baseline, na opdracht voor die implementatie. De operationele demonstratie is pas vrijgegeven na bovenstaande fysieke acceptatie; bij een mislukte YubiKey-/TLS-/netwerkproef eerst het concrete probleem reproduceren en herstellen. Geen authenticatieherontwerp of eventmigratie nodig op grond van de huidige resultaten.

Gewijzigd bestand: uitsluitend dit verslag. De kandidatencontrole vond dezelfde tien bestaande treffers: zeven eerder toegestane publieke hostconfiguratiewaarden, twee test-/patroontreffers en één fictieve testwaarde. Geen nieuwe treffer, secret of persoonsgegevens in dit verslag; geen beschermde runtimebestanden onder de kandidaten. Dit is een patrooncontrole, geen volledige securityaudit. Rollback: alleen het document terugnemen; geen runtime- of dataherstel nodig. Lokale commit na inhouds-, staging- en kandidatencontrole; geen push, merge, deployment of wijziging op `main`.

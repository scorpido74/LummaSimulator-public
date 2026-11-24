# Event Edition — sprint 3

Baseline `449233c`, branch `feature/phase5-domain-foundation`, aanvankelijk schone werkboom. Datum 1 november 2026. Geen push, deployment of wijziging van `main`.

## Gebruik

1. Meld de operator aan via bestaande WebAuthn op `/Presenter`; open **Event Edition**. Plan naam en UTC-start/einde. De ingelogde operator wordt toegewezen; toewijzing geeft geen extra rechten. Bestaande bevoegde presentatoren delen het beheer. Start het evenement expliciet.
2. Laat de bezoeker registreren en aanmelden; noteer diens bestaande portal-DemoRunId. Kies in WPF **Event Edition**, schakel begeleide evenementrun in en kies **Nieuwe evenementrun voorbereiden**.
3. Koppel in het presentatorportaal de twee runreferenties aan het actieve evenement. Plaats de eenmalig getoonde tijdelijke leescode in het WPF-wachtwoordveld. Controleer het portaal; WPF toont evenement, planning en portalrun. Laat de bezoeker ClickFix opnieuw openen voor de gekoppelde oefening.
4. Start de WPF-run. De optionele evenementmodus wacht **vóór fase 1**, zonder uitvoering van de zes fases. De eerste bezoekerkeuze wordt gemeld. De presentator bevestigt het aparte vak en de ontvangen actie. Bij ontbrekende verbinding bestaat een afzonderlijke expliciete handmatige fallback, zonder claim van ontvangen slachtofferactie. Daarna verlopen de zes bestaande fases normaal; geen extra bevestiging tussen iedere bestaande fase toegevoegd.
5. Stop/Reset blijven beschikbaar; reset wist run en code. Sluit het evenement in het portaal expliciet af; historie blijft behouden. Voor een volgende demonstratie: nieuwe bezoeker en nieuwe WPF-run. Bestaande runs worden niet omgekoppeld of heropend.

De modus staat standaard uit. Standalone WPF, SAFE/LIVE, cleanup, export, logging en monitor blijven beschikbaar. WithSecure-testtoestemming, uitvoering op de aparte VM, detectieverificatie en respons blijven uitsluitend handmatig. Geen nieuwe code markeert een MDR-assessment als gedetecteerd.

## Communicatie en beveiliging

- De nieuwe presenterpagina gebruikt bestaande PresenterOnly-authenticatie, actuele `PresenterService.Actor`-validatie, antiforgery, exacte Origin en expliciete bevestiging. Geen nieuwe loginarchitectuur. Pairing vereist actief evenement, bestaand niet-verlopen bezoekersaccount en unieke canonieke portal-/WPF-runreferenties.
- Een afzonderlijke willekeurige leescode is rungebonden en wordt alleen gehasht opgeslagen. De koppeling verwijst naar de huidige operatorsessiehash. Geldigheid: maximaal 30 minuten, begrensd door operatorsessie, accountintrekking en bezoekersvervanging/verloop. Geen monitor-key of bezoekerscookie als WPF-autorisatie.
- Bij geldige passkeyaanmelding krijgt de gekoppelde bezoeker een Data Protection-ticket voor portalrun, sessiehash, generatie en vervaltijd. `POST /ClickFix?handler=Choice` vereist antiforgery en exacte Origin en accepteert alleen `attempt` en een gesloten actietype: `ClickFixFollowSimulated` of `ClickFixStopSelected`. Onbekende velden zoals een client-run-ID/completed-flag worden geweigerd; de server bepaalt run, volgnummer en UTC-tijd.
- De minimale slice bewaart **één keuze per gekoppelde portalrun**, dus volgnummer 1. Identieke retries/herladen/herstart geven hetzelfde record/tijdstip; conflicterende keuzes overschrijven niets. UI-reset/herladen maakt geen gebeurtenis. Opnieuw oefenen verandert de eerste vastgelegde keuze niet; een nieuwe beursdemonstratie vereist een nieuwe bezoeker en run.
- WPF gebruikt `GET /api/event-edition/poll/{wpfRunId}` met header `X-Lumma-Event-Code`. Backendtransport moet direct loopback zijn; forwardingheaders worden vóór proxyverwerking geweigerd. WPF accepteert alleen bestaande HTTPS-hostgrenzen, verbindt alleen met loopback, gebruikt geen proxies/cookies/redirects en behoudt TLS-validatie. Geen SignalR, browser–WPF-verbinding, eventbus, agent of remote commando.
- Antwoord bevat beide runreferenties, evenement en hoogstens één actie. WPF valideert eigen run, vaste types/velden en consistentie met de vorige inhoud. Polling herhaalt geen fase. Scopewijziging wist de oude weergave. Netwerkverlies wordt geen serverbevestiging; normale bevestiging vereist actuele serverstatus, ontvangen actie en expliciete toestemming. Een bekende gesloten status blokkeert normale voortgang.
- Bevestigde voortgang heeft een eigen blijvend WPF-statusveld, los van latere pollmeldingen. Bij een onleesbaar/onbeschikbaar manifest blijft de geauthenticeerde browseroefening werken, zonder meldticket en met uitleg over handmatige voortgang. Dit herstelt of omzeilt geen opslag-/autorisatiecontrole.
- Een aangepaste browser kan zijn eigen toegestane keuze nabootsen. Het record bewijst geen menselijke aandacht, gelezen uitleg, endpointuitvoering, sessiediefstal of detectie. Er is geen API die zulke voltooide stappen accepteert. Na WPF-herstart kan informatie opnieuw worden getoond, maar niets wordt automatisch uitgevoerd; geen exactly-once-claim voor externe effecten.
- Het bestaande Session Exposure Monitor-endpoint, DTO, grant en key zijn ongewijzigd. Er zijn geen wachtwoorden, tokens of gevoelige browsergegevens verzameld door deze flow. Tickets/leescodes zijn wel tijdelijke autorisatiegeheimen: niet loggen/exporteren/delen.

## Opslag en herstel

ExperienceEvents is onderzocht in `Services/ExperienceStatistics.cs`: Active/Closed/Imported en statistiektellingen, geen Scheduled/planning/presentatortoewijzing/WPF-runrelaties. Hergebruik zou schema- en statistieksemantiek wijzigen. Daarom blijven ExperienceEvents, PrizeEvents en hun historie ongewijzigd. Geen synchronisatie met de bevroren DemoEvent-doelarchitectuur.

Het nieuwe `event-edition.manifest` staat naast het **werkelijk geconfigureerde** databasebestand, buiten wwwroot, en wordt niet tijdens startup automatisch aangemaakt of hersteld. Geen SQLite-tabellen/migraties gewijzigd. Het bevat evenementmetadata, presenterreferentie, runreferenties, gehashte grants/sessie en keuze; geen passkeyrecords, cookies of plaintext leescodes.

Data Protection versleutelt/authenticeert het gehele manifest met eigen purpose via de bestaande keyring/Windows-DPAPI-context. Bestanden erven de bestaande beschermde runtimefolder-ACL; geen ACL-wijzigingen. Semaphore plus exclusieve OS-filelock serialiseert toegang over instanties/processen; writes gebruiken nieuw tijdelijk bestand, WriteThrough/Flush en atomaire replace met vorige versie. Contention/corruptie/ongeldige versies falen gesloten. Grenzen: 100 evenementen, 1000 runs per evenement, 4 MB manifest; geen automatische verwijdering.

Herstel is uitsluitend expliciet en bevestigd door een huidige administrator. De vorige versie wordt gecontroleerd; herstelbron en beschadigde versie blijven bewaard. Alle herstelde evenementen sluiten en leescodes worden ingetrokken. De laatste metadatawijziging kan verloren gaan; nieuwe runs zijn verplicht. Dit is geen herstelprocedure voor SQLite/passkeys/keyrings. Nieuwe manifestversies, locks, pending/back-up-/herstelbestanden zijn specifiek in `.gitignore` uitgesloten en blijven beschermd materiaal.

## Tests

Alle builds/integratietests gebruiken de eigen bronkopie `artifacts/event-edition-sprint3-cd66c2fe22be427eb2dad82bc4e158d1/work`, nieuw synthetisch SQLite-materiaal, eigen temp/appdata/browserprofielen, loopback-HTTPS en expliciet `LUMMA_TEST_WEB_DLL`. Geen actieve portal of productiegegevens gestart/gestopt/geopend/gewijzigd. Testcertificaatuitzondering beperkt tot de eigen nieuwe SPKI; geen wijziging aan certificaatvertrouwen.

Nieuwe regressies: operator- en bezoekersautorisatie, registratie versus login, run-/ticketisolatie, toegestane acties/velden, duplicaten/conflicten, echte gekoppelde browserflow en polling, refresh, opslagversleuteling, herstart/herstel, gesloten historie, netwerkverlies/herstel, handmatige fallback en nul fase-uitvoering vóór expliciete bevestiging. WPF-HTTP-tests controleren typed responses, verkeerde run/origin en ontbreken van bezoekerscookies.

Een generieke type-inferentiefout in nieuwe code is hersteld. Browserfixtures kregen begrensde CDP-opdrachten en correcte afhandeling van socket-sluiting bij Browser.close, met vaste diagnostiek. Geen applicatiebeveiliging of asserts verwijderd. De grotere HTTP-testset wacht uitsluitend bij HTTP 429 één bestaand minuutwindow vóór één retry van de niet-verwerkte aanvraag; productielimieten zijn niet verhoogd. Oude mislukte testlogs zijn behouden.

Definitieve logs: `restore-acceptance.log`, `build-acceptance.log` en `regression-acceptance.log` in bovengenoemde artefactmap. De eerdere runs blijven bewaard, inclusief de groene tussenrun vóór de laatste tekst- en fallbackverificatie. Ruwe LIVE-testlogs kunnen de lokale procesidentiteit bevatten; logs, browserprofielen, tickets, codes, certificaten en beelden worden niet gecommit. In de eindrun is de bestaande HTTP-429-limiet daadwerkelijk bereikt en gerespecteerd; na het window slaagde de oorspronkelijke beheerassertie.

| Controle | Resultaat |
|---|---|
| Locked restore | Geslaagd; geen dependency-/lockfilewijziging |
| Solution, zes directe projecten plus transitieve sidecar | Build geslaagd, 0 waarschuwingen, 0 fouten |
| Web/FIDO2/configuratie/HTTPS/Chrome | 618 checks geslaagd; baseline 579 |
| Node, alle vier UX-bestanden | 51 tests geslaagd; 0 mislukt/overgeslagen/geannuleerd |
| WPF smoke en nieuwe eventclient/gate/fallback | Geslaagd |
| LocalTestServer | 24 lifecyclechecks geslaagd |
| ATT&CK/Navigator | 2778 assertions geslaagd |
| Monitor en Windows WPF-verificatie | Geslaagd; bestaande functies en derde eventtab gecontroleerd |
| Volledige orchestrator | Exitcode 0; alle geconfigureerde suites geslaagd |

De gekoppelde mobiele browserpreview is visueel gecontroleerd. Fysieke WPF-/mobiele acceptatie blijft hieronder open. Hashvergelijking bevestigt dat de applicatie- en testbronnen gelijk zijn aan de geteste kopie; alleen documentatie en de specifieke ignore-regel zijn nadien aangepast. Geen publish-/sidecarintegratiewijziging; geen nieuwe publishronde nodig. Kandidatencontrole: 13 patroontreffers, waarvan dezelfde tien eerdere configuratie-/fixturetreffers, één hergebruik van de reeds toegestane publieke host in de nieuwe client en twee aantoonbare Razor-ID-interpolaties in EventEdition.cshtml regels 23/24, geen echte e-mailadressen. Geen nieuwe secrets, persoonsgegevens of beschermde runtimebestanden onder de kandidaten. Dit is geen volledige securityaudit. `main` en `origin/main` blijven op `a1d30fd584acfa5091cf4fe58a83611f66346086`.

## Gewijzigde bestanden (35)

| Onderdeel | Bestanden |
|---|---|
| WPF | `LummaSimulator.App/EventEditionClient.cs`, `EventEditionViewModel.cs`, `MainViewModel.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs` |
| Portalpagina's | `LummaSimulator.Web/Pages/ClickFix.cshtml`, `ClickFix.cshtml.cs`, `Presenter/Index.cshtml`, `Presenter/EventEdition.cshtml`, `Presenter/EventEdition.cshtml.cs` |
| Portalservices | `LummaSimulator.Web/Services/EventEditionStore.cs`, `EventEditionService.cs`, `PresenterEventEdition.cs`, `LummaSimulator.Web/Program.cs` |
| Portalassets | `LummaSimulator.Web/wwwroot/js/clickfix.js`, drie `Resources/Localization/UiText*.resx` |
| Webtests | `tests/LummaSimulator.Web.Tests/EventEditionChecks.cs`, `EventEditionHttpChecks.cs`, `ClickFixBrowserChecks.cs`, `PrizeBrowserChecks.cs`, `HttpChecks.cs`, `Program.cs`, `clickfix-browser.mjs`, `prize-browser-form.mjs` |
| Andere tests | `tests/WebAuthn.Ux.Tests/clickfix.test.cjs`, `tests/LummaSimulator.SmokeTests/EventEditionChecks.cs`, `Program.cs`, `LummaSimulator.SmokeTests.csproj`, `verification/Program.cs` |
| Documentatie/ignore | `.gitignore`, `README.md`, dit verslag, `docs/architecture/event-edition-sprint3-clickfix-contract.md` |

## Fysieke acceptatie en open risico's

- Lokale HTTPS-integratie: WPF en portal op dezelfde host voor dit kanaal; geen impliciete remote/centrale hosting. Controleer DNS/TLS/wifi, YubiKey/LastPass/mobiel en de juiste koppeling met twee bezoekers.
- Blokkeer de pollingroute ook aan publieke proxy-/firewallranden. Een proxy die alle forwardinginformatie verbergt is backend-side niet betrouwbaar herkenbaar. Geen productieconfiguratie aangepast of operationele netwerkvrijgave geclaimd.
- Test presentatiebeeldscherm/DPI, toegankelijkheid, operatorlogout, netwerkverlies/fallback en zelfstandige WPF bij MDR-VM-isolatie. Echte detectie blijft uitsluitend operatorverificatie in Elements.
- Gebruik fictieve bezoekers en niet-gevoelige evenementnamen. Bescherm manifestversies en keyring in de oorspronkelijke installatiecontext. Geen automatische retentie/opschoning; capaciteit kan veilig gesloten raken. Retentiebeheer vereist een afzonderlijke goedgekeurde handeling.
- Planning is metadata; start/sluiten handmatig. Geen scheduler, klokblokkade, uitgebreide capability-engine, multi-host-sync, nieuwe endpointagent of Prize-uitbreiding. Bestaande globale operatorrechten blijven leidend; toewijzing is geen autorisatie.

Rollback: optionele modus uit laten en bronwijziging terugnemen, nieuw manifest/keyring bewaren als beschermd bewijs. Geen oude database terugplaatsen of automatische bestandsopruiming. Eén lokale commit na geslaagde controles, daarna stoppen.

# LummaSimulator v0.4 — Passkey Portal, Session Exposure Monitor en ATT&CK

## Scope en architectuur

De twee applicaties blijven zelfstandig: `LummaSimulator.Web` is ASP.NET Core 10/Razor Pages; `LummaSimulator.App` is WPF/.NET 10/MVVM. Er is geen runtime-projectreferentie tussen beide. Alleen de kleine BCL-only padresolver in `shared/RuntimePaths.cs` wordt in beide projecten meegecompileerd. De WPF-simulator werkt ook als de webserver uitstaat; de portal werkt zonder de simulator.

Start de portal en de simulator in aparte processen en verplaats hun vensters handmatig naar de gewenste schermen. De browser en WPF-app kunnen op twee schermen van dezelfde laptop staan. Er is geen launcher, automatische schermindeling, autostart, GitHub-publicatie of distributie-ZIP gebouwd.

Gebruikte directe NuGet-packages blijven uitsluitend `Fido2.AspNet` 4.3.1 en `Microsoft.Data.Sqlite` 10.0.14. Lockbestanden leggen transitieve versies vast. Er zijn geen nieuwe NuGet-/npm-packages of frontendframeworks toegevoegd. Node.js is alleen nodig voor de regressietests, niet voor de draaiende applicaties.

## Installeren en onafhankelijk starten

Eerste voorbereiding vereist Windows, de .NET 10 SDK en toegang tot de reeds goedgekeurde packages of een gevulde lokale NuGet-cache:

```powershell
dotnet restore LummaSimulator.slnx --locked-mode
dotnet build LummaSimulator.slnx --no-restore
dotnet dev-certs https --trust
```

Een lokaal certificaat vertrouwen kan een OS-bevestiging vragen; de applicaties zelf vereisen geen administratorrechten en wijzigen geen truststores of browserinstellingen. Voor een demo met reeds gebouwde bronnen, zonder nieuwe restore:

```powershell
# Terminal 1: portal op https://localhost:7243
dotnet run --project LummaSimulator.Web --no-build --no-restore --launch-profile https

# Terminal 2: zelfstandig WPF-venster
dotnet run --project LummaSimulator.App --no-build --no-restore
```

Geen echte WebAuthn-privésleutels worden door de server ontvangen. De testsoftware-authenticator staat uitsluitend in het testproject.

## Passkey-UX en herkenbare namen

- Registratie en authenticatie zijn afzonderlijke acties. Registratie toont **Registratie voltooid** en verandert de knop in **Nog een passkey toevoegen**. Alleen een geldige serververificatie van een assertion verhoogt de authenticatieteller.
- Na authenticatie verschijnt **Je bent veilig aangemeld!**, met tijdelijke gebruikersnaam, servertijd, verificatiestatus en de alias van de daadwerkelijk geverifieerde credential, bijvoorbeeld `Passkey 1`. Dit is geen claim over het merk of de opslaglocatie van die credential.
- **Opnieuw aanmelden** gebruikt dezelfde requestcoördinator. Ook de knoppen op het bevestigingsscherm worden tijdens een aanvraag uitgeschakeld. **Demonstratie beëindigen** verwijdert de serverregistratie en bijbehorende sessiestaat.
- De zichtbare RP name is `Infinigate | Yubico Passkey Demo`; user name is `Visitor-XXXXXXXX`; displayName is `Infinigate Demo – XXXXXXXX`.
- Development RP ID blijft `localhost`; productie gebruikt `passkey.example.invalid`. Een naamswijziging verandert de RP ID niet en verwijdert bestaande credentials niet. Browser en passkeymanager bepalen hoe zij een naam tonen. Een bestaande passkey op het apparaat wordt niet stilzwijgend hernoemd.

Het eerdere probleem had aantoonbare applicatie-oorzaken: options-aanvragen konden elkaars challenge overschrijven en de browsercode gaf apparaatvoorkeuren mee. Verder kan herkenning uitblijven na het verlopen/verwijderen van een demo, bij een andere browser-eigenaarsessie, een andere RP ID of een andere passkeyprovider. Zonder een fysieke browser-/providertrace wordt niet beweerd dat één specifieke provider de oorzaak was.

De nieuwe flow heeft een idempotente initialisatie, een busy-guard vóór de eerste await en waar beschikbaar een Web Lock over tabs heen. De server staat maar één lopende operatie per demo toe en geeft anders HTTP 409. Een clientgegenereerde operatie-ID bindt options, verify en cancel aan dezelfde aanvraag. Een late annulering kan geen nieuwere operatie wissen. De server verbruikt een challenge atomair voor verificatie; de challenge verloopt na twee minuten.

Annuleren gebruikt AbortController voor browser en fetch; een eigen deadline begrenst de aanvraag. De UI wacht op afhandeling voordat hij de vergrendeling vrijgeeft. Na een fout/annulering wordt alleen de eigen serveroperatie geannuleerd. Als dat door een netwerkprobleem niet kan worden bevestigd, meldt de UI de resterende challenge-timeout. Er worden geen browserinstellingen, passwordmanagers of beveiligingswaarschuwingen omzeild.

Windows Hello, fysieke FIDO2-sleutel, telefoon en LastPass/andere providers blijven browserkeuzes. De kaarten geven uitleg, geen bewijs van de gebruikte authenticator. De portal forceert geen attachment/hints. Kies in de browser de gewenste optie, bijvoorbeeld een beveiligingssleutel of een andere provider. Kies bij authenticatie de provider waar de passkey van dit account is opgeslagen.

Bij een upgrade worden de extra operatie-ID, credentialalias en authenticatietijd kolommen toegevoegd zonder accounts, user handles, publieke sleutels of signatuurtellers te wissen. Oude eenmalige challenges worden ongeldig gemaakt; vernieuw daarom open browserpagina's na de upgrade. Een oude succes-teller zonder betrouwbare authenticatietijd wordt niet als bewijs van een huidige sessie gebruikt: meld opnieuw aan. De automatische tests controleren behoud van legacy-account- en credentialgegevens.

## Huisstijl en lokale assets

De portal, formulieren, foutmeldingen en het presentatordashboard gebruiken dezelfde donkere huisstijl. Kleuren zijn gecontroleerd tegen de officiële [Infinigate-site](https://www.infinigate.com/nl/) en de [officiële stylesheet](https://www.infinigate.com/nl/wp-content/themes/nuvias/assets/main.1790780110.css):

| Gebruik | Kleur |
| --- | --- |
| Primaire donkerblauwe achtergrond | `#153447` |
| Primair koraalaccent | `#ED5740` |
| Cyan-bron voor secundaire accenten | `#009FC7` |
| Witte hoofdtekst | `#FFFFFF` |
| Groen uit het officiële Yubico-SVG | `#84BD00` |

Lichtere secundaire tekstkleuren zijn afgeleide UI-contrastkleuren, geen extra geclaimde officiële merkkleuren. Groen wordt gebruikt voor geslaagde acties en het Yubico-logo. Knoppen zijn groot, navigatie heeft ruime aanraakvlakken en animaties respecteren reduced-motion.

Officiële, lokaal opgeslagen assets:

- `wwwroot/images/infinigate-official.svg`: de ongewijzigde negatieve anniversary-variant uit de header van de officiële Nederlandse Infinigate-site. Het is dus de actuele sitevariant, geen zelf nagemaakte standaard-wordmark. SHA-256: `3541DBDE562EB0F08B455AED999EC55AC6F3FFA9A7186022FED4E36FE971715A`.
- `wwwroot/images/yubico-official.svg`: [officiële Yubico-resource](https://resources.yubico.com/XEG5TAAH/at/8kq7984j7ffgq82qvfgqvbnh/yubico-logo.svg), gelinkt door [yubico.com](https://www.yubico.com/). SHA-256: `1B2248DA095732420D48F2EC4562DAFE72B4D8365CE8C8547AFC46EB3CB14BD9`.

De merkrechten blijven bij de eigenaren. De site vermeldt expliciet dat dit een Infinigate-demonstratieomgeving is en geen officiële Yubico-website. Alle CSS, JS en afbeeldingen worden lokaal geladen; fonts zijn lokale systeemfonts. De SVG-responses hebben een eigen beperkte CSP om de ingebedde officiële SVG-opmaak toe te staan zonder scripts of externe resources toe te staan.

## Offline HTTPS, RP ID en cybertrucknetwerk

De applicatiecode, database, assets, WebAuthn-serververificatie, WPF-simulatie, sessiemetadata en sandbox hebben geen internetdienst nodig tijdens uitvoering. Eerste package-installatie, brononderzoek en het verkrijgen van de logo's zijn voorbereidingsstappen.

Development: `https://localhost:7243`, RP ID `localhost`. Configuratie via `WebAuthn:RpId` en `WebAuthn:Origins` blijft strikt: expliciete canonical HTTPS-origins, geen wildcard, query, fragment of URI-geheimen. Productie staat het afgesproken domein `passkey.example.invalid` toe. Een ander domein vereist een bewuste wijziging van het configuratiebeleid.

Voor telefoon/iPad op het lokale netwerk:

1. Gebruik lokaal DNS dat `passkey.example.invalid` naar de demo-laptop laat wijzen.
2. Gebruik een vertrouwd certificaat met die naam en bereid het vertrouwen op de demonstratieapparaten vooraf voor.
3. Draai de portal met Production-configuratie, de publieke RP ID/origin en een bewust ingestelde HTTPS-listener voor het lokale netwerk. Configureer een certificaat via de normale ASP.NET Core/Kestrel-configuratie. Browserbeveiligingswaarschuwingen worden niet omzeild.
4. Open de portal rechtstreeks in de browser op de telefoon/iPad. De browser kiest de lokaal beschikbare authenticator of aangesloten FIDO2-sleutel.
5. Laat op de laptop zelf dezelfde naam naar loopback verwijzen als de WPF-monitor deze origin gebruikt. Op de telefoons wijst deze naar het LAN-adres; metadata-API-toegang vanaf het LAN blijft geweigerd.

Gebruik voor deze productie-/LAN-configuratie `--no-launch-profile`; het development-launchprofiel zou de omgeving weer op Development zetten. Configureer een Kestrel-certificaat en listener via de normale hostinstellingen, met een vooraf toegestane lokale firewallroute. De app past de netwerk- of certificaatinstellingen van de laptop en telefoons niet zelf aan.

**Niet iedere smartphone- of hybride QR-flow werkt offline.** Een browser-QR-flow kan een internetrelay gebruiken; een cloudpasskeymanager kan internet nodig hebben voor login, opslag of synchronisatie. Dit wordt niet door de portal nagemaakt of omzeild. Windows Hello en een voorbereide FIDO2-sleutel zijn de lokale demobasis; telefoon/iPad-ondersteuning moet met de werkelijk gebruikte provider en het afgesneden netwerk worden geaccepteerd. Zie ook [Microsofts cross-device-vereisten](https://learn.microsoft.com/en-us/entra/identity/authentication/passkey-faq).

Latere publieke hosting kan dezelfde productie-RP ID gebruiken, met een correct geconfigureerde reverse proxy/Cloudflare. Alleen expliciet vertrouwde proxies mogen forwarded headers leveren. Er is geen tunnel, DNS-wijziging, hostinguitrol of externe integratie aangemaakt.

## Portable paden

Er zijn geen hardgecodeerde ontwikkelpaden in de runtimecode. Relatieve paden worden ten opzichte van de executablemap opgelost, niet ten opzichte van de actuele werkmap. Configuratie, statische assets en WPF-settings kunnen daardoor later met een normale self-contained publish meegeleverd worden. Een Debug-binmap met ontwikkelmanifest is geen getest distributiepakket; deze versie bouwt geen publish-ZIP.

| Instelling | Werking |
| --- | --- |
| `LUMMA_DATA_DIRECTORY` | Gedeelde, optionele dataroot; relatief aan de executablemap. Default: de gebruikersmap LocalApplicationData plus `LummaSimulator`. |
| Web `Storage:DataDirectory` | Webalternatief voor de dataroot als de omgevingsvariabele niet is ingesteld. |
| Web `Database:Path` | Expliciet SQLite-pad, relatief aan de executablemap. Default: `PasskeyPortal/portal.db` onder de dataroot. Buiten `wwwroot` houden. |
| `LUMMA_LOG_DIRECTORY` | Absoluut of relatief aan de dataroot. Default `Logs`. |
| `LUMMA_EXPORT_DIRECTORY` | Absoluut of relatief aan de dataroot. Default gelijk aan de logdirectory. |
| `LummaSimulator.App.settings.json` | Naast de WPF-executable; configureert DataDirectory, LogDirectory, ExportDirectory en PortalOrigin. Wordt bij bouwen/publiceren meegekopieerd. Omgevingsvariabelen hebben voorrang. |
| `LUMMA_PORTAL_ORIGIN` | Exacte lokale HTTPS-origin voor de WPF-monitor; standaard `https://localhost:7243`. |
| `LUMMA_MONITOR_KEY` | Alleen het afzonderlijke integratiegeheim voor het WPF-proces. Niet in settings, logs of exports opnemen. |

SQLite gebruikt cascadeverwijdering, secure_delete en een lokale DELETE-journalmodus. Data Protection-sleutels staan naast de database en gebruiken op Windows DPAPI voor het uitvoerende account. Unixrechten en bestaande hostingmappen moeten bij implementatie door de beheerder worden beoordeeld. De bestaande Data Protection-applicatienaam en user-secrets-ID zijn bewust behouden om configuratie en tickets niet door alleen een labelwijziging te breken.

## Presentatordashboard en privacy

De bestaande aparte presentatorlogin blijft behouden en heeft geen standaardwachtwoord. Maak een wachtwoordhash interactief:

```powershell
dotnet run --project LummaSimulator.Web -- --hash-presenter-password
```

Configureer die buiten Git als `Presenter__PasswordHash`, start de portal opnieuw en log in op `/Presenter`. Het dashboard toont actieve demo's, credentials, succes-/faaltellingen, laatste activiteit, verstreken tijd en een geaggregeerd aantal verlopen demo's sinds processtart. Verwijderde gebruikers worden niet als historie bewaard. Achtergrondmonitoring en dashboardbezoeken verlengen bezoekerssessies niet.

Accounts en bezoekerssessies verlopen na 29 minuten inactiviteit; de opruimtaak draait elke 30 seconden. Onafhankelijk daarvan is de bevestigde WebAuthn-status maximaal 30 minuten na de laatste geldige assertion actief. De vroegste grens geldt. Bij beëindiging verdwijnen account, credentials, bezoekerssessiehash, challenges en monitorgrant. Een gestopt proces kan niet opruimen; bij opstart gebeurt eerst opschoning. Langdurige processtilstand, volle schijven en externe backups begrenzen fysieke verwijderingsgaranties.

Er worden geen naam, e-mailadres of telefoonnummer gevraagd en geen privésleutels ontvangen. Willekeurige account-ID's, user handles, credential-ID's en publieke sleutels zijn noodzakelijke tijdelijke, pseudonieme gegevens; er wordt niet absoluut geclaimd dat nergens persoonsgegevens worden verwerkt. Applicatielogs bevatten geen bezoekersnamen, cookies, sessiegeheimen, credentialmateriaal of volledige bezoekers-IP-adressen. Infrastructuurlogs vereisen een afzonderlijk beleid.

Een passkey kan na afloop op het apparaat achterblijven. Na verwijdering van de serverregistratie werkt die niet meer voor dit demoaccount; de bezoeker kan hem zelf uit de passkeymanager verwijderen.

## Session Exposure Monitor veilig koppelen

De bezoekerscookie bestaat al vóór registratie/authenticatie en geeft alleen eigenaarschap van de tijdelijke demo. De metadata-API gebruikt daarom `LastAuthenticatedAt`, dat uitsluitend na geldige server-side WebAuthn-verificatie wordt gezet. Registratie of alleen een cookie is geen actieve geauthenticeerde sessie.

De API is `GET /api/local/session-status/{DemoRunId}` en vereist tegelijkertijd:

- een loopbackverbinding op dezelfde laptop;
- een apart 256-bit integratiegeheim in `X-Lumma-Monitor-Key`, waarvan de portal alleen de SHA-256-hash configureert;
- een actuele presentatorgrant voor exact die demo. De grant vervalt binnen 29 minuten of eerder bij verwijdering/intrekken.

Een willekeurige bezoeker kan geen grants maken of andermans metadata opvragen. Een integratiegeheim alleen is evenmin voldoende. Onbekende, ongeautoriseerde en verwijderde demo's geven uniform 404 na de sleutelcontrole. Onjuiste of ontbrekende sleutels en niet-lokale clients krijgen 401. Er is geen lijst-API voor bezoekers en geen CORS-toegang voor andere sites. Responses zijn no-store.

Maak eenmalig het integratiegeheim via de lokale CLI-helper (dit is geen tokenuitgifte-API):

```powershell
dotnet run --project LummaSimulator.Web -- --generate-monitor-key
```

Bewaar de getoonde raw key privé. Stel de getoonde hash in voor de webserver en de raw key uitsluitend voor het WPF-proces, bijvoorbeeld in afzonderlijke terminals via Read-Host:

```powershell
# Webterminal, naast de presentatorhash
$env:Monitor__KeyHash = Read-Host 'Plak Web Monitor__KeyHash'
dotnet run --project LummaSimulator.Web --no-build --no-restore --launch-profile https

# WPF-terminal
$env:LUMMA_MONITOR_KEY = Read-Host 'Plak de raw WPF-key'
$env:LUMMA_PORTAL_ORIGIN = 'https://localhost:7243'
dotnet run --project LummaSimulator.App --no-build --no-restore
```

Ga in het presentatordashboard bij de gewenste Visitor naar **Toestaan**. Kopieer de zichtbare, niet-geheime DemoRunId naar het WPF-tabblad **Session Exposure Monitor** en kies **Sessiestatus controleren**. Met **Intrekken** sluit de presentator toegang direct af.

De WPF-client gebruikt standaard certificaatvalidatie, geen proxy, geen cookies en geen redirects. Hij accepteert alleen de geconfigureerde localhost- of productie-origin en maakt uitsluitend verbinding met loopbackadressen. Voor een publieke RP-naam moet die op de laptop dus lokaal naar loopback wijzen. Een key wordt niet naar een externe endpoint of redirect doorgestuurd.

Metadata bevat alleen DemoRunId, RP-doel, aanwezigheid, WebAuthn-methode, actieve status, verificatietijd en `sessionTokenExposed:false`. Deze laatste waarde beschrijft dat deze API geen sessietoken blootlegt; het is geen bewijs dat exposure elders onmogelijk is. Bezoekersnaam, credential-ID, cookie, sessiehash, private key en providerfingerprint ontbreken.

| Monitorstatus | Betekenis |
| --- | --- |
| Not evaluated | Nog geen actuele servercontrole of de actie is geannuleerd. |
| Geen geauthenticeerde sessie | Er is een toegestane demo, maar nog geen succesvolle WebAuthn-authenticatie. |
| Actieve, server-side bevestigde WebAuthn-sessie | De server heeft een geldige assertion bevestigd en de sessie is bij de laatste controle actief. Dit is geen gestolen sessie. |
| Fictief blootgestelde demosessie | De presentator heeft bewust een apart fictief sessieobject in het simulatorgeheugen laten maken. De echte portalsessie is niet gewijzigd. |
| Ongeldige, ingetrokken of verlopen sessie | Demo/authenticatie/grant is niet meer bruikbaar. |
| Niet beschikbaar | HTTPS, bereikbaarheid of authenticatie van de koppeling kon niet worden vastgesteld; er wordt geen positieve conclusie getrokken. |

De monitor toont het tijdstip van controle: een status is een momentopname, geen continue browserinspectie. Voor iedere fictieve exposure/replay wordt de serverstatus opnieuw gelezen. **Fictieve replay** hergebruikt alleen het interne fictieve object; het wordt nooit als cookie of bearer-token naar de echte portal gestuurd. Alle sandboxactiviteiten blijven `Simulated`. Een actieve applicatiesessie is ook geen Rapid7-/WithSecure-waarneming.

Het leerpunt: passkeys bieden phishingbestendige authenticatie, maar beschermen niet automatisch iedere reeds bestaande applicatiesessie tegen later misbruik. Deze demo toont dat onderscheid zonder een echte sessie te onderscheppen of over te nemen.

## MITRE ATT&CK-mapping

De originele zes interne fases blijven bestaan. De kaarten tonen techniek, tactiek, beschrijving, activiteitstype, uitvoeringsstatus, duur, tijdstip en afzonderlijke productbevestigingen. De mapping is gecontroleerd tegen Enterprise ATT&CK **20.0**. De gebruikte techniekfacts, versies en bronhash staan in `docs/references/enterprise-attack-20.0-subset.json`.

| Fase/scenario | Techniek | Toepassing |
| --- | --- | --- |
| Initial execution, visueel | T1204 User Execution — Execution | Alleen een fictief gebruikersgestuurd uitvoeringsscenario. Start aanklikken geldt niet als echte kwaadaardige User Execution. |
| Initial execution, live | T1033 System Owner/User Discovery — Discovery | Zichtbare whoami.exe; geen vastlegging van de werkelijke accountnaam. |
| System discovery | T1082 System Information Discovery — Discovery | Vaste fictieve inventaris, ook in LIVE TELEMETRY. |
| Browser discovery | T1217 Browser Information Discovery — Discovery | Fictief browserinventaris-scenario. Aanmaken van dummy-JSON is echte filetelemetrie, geen echte browserdiscovery. |
| Credential collection | T1555.003 Credentials from Web Browsers — Credential Access | Alleen eigen dummydata; techniek blijft gesimuleerd. |
| Data staging | T1074.001 Local Data Staging — Collection | Preciezere subtechniek van T1074; live schrijven van dummyrecords kan als gecontroleerde uitvoering gelden. |
| Simulated exfiltration | T1041 Exfiltration Over C2 Channel — Exfiltration | Uitsluitend fictief C2-scenario. Een optionele loopback-POST is geen C2-kanaal of echte exfiltratie. |
| Expliciete sandbox-exposure | T1539 Steal Web Session Cookie — Credential Access | Alleen fictief object; geen cookie-uitlezing. |
| Expliciete sandbox-replay | T1550.004 Web Session Cookie — Lateral Movement | Alleen fictief web-/SaaS-sessiescenario; geen toegang tot een echte applicatie. |

Voor T1550.004 gebruikt v20.0 de tactiek Lateral Movement en onder andere platform SaaS. De export neemt SaaS daarom mee als deze sessietechniek aanwezig is. Deze twee scenario's zijn optioneel en worden niet als extra uitvoeringsfases aan de zesfasenrunner toegevoegd.

Statussen zijn gescheiden: `Simulated`/`Executed` voor activiteit en techniek, `Observed` voor expliciet bevestigde productwaarneming, `Detected`/`Not detected` voor expliciete productbeoordeling en anders `Not evaluated`. Werkelijke dummybestands-I/O kan `Executed` zijn terwijl de voorgestelde aanvalstechniek `Simulated` blijft.

Niets wordt automatisch `Detected`. In het presenter-confirmationpaneel kiest de operator techniek, product (Rapid7 InsightIDR of WithSecure Elements EDR), beoordeling en de expliciete bevestiging. Product, bevestigingstijd en de bron 'Presenter' worden opgeslagen. Er is nog geen gevalideerde EDR-integratie. Een API-controle van deze portal vormt geen EDR-detectie.

## Navigator-export

**Export MITRE ATT&CK** maakt een lokale eigen layer: geen publicatie of toevoeging aan de officiële MITRE ATT&CK-kennisbank. Het formaat is de officiële Navigator layer **4.5**, met Navigator **5.3.2** en dataset **20.0**. De officiële formatspecificatie staat vastgelegd in `docs/references/navigator-layer-format-v4.5.md`, met de upstream-licentie erbij.

Defaultlocatie is de bestaande demologdirectory, of de expliciet geconfigureerde exportdirectory. Bestandsnaam: `Lumma-Demo-<datum>-<DemoRunId>-<unieke-id>-ATTACK.json`. De layer bevat scores, RGB-kleuren, vaste opmerkingen, DemoRunId, duur/tijdstip en per-productstatussen. Bij een gekoppeld sessiescenario is de portal-DemoRunId de correlatiereferentie; de oorspronkelijke simulator-DemoRunId wordt apart vastgelegd. Er worden geen raw logs, gebruikersnamen, systeempaden, credentials, cookies, tokens of integratiegeheimen gekopieerd.

Scores zijn een eigen legenda, **geen dekkings- of detectiekansmeting**: 0 niet beoordeeld, 5 gesimuleerd, 15 gecontroleerd uitgevoerd, 25 expliciet niet gedetecteerd, 50 expliciet waargenomen en 100 expliciet gedetecteerd. Een hogere bevestigde productstatus kan de kleur bepalen; de metadata bewaart de afzonderlijke dimensies en producten.

De geautomatiseerde validator controleert de gebruikte velden en types tegen de officiële Markdown-specificatie, metadata als stringparen, versies, IDs/tactieken tegen de gepinde dataset, scores en kleuren. Er is geen fictieve claim dat MITRE een externe validatieservice heeft aangeroepen. Import via de Navigator-browserinterface blijft een handmatige acceptatiestap. Een offline Navigator-installatie met de passende dataset is nodig om dit zonder internet te demonstreren; Navigator zelf is niet meegebundeld.

## Testresultaten en handmatige acceptatie

Uitgevoerd: restore met locked-mode, volledige build met **0 fouten/0 waarschuwingen**, en `dotnet test LummaSimulator.slnx --no-restore`. De definitieve volledige suite slaagde voor 121 service-/HTTPS-webcontroles, 19 browserlogica-tests, 2778 ATT&CK/status/formaatasserties, 17 monitor/clientcontroles, de bestaande simulator-smoke-tests en de WPF-integratietest. Alle runners eindigden succesvol; fysieke authenticators zijn hiermee niet getest.

`dotnet test` gebruikt een expliciete MSBuild-runner zonder nieuwe testadapterpackages; hij voert de werkelijke console-assertierunners en Node's ingebouwde test runner achtereenvolgens uit en faalt bij een fout. Testprocessen krijgen eigen data-, log- en exportmappen onder de genegeerde test-binmap. Er worden geen werkelijke Rapid7-/WithSecure-bevestigingen door tests gefabriceerd. Fixtures met productstatussen zijn unit-testdata, geen securityplatformbewijs.

```powershell
dotnet restore LummaSimulator.slnx --locked-mode
dotnet build LummaSimulator.slnx --no-restore
dotnet test LummaSimulator.slnx --no-restore
```

Handmatig accepteren op de cybertrucklaptop:

1. Start WPF en portal onafhankelijk en verplaats ze naar twee schermen; stop elk proces apart en controleer dat het andere doorwerkt.
2. Controleer desktop, tablet/telefoon en groot scherm, leesbaarheid van beide officiële logo's, touchscreenknoppen, toetsenbordfocus en reduced-motion.
3. Registreer met voorbereide Windows Hello en YubiKey; controleer Visitor/displayName, registratie voltooid, afzonderlijk aanmelden, credentialalias, timestamp en betrouwbare tellers.
4. Probeer annuleren, timeout, dubbelklikken en een tweede tab. Controleer dat LastPass niet wordt geblokkeerd en de gebruiker de gewenste authenticator via de normale browserdialoog kan kiezen.
5. Schakel internet uit, behoud lokaal DNS/HTTPS en test Windows Hello/YubiKey. Test telefoon/iPad rechtstreeks op de lokale portal. Noteer welke provider-/QR-combinaties alsnog internet vereisen; presenteer die niet als offline geverifieerd.
6. Controleer de monitor eerst vóór authenticatie, daarna na een geldige assertion. Geef alleen de geselecteerde demo toestemming. Test verkeerde sleutel, andere demo, intrekken, beëindigen en inactiviteitsverval.
7. Voer fictieve exposure en replay uit. Controleer dat een actieve sessie niet als gestolen wordt aangeduid en dat de echte browsersessie niet verandert.
8. Geef uitsluitend na daadwerkelijke controle in Rapid7/WithSecure een productbevestiging. Importeer de JSON via Navigator en controleer de techniekkaarten, optionele SaaS-replaykaart, legenda en metadata.
9. Start vanuit een andere werkmap en met een relatieve dataroot. Controleer dat database/logs/export op de geconfigureerde locaties komen. Een latere self-contained publish/ZIP vereist nog een aparte distributieacceptatie; er is geen pakket of launcher gebouwd.

Visuele browserautomatisering is afgebroken: Computer Use kon de actieve browser-URL niet met voldoende zekerheid vaststellen. Er zijn geen veiligheidswaarschuwingen of browserinstellingen omzeild. Fysieke authenticators, smartphoneproviders, het echte tweeschermenarrangement en daadwerkelijke Navigator-UI-import zijn niet als automatisch getest gerapporteerd.

## Gewijzigde/toegevoegde bestanden

- `LummaSimulator.slnx`, projectbestanden en lockbestanden: v0.4 en de dependencyvrije regressierunner; geen nieuwe directe packages.
- `shared/RuntimePaths.cs`; `LummaSimulator.App/DesktopSettings.cs`, `LummaSimulator.App.settings.json`: portable runtimeconfiguratie.
- WPF: `Models.cs`, `MainViewModel.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Services.cs`, `TelemetryService.cs`, `AttackMapping.cs`, `NavigatorExportService.cs`, `SessionMetadataClient.cs`, `SessionMonitorViewModel.cs`.
- Web: `Program.cs`, `appsettings.json`, `Configuration/WebAuthnSettings.cs`, `Models/DemoModels.cs`, `Services/DemoService.cs`, `Services/DemoStore.cs`, `Services/SessionStatusService.cs`, de welkomst-/demopagina, de presentatorpagina plus code-behind en `Pages/Shared/_Layout.cshtml`.
- Webassets: `wwwroot/css/site.css`, `wwwroot/js/passkeys.js`, `wwwroot/images/infinigate-official.svg`, `yubico-official.svg`, `README.md`.
- Tests: nieuwe/uitgebreide webchecks (`Program.cs`, `HttpChecks.cs`, `SessionStatusChecks.cs`), simulatorchecks (`Program.cs`, `NavigatorChecks.cs`, `SessionMonitorChecks.cs`), `tests/WebAuthn.Ux.Tests/passkeys.test.cjs`, regressierunner en `verification/Program.cs`.
- Documentatie: README, dit bestand en de officiële Navigator-/Enterprise-referentiebestanden onder `docs/references`.

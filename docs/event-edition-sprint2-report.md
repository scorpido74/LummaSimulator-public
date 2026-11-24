# Event Edition — sprint 2

Datum: 1 november 2026. Baseline: `d7de690`, branch `feature/phase5-domain-foundation`. Bij aanvang was de werkboom schoon. Geen wijzigingen op `main`, geen push of deployment.

## Opgeleverd gedrag

### WPF-presentator

De bestaande WPF-app opent met een scrollbare scenario-introductie in de bestaande huisstijl. De introductie bevat titel, beschrijving, aanvallersdoel, de rollen hacker/slachtoffer/SOC, de zes bestaande ATT&CK-fases en leerpunten. Start scenario gebruikt precies het bestaande `StartCommand`, inclusief SAFE/LIVE- en sidecar-readinesscontroles. Openen van de introductie start niets automatisch.

De presentator kan zonder uitvoering naar de bestaande bediening om de modus te kiezen. De knop Scenario opent de introductie opnieuw wanneer de app idle is. Alle bestaande dashboardtabs, start/stop/reset, telemetrie, monitor, log- en exportfuncties blijven aanwezig.

`ScenarioOverview` is uitsluitend presentatie-inhoud, los van uitvoering. Eén huidig scenario, geen nieuwe scenario-engine of keuzearchitectuur. De introductie bindt direct aan `MainViewModel.Phases`; geen tweede lijst met ATT&CK-identifiers, fasemapping of uitvoeringsstappen. Het inhoudsmodel laat latere afzonderlijke onderwijs-/awarenessinhoud toe zonder die scenario's nu te implementeren.

### Passkey Portal

Na de bestaande geslaagde server-side WebAuthn-aanmelding verschijnt een link naar `/ClickFix`. De pagina vereist server-side de bestaande eigen bezoekerssessie, minstens één geregistreerde credential en een aanmeldtijd die niet in de toekomst ligt en minder dan 30 minuten oud is. Deze bovengrens volgt de bestaande monitorsemantiek voor actieve WebAuthn-aanmelding; bestaande registratie-/login-/sessievalidatie is niet gewijzigd. Het bestaande kortere idlebeleid kan de account eerder beëindigen. Bij ontbrekende aanmelding keert de bezoeker met een begrijpelijke NL/EN-melding terug naar de demo; een verdwenen account gaat naar de startpagina.

De bezoeker leest een duidelijk fictieve herstelmelding en kiest tussen de melding volgen **in simulatie** of stoppen en controleren. Beide keuzes geven gerichte uitleg; daarna volgen leerpunten en een expliciete afronding. De voortgang, toetsenbordfocus en statusmeldingen worden bijgewerkt. Bestaande taalwisseling werkt ook tijdens de oefening. Zonder JavaScript blijven de keuzeknoppen uitgeschakeld met uitleg.

Alle oefenstatus bestaat alleen in de geladen pagina. Opnieuw beginnen, herladen en navigeren (ook back/forward-cacheherstel) wissen die status; dit reset geen account, passkey, authenticatieteller of andere bezoeker. Er is geen database-, cookie-, browserstorage- of serveropslag van keuzes, geen actie-API en geen WPF-/SOC-melding.

De oefening bevat geen shellcommando, payload, download, clipboardactie, installatie, vrije gebruikersinvoer, endpointuitvoering of credential-/tokenverzameling. Keuzes wijzigen alleen zichtbaarheid en vaste gelokaliseerde tekst. Een aangepaste browser kan dit lokale spel beïnvloeden; de uitkomst is daarom geen beveiligingsbewijs of betrouwbare telemetrie.

### Sprint 3

Zie [contractvoorstel](architecture/event-edition-sprint3-clickfix-contract.md). Het ontwerp gebruikt de bestaande **portal**-DemoRunId, gesloten actietype, oplopend servervolgnummer en UTC-servertijd. De WPF-run heeft een andere identiteit; expliciete geautoriseerde koppeling blijft nodig. Het voorstel behandelt duplicaten aan bron en ontvanger, beperkte rechten, Origin/CSRF, strikte validatie, retentie en verbindingsverlies. Transport, pairing en duurzame deduplicatie zijn nog te besluiten. Geen nieuwe communicatie, queue, schema of MDR-activiteit geïmplementeerd.

## Verificatie

Builds en integratietests zijn uitsluitend in een nieuw gemaakte bronkopie onder de genegeerde `artifacts/` uitgevoerd. Eigen tijdelijke/appdata-mappen, expliciet `LUMMA_TEST_WEB_DLL`, nieuwe synthetische SQLite-databases en alleen loopback-HTTPS. De Chrome-fixture gebruikt uitsluitend een nieuwe testvisitorcookie en een eigen profiel; de testcertificaatuitzondering is beperkt tot de SPKI van het nieuw gegenereerde testcertificaat. Geen wijziging aan productiecertificaatvertrouwen of WebAuthn-validatie.

Nieuwe controles:

- Windows WPF-verificatie: introductie als initiële weergave, drie rollen/leerpunten/doel, dezelfde zes faseobjecten, dezelfde startcommandbinding, naar bediening en terug zonder uitvoering, en introductie verbergen bij expliciete start. PNG-preview in de geïsoleerde fixture.
- Web: eigenaarschap zonder aanmelding, registratie zonder aanmelding, geldige echte FIDO2-testassertion, toekomstige/verlopen aanmeldtijd, herauthenticatie en beëindiging. HTTPS: anoniem/andere browser/intrekking geweigerd, geautoriseerde pagina no-store, geen reflectie van onbetrouwbare query-inhoud en verse status bij herladen.
- Node: beide keuzes, uitleg/afronding, voortijdige/duplicaatklikken, reset, navigatie/cacheherstel, lokale NL/EN-teksten en ontbreken van uitvoerings-/netwerk-/opslagsinks. De harness biedt geen endpoint-, clipboard-, opslag- of netwerk-API's.
- Echte geïsoleerde Chrome: beide takken, focus, reset, afronding, taalwisseling, mobiele breedte, herladen en geen netwerkverzoeken door de keuzes. Desktop-/mobiele PNG-previews zijn visueel gecontroleerd.

De eerste build vond een ontbrekende linked-sourceverwijzing naar `ScenarioOverview.cs` in het smoke-testproject; deze testintegratie is toegevoegd. De eerste volledige testrun stopte in de nieuwe browserfixture; de oorspronkelijke diagnose werd overschaduwd door bestaande teardown-asserties. De nieuwe fixture schrijft daarom eigen vaste foutdiagnostiek vóór teardown, begrenst iedere browseropdracht, wacht op het werkelijk nieuwe document na reload en behandelt een verbroken verbinding bij Browser.close. De oorspronkelijke browserstoring is daarna niet gereproduceerd; de precieze eerste oorzaak is niet bewezen.

Een volgende run vond een ontbrekende aanmaak van de nieuwe WPF-previewmap; die wordt nu expliciet als eigen fixturemap aangemaakt. Een verdere run reproduceerde een bestaande Windows-teardownrace in `LocalServerChecks.RejectUnknownAsync`: een geannuleerde NetworkStream-read eindigde in IOException/socketfout 995. Alleen IOException **nadat de eigen fixture-annulering is aangevraagd** wordt nu opgevangen, naast de bestaande OperationCanceledException. Fouten tijdens de controles blijven falen; de assertions zijn behouden. Geen productie-sidecarcode gewijzigd.

Definitieve logs: `artifacts/event-edition-sprint2-1856720bf9f442e0ab64d0d4ae862993/`, met `restore-final.log`, `build-acceptance.log` en `regression-acceptance.log`. De eerdere mislukte runs zijn daar eveneens behouden. Alle testgegevens, browserprofielen en beelden blijven genegeerd en buiten de commit. Ruwe LIVE-testlogs kunnen de lokale procesidentiteit bevatten en zijn geen publicatieartefacten.

| Eindcontrole | Werkelijk resultaat |
|---|---|
| Locked restore | Geslaagd; geen nieuwe packages/lockfilewijziging |
| Solutionbuild, zes directe projecten plus transitieve LocalTestServer | Geslaagd; 0 waarschuwingen, 0 fouten |
| Web services/configuratie/FIDO2/HTTPS/echte browser | 579 checks geslaagd, inclusief 13 nieuwe ClickFix-checks |
| Node, bestaande drie bestanden plus ClickFix | 49 tests geslaagd, inclusief 6 nieuwe; 0 mislukt/overgeslagen/geannuleerd |
| WPF smoke en SAFE/LIVE | Geslaagd: zes fases, timing, stop/reset/fouten, lokale proces-/bestands-/HTTP-telemetrie |
| LocalTestServer | 24 lifecyclechecks geslaagd |
| ATT&CK/Navigator | 2778 assertions geslaagd |
| Session Exposure Monitor | Bestaande metadata-/simulatie-/intrekkingschecks geslaagd |
| Windows WPF | Bestaande en nieuwe introductiechecks geslaagd; ook afzonderlijke visuele acceptatierun exitcode 0 |
| Volledige regressieorchestrator | Exitcode 0; alle geconfigureerde suites geslaagd |

WPF-introductie en ClickFix-desktop-/mobiele previews zijn visueel gecontroleerd. De geteste applicatie-/testbronnen zijn gelijk aan de commitkandidaten; na de eindrun zijn alleen README en dit verslag bijgewerkt. Geen publishwijziging, dus geen nieuwe publishacceptatie uitgevoerd. De kandidatencontrole vond uitsluitend dezelfde tien eerder beoordeelde hostconfiguratie-/fictieve fixture-/patroontreffers, geen nieuwe secrets of persoonsgegevens en geen beschermde runtimebestanden. Dit is geen volledige securityaudit. `main` en `origin/main` bleven ongewijzigd op `a1d30fd584acfa5091cf4fe58a83611f66346086`.

## Gewijzigde bestanden

| Onderdeel | Bestanden |
|---|---|
| WPF | `LummaSimulator.App/ScenarioOverview.cs`, `MainViewModel.cs`, `MainWindow.xaml` |
| Portal | `LummaSimulator.Web/Pages/Demo.cshtml`, `ClickFix.cshtml`, `ClickFix.cshtml.cs` |
| Portalassets | `LummaSimulator.Web/wwwroot/js/clickfix.js`, `wwwroot/css/site.css`, drie `Resources/Localization/UiText*.resx` |
| Tests | `tests/WebAuthn.Ux.Tests/clickfix.test.cjs`, `tests/LummaSimulator.Web.Tests/ClickFixChecks.cs`, `ClickFixBrowserChecks.cs`, `clickfix-browser.mjs`, `HttpChecks.cs`, `Program.cs` |
| Testintegratie | `tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj`, `tests/LummaSimulator.SmokeTests/LummaSimulator.SmokeTests.csproj`, `tests/LummaSimulator.SmokeTests/LocalServerChecks.cs`, `verification/Program.cs` |
| Documentatie | `README.md`, dit verslag en `docs/architecture/event-edition-sprint3-clickfix-contract.md` |

## Grenzen en fysieke acceptatie

- Bestaande productiebezoekers en presentatoren zijn niet aangemeld of aangepast. Behoud is onderbouwd door ongewijzigde authenticatie-/migratiecode en regressies met synthetische accounts, niet door toegang tot actieve gegevens.
- Toegang wordt bij openen van de ClickFix-pagina gecontroleerd. Een al geladen, uitsluitend fictieve browseroefening heeft geen serveracties en kan na sessie-intrekking nog lokaal worden bekeken. Herladen/openen controleert opnieuw; server-side telemetrie vereist later een nieuwe autorisatiecontrole per actie.
- Oefenvoortgang overleeft herladen niet en wordt niet met de presentator gedeeld. WPF en portal hoeven niet gelijktijdig beschikbaar te zijn. Geen nieuwe internetafhankelijkheid voor WPF.
- WPF-introductie is Nederlandstalig; portalinhoud is NL/EN via het bestaande offline resourcecatalogus. Geen nieuwe WPF-taalarchitectuur geïntroduceerd.
- Nog fysiek accepteren: YubiKey/LastPass/telefoon, productie-HTTPS en wifi/DNS, browser back/forward op beoogde mobiele apparaten, toetsenbord/screenreader en WPF op presentatiebeeldscherm/DPI. Voorafgaand aan de volledige demonstratie tevens zelfstandige werking bij uitval/isolatie van de MDR-VM en echte consolewaarneming controleren.
- Geen database/schemawijziging, configuratieverruiming, migratie, opschoning van bestaande gegevens, nieuwe agent, MDR-integratie of implementatie van de uitgebreide v0.5-architectuur. De bestaande Prize-grens blijft behouden.

Rollback van deze sprint betreft uitsluitend bron-/asset-/test-/documentwijzigingen; geen dataherstel. Een lokale commit volgt alleen op geslaagde controles. Daarna stoppen; sprint 3 vereist een nieuwe opdracht.

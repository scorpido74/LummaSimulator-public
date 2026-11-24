# Cybertruck-evenementbediening en bezoekersflow — 12 november 2026

## Gedrag en bestaande regels

AGENTS.md, git status, requirements, Prize-architectuur en security-eisen zijn gecontroleerd. Bestaand lokaal werk is behouden. De volgende wijzigingen gebruiken de bestaande portalservices en SQLite-schema's; er is geen migratie toegevoegd en geen actieve database geopend of gewijzigd.

- /Presenter/Events begint met Actief evenement. De selectie vereist voor iedere rol (ook Administrator) een expliciete eigenaars-/ledenrelatie met actieve eigenaar en sleutel. Daarna geldt per type de bestaande LiveAsync-definitie: Active voor Experience/Edition, Open voor Prize. Het admin-beheeroverzicht onder Instellingen en details behoudt alle eerder toegankelijke scopes, inclusief historie/import/onverdeeld, met een expliciete overview-route voor beheer en eigendomsherstel. Dit is geen uitbreiding van bezoekers- of beheerrechten.
- De geselecteerde Prize-ronde toont Occupied/Capacity, unieke afgeronde demo's en actuele actieve lotingsinschrijvingen. De loting telt volgens de bestaande CloseAsync-query actieve PrizeParticipants, niet alle credentials of aanmeldingen en niet uitsluitend afgeronde oefeningen. Dit onderscheid is zichtbaar uitgelegd.
- ProgressAsync gebruikt COUNT DISTINCT. Afronding schrijft in dezelfde transactie als VisitorExercises.CompletedAt hoogstens één RoundDemoCompleted-auditactie per bestaande ronde-deelnemer. Daarmee blijft een nieuwe voltooiing meetellen na bestaande accountopschoning, zolang de bestaande evenementgegevens bestaan. Oude reeds opgeruimde oefeningen zonder deze audit zijn niet achteraf gereconstrueerd. De bestaande retentie en auditopschoning blijven leidend.
- Viersecondenpolling werkt uitsluitend voortgangstekst en winnaars bij; instellingenformulieren worden niet vervangen. Ronde afsluiten vereist bevestiging, actuele presentatorkoppeling, Origin en antiforgery. Het bestaande transactionele CloseAsync bewaart de uitslag en volgende ronde eenmalig; een volle ronde wordt niet automatisch gesloten. Nul gerechtigden levert geen winnaar op. De rondeknop en evenementsluiting blijven aparte acties. Uitreiking blijft apart op de bestaande Prize-detailpagina geregistreerd.
- Na loting blijft de gesloten ronde zichtbaar via roundId; Actieve ronde tonen toont de volgende ronde. QR en kopieerbare centrale bezoekerslink staan direct onder voortgang. De beheeracties, aanmeldopties, uitnodigingen, rapportage en eigendomsherstel staan onder Instellingen en details; dubbele totaaltellers zijn verwijderd.
- /Demo gebruikt de server-side evenementkoppeling voor Welkom bij [eventnaam], of de zelfstandige passkey-demo-titel. Actieve sessies en Nieuwe bezoeker staan in Demo beheren (standaard dicht); beëindigen blijft rechtstreeks bereikbaar. De privacy-/passkey-uitleg en het effect van Nieuwe bezoeker zijn in NL en EN toegevoegd.
- Nieuwe bezoekers ontvangen vóór de eerste registratie een willekeurige woordalias uit de bestaande rondewoordlijsten. De transactionele allocator vermijdt actieve en historische aliasbotsingen. Nieuwe WebAuthn-options gebruiken deze alias voor user.name en user.displayName, met dezelfde 32-byte user handle. De ronde neemt deze naam over; bestaande rondealiassen worden behouden en opnieuw gebruikt bij extra registraties. Voor uitzonderlijke historische namen langer dan de bestaande rondegrens van 40 tekens blijft de oude ronde-aliasallocator als compatibiliteitsterugval behouden. Bestaande credentials, RP-ID en origin zijn niet gewijzigd.
- Na registratie: extra passkey donkerblauw, inloggen oranje, stap 01 blauw, stap 02 groen. De tekst Registratie voltooid blijft. Voor registratie blijft inloggen uitgeschakeld, ook server-side.
- /SignedIn bundelt bevestiging, alias, uitleg en feitelijke ronde-inschrijving. Er wordt geen prijs beloofd. Het scherm blijft staan tot Start de demo-oefening. De oefening gebruikt de bestaande tijdlijn en uitsluitend browserinhoud. Uitvoeren bevat nu een niet-interactieve div en knopvormen, geen invoerveld of actieve afgebeelde knoppen. Beide mock-ups dragen Demo-oefening. Alleen LUMMA-DEMO-ONLY wordt afgebeeld; geen klembord, systeemacties of native meldingen. Er waren in dit verzoek/de checkout geen beschikbare schermafbeeldingsbestanden; de bestaande schone Windows-mock-up is daarom in code doorontwikkeld.

## Werkelijk uitgevoerd

- Volledige expliciete Node-regressieset: **103 tests, 101 geslaagd, 2 bestaande PE-/publishiconcontroles overgeslagen, 0 fouten**. Nieuw: werkelijke SQL voor admin-/presentatorkoppeling, unieke afrondings-/lotingspopulatie, behoud na opruiming en voortgangspolling; bestaande passkey-, lokalisatie-, consent-, QR- en oefeningstests blijven slagen.
- De statische Chrome-bronpreviewtest is daadwerkelijk gestart in een eigen profiel, maar faalt op CDP timeout: Page.enable. Geen visuele of mobiele browseracceptatie geclaimd; geen ander browserpad gebruikt om deze blokkade te omzeilen.
- Nieuwe geïsoleerde startkopie: artifacts/dev-start-2fc94d9394d24104acc86fe3179a8638. Start-LummaDev.bat verify gebruikt locked restore en faalt op geweigerde leestoegang tot %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. Geen ACL/configuratie/identiteitsomweg toegepast. Release-build, bestaande regressieorchestrator en executable Web.Tests-runner zijn daarna met --no-restore geprobeerd; NETSDK1004 (geen project.assets.json). LUMMA_TEST_WEB_DLL is expliciet op de geïsoleerde portal-DLL ingesteld. Er is geen geslaagde nieuwe .NET-/Razor-build en geen nieuwe portal of WPF gestart.
- De 12 gewijzigde C#-bestanden zijn met de beschikbare Roslyn-parser op syntax gecontroleerd; dit is geen typecontrole of build. XML-resources, JavaScript-syntax en git diff --check slagen.
- C#-regressies zijn aangepast/uitgebreid voor selectie als admin/presentator, volle ronde en unieke voortgang, nul gerechtigden en herhaald sluiten, dezelfde registratie-/rondealias en behoud van user-handle-formaat. De bestaande tests voor gelijktijdig sluiten en eenmalige uitreiking blijven aanwezig. Door restore zijn deze tests niet uitgevoerd.

Bewijs/logs: artifacts/event-demo-refresh-073dbb8d39a7457e848382cd231a2a13 (node.log, browser.log, build.log, regression.log, web-tests.log, syntax.log); restore.log in de dev-start-map. Latere kleine titel-/compatibiliteitscorrecties zijn naar dezelfde eigen bronkopie gesynchroniseerd, zonder een geslaagde build te suggereren.

## Nog open

- NuGet-leestoegang via de normale beheerroute herstellen; locked restore, solution/Razor-build en C#-regressies opnieuw uitvoeren.
- Volledige presentatie als presentator én admin: selectie, vrije/volle ronde, lege loting, concurrent bevestigen, opgeslagen uitslag en eenmalige uitreiking.
- Live NL/EN-bezoekersflow op desktop en mobiel, eerste/extra/bestaande passkey, Windows-passkeyvenster (werkelijk zichtbare accountnaam), blijvende SignedIn-pagina en volledige oefening tot bedankscherm.
- Fysieke telefoon: centrale HTTPS-link/QR, DNS/TLS op het demonetwerk en daadwerkelijke scan/aanmelding.

Geen van deze fysieke scenario's is als geslaagd aangemerkt. Geen actieve processen, databases, keyrings, certificaten of firewallregels zijn gewijzigd.

## Bestanden en regels in deze wijziging

| Bestand | Regel |
|---|---|
| LummaSimulator.Web/Services/EventAccess.cs | 94 |
| LummaSimulator.Web/Services/Prizes/PrizeRoundsService.cs | 310 |
| LummaSimulator.Web/Services/DemoService.cs | 49 |
| LummaSimulator.Web/Pages/Presenter/Events/Index.cshtml.cs | 12 |
| LummaSimulator.Web/Pages/Presenter/Events/Index.cshtml | 5 |
| LummaSimulator.Web/Pages/Shared/_EventRoundProgress.cshtml | 1 |
| LummaSimulator.Web/wwwroot/js/event-round-progress.js | 3 |
| LummaSimulator.Web/Pages/Demo.cshtml.cs | 13 |
| LummaSimulator.Web/Pages/Demo.cshtml | 12 |
| LummaSimulator.Web/Pages/SignedIn.cshtml.cs | 8 |
| LummaSimulator.Web/Pages/SignedIn.cshtml | 8 |
| LummaSimulator.Web/Pages/Shared/_ClickFixExercise.cshtml | 11 |
| LummaSimulator.Web/Pages/ClickFix.cshtml.cs | 24 |
| LummaSimulator.Web/wwwroot/js/passkeys.js | 35 |
| LummaSimulator.Web/wwwroot/js/clickfix.js | 57 |
| LummaSimulator.Web/wwwroot/css/site.css | 121 |
| LummaSimulator.Web/Resources/Localization/UiText.resx | 1270 |
| LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx | 1270 |
| LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx | 1270 |
| tests/LummaSimulator.Web.Tests/Program.cs | 143 |
| tests/LummaSimulator.Web.Tests/LocalizationChecks.cs | 52 |
| tests/LummaSimulator.Web.Tests/Phase65eChecks.cs | 28 |
| tests/LummaSimulator.Web.Tests/PrizeRoundsChecks.cs | 36 |
| tests/LummaSimulator.Web.Tests/RoundVisitorBaseline.cs | 70 |
| tests/WebAuthn.Ux.Tests/clickfix.test.cjs | 45 |
| tests/WebAuthn.Ux.Tests/phase65e.test.cjs | 134 |
| tests/WebAuthn.Ux.Tests/responsive-browser.mjs | 52 |

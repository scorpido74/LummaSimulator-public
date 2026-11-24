# Laatste Cybertruck-demo-UI — 12 november 2026

## Scope en behoud

AGENTS.md, git status, requirements en de relevante Prize-/securitydocumentatie zijn gelezen. Bestaand lokaal werk is behouden. Geen actieve database, sessie, keyring, certificaat, firewallregel of bestaand proces is gewijzigd. Er is geen schemawijziging of afhankelijkheidsupgrade toegevoegd.

## Bezoekerspagina en echte uitslag

- Welkom [alias] en het groene aanmeldblok blijven bestaan. De registratie-/inlogsectie wordt server-side en na bevestigde browserauthenticatie verborgen; de dubbele Opnieuw aanmelden-knop is uit de pagina verwijderd. Een gemelde verlopen toegang herstelt de opties zonder zelf een WebAuthn-ceremonie te openen. Beëindigen blijft een secundaire knop. Technische verificatiedetails zijn ingeklapt.
- De echte deelname is leesbaar op /Participation, uitsluitend vanuit de bestaande Owner-/ForDemo-controle van de bezoekerscookie. De pagina vereist geen voltooiing van de fictieve oefening en toont de opgeslagen uitslag van deze deelnemer; URL-parameters kiezen geen andere deelnemer. De GET verandert geen inschrijving, prijs of uitreiking.
- YubiKey-teksten komen van de werkelijk geconfigureerde beschikbare prijstypen en, na loting, de opgeslagen prijzen van de ronde. De aanmeldroute of authenticator wordt daarvoor niet gebruikt. Zonder geschikte YubiKey-prijs staat er een generieke deelname-/uitslaglink. Een eigen, no-store statuspoll werkt de tekst na de loting bij en verbergt de link bij verlies van toegang.

## Overlay

Het blauwe oefenpaneel staat absoluut binnen de container van het groene blok (dezelfde top, breedte en hoogte). Tijdens de oefening houdt de groene kaart onzichtbaar haar ruimte vast en is zij inert; de overlay is intern scrollbaar en voegt geen documenthoogte toe. Welkom staat buiten deze container.

Sluiten, Escape en afronden verbergen de overlay, herstellen het groene blok en zetten de focus terug op de startknop. Leeswachttijden worden bij sluiten opgeruimd; oudere async-responses mogen geen nieuwe tijdlijn openen. De afhandeling van Escape geldt alleen terwijl de overlay actief is; gewone browserbediening blijft beschikbaar.

De eerste volledige oefening gebruikt de bestaande toegangscontrole en idempotente voltooiing. Herhalen vraagt dezelfde geldige sessie opnieuw na en speelt uitsluitend de visuele tijdlijn af, zonder extra Choice-/Complete-POST, deelnemer of lotingsinschrijving. Afronden navigeert niet langer automatisch naar het bedankscherm: de nabespreking verschijnt op dezelfde groene kaart. De bestaande bedankpagina blijft compatibel.

De oefening heet expliciet Demo-oefening en gebruikt Prijsclaim onderbroken als fictief voorbeeld, met niet-interactief Uitvoeren / Openen: / LUMMA-DEMO-ONLY. De NL/EN-nabespreking legt tijdsdruk uit en scheidt dit van de echte loting/prijs. Er worden geen native meldingen, systeemvensters, klembord of systeembewerkingen gebruikt.

## Deelnemers, voorraad en rondehistorie

- Eén rij per deelnemer: tijdelijke alias, geregistreerde passkey, vastgelegde aanmelding, afronding en lotingsstatus/relevante reden. Het tellen gebruikt EXISTS en bestaande deelnemeridentiteiten; extra credentials maken geen extra rij. Nog niet ingeschreven bezoekers zijn bij de open ronde herkenbaar. Historie blijft na bestaande accountopschoning raadpleegbaar binnen autorisatie en bewaartermijn.
- Registratie/aanmelding in deze tabel zijn vastgelegde stappen, geen bewijs van een nu geldige sessie; dit staat bij de tabel. Een geldige sessie en toestemming blijven afzonderlijk vereist voor beschermde bezoekers- en presentatorweergaven.
- De bestaande voorraadberekening is in één helper ondergebracht en wordt ongewijzigd gebruikt door de echte CloseAsync-transactie én de voortgang. Vaste/willekeurige selectie, actieve types, Total, Reserved, Issued en eerdere toekenningen tellen hetzelfde mee als voorheen.
- Vóór sluiten staat de concrete prijsbeschikbaarheid vermeld. Prijzen uit geeft exact Prijzen zijn uitgeschakeld voor dit evenement. Ronde afsluiten en winnaar loten staat alleen bij een beschikbare prijs, gerechtigden en een sluitbare ronde. Anders staat Ronde afsluiten met reden; de bestaande minimum-deelnemersregel blijft gelden. Bij een gesloten ronde wordt de nog actieve populatie niet als de historische lotingspopulatie gepresenteerd.
- De rondekeuze maakt gesloten rondes raadpleegbaar, ook buiten de oude laatste-25-weergave. Iedere read blijft event-scoped en de retentiegrens wordt gecontroleerd. Een onbekende/onbevoegde/verlopen selectie geeft geen gegevens terug. Het aparte admin-beheer en de bestaande uitreikingsregistratie blijven behouden. Bestaande uitslagen worden niet opnieuw getrokken.

## WPF

EqualRowPanel meet de kaarten per rij en geeft elke kaart de natuurlijke maximale rijhoogte. Er zijn geen vaste kaarthoogtes ingesteld. Fasedetails staat onderaan in een aparte gridrij; het standaard WPF-expandermechanisme opent omhoog zodat de headers ook met open details onderaan blijven uitgelijnd. De bestaande kolomkeuze en scrollterugval blijven behouden.

De actieve WPF-verificatie controleert zes kaarten, gelijke hoogtes en headeronderkanten met details dicht/open/dicht. De synthetische webkoppelingstest roept dezelfde controle aan bij effectieve DIP-afmetingen voor 100% en 150%. Dit verandert Windows-schaal niet en is geen fysieke DPI-acceptatie.

## Werkelijk uitgevoerd

- Volledige expliciete Node-orchestratorset: **111 tests, 109 geslaagd, 2 bestaande PE-/publishiconcontroles overgeslagen, 0 fouten**. Nieuw/uitgebreid: sluiten/Escape/focus/herhalen, geen tweede voltooiing, herstel bij verlopen toegang, verbergen van aanmeldopties, prijslinkstatus, en de echte deelnemers-SQL met dubbele credentials, pending bezoekers en historie.
- JavaScript-syntax, XML-resources, XAML als XML, gewijzigde C# via syntaxparser en git diff --check slagen. Dit is geen Razor-/C#-typecontrole of geslaagde build.
- Start-LummaDev.bat verify met locked restore creëerde artifacts/dev-start-32a40e88bf3a467b99164e142100461e. Restore faalt op geweigerde leestoegang tot %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. Geen configuratie-, ACL- of identiteitsomweg gebruikt.
- Release-build, de bestaande regressieorchestrator en de executable Web.Tests-runner zijn in de geïsoleerde kopie geprobeerd met --no-restore; NETSDK1004 doordat project.assets.json ontbreekt. LUMMA_TEST_WEB_DLL is expliciet op de geïsoleerde portal-DLL ingesteld. Geen nieuwe WPF of portal gestart.
- De statische bronpreview-browsertest is gestart met een eigen profiel maar faalt met Isolated preview browser unavailable. Geen andere browserroute gebruikt om de blokkade te omzeilen.
- C#-tests zijn uitgebreid voor één gerechtigde met een beschikbare YubiKey-prijs en één gerechtigde met prijzen uit, plus herladen van deelnemersrijen/uitslag en daadwerkelijke eigen resultaatlink. Deze tests zijn niet uitgevoerd.
- De echte browserfixture controleert desktop/mobiel, overlaytop/breedte/documenthoogte, sluiten/Escape/focus, afronden ter plaatse, herladen en herhalen zonder extra WebAuthn of voltooiing. Deze fixture kan zonder geslaagde geïsoleerde portalbuild niet worden uitgevoerd.

Logs: artifacts/cybertruck-final-ui-b1ffe7c087be44479a348879eaf9d3b3 (node.log, focused.log, browser.log, build.log, regression.log, web-tests.log, syntax.log). Restorelog staat in de dev-start-map. De eigen bronkopie is met de laatste correcties gesynchroniseerd; dit is geen buildacceptatieclaim.

## Handmatig/open

1. Normale NuGet.Config-leestoegang herstellen en locked restore, solution-/Razor-build, .NET-regressies en de uitgebreide browserfixture uitvoeren.
2. Echte desktop- en mobiele browser: overlaygrenzen, geen extra paginahoogte, sluiten, Escape, focus, herhalen en verlopen sessie.
3. Twee echte synthetische rondes met elk één gerechtigde: prijzen uit versus één beschikbare prijs; sluiten, herladen, één deelnemersrij, stabiele uitslag en aparte uitreiking.
4. Fysieke demo-laptop tijdens de webkoppeling: Windows-schaling 100% en 150%, details open/dicht en tekst-/headeruitlijning. Telefoon/QR en de native passkeydialoog blijven eveneens fysieke acceptatiepunten.

Geen volledig fysiek scenario is als geslaagd gemarkeerd.

## Gewijzigde bestanden en regels

| Bestand | Regel |
|---|---|
| LummaSimulator.Web/Pages/Demo.cshtml | 22 |
| LummaSimulator.Web/Pages/Demo.cshtml.cs | 12 |
| LummaSimulator.Web/Pages/Participation.cshtml | 1 |
| LummaSimulator.Web/Pages/Participation.cshtml.cs | 9 |
| LummaSimulator.Web/Program.cs | 305 |
| LummaSimulator.Web/Services/Prizes/PrizeRoundsService.cs | 298 |
| LummaSimulator.Web/Pages/Presenter/Events/Index.cshtml | 6 |
| LummaSimulator.Web/Pages/Presenter/Events/Index.cshtml.cs | 14 |
| LummaSimulator.Web/Pages/Shared/_EventRoundProgress.cshtml | 6 |
| LummaSimulator.Web/Pages/Shared/_ClickFixExercise.cshtml | 2 |
| LummaSimulator.Web/wwwroot/js/clickfix.js | 12 |
| LummaSimulator.Web/wwwroot/js/passkeys.js | 34 |
| LummaSimulator.Web/wwwroot/js/participation-status.js | 2 |
| LummaSimulator.Web/wwwroot/js/event-round-progress.js | 19 |
| LummaSimulator.Web/wwwroot/css/clickfix.css | 40 |
| LummaSimulator.Web/Resources/Localization/UiText.resx | 1297 |
| LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx | 1297 |
| LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx | 1297 |
| LummaSimulator.App/MainWindow.xaml | 76 |
| LummaSimulator.App/EqualRowPanel.cs | 5 |
| verification/Program.cs | 144 |
| verification/EventParticipantsUiChecks.cs | 37 |
| tests/LummaSimulator.Web.Tests/PrizeRoundsChecks.cs | 69 |
| tests/LummaSimulator.Web.Tests/clickfix-browser.mjs | 100 |
| tests/WebAuthn.Ux.Tests/clickfix.test.cjs | 86 |
| tests/WebAuthn.Ux.Tests/passkeys.test.cjs | 427 |
| tests/WebAuthn.Ux.Tests/phase65e.test.cjs | 170 |
| tests/WebAuthn.Ux.Tests/responsive-browser.mjs | 52 |

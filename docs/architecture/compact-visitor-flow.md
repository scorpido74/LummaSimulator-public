# Compacte bezoekersflow en aanmeldopties

Implementatiecheckpoint 11 november 2026. Bronwijzigingen aanwezig; .NET-build en operationele acceptatie nog geblokkeerd. Bestaand lokaal helper-/sessiedemowerk is behouden. Dit document beschrijft implementatie en werkelijk uitgevoerde controles, geen wijziging van toekomstige requirements.

## Gedrag

- WPF gebruikt inhoudsafhankelijke ATT&CK-kaarten in een WrapPanel zonder de oude minimumhoogte van 238 DIP. Marges en padding zijn kleiner. De presentatorbevestiging begint op laptopbreedte naast de bediening, bovenaan de werkruimte; onder 700 beschikbare DIP volgt die onder de kaarten. Andere tabbladen krijgen de volledige breedte. De oude aanduiding VERSIE/VERSION 0.4 is uit beide taalcatalogi verwijderd.
- De deelnemersintro en illustratie zijn compacter; bij een lage viewport krimpt de illustratie. De aanmeldkaarten vullen de beschikbare kolommen op basis van het aantal routes; mobiel staan ze onder elkaar.
- Evenement beheer biedt Aanmeldopties aan eigenaren/beheerders met bestaande server-side evenementrechten. YubiKey, Windows Hello, Mijn telefoon en iPad hebben onafhankelijke schakelaars en NL/EN-uitleg over benodigdheden en het vervolg. De wijziging gebruikt bestaande antiforgery, exacte Origin en audit. Browserkeuze en WebAuthn-verificatie blijven ongewijzigd: een portalroute bewijst geen authenticatortype.
- Zonder opgeslagen instelling blijven alle vier routes beschikbaar. Dat geldt ook voor de openbare demo zonder evenement. Een masker van nul toont de hulptekst. Nieuwe toelating wordt gecontroleerd op Index, opnieuw bij het binden aan het evenement, en bij Nieuwe bezoeker. Hervatten van een bestaande demo wordt niet door route-instellingen geblokkeerd; bestaande account-, evenement-, sessie- en authenticatieregels blijven gelden.
- Na geslaagde WebAuthn-verificatie navigeert de browser naar `/SignedIn`. Deze pagina controleert de werkelijke IdentitySession, toont de tijdelijke naam (of bestaande rondealias) en de expliciete uitnodigingsknop met label Demo-oefening. Registratie alleen start geen oefening.
- Na een beveiligde start-POST vanaf de knop op `/SignedIn` toont diezelfde pagina automatisch een fictieve waarschuwing rechtsonder, daarna een mock-up van Uitvoeren linksonder, fictieve tekst en de gesimuleerde uitkomst. Leestijden: 10 + 10 + 10 + 6 seconden. De vensters passen binnen de browserviewport en gebruiken geen modaliteit, fullscreen, toetsenbordonderschepping, echt klembord of uitvoerbare opdracht. Navigeren blijft mogelijk. Herhaald initialiseren start geen tweede tijdlijn.
- Na de laatste stap volgt een antiforgery-/Origin-beveiligde voltooiings-POST en automatische navigatie naar `/ClickFixComplete`. De server vereist een geldige bezoekersauthenticatie, een begonnen oefening en minimaal 32 seconden sinds de start. Voltooiing is idempotent, blijft bij herladen behouden en is aan het demoaccount gebonden. De afsluitpagina kan niet via alleen een directe URL een oefening voltooien. De status is browserdemo-evidence, geen gemeten endpointactiviteit of MDR-respons.
- De bestaande optionele, rungebonden WPF-melding `ClickFixFollowSimulated` wordt aan het einde van de automatische visuele flow verzonden. Dat is geen bewijs van een handmatige keuze of echte Windows-opdracht. Het uitblijven van deze optionele melding blokkeert de zelfstandige oefening niet.

## Opslag en compatibiliteit

`VisitorFlowMigration` voegt uitsluitend `EventLoginRoutes` en `VisitorExercises` toe. Het route-masker (0–15) is gekoppeld aan de bestaande evenementscope; oefenstatus is gekoppeld aan het demoaccount met dezelfde verwijderlevensduur via ON DELETE CASCADE. Historische DDL, migratienummers, schemafingerprints, credentials en bestaande records blijven behouden. De bestaande schema-preflight herkent de nieuwe, exact genormaliseerde tabellen; een gedeeltelijk of afwijkend contract wordt geweigerd. Creatie gebeurt in één transactie.

Er is geen actieve database geopend of gemigreerd voor deze wijziging. Nieuwe regressies gebruiken alleen nieuwe synthetische databases en bevatten upgradebehoud, herhaalde initialisatie en geïnjecteerde rollback. Ze zijn hier **nog niet uitgevoerd** door de restoreblokkade.

Rollback naar oudere programmatuur is niet automatisch compatibel: de oude preflight kent de nieuwe tabellen niet. Voor een echte installatie is een apart, expliciet toegestaan onderhoudsplan nodig. Bewaar de consistente database/keyring/DPAPI-context; overschrijf geen actieve database. Alleen de twee nieuwe tabellen terugnemen verliest de nieuwe route-instellingen en oefenstatus en mag uitsluitend als gecontroleerde, toegestane gegevenshandeling worden beoordeeld. Er is geen rollback of opschoning uitgevoerd.

## Gewijzigde bestanden voor deze opdracht

- WPF: `LummaSimulator.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `DesktopText.resx`, `DesktopText.en-GB.resx`.
- Pagina's: `LummaSimulator.Web/Pages/Index.cshtml` en `.cshtml.cs`, `Demo.cshtml`, `ClickFix.cshtml` en `.cshtml.cs`, `Presenter/Events/Index.cshtml` en `.cshtml.cs`; nieuw `SignedIn.cshtml` en `.cshtml.cs`, `ClickFixComplete.cshtml` en `.cshtml.cs`.
- Services: nieuw `EventLoginRoutes.cs`, `VisitorFlowMigration.cs`; aangepast `DemoService.cs`, `DemoStore.cs`, `EventAccessDemos.cs` en de schema-preflight in `Services/Prizes/PrizeMigration.cs`.
- Webassets: `wwwroot/css/site.css`, `wwwroot/css/clickfix.css`, `wwwroot/js/passkeys.js`, `wwwroot/js/clickfix.js`; de drie `Resources/Localization/UiText*.resx`-bestanden.
- Tests: `tests/WebAuthn.Ux.Tests/passkeys.test.cjs`, `clickfix.test.cjs`, `branding.test.cjs`, `responsive-browser.mjs`; `tests/LummaSimulator.Web.Tests/ClickFixChecks.cs`, `ClickFixBrowserChecks.cs`, `EventEditionChecks.cs`, `EventEditionHttpChecks.cs`, `Phase65eChecks.cs`, `clickfix-browser.mjs`; `verification/Program.cs`.

## Werkelijke verificatie

- `Start-LummaDev.bat` uitgevoerd: geweigerd omdat het ontwikkelstartvenster al actief is. Geen bestaande processen gestopt en geen bestaande portal aangeroepen.
- Nieuwe geïsoleerde bronkopie: `C:/src/LummaSimulator\artifacts\visitor-flow-232821a690064ee8bde119f1c702f5aa\source`. Inclusief de drie helperprojecten en actuele lokale bronnen. Dit is een verificatiekopie, geen geslaagde dev-start. De finale bronversie is na de verdere testwijzigingen opnieuw naar deze kopie gesynchroniseerd.
- Locked restore met .NET SDK 10.0.403 en `RuntimeIdentifiers=win-x64` geprobeerd: geblokkeerd door ontbrekende leestoegang tot `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. Geen configuratie, identiteit, ACL of restorebeleid gewijzigd om deze blokkade te omzeilen. Daardoor geen .NET-build, .NET-regressieorchestrator, nieuwe portal of WPF gestart.
- De volledige expliciete Node-set uit de bestaande regressieorchestrator: **88 tests, 86 geslaagd, 2 bestaande PE-/publishiconcontroles overgeslagen, 0 fouten**. Log: `node-regression.log` in de genoemde artifacts-map. Inclusief NL/EN-flow, verificatie vóór navigatie, automatische leestijdstappen, dubbele initialisatie, navigatieannulering en voltooiingsretry.
- XAML en de WPF-/portalresources als XML geparseerd. JavaScript van de bijgewerkte browserfixtures syntactisch gecontroleerd. `git diff --check` geslaagd.
- Een aanvullende wildcard-Node-run omvatte ook de afzonderlijke `monitor-origin.browser.test.cjs`: de geïsoleerde headless browser beëindigde niet normaal binnen de fixturetermijn. Die browsercontrole is niet als geslaagd gerapporteerd. De expliciete orchestratorset hierboven slaagt.
- Nog uit te voeren: C#-compilatie; synthetische migratie-/rollback-, autorisatie-, directe-link-, route-/hervat- en HTTPS-flowtests; werkelijke browserflow van aanmelding tot afsluiting; mobiele visuele inspectie; fysieke demo-laptop met Windows-schaal 100% en 150%. De bijgewerkte browser- en WPF-fixtures bieden viewport-/DIP-controles, geen bewijs van echte Windows-DPI-, fysieke authenticator- of productie-TLS-acceptatie.

## Aanvullende verificatie 11 november 2026

Zie [lokaal demonetwerk en scenarioverificatie](cybertruck-local-network-acceptance.md) voor de actuele gebruiksroute, handmatige prijsuitslag, bronkopie, bouwblokkades en nog open fysieke acceptatie. Oudere verificatiecijfers hierboven blijven historische resultaten.

# Fase 6.5 — implementatie en overdracht

Bijgewerkt op 10 november 2026. Dit document beschrijft de opgeslagen ontwikkelbron, niet een vrijgegeven of operationeel geaccepteerde versie. Geen deployment, actieve portalstart of actieve databasemigratie uitgevoerd.

## Status

De centrale autorisatielaag voor de drie eventtypen, de versioneerde schema-uitbreiding, workspace, uitnodigingen/overdracht, monitorcapabilities, configuratie en first-runroute zijn geverifieerd met de volledige regressieorchestrator. De eerdere NuGet-toegangsblokkade van 6 november is historisch; op 10 november slaagden build en tests in de toegestane werkomgeving.

De groene v0.7/ClickFix-baseline staat in commit `99f738f`. De volledige regressie voor deze rechtenstap slaagde onder `V07Issue1_20261110`: Web.Tests 1228 checks; Node 79 geslaagd, 0 mislukt en 2 overgeslagen; SmokeTests en Windows Verification slaagden; orchestrator exitcode 0. De expliciete `LUMMA_TEST_WEB_DLL` wees naar de Web-host-DLL uit die configuratie. De volledige run gebruikte nieuwe configuratie-output en testdata. Dit blijft geen vrijgave of operationele acceptatie.

## Bestaande architectuur en keuze

- ExperienceEvents/VisitorCounts: bestaande experiencecounters in SQLite; historisch één globaal actief experience-event.
- PrizeEvents: eigen lifecycle Draft/Open/Closed/Purging, configuratiesnapshots, idempotentie, voorraad, audit en strikt schemafingerprint.
- Event Edition: eigen versleuteld manifest via EventEditionStore, geplande tijden, expliciete portal/WPF-runbinding en readcodes.
- Demos: kortlevende bezoekersdemonstraties; DemoRunId is een correlatiereferentie, geen authenticatie.
- PresenterAccounts/Keys/AuthSessions: bestaande FIDO2-verificatie met vereiste user verification, actieve server-side sessies en eenmalige herbevestiging.
- Monitor: bestaande integratiesleutel, loopback/TLS, MonitorGrants en actieve WebAuthn-demosessie. Event Edition blijft een afzonderlijk contract.

Deze modellen worden niet kunstmatig samengevoegd. De authority gebruikt `(Kind, SourceId)` met experience/prize/edition. Bestaande bron-ID's en manifests blijven behouden. PresenterEventScopes bevat alleen een autorisatiereferentie, naam en primaire eigenaar.

## Centrale autorisatie

EventAccess.Require/Allowed controleren de daadwerkelijke accountrol en geldige server-side sessie. Administrator mag alle geregistreerde scopes bedienen; iedere geauthenticeerde presenter mag een event aanmaken. De maker krijgt het eigenaarschap. Presenters zien hun eigen en geaccepteerde gedeelde events; leden behouden hun bestaande gedelegeerde toegang zonder eigenaar te worden.

Huidige uitnodigingsregels: alleen een administrator mag een nieuw presenteraccount met eventgebonden enrollment uitnodigen. De eigenaar of een geaccepteerd eventlid mag een reeds bestaand, actief presenteraccount voor dat event uitnodigen. De ontvanger moet met de eigen passkey accepteren; een pending uitnodiging geeft nog geen toegang. De issuerrol wordt opnieuw gecontroleerd bij enrollment redemption, zodat oude of ingetrokken niet-admincodes de administratorgrens niet omzeilen.

Voor Prize-events mag uitsluitend de exacte primaire eigenaar de eventconfiguratie/prize policy wijzigen. Een administrator zonder eigenaarschap krijgt geen algemene admin-bypass voor `Configure`; een administrator die zelf eigenaar is mag dit wel. Rename en overige handelingen behouden hun bestaande scope- en beheerregels. De HTTP-pagefilter weigert directe non-owner configure-POSTs; de transactionele Prize-store controleert eigendom opnieuw.

Onbekend historisch eigenaarschap wordt NULL: uitsluitend administrators zien/bedienen het totdat een administrator na WebAuthn-herbevestiging gecontroleerd een actieve eigenaar met passkey aanwijst. Het systeemevent Open demo blijft administrator-only en kan niet worden overgedragen. Deactiveren van een eigenaar wordt zowel in de service als met een databasetrigger geweigerd. Laatste-administrator- en laatste-passkeybeveiligingen blijven bestaan.

## Toegangspaden

| Pad / component | Controle en gedrag |
|---|---|
| /Presenter, /Presenter/Index GET | Bestaand globaal overzicht alleen voor administrator; normale presentator naar eventworkspace |
| Legacy Event/Rename handlers | Globale acties administrator-only; scoped actions lopen via EventAccess |
| Legacy Monitor/End handlers | DemoContext plus actuele actor/eventrechten; monitorcapability per actor en sessie |
| /Presenter/Events | Gefilterd dashboard, uitnodigingen en overdrachtsaanbiedingen; geen globale visitorlijst |
| /Presenter/Events?kind=...&id=... | Scope verplicht; onbekende/niet-toegestane scope 404 |
| /Presenter/Events?handler=Export | Dezelfde scopecontrole; JSON met uitsluitend publieke demo-ID, fictieve naam en aantallen |
| /Presenter/Prizes en /New | Bestaande WebAuthn-policy; overzicht vóór paginering op rechten gefilterd; maker eigenaar |
| /Presenter/Prizes/Event/{id} | Centrale paginacontrole en transactionele repositorycontrole; present/present versus owner/manage |
| Prize Rename | Bestaande eventbeheerautorisatie; versiecontrole blijft gelden |
| Prize Configure / Prize policy | Alleen exacte eventeigenaar; geen administrator-bypass zonder eigenaarschap |
| Prize SaveProfile | Bestaande eventtoegang plus administratorrol |
| Prize creation retry | Hercontrole eventrecht vóór teruggeven van eerder aangemaakt event |
| /Presenter/EventEdition | List/Create/Status/Link via bestaande service plus scopecontrole; recovery administrator-only |
| /api/event-edition/poll/{wpfRunId} | Bestaande lokale readcode en runbinding plus actuele actor/sessie/eventlidmaatschap |
| /ClickFix en Choice | Bestaande visitor-auth, ticket, antiforgery, Origin; gekoppelde actor/eventtoegang opnieuw geverifieerd |
| /api/local/session-status/{DemoRunId} GET | Integratiesleutel, loopback/TLS, rungebonden capability, actuele actor/sessie/lidmaatschap, live event, grant en actieve demosessie |
| Dezelfde route HEAD | Verifieert alleen integratiesleutel/protocol; levert geen metadata of eventtoegang |
| /Presenter/Accounts | Bestaande administratorpolicy plus transactionele actorcontrole en herbevestiging |
| /Presenter/Security | Eigen keys/sessies; vreemde sessionref/keyref geweigerd; laatste key behouden |
| /Presenter/Audit, /AuditExport | Globale informatie administrator-only; identity-exporter zelf ongewijzigd |
| /Presenter/Configuration | Administrator-only, wijzigingen met verse herbevestiging, antiforgery en exacte Origin |
| /Presenter/Integrations/Monitor | Bestaand 6.4-sleutelbeheer; bestaande hash/DPAPI/eenmalige uitgifte behouden |
| /Presenter/Setup?handler=Begin | Alleen echte nieuwe installatie, direct loopback, HTTPS, exacte Origin en antiforgery; geen bestaande geactiveerde identiteit |
| /Presenter/Onboarding | Administrator-only; normale presenterpagina's geblokkeerd voor nieuwe onvoltooide installatie |
| Visitor WebAuthn/identity/Sessions/Complete | Bestaande eigen visitor-sessiegrenzen; geen presentercredentials als vervanging |
| /api/events, /api/experience/statistics, /api/prizes | Geen nieuwe publieke managementroutes toegevoegd |
| /branding/logo/{slot} | Alleen canonical PNG voor organization/presenter/visitor; branding is bewust publiek |

De pagefilter geeft beveiligde native presenterformulieren Referrer-Policy: same-origin. Origin blijft een exacte allowlistvergelijking: ontbrekend, extern en letterlijk null worden nooit vertrouwd. Prize New behoudt zijn eigen Origin-diagnostiek; de legacy monitorhandler behoudt zijn PRG-foutmelding. Beide controleren zelf dezelfde exacte allowlist.

## Migratie (broncode; nog niet op actieve data uitgevoerd)

EventAccessMigration is een afzonderlijke, exacte schema-uitbreiding. PrizeMigration erkent de specifieke verwachte objecten; geen prefix-wildcard. Het oude Prize-schemafingerprint en historische migraties worden niet herschreven.

Nieuwe tabellen:

1. PresenterEventMigrations — marker 1 voor SQLite; marker 2 voor gecontroleerde manifestimport.
2. PresenterEventScopes — typed source-ID en nullable primaire eigenaar.
3. PresenterEventMembers — geaccepteerde leden per typed event.
4. PresenterEventInvites — beoogde ontvanger/uitgever, vervaltijd en optionele enrollmenthash.
5. PresenterEventTransfers — één tijdelijk overdrachtsaanbod per event.
6. PresenterDemoContexts — expliciete typed context per demo.
7. PresenterMonitorCapabilities — uitsluitend tokenhash, demo/event/actor/sessionhash en vervaltijd.
8. PresenterPresentationSettings — cosmetic/default/proposed-hostingprofiel met revisie.
9. PresenterLogos — begrensde canonical PNG-blobs per slot.
10. PresenterInstallation — bestaande/nieuwe installatie, hostingcontrole, eerste event en voltooiing.
11. PresenterSessionRefs — niet-authenticerende publieke refs voor eigen sessiebeheer.

Drie triggers blokkeren verwijderen/resetten van installatievoltooiing en deactiveren van eventeigenaren. De uitbreiding heeft 14 exacte schemaobjecten.

BEGIN IMMEDIATE omvat DDL, bestaande SQLite-contextmapping, installatieclassificatie en marker 1. Een fout rolt die transactie terug. De globale OneActiveExperienceEvent-index vervalt, zodat expliciete onafhankelijke events tegelijk actief kunnen zijn; ExperienceStatistics maakt hem bij een gemigreerde herstart niet opnieuw. Oude visitors worden op bestaande VisitorCounts gemapt, anders Open demo. Nieuwe generieke bezoeken krijgen geen impliciete toegang tot nieuw toegewezen events: gebruik de eventbezoekerslink.

Event Edition staat in een ander opslagmedium. De beschermde manifestimport schrijft onbekende owners als NULL; marker 2 volgt pas na succesvolle import. Geen verzonnen distributed transaction: een fout verhindert serverstart en kan idempotent worden hervat. Een bestandsschrijffout bij nieuw Event Edition-event kan een ongebruikte autorisatiescope achterlaten; niet automatisch verwijderen/herstellen.

## Uitnodigingen, eigenaarschap en monitor

Bestaande presentator: eigenaar of geaccepteerd eventlid selecteert een actief account; invite is 24 uur geldig. Ontvanger logt met eigen passkey in en accepteert of weigert. Alleen een administrator kan een nieuw presenteraccount via eventgebonden enrollment uitnodigen: random 32-byte Base64URL-code, alleen hashopslag, 10 minuten geldig, uitsluitend een nieuwe Presenter-identiteit. Na registratie en afzonderlijke login volgt expliciete eventacceptatie. Uitgeverbevoegdheid wordt ook bij redemption hergecontroleerd. Intrekken verwijdert nog ongebruikte pending enrollmentbevoegdheid. Een enrollmentinvite kan geen actieve of eerder geactiveerde identiteit vervangen.

Overdracht vereist verse herbevestiging bij aanbod én acceptatie; maximaal 24 uur en alleen geaccepteerde actieve leden als beoogde opvolger. Administrator kan onbekend eigenaarschap herstellen. Rechten worden bij gebruik en acceptatie opnieuw gecontroleerd.

De integratiesleutel blijft integratie-identiteit, geen eventrecht. De presenter geeft daarnaast een eenmalig zichtbare capability voor exact één portalrun, eigen actor en authsessie; maximaal 29 minuten. WPF bewaart die uitsluitend in geheugen en verstuurt X-Lumma-Monitor-Grant. Wisselen van portalrun of verwijderen/vervangen van integratieconfiguratie wist de capability. Bestaande sleutel blijft DPAPI CurrentUser. Geen nieuwe listener of browser-naar-WPF-API. Oude GET-clients zonder capability krijgen 404; HEAD en Event Edition-polling blijven afzonderlijke protocollen.

## UI/configuratie en first-run

Bestaande donkere layout behouden; dashboard gaat naar /Presenter/Events. Eigen events, selectie, echte actuele demoaantallen, people en beperkte activiteit worden getoond. Configuratie ondersteunt organisatienaam, displaynaam, NL/EN-default, nieuwe-eventdefaults, voorstel voor HTTPS-basis-URL en drie PNG-logo-slots. Geen RP-ID/origin/certificaatwijziging door een cosmetic update. De voorgestelde URL wordt opgeslagen maar niet geactiveerd.

Een echt nieuwe DB krijgt Existing=0. Oude bestanden/installaties worden niet opnieuw onboarded. Ontbrekende DB naast bekende lokale beschermde bestanden veroorzaakt een fout, geen reset; dit is een aanvullende guard en geen volledige inventarisatie van alle extern overschreven runtimepaden. De bestaande lokale bootstrap blijft beschikbaar als onderhoudsprocedure. De nieuwe lokale Setup-Begin is alleen voor nieuwe installaties; één live autorisatie tegelijk; een verlopen, nooit geactiveerde/keyloze poging kan veilig worden hervat. Eerste admin vereist bestaande FIDO2-registratie en afzonderlijke login. Backupkey via Mijn account. Organisatie via Configuratie, hosting met effectieve HTTPS/origincontrole én expliciete operatorcontrole van browsercertificaat, eerste Experience-event atomair, finish na herbevestiging en verplichte checks.

## Bestanden van deze vervolgstap

Nieuwe services: EventAccess.cs, EventAccessDemos.cs, EventAccessPeople.cs, EventAccessActivity.cs, EventAccessMigration.cs, PresenterEventAccounts.cs, PresenterPageAccessFilter.cs, PresenterOnboarding.cs, PresentationSettings.cs, SafeLogo.cs. Nieuwe Razor-paden: Presenter/Events/Index en Presenter/Onboarding. Nieuwe scripts: event-invitations.js en invite-enrollment.js.

Integratie: Program.cs, DemoStore.cs, DemoService.cs, ExperienceStatistics.cs, EventEditionService.cs, PresenterAuthentication.cs, PresenterManagement.cs, PresenterEndpoints.cs, PresenterOwnAccount.cs; PrizeAuthorization/PrizeStore/PrizeEventStore/PrizeMigration; Index, Setup, Enroll, presenter Index/Security/Configuration en beide layouts; UI-resourcecatalogi. WPF: SessionMetadataClient.cs, MonitorIntegrationSettings.cs, SessionMonitorViewModel.cs, MainWindow.xaml(.cs), DesktopText-catalogi.

Tests: nieuw EventAccessChecks.cs, uitgebreid PresenterChecks/HttpChecks/MonitorGrantHttpChecks/EventEditionChecks/EventEditionHttpChecks/PrizeEventChecks/PrizeEventHttpChecks/PrizeStoreChecks/Program.cs; MonitorIntegrationChecks smoke; branding.test.cjs en presenter-shell.test.cjs. Een deel van deze bestanden bestond al als niet-gecommitte eerdere wijzigingen; git status is geen exclusieve lijst van deze vervolgstap.

## Nog af te ronden / hervatpunt

1. De huidige .NET-/HTTP-/Node-/Smoke-/WPF-regressies zijn uitgevoerd op synthetische, geïsoleerde data; geen actieve installatie is gestart of gemigreerd.
2. De HTTP-matrix dekt directe requests met Administrator, eigenaar, bestaand gedeeld lid en presenter zonder toegang. Nieuwe invites, acceptatie, policyweigering en onveranderde state zijn getest.
3. De wizard maakt momenteel alleen een eerste Experience-event; typekeuze voor een eerste Prize/Event Edition is nog niet geïmplementeerd. Na onboarding zijn die via bestaande pagina's/nieuwe workspace beschikbaar.
4. Geen automatische certificaat-chaininspectie of hosting/RP-ID-migratie; geen automatische WPF-logo-sync. Dit vereist gecontroleerde operationele procedures.
5. Expliciete eventverwijdering/pauzeren is niet toegevoegd; bestaande close/historysemantiek behouden. Herstel van een account zonder enige eigen authenticator wordt niet met een overnamegevoelige enrollmentlink aangeboden.
6. Bij eigendomsoverdracht vervallen eventinvites, maar een reeds afgegeven enrollmentgrant kan nog een uitsluitend ongekoppeld Presenter-account registreren tot zijn korte vervaltijd; eventacceptatie is daarna onmogelijk. Beoordeel verdere intrekking als vervolghardening.
7. Nieuwe invitation/monitor/transfer-races, bootstraponderbreking en echte HTTP-grenzen verder uitbreiden; geschreven tests zijn geen volledige runtimeacceptatie.
8. Visueel responsief/DPI, fysieke YubiKeys, twee parallelle WPF-contexten en distributiecompatibiliteit handmatig verifiëren.

Dit is een concrete code-overdracht, geen claim dat alle gevraagde fase-6.5-acceptatiecriteria al zijn gehaald.

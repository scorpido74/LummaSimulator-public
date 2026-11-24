# Fase 5 - Prize Experience: implementatieplan

Status: werkpakketten 5.1 en 5.2a afgerond en getest; 5.2b-5.6 blijven plan en wachten op een nieuwe expliciete opdracht.
Datum: 30 oktober 2026.
[Functionele eisen](phase5-prize-experience-functional.md) | [architectuur](phase5-prize-experience-architecture.md) | [roadmap](LummaSimulator-v0.5.md).

## Uitvoeringsafspraken

De zes pakketten zijn afzonderlijke review-/acceptatie-eenheden en volgen 5.1 → 5.2 → 5.3 → 5.4 → 5.5 → 5.6. De module blijft standaard uit en publiek onbereikbaar tot alle noodzakelijke veiligheidsvoorzieningen actief zijn. Pakketten 5.2-5.4 mogen geen live deelnemersaccounts verzamelen voordat de volledige retentie-/herstel-/claimketen van 5.5 werkt. Gebruik featuregate en uitsluitend synthetische testdata voor tussenresultaten.

Per pakket: eerst relevante bron opnieuw lezen, wijziging klein houden, eigen tijdelijke testdatabase en aparte build-output, relevante runners uitvoeren, resultaten/risico's documenteren en stoppen bij de pakketgrens. Geen live migratie, serverrestart, package-installatie of deployment impliciet in een implementatieopdracht opnemen. WPF-productiecode blijft buiten scope. Huidige testconventies behouden: eigen executable assertions, SoftwareAuthenticator/TestClock, echte HTTPS-childserver, Node node:test, smoke/WPF-runners. Geen nieuw testframework nodig.

Complexiteit is relatief (M/L/XL), geen tijdsbelofte. Iedere pakketoplevering noemt gewijzigde bestanden, feitelijk uitgevoerde checks, resterende handmatige acceptatie en rollbackimpact.

## 5.1 - Architectuur en opslagfundament

**Doel:** behoudend opslagfundament, types, transacties en migratie; nog geen deelname-/trekkingroutes.

**Bestaande bestanden:** LummaSimulator.Web/Services/DemoStore.cs (migratiehook), Properties/TestAccess.cs waar nodig, tests/LummaSimulator.Web.Tests/Program.cs. Geen wijzigingen aan bestaande WebAuthn-verificatie.
**Nieuw:** Models/PrizeModels.cs; Services/Prizes/PrizeMigration.cs, PrizeStore.cs, PrizeAuthorization.cs; tests/LummaSimulator.Web.Tests/PrizeMigrationChecks.cs, PrizeStoreChecks.cs. Exact schema conform A03; vaste migratieversie 6.
**Afhankelijkheden:** huidige marker-5-baseline, bestaande Microsoft.Data.Sqlite en TimeProvider. Geen nieuwe packages.

Implementatiestappen:
1. Inventariseer schema/markers, voeg invarianten en statusovergangen als expliciete types toe; onderscheid eisen en ontwerpkeuzes.
2. Bouw transactionele migration/preflight met immutable profielsnapshots, seeds en installatievoorkeur.
3. Voeg eventgebonden tabellen, samengestelde FK's, unieke/partiële indexen, CHECKs en audit/purgebegrenzing toe.
4. Bouw transaction helper met één connection/tx, parameterbinding, CAS, idempotentierecord en bounded busyafhandeling.
5. Koppel migratie na bestaande initialisatie en vóór cleanup; feature blijft uit. Schrijf onderhouds-/backup-/rollbackhandleiding.

**Beveiliging:** geen foreign_keys=OFF; geen secrets/PII in foutuitvoer; geen gewijzigde accounts/keys; onbekend schema fail-closed; autorisatiehelper hergebruikt server-side actorvalidatie zonder rechtenverbreding.
**Testgevallen:** migratie van synthetische huidige en oudere ondersteunde schema's; bytevergelijking accounts/keys/counters/handles vóór/na; twee initialisaties; actieve ceremonies behouden; foutinjectie midden migratie/voor marker; onverwachte kolommen/index/triggers/FK; foreign_key_check; cross-event FK-insert afgewezen; negatieve stock/capacity; geen tweede Initial/replacement; profielseed overschrijft wijzigingen niet.
**Acceptatiecriteria:** volledige migratie rollbackt bij fout, idempotent, bestaande tabelleninhoud onveranderd, marker correct, baseline demo-/presentatortests groen, geen nieuwe publieke Prize-routes, ontwerp/documentatie bijgewerkt.
**Risico's:** initialisatierace, markerconflict, cyclische vaste-prijs-FK, incompleet rollbackpad. **Complexiteit: L.**

## 5.2 - Evenementen, profielen en deelnemers

**Doel:** snelle start, afzonderlijke evenementen, profielen, start-/groepscodes en capaciteit; opt-in-accountbinding voorbereiden.

**Bestaande bestanden:** Program.cs, Configuration/ExperienceSettings.cs, Services/DemoService.cs (alle lifecyclepaden), Services/SessionStatusService.cs (monitorverwijdering), Services/PresenterAuthentication.cs of gerichte gedeelde actorhelper, Pages/Presenter/Index.cshtml(.cs), Resources/Localization/UiText*.resx, wwwroot/css/site.css.
**Nieuw:** PrizeEventService.cs, PrizeProfileService.cs, PrizeParticipationService.cs, PrizeEndpoints.cs; Pages/Presenter/Prizes/Index/Event/New/Profile/Groups.cshtml(.cs), Pages/Prizes/Event.cshtml(.cs), wwwroot/js/prizes.js; PrizeEventChecks.cs, PrizeParticipationChecks.cs en Node prize-tests.
**Afhankelijkheden:** 5.1, bestaande bezoekersassertion/sessionbinding; live-enable blijft geblokkeerd tot 5.5.

Implementatiestappen:
1. Bouw PrizeManage policy en dashboardsectie met expliciete PrizeEventId, zonder ExperienceStatistics of OneActiveExperienceEvent te verbouwen.
2. Bouw snelle start en vier profielen, transactiesnapshot en installatiebrede last-profile bij commit; gescheiden basis/geavanceerd.
3. Implementeer configuration freeze, openbaar→code-requiredvalidatie, enable/disable en open/close/reopen.
4. Implementeer code-uitgifte/hashes en join/cancel, nummering, groepscodes, conditionele Occupied, automatische/handmatige wisseling.
5. Voeg retentiebinding voor alle deelnames toe aan idle/owned/end/presenter-end/new-visitor; aparte accountcap en sessieverificatie. Beveilig bestaande demo-eindknop met duidelijke tekst voor behouden accounts.
6. Voeg audit/revisie/outboxrecords toe, nog geen werkelijke realtimepresentatie.

**Beveiliging:** exacte Origin/CSRF, per-account/IP/installatielimieten, geen codes in URL/log; deelnameaccount altijd uit geverifieerde sessie; geen client AccountId vertrouwen.
**Tests:** twee gelijktijdige events; 100 joinpogingen/15 capaciteit met onafhankelijke connections; zelfde account dubbel/na annulering; code-race/expiry/rollback; verkeerde groepscode/event; vol geeft geen codeverbruik; meerdere presentatoren profielselectie; gewijzigde profielen veranderen bestaande events niet; alle deletepaden respecteren binding; 200 tijdelijke plekken en bewaarde accounts apart.
**Acceptatie:** Cybertruck exact configureerbaar; snelle start toont link/QR-placehouder tot QR-component in 5.4; volledige deelnameconcurrentietests groen; oude statistiekevenementen en rechten buiten module behouden; geen publieke productie-opt-in vóór 5.5.
**Risico's:** tijdelijke accountcap, vergeten deletepad, verwarring VisitorNumber versus participantnummer, openbaar/codevrije default. **Complexiteit: XL.**

## 5.3 - Trekking, prijzen en voorraad

**Doel:** veilige directe winst en groepstrekking, atomaire reservering, immutable awards en basisclaimtoestanden.

**Bestaand:** PrizeEndpoints.cs, PrizeModels.cs, PrizeStore.cs, Prize-dashboardpages.
**Nieuw:** PrizeDrawService.cs, PrizeInventoryService.cs, PrizeClaimService.cs (status/stockkern), PrizeDrawChecks.cs, PrizeInventoryChecks.cs, PrizeConcurrencyChecks.cs.
**Afhankelijkheden:** 5.1-5.2; geen live uitgifte vóór 5.5.

Implementatiestappen:
1. Implementeer kans a/b met CSPRNG en historisch max M; persist één InstantDecision per participant.
2. Bouw freeze/snapshot, uniforme selectie zonder terugleggen, aantal-/stockvalidatie.
3. Bouw vaste/uniforme prijstypekeuze met conditionele reservering en ledger.
4. Sla alle awards/revealvolgorde/tijden en Pending claims vóór response op.
5. Implementeer Forfeited/Redeemed/Expired transitioncore; replacement alleen voor expliciet Forfeited award, ooit gewonnen blijft uitgesloten.
6. Voeg idempotentie, businessunique guards en audit in dezelfde transactie toe.

**Beveiliging:** geen System.Random/clientseed; geen wijzigen van vastgelegde awards; geen stockrestitutie na Redeemed; geen verborgen winnaars in DTO/log/export.
**Tests:** p=0/p=1, maximum=0/bereikt, voorraad op, null voorraad; vijf winnaars zonder duplicaat; te weinig kandidaten; laatste prijs onder 50 parallelle trekkingen; tegelijk draw/draw en redeem/forfeit; retries zelfde/andere sleutel; rollback na award maar vóór voorraadcommit; vervallen winnaar nooit opnieuw; randomtypekans onafhankelijk van voorraad (algoritmetest, geen instabiele statistische releasepoort).
**Acceptatie:** één commit per geldige trekking; invariant Total≥Reserved+Issued; ledger/counters consistent; geen automatische hertrekking, gegarandeerde-slotvariant niet geïmplementeerd.
**Risico's:** lange writertransacties, dubbele replacements, instant maximum onterecht herstellen, gedeeltelijke multiwinnercommit. **Complexiteit: XL.**

## 5.4 - Realtime presentatie

**Doel:** servergestuurde bekendmaking met gesynchroniseerde schermen, aparte capability en herstel.

**Bestaand:** Program.cs (SignalR/afzonderlijke scopes/security), PrizeEndpoints.cs, resources/CSS/dashboard; regression csproj voor extra Node-runner.
**Nieuw:** PrizeHub.cs (of drie gescheiden hubtypes), PrizeOutboxWorker.cs, Pages/Prizes/Presentation.cshtml(.cs), wwwroot/js/prize-presentation.js, lokaal vastgezette SignalR-client/QR-assets, PrizePresentationChecks.cs, PrizeRealtimeChecks.cs, Node presentation-tests.
**Afhankelijkheden:** 5.1-5.3, T1 exact dependency-/licentiebesluit vóór toevoegen; huidige opdracht verleent geen package-installatie.

Implementatiestappen:
1. Selecteer lokale client/QR-distributie, controleer CSP/offline/licenties; browser QR voor eventlink.
2. Bouw read-only presentatiegrant, fragmentexchange, aparte sessioncookie/grants per tab/event en intrekking.
3. Bouw gescheiden hubauth/snapshots, outboxpublisher en periodieke revalidatie.
4. Bouw animaties 5/10/15 seconden; afzonderlijk/tegelijk, servertijdoffset, hervatten.
5. Voeg dashboardknop voor tweede venster en eigen notificatiekanaal toe zonder identiteitsscope te mengen.
6. Beveilig alle DTO-paden op RevealAt; geen toekomstige winners in HTML/JS/hub/export.

**Beveiliging:** connect/invocation/publicatierevalidatie, exacte WebSocket Origin, read-only capability nooit beheerpolicy, geen querytokens of publieke claimdata.
**Tests:** twee presentatoren en drie schermen; herladen halverwege; offline tot na reveal; serverkill na commit vóór publish; outbox duplicaten/gaten; achtergrondtab; intrekken en accountdisable tijdens verbinding; cross-event subscriptions; presenterlogin in bezoekersbrowser houdt CSRF correct; alle verborgen awardvelden afwezig vóór deadline.
**Acceptatie:** geen nieuwe draw na reconnect; alle clients tonen dezelfde gepubliceerde nummers; maximaal 15 seconden detectie van ingetrokken idle verbinding en geen nieuwe payload na serverintrekking; QR/link bruikbaar op vertrouwde bereikbare HTTPS-origin.
**Risico's:** cached hubprincipal, datalek door vroeg DTO, klokverschil, popupblocker, CSP-beperking, ongeteste JS-clientversie. **Complexiteit: L/XL.**

## 5.5 - Claims, persoonsgegevens en bewaartermijnen

**Doel:** volledige claimuitgifte, passkeyherstel en veilige account/eventretentie; pas daarna productie-enable kandidaat.

**Bestaand:** DemoService.cs alle lifecyclepaden, Services/PresenterService.cs CleanupWorker, Program.cs startupvolgorde/schemes; bestaande IFido2/credentialopslag.
**Nieuw:** PrizeVisitorAuthentication.cs, PrizeRetentionService.cs, PrizeClaimService.cs afronden, Pages/Prizes/Claims/Recovery.cshtml(.cs), claimafhandelingspages, wwwroot/js/prize-claims.js; PrizeClaimChecks.cs, PrizeRetentionChecks.cs, PrizeRecoveryChecks.cs.
**Afhankelijkheden:** 5.1-5.4; keyring-/backupbeleid T3 operationeel invullen.

Implementatiestappen:
1. Bouw browser-/operationgebonden discoverable bezoekersherstel; credentiallookup alleen Credentials; single-use challenge vooraf verbruiken, volledige IFido2-validatie en handle/counter/backupchecks.
2. Geef beperkte nieuwe sessie, claims per account, key toevoegen na verse eigen assertion; behoud max vijf.
3. Genereer 128-bit leesbare claimcodes, purpose-hash+DP-protected terugtooncopy; QR/leescode zelfde claim.
4. Bouw expliciete scan/resolve/bevestig/redeem, persoonsgegevens pas na RevealAt en alleen geconfigureerde velden.
5. Bouw reactie-/definitieve deadlines, handmatig verval, tokenintrekking en voorraadrelease.
6. Bouw afsluit-/heropeningsgrenzen, Purging/restartbare deletebatches, verwijdering PII/secrets/outbox/exports/audit, vrijgeven laatste accountbinding.
7. Documenteer privacytekst, backuprotatie en herstelincidenten. Bewaarde accounts nooit via oud idlepad verwijderen.

**Beveiliging:** bestaande RP/origins/verificatie intact; geen code als login; encryptie/purposebinding; PII-autorisatie; geen automatische marketing of eigendomsoverdracht; retention grens bij requests.
**Tests:** browsercookie weg, herstel met twee keys; unknown/presentercredential/lege handle/verkeerde signature/challenge/origin/RP/UV/BE/counter/replay; open claims na 29 minuten; End/NewVisitor behoudt deelnemer maar revoket sessie; redeem-race; forfeit maakt oude code ongeldig; 2-minuten geen automatische verval; 24-uur en 90-dagen hard cutoff; heropenen vóór/na grens; twee events met verschillende retentie; kill midden purge, restore vóór verkeer, keyringfout zonder nieuwe draw.
**Acceptatie:** claim precies eenmaal; alle bewaarde claims na herauth terugvindbaar; persoonsgegevens alleen nodig/geautoriseerd; geen claimdeadline na PurgeAt; niet-deelnemers oude 29-minuten/eindgedrag; tijdelijk en permanent auditbeleid gescheiden.
**Risico's:** belangrijkste privacy-/accountintegratierisico, keyringverlies, backupdata, handover op gedeelde laptop, open evenement zonder einddatum. **Complexiteit: XL.**

## 5.6 - Rapportage, exports en acceptatietests

**Doel:** complete anonieme rapportage, veilige CSV/.xlsx, end-to-end acceptatie en regressie.

**Bestaand:** tests/LummaSimulator.Web.Tests/Program.cs, tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj, resources/dashboard, README en roadmap.
**Nieuw:** PrizeReportService.cs, rapportpages, PrizeReportChecks.cs, PrizeAcceptanceChecks.cs, aanvullende Node-tests; geen .xlsx productiepackage zonder afzonderlijk besluit.
**Afhankelijkheden:** 5.1-5.5; T2 Excel-interoperabiliteit en T5 fysieke acceptatie.

Implementatiestappen:
1. Bouw snapshotrapport en eenduidige tellingen, voorraad/ledgerreconciliatie.
2. Bouw CSV en minimale echte .xlsx als ZIP/XML; streaming/downloadautorisatie.
3. Test volledige Cybertruckflow met 100 synthetische accounts/zeven groepen, instant win, meerdere events en meerdere presenters.
4. Voeg alle nieuwe checks aan bestaande runners toe; draai bestaande web/JS/smoke/WPF.
5. Voer handmatige hardware-/browser-/mobiel-/tweede-computer-/offlineacceptatie uit op geïsoleerde juiste HTTPS-opstelling.
6. Werk operatorhandleiding, migratie/rollback, privacy en bekende beperkingen bij; module alleen expliciet per event inschakelen na vrijgave.

**Beveiliging:** formule-injectie, geen verborgen winnaars of standaard PII, toestemming/doel voor PII-export, no-store, korte serverexportretentie.
**Tests:** alle voorgeschreven metrics en terminalstatussen; onbeperkte voorraad; CSV speciale tekens/newlines/formules; .xlsx celtypen/formuleafwezigheid en openen in Excel; ingetrokken presentator download geweigerd; twee events nooit vermengd; exports vóór reveal lekken niets; module uit volledige oude flow.
**Acceptatie:** alle vier regressierunners groen, 100-accountscenario aantoonbaar, PII-uitgesloten standaardexports, fysieke acceptatie apart geregistreerd met omgeving/resultaat; geen releaseclaim op basis van mocks alleen.
**Risico's:** zelfgebouwde OOXML-compatibiliteit, exportbestanden buiten serverretentie, verschillen browser/hardware, beperkte SQLite-doorvoer. **Complexiteit: L.**

## Controleplan en vrijgavepoorten

| Poort | Benodigde bewijzen |
| --- | --- |
| Opslag (5.1) | Behoudvergelijking en rollback/herhaalbaarheid, FK/integriteitschecks op testdatabase |
| Deelname (5.2) | Atomic code+capacity+accountbinding, parallelle multi-event tests |
| Trekking (5.3) | Stock/award-idempotentie en race-tests op onafhankelijke connections |
| Publicatie (5.4) | Geen vroege uitslag via enige DTO; herstart/reconnect dezelfde draw |
| Claims/privacy (5.5) | Cookievrije passkeyherstel, exact eenmaal uitgifte, deletepad/retentie-matrix |
| Vrijgave (5.6) | Hele regressie plus feitelijke fysieke/visuele/operationele acceptatie |

Concurrencytests mogen niet alleen dezelfde singleton Gate gebruiken: meerdere DemoStore/service-instanties op dezelfde testdatabase, eigen connections, gelijktijdige startbarrière. Test crashpunten vóór commit, na commit vóór response, vóór/na outboxpublish; assertions controleren database én uitgegeven respons. TimeProvider bestuurt deadlines; geen lange sleeps. RNG kan via interne interface met vast testscript voor grensgevallen, productie uitsluitend cryptografisch; geen testseed in config/HTTP.

Bij latere validatie kunnen bestaande commando's worden hergebruikt met aparte output zoals verification/bin/phase5-51-tests en LUMMA_TEST_WEB_DLL naar die build. Voeg nieuwe Node-bestanden expliciet aan de bestaande runner toe. Tijdens de oorspronkelijke specificatieopdracht zijn geen builds/runners uitgevoerd; zie hieronder voor de nu daadwerkelijk uitgevoerde 5.1-validatie.

## Oorspronkelijke vervolginstructie voor 5.1 (uitgevoerd)

> Implementeer uitsluitend werkpakket 5.1 uit docs/phase5-prize-experience-implementation.md op basis van de functionele en architectuurspecificatie. Lees eerst de actuele bron en schema-markeringen opnieuw. Voeg het status-/datamodel, PrizeStore, behoudende transactionele migratie met vaste migratieversie 6, profielseeds en synthetische migratie-/integriteitstests toe. Houd Prize Experience standaard uit en voeg nog geen functionele deelname-, trekking-, claim- of presentatieroutes toe. Wijzig geen RP ID, passkeys of WPF-code; gebruik geen live database en herstart geen draaiende server. Installeer geen packages. Test uitsluitend met aparte tijdelijke databases en aparte build-output. Rapporteer gewijzigde bestanden, feitelijke testresultaten, behoud/rollbackbewijs en resterende risico's; stop na 5.1.

## Uitvoering werkpakket 5.1 - afgerond op 30 oktober 2026

Status: opslagfundament geïmplementeerd en getest. Geen 5.2 gestart. Functionele Prize Experience blijft uit en is niet publiek toegankelijk. Webassemblyversie blijft 0.4.1.

### Herstelbaseline en wijzigingscontrole

Baseline buiten project: C:/Example/Baselines/phase5-51-20261030-210247.
124 bronbestanden, source.zip, sha256.json, consistente SQLite-onlinebackup database-0.db, database-manifest.json, keys-0 en RESTORE.txt. Archiefinhoud en oorspronkelijke bronhashes zijn bij opname geverifieerd; backup heeft integrity_check=ok en geen FK-overtredingen. De backup bevat schema 3/4/5, nul tijdelijke bezoekersaccounts/credentials en één presentatoraccount met twee keys. Geen account-, credential- of sessiegeheimen zijn in documentatie opgenomen.

Herstel: server stoppen; actuele toestand eerst apart veiligstellen; source.zip naar een nieuwe map uitpakken en sha256.json controleren; bijbehorende database en keyring volgens database-manifest.json terugzetten in oorspronkelijke Windows/DPAPI-context. Geen oude en nieuwe build tegelijk op dezelfde database. Gegenereerde bin/obj/node_modules zijn niet in het bronarchief opgenomen. Er is geen herstel of live migratie uitgevoerd.

Omdat .git ontbreekt is het wijzigingenoverzicht gebaseerd op SHA-256 tegen deze baseline, niet op een git-diff. Rapport: verification/bin/phase5-51-source-diff.json. Geen bronbestanden verwijderd; projectbestanden, lockfiles, WPF-productiecode, Program.cs, WebAuthnSettings, DemoService en SessionStatusService zijn ongewijzigd.

### Geleverde bestanden en afwijkingen

Nieuw:
- LummaSimulator.Web/Models/PrizeModels.cs
- LummaSimulator.Web/Services/Prizes/PrizeMigration.cs
- LummaSimulator.Web/Services/Prizes/PrizeStore.cs
- LummaSimulator.Web/Services/Prizes/PrizeAuthorization.cs
- tests/LummaSimulator.Web.Tests/PrizeMigrationChecks.cs
- tests/LummaSimulator.Web.Tests/PrizeStoreChecks.cs

Beperkt gewijzigd: Services/DemoStore.cs (preflight, legacy-initialisatieseam, vaste migratie 6), Services/PresenterAuthentication.cs (alleen Actor van private naar internal), tests/LummaSimulator.Web.Tests/Program.cs (checks aansluiten). Properties/TestAccess.cs hoefde niet te wijzigen; de testassembly had al toegang.

Noodzakelijke afwijking: tests/LummaSimulator.Web.Tests/ExperienceChecks.cs past één synthetische legacyfixture aan. Die verwijdert opzettelijk markering 3; de test roept nu de historische statistiekmigratie direct aan, waarna volledige startup opnieuw wordt gecontroleerd. Nieuwe startup mag een onvolledige migratiegeschiedenis niet stilzwijgend repareren. Het oorspronkelijke behoud-/importdoel van de test blijft intact.

PrizeStore levert uitsluitend interne opslag van disabled Drafts, defaultprofiellezen en een Draft-CAS-edit. Geen snelle-startservice, publieke route, deelname, code-uitgifte, trekking, claimuitgifte, SignalR, QR, export of purgeworker. PrizeAuthorization hergebruikt de bestaande transactionele Actor-controle voor beide bestaande rollen. Geen nieuwe DI-registratie of authenticatieschema.

### Databaseobjecten

Vaste versie 6 maakt **26 tabellen en 8 expliciete indexen**. Bestaande tabellen worden niet verbouwd.
- Configuratie: PrizeProfiles, PrizeInstallationSettings, PrizeEvents.
- Groepen/toelating (uitsluitend schema): PrizeGroups, PrizeParticipants, PrizeStartCodes, PrizeGroupCodes.
- Prijzen/trekking (uitsluitend schema): PrizeTypes, PrizeDraws, PrizeDrawCandidates, PrizeInstantDecisions, PrizeAwards, PrizeInventoryLedger.
- Claims (uitsluitend schema): PrizeClaims, PrizeClaimSecrets, PrizeClaimData.
- Presentatie/herstel (uitsluitend schema): PrizePresentationGrants, PrizePresentationSessions, PrizePresentationSessionGrants, PrizeVisitorSessions, PrizeVisitorCeremonies.
- Infrastructuur: PrizeOperations, PrizeCreationOperations, PrizeAudit, PrizeOutbox, PrizeSchemaContract.

Indexen: PrizeOneOpenGroup, PrizeOneInitialDraw, PrizeLedgerOnce, PrizePurgeDue, PrizeClaimsDue, PrizeOutboxDue, PrizeParticipantsAccount, PrizePresentationExpiry. SQLite maakt daarnaast eigen unieke auto-indexen voor PK/UNIQUE. De precieze DDL staat in PrizeMigration.Statements.

Preflight controleert objectdefinities en ondersteunde migratiegeschiedenis vóór eventuele legacy-initialisatie. Een gevalideerde bestaande versie-5/6-database slaat de oude initialisatie over: dit voorkomt onder meer een historische extra policyrij bij heropening. Verse/ondersteunde oude databases doorlopen eerst de bestaande upgrade. DDL, vier profielseeds, installatievoorkeur, schemacontract en marker 6 staan in één BEGIN IMMEDIATE-transactie. Onbekende objecten, ontbrekende vereiste objecten, conflicterende markers/fingerprint stoppen veilig. Heropening verandert profielwijzigingen of voorkeuren niet.

### Feitelijk uitgevoerde validatie

| Controle | Resultaat |
| --- | --- |
| Volledige regressieprojectstructuur, aparte output | Build geslaagd; 0 waarschuwingen, 0 fouten |
| Definitieve webbuild na laatste preflightwijziging | Geslaagd; 0 waarschuwingen, 0 fouten |
| Webservices, nieuwe opslagchecks en echte HTTPS/WebAuthn-integratie | **467 checks geslaagd**, exitcode 0 |
| Bestaande JavaScript bezoekers/lokalisatie/presentatoren | **43 tests geslaagd**, 0 mislukt |
| Simulator-smoke | Exitcode 0; inclusief 24 receiver-lifecyclechecks en 2778 ATT&CK/status/exportasserties |
| WPF-verificatie | **20 PASS-controles**, exitcode 0 |

Alle vijftien gevraagde controlethema's zijn uitgevoerd in de websuite: leeg/schema 5, herinitialisatie, rollback na DDL én na seeds vóór marker, schema-afwijkingen, FK/integriteit, cross-event-isolatie, onafhankelijke schrijvers, idempotente creatie en payloadconflict, vier profielseeds, Cybertruckdefault, behoud accounts/credentialbytes/sessies/statistieken en bestaande WebAuthn-regressies. Aanvullend: gelijktijdige initialisatie, conflicterende schemafingerprint, ontbrekende index, historische preflight zonder stilzwijgend herstel, profielwijziging na idempotente creatie, stockoversubscriptie, CAS-conflict en ingetrokken presentatoraccount.

Destructieve mutaties en foutinjectie zijn uitsluitend op synthetische databases uitgevoerd. De originele ontwikkeldatabase is alleen voor de SQLite-onlinebackup read-only geopend en niet door de nieuwe build geopend of gemigreerd. Tests gebruiken tijdelijke databases en een eigen HTTPS-server met testcertificaat. Er is geen fysieke authenticatoracceptatie geclaimd.

Logs: verification/bin/phase5-51-build-results.log, phase5-51-final-web-build.log, phase5-51-web-results.log, phase5-51-js-results.log, phase5-51-smoke-results.log, phase5-51-wpf-results.log. Uitvoer: verification/bin/phase5-51-regression. De vier bestaande runners zijn expliciet uitgevoerd; dit is geen claim van een extra dotnet test-run. Geen package-installaties of restores uitgevoerd.

### Resterende risico's en poort naar 5.2

Werkpakket 5.2 kan na expliciete opdracht starten. Vóór deelnemersregistratie activeren moeten alle lifecyclepaden, inclusief SessionStatusService.GetAsync, retentie respecteren; anders kan RESTRICT leiden tot een fout of onbeschermde cleanup credentials verwijderen. Houd deze functie gesloten tot de gehele claim-/herstel-/retentieketen gereed is.

5.1 levert geen operationele immutable-snapshot-/claim-/purgeguards, realtimepublisher, cryptografische codegeneratie of automatische busy-retryservice. De schemawaarden, constraints en opslagtransacties vormen daarvoor het fundament; de betreffende werkpakketten moeten bedrijfsregels en tijdguards afdwingen vóór activering. Automatische SQL_BUSY-retries zijn hier niet toegevoegd; creatie geeft een retrybaar opslagconflict en behoudt de idempotentiesleutel.

Verdere operationele risico's: sleutel-/backupbescherming, conservatieve schema-preflight bij handmatige extensies, writerconcurrentie op één SQLite-host, fysieke/browserverificatie en permanente versus tijdelijke auditretentie. QR-/SignalR-assets en Excelkeuze blijven latere besluiten. Geen verdere implementatie gestart.

## Uitvoering werkpakket 5.2a - evenementbeheer en profielen

Afgerond op 30 oktober 2026. Alleen 5.2a uitgevoerd; 5.2b en volgende pakketten niet gestart. Actuele contracten: functioneel F12 en architectuur A12. Bronversies blijven ongewijzigd; nieuwe databaseobjecten: **geen**. Bestaande migratie 6 is byte-identiek met de herstelbaseline.

### Baselines en behoud

5.1-baseline gecontroleerd: C:/Example/Baselines/phase5-51-20261030-210247. Nieuwe herstelbaseline vóór wijzigingen: **C:/Example/Baselines/phase5-52a-20261030-221240**. Bevat 136 oorspronkelijke projectbestanden in source.zip, sha256.json, database-0.db, database-manifest.json, keys-0 en RESTORE.txt. Archief/hashes en backupintegriteit geverifieerd; sourceDB alleen read-only voor consistente SQLite-onlinebackup geopend.

Herstel: server stoppen; actuele toestand eerst apart veiligstellen; source.zip in nieuwe map uitpakken en hashes controleren; alleen indien nodig de bijbehorende database/keyring volgens manifest terugzetten, onder oorspronkelijke DPAPI-gebruiker/machine. Geen oudere build op nieuwere database laten schrijven. Backup bevat authenticatiemateriaal en mag niet worden gepubliceerd. Geen herstel, migratie van ontwikkeldatabase of herstart uitgevoerd.

### Geleverde onderdelen en wijzigingsgrens

Nieuwe componenten:
- Models/PrizeEventModels.cs: beheer-DTO, paginering en lifecycleactie.
- Services/Prizes/PrizeEventService.cs: gevalideerde beheerorchestratie.
- Services/Prizes/PrizeEventStore.cs: partial repository, transacties, CAS, eventscoped idempotentie, profielkopie en audit/outbox.
- Pages/Presenter/Prizes/{Index,New,Event}.cshtml en bijbehorende PageModels, PrizePageModel.cs, PrizeEventInput.cs, _Settings.cshtml.
- Tests PrizeEventChecks.cs en PrizeEventHttpChecks.cs.

Bestaande bestanden beperkt aangepast: PrizeStore.cs (gedeeld creatiepad + atomair startcontract), PrizeModels.cs (configuratie/profiel toegankelijk voor servicelaag), Program.cs (DI), Presenter/Index.cshtml (navigatie), drie UiText-resources en site.css (bestaande vormgeving/lokalisatie), test Program.cs en HttpChecks.cs (checks aansluiten).

Geen nieuwe migration/package/projectinstelling, WPF-code, gewijzigde authpolicy of account-/sessie-/statistiekcode. Alle gewijzigde/nieuwe bronpaden staan in [codex-report.md](codex-report.md) en verification/bin/phase5-52a-source-diff.json. Omdat .git ontbreekt zijn SHA-256-hashes vergeleken met de baseline; geen git-diff geclaimd.

### Testresultaten

| Controle | Feitelijk resultaat |
| --- | --- |
| Volledige regressiebouw, --no-restore, gescheiden output, definitief -m:1 | Geslaagd; 0 waarschuwingen, 0 fouten |
| Webservices en HTTPS/WebAuthn (inclusief 5.1, 5.2a en bestaande regressies) | **547 checks geslaagd**, exit 0 |
| Bestaande JavaScript-tests passkeys/localization/presenter | **43 geslaagd**, 0 mislukt, exit 0 |
| Volledige simulator-smoke | Geslaagd, exit 0; o.a. 24 receiver-lifecyclechecks, 2778 ATT&CK/status/exportasserties |
| WPF-verificatie | **20 PASS-controles**, exit 0 |

Eerste parallelle regressiebouw slaagde met vier tijdelijke MSB3026-kopieerwaarschuwingen door gedeelde helperoutput. Definitieve sequentiële bouw lost dit op zonder project- of codewijziging.

Nieuwe servicechecks: vier profielen, Cybertruckterugval, installatiebrede voorkeur, concept versus echte start, custom profielkopie, mislukte start met triggerinjectie/volledige rollback, identieke en afwijkende idempotentie, 12 onafhankelijke concurrerende creaties, twee concurrerende presentatoren met één revisiewinnaar, route/body-isolatie, configuratiefreeze, typed kolommen/snapshot/prijsconfiguratie, Close/Reopen/PurgeAt, verval, sessie-/actorintrekking, voorraad zonder uitgifte, FK/integriteit en disabled/no-claims-asserties.

Nieuwe HTTPS-checks: bezoeker geweigerd, admin/presenter toegestaan, Nederlandse teksten en velden, standaard/expliciet/laatst profiel, CSRF en Origin, dubbel POST versus afwijkende payload, HTML-encoding, verkeerde eventbinding, stale revisie, sluiten/heropenen, profielopslaan, instellingen opslaan/herladen/afwijzen, overzicht en ontbrekende publieke Prize-routes. Bestaande suite controleert behoud/migratie van account-/credentialbytes, sessies en statistieken plus echte cryptografische software-authenticatorceremonies. Geen fysieke passkey- of visuele browseracceptatie geclaimd.

Output: verification/bin/phase5-52a-regression; logs phase5-52a-{build,final-build,web-tests,js-tests,smoke-tests,wpf-tests}.log in verification/bin. Geen extra dotnet test-run geclaimd: alle vier bestaande runners zijn expliciet uitgevoerd. Geen package-installatie of restore. Testdatabases zijn gescheiden/tijdelijk; WPF/smoke gebruiken phase5-52a-runtime.

### Risico's, resterende poorten en vervolg

- Geen live migratie/deployment: ontwikkeldatabase blijft schema 3/4/5; nieuwe app daar pas na expliciet goedgekeurde backup/migratiestap starten.
- SQLite serializeert schrijvers op één host; bij conflict verversen en met dezelfde intentie herhalen. De bestaande managementlimiter is gedeeld, niet per evenement; belastingacceptatie blijft nodig.
- Geen automatische eventpurge, claimafhandeling, herstel-login of deelnemersretentie. Verstreken eventbeheer is alleen-lezen. Geen garantie van een volledig operationeel prijsevenement.
- UI/taal zijn HTTPS-getest; fysieke authenticator, visuele/mobiele bediening en meerdere echte machines blijven acceptatiepunten.
- Bestaande securitybeoordeling is een historisch 5.1-checkpoint, geen releasevrijgave voor 5.2a.
- **Verplichte 5.2b-acceptatievoorwaarde:** alle accountverwijderpaden inclusief SessionStatusService.GetAsync eerst retentiebewust maken en regressietesten. Geen deelnemersregistratie activeren vóór de volledige retentie-/herstel-/claimketen.

Voorgestelde volgende opdracht: ontwerp en implementeer uitsluitend 5.2b na expliciete toestemming, beginnend met gezamenlijke retentieguard en tests voor OwnedCoreAsync, CleanupCoreAsync, EndOwnAsync, EndAsPresenterAsync, CreateAsync(previousToken), accountcap en SessionStatusService.GetAsync. Houd publieke deelname gesloten totdat alle afhankelijkheden bewezen zijn. Deze vervolgstap is nu niet uitgevoerd.

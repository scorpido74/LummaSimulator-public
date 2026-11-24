# Impactanalyse UX-modernisering LummaSimulator

Datum: 31 oktober 2026. Status: analyse en voorstel; geen implementatieopdracht.

## 1. Scope en bewijskracht

[functional-requirements.md](functional-requirements.md) is volledig gelezen, inclusief alle besluiten, veiligheidsgrenzen, rapportages, acht fasen en open punten. Dit document vergelijkt die eisen met de beschikbare bron. Aanvullende context: bestaande [Prize-eisen](phase5-prize-experience-functional.md), [Prize-architectuur](phase5-prize-experience-architecture.md) en het [Prize-implementatieplan](phase5-prize-experience-implementation.md).

Dit is statische bronanalyse. Er zijn geen builds, tests, applicatiestarts, migraties, package-installaties of databasequeries uitgevoerd. Applicatiestart zou migratie en cleanup starten en is daarom niet gebruikt als verificatiemethode. Live database, hardware, DNS, certificaten en MDR-omgevingen zijn niet onderzocht. Alleen dit analysedocument wordt toegevoegd; geen bestaande code, configuratie of database wordt gewijzigd.

**Aanwezig** betekent herkenbaar geïmplementeerd in bron, niet opnieuw runtime-geaccepteerd. **Gedeeltelijk** betekent bruikbare basis met functionele gaten. **Ontbrekend** betekent geen implementatie aangetroffen. **Onzeker** betekent dat een besluit of operationeel bewijs ontbreekt.

De werkmap heeft geen beschikbare `.git`-repository (`git status` meldt geen repository). Commitidentiteit en een Git-diff konden dus niet worden vastgesteld. In de bestandsinventarisatie is geen `AGENTS.md` aangetroffen. Dit is geen volledige securityscan of externe dependency-audit.

## 2. Inventarisatie en gecontroleerde baseline

### 2.1 Componenten

| Component | Implementatie / bewijs |
| --- | --- |
| WPF | `LummaSimulator.App`, `net10.0-windows`, MVVM via `MainViewModel`, `SessionMonitorViewModel`, `Infrastructure`. `MainWindow.xaml` heeft tabs ATT&CK · zes fases en Session Exposure Monitor. Werkt zelfstandig. |
| Simulator | `Services.cs`, `TelemetryService.cs`, `Models.cs`: zes vaste stappen; visuele simulatie of veilige lokale telemetrie, zichtbare `whoami`, eigen synthetische JSON-bestanden, optioneel loopback-HTTP; stop, reset, cleanup en JSON-logging. |
| ATT&CK | `AttackMapping.cs`, `NavigatorExportService.cs`: techniekmapping en Navigator-export. `MainViewModel` ondersteunt expliciete presentatorbeoordeling per product. Uitvoering wordt niet automatisch detectie. |
| HTTP-hulpproces | `LocalTestServerManager`, `shared/LocalTestServerProtocol.cs`, `tools/LummaSimulator.LocalTestServer`: lokale telemetrieontvanger, geen portal of externe agent. |
| Portal | `LummaSimulator.Web`, `net10.0`, Razor Pages/minimal APIs, projectversie 0.4.1. Projectafhankelijkheden: `Fido2.AspNet` 4.3.1 en `Microsoft.Data.Sqlite` 10.0.14; lockfiles aanwezig; geen EF Core. Dit zijn bronversies, geen actuele externe veiligheidsbeoordeling. |
| Bezoekersauthenticatie | `DemoService`, `DemoStore`, `passkeys.js`: willekeurige aliassen, visitorcookiebinding, echte FIDO2-verificatie, single-use challenges, maximaal vijf credentials server-side en client-side, counters en backupflags. |
| Beheeridentiteit | `PresenterAuthentication`, `PresenterManagement`, `PresenterLoginMigration`, `PresenterEndpoints`: persoonlijke accounts, Administrator/Presenter, discoverable login, tijdelijke uitnodigingscodes, lokale CLI-bootstrap, keybeheer, herauthenticatie, sessieintrekking en audit. Admin krijgt ook Presenter-rolclaim. |
| Beheerportal | `/Presenter`, Accounts, Audit, Security en Prizes; gedeelde interface met extra adminpagina's. Rolpolicies en server-side actorvalidatie bestaan; eigendom/delegatie per event niet. |
| Prize | `Models/PrizeModels.cs`, `PrizeEventModels.cs`, `Services/Prizes/*`, `Pages/Presenter/Prizes/*`: opslag, profielen, snelle aanmaak, configuratie, starten/sluiten/heropenen, paginering, versies, idempotentie en audit. Geen volledige deelname-/trekking-/claim-/presentatiestroom. |
| Session Exposure Monitor | `SessionStatusService`, `SessionMetadataClient`, `SessionMonitorViewModel`: apart geheim plus grant per demo; uitsluitend lokale servermetadata. Exposure/replay is een fictief object in geheugen; `SessionTokenExposed` blijft false. |
| Stijl/lokalisatie | NL/EN-resources, `localization.js`, `site.css`, gedeelde layout, organisatienaam Infinigate. WPF heeft vaste donkere kleuren; geen volledige licht/donker-omschakeling voor beide clients. |

### 2.2 Routes en rollen

- Publieke Razor Pages: `/`, `/Demo`, `/Complete`, `/Privacy`, `/Error`; Demo heeft eindigen/nieuwe-bezoekerhandlers. Bezoekersacties zijn aan de eigen visitorcookie gebonden.
- `POST /api/webauthn/{register|authenticate}/{options|verify|cancel}`: Origin, antiforgery, operatie-ID en rate limiting.
- `POST /api/presenter/enrollment/open` en `POST /api/presenter/{kind}/{options|verify|cancel}`: enrollment en WebAuthn-ceremonies; beheerwijzigingen via beveiligde Razor Page-handlers.
- `/Presenter`: dashboard, einde demo, monitorgrant, statistiekevent starten/sluiten/hernoemen. Accounts/Audit vereisen adminpolicy; Security is onderdeel van de beveiligde presentatormap.
- `/Presenter/Prizes`, `/Presenter/Prizes/New`, `/Presenter/Prizes/Event`: lijst, aanmaak, lifecycle, configuratie en profielopslag.
- `GET /api/local/session-status/{demoRunId}`: loopback, apart geheim en geldige grant; geen remote pairing of algemene beheer-API.
- `POST /api/language`: taal per publiek/presentator, met Origin/CSRF.

`Program.cs` heeft exacte HTTPS-origincontrole, CSP, no-store, antiforgery en rate limiting. Presentatorclaims worden buiten presentatorroutes gewist, met een uitzondering voor de taalkeuze. Nieuwe API's/realtimeverbindingen moeten hier bewust op aansluiten. CSP bevat `connect-src 'self'`. Geen agent-, MDR-, scenario-editor-, PDF- of publieke Prize-realtime-endpoints aangetroffen.

### 2.3 Database en bestanden

`DemoStore` beheert één lokale SQLite-database, standaard `PasskeyPortal/portal.db` onder de runtime-datamap; configureerbaar, buiten `wwwroot`. Verbindingen gebruiken foreign keys, secure delete en busy timeout. Legacy-initialisatie zet DELETE journaling. Er is een processpecifieke `Gate`; die is geen vervanging voor transactionele concurrentiebeveiliging tussen instanties.

| Groep | Tabellen / gegevens |
| --- | --- |
| Bezoekers | `Demos`, `Credentials`, `Challenges`, `MonitorGrants`; accountverwijdering cascadeert naar credentials/challenges/grants. Demos bevat onder meer DemoRunId, sessiehash, LastAuthenticatedAt en ClosedAt. |
| Statistiek | `ExperienceEvents`, `VisitorCounts`, `DailyExperienceStats`, `EventPrizePolicies`, `ExperienceMigrations`; één actief statistiekevent via `OneActiveExperienceEvent`. Geen volledige demo-eventtijdlijn. |
| Beheer | `PresenterAccounts`, `PresenterKeys`, `PresenterAuthSessions`, `PresenterInvitations`, `PresenterEnrollmentGrants`, `PresenterCeremonies`, `PresenterAudit`; legacy `PresenterSessions`. Audit heeft append-only triggers. |
| Prize-configuratie | `PrizeProfiles`, `PrizeInstallationSettings`, `PrizeEvents`, `PrizeTypes`, `PrizeGroups`; configuratiesnapshots en installatiebrede laatste profielkeuze. |
| Prize-fundament | Participants, start-/groepscodes, draws/candidates/instant decisions, awards, claims/secrets/data, inventory ledger, presentation grants/sessions/koppelingen, visitor sessions/ceremonies, operations/creation operations, audit, outbox en `PrizeSchemaContract`. Accountrelaties gebruiken onder meer RESTRICT. Aanwezige tabellen betekenen geen complete runtimefunctie. |
| Bestanden | WPF JSON-runlogs, monitorlogs, synthetische DemoRuns, Navigator-exports; Data Protection-keyring naast database, op Windows DPAPI. Exports/back-ups vallen niet automatisch onder databasecleanup. |

**Verificatie 5.1/5.2a:** `PrizeMigration.Version = 6`, schemacontract/fingerprint, preflight en migratiehook in `DemoStore.InitializeAsync` bevestigen het 5.1-opslagfundament in bron. `PrizeEventService`, `PrizeEventStore`, beheerpagina's en `PrizeEventChecks` bevestigen 5.2a-eventbeheer. `PrizeEventService.Validate` forceert `PrizeEnabled=false`; `ExperienceSettings.Validate` weigert globale Prize/startcode-activatie. Geen publieke deelnameketen aangetroffen: 5.2b is in de onderzochte bron niet geïmplementeerd. Het implementatieplan rapporteert eerdere tests; die zijn hier niet herhaald. De live schemaversie is zonder operationele inspectie **onzeker**.

### 2.4 Tests

`tests/LummaSimulator.Web.Tests` gebruikt executable assertions, `SoftwareAuthenticator`, testklok en HTTPS-childservers; bevat service-, migratie-, multikey-, presenter/discoverable-login-, lokalisatie-, Experience-, SessionStatus- en Prize-store/eventchecks. `tests/WebAuthn.Ux.Tests` bevat Node-tests voor passkeys, presentator en lokalisatie. SmokeTests dekken telemetrie, lokale server, monitor en Navigator. `verification` bevat WPF-verificatie en `Test-LocalHttpPublished.ps1`. De eigen VSTest-target in RegressionTests koppelt .NET/Node en Windows-runners.

Hergebruik deze runners en voeg negatieve objectautorisatie-, retentie-, offline-, agent- en MDR-contracttests toe bij bouw. Softwareauthenticators bewijzen geen fysieke YubiKey, LastPass, mobiele browser of certificaatopstelling. Geen nieuwe testuitslagen worden geclaimd.

## 3. Requirementsmatrix

| Besluit | Status | Aanwezig en ontbrekend |
| --- | --- | --- |
| 01 Beheerinterface | Gedeeltelijk | Gedeelde Presenter-interface/adminpolicies; consistente nieuwe navigatie en objectrechten ontbreken. |
| 02 Portalkoppeling | Gedeeltelijk | Apart geheim/opt-in grant; transport dwingt loopback af. Externe portal en veilige pairing ontbreken. PortalOrigin wijzigen volstaat niet. |
| 03 Eventregistratie | Gedeeltelijk | Runlogs, audit en statistiek; geen volledige gecorreleerde filterbare tijdlijn. |
| 04 Visualisatie | Gedeeltelijk | Fasekaarten/logs; geen interactief aanvalsschema met filters. |
| 05 Sessiestart | Gedeeltelijk | Nieuwe WPF-run-ID bij Start; geen startup-demosessie, sessieselector/herstartmodel. |
| 06 Eventrelatie | Gedeeltelijk | VisitorCounts koppelt aan statistiekevent; geen generieke optionele relatie voor WPF/labsessies. |
| 07 Privacy | Gedeeltelijk | Verwijdering bij einde/idle 1–29 minuten; logs blijven. Geen integraal instelbaar bewaarbeleid. |
| 08 Resultaten | Gedeeltelijk | WPF, Navigator en beheerstatistiek; geen uniforme resultaten en geschoonde publieke projectie. |
| 09 Bediening/eigenaar | Ontbrekend | Geen gedeelde commando's/exclusieve sessie-eigenaar voor WPF en portal. |
| 10 Schermselectie | Ontbrekend | Geen monitorselectie/éénklik-plaatsing. |
| 11 Introductie | Ontbrekend | Geen interactieve geanimeerde Lumma-intro. |
| 12 Uitlegniveau | Ontbrekend | Geen centrale eenvoudig/technisch-schakelaar. |
| 13 Navigatie | Ontbrekend | Huidige twee WPF-tabs voldoen niet aan Intro/Simulator/Resultaten en overslaan. |
| 14 Instellingen | Gedeeltelijk | Settings/environmentconfig; geen volledige gebruikersvoorkeuren of verse startup-sessie. |
| 15 Centrale policies | Gedeeltelijk | Serverconfig/Prize-profielen; geen versieerbare policies met lokale voorkeuren. |
| 16 Dashboard | Gedeeltelijk | Dashboard/eventlijst; geen laatste toegankelijk event per presentator. |
| 17 Rechten | Gedeeltelijk | Rollen/actorvalidatie; geen eigenaar, delegatie, herroeping of objectfilters. |
| 18 Event aanmaken | Aanwezig voor Prize-beheer | Snelle aanmaak, laatste installatieprofiel, geavanceerde opties; niet generiek voor alle demo's. |
| 19 Uitnodiging/bootstrap | Gedeeltelijk | Tijdelijke codes/lokale CLI-bootstrap; geen complete link-/e-mail-/wizardflow. Fysieke admin-YubiKey niet afgedwongen. |
| 20 Vormgeving | Gedeeltelijk | Organisatie/CSS/resources; geen volledige licht/donkerkeuze. |
| 21 Impactanalyse | Aanwezig bij oplevering | Dit document; bouw niet gestart. |
| 22 Detection Lab | Gedeeltelijk | Visuele/veilige lokale telemetriemodus; geen onafhankelijk configureerbare log-/endpointmodules. |
| 23 MDR-resultaten | Gedeeltelijk | Handmatige productbeoordeling; geen bewijsworkflow/API. |
| 24 Scenario's | Gedeeltelijk | Vaste geautomatiseerde reeks; geen configureerbare handmatige/automatische scenario's. |
| 25a Scenario-editor | Ontbrekend | Geen veilige stapbibliotheek/editor; geen generieke scripts introduceren. |
| 25b Sessiedemo | Gedeeltelijk | Fictief geheugenobject; geen werkende geïsoleerde browserdemosessie. |
| 26 Startmethode | Onzeker/open | Functioneel besluit ontbreekt; niet stilzwijgend invullen. |
| 27 Authenticatorselectie | Gedeeltelijk | Routekeuze/providerhulp; geen complete OS-suggestie plus Apple/Android/YubiKey-keuze. Browser beheert werkelijke providerkeuze. |
| 28 Onboarding | Gedeeltelijk | Instructies/providerhulp; geen volledige visuele uitleg met optionele wizard. |
| 29 Passkeys | Aanwezig voor bezoekerslimiet | Maximaal vijf server/client; backupflags beschikbaar. Geen bewezen merkclassificatie. Presenter-enrollment heeft apart maximum tien: scope van 'per account' verduidelijken. |
| 30 Phishingdemo | Ontbrekend | Geen optionele interactieve demo na registratie. |
| 31 Phishing per event | Ontbrekend | Geen automatisch/optioneel-eventpolicy. |
| 32 Uitleg phishing | Ontbrekend | Geen bezoekersuitslag/presentatortechniek. |
| 33 WebAuthn-domeinen | Ontbrekend/onzeker | Eén RP-context, geen tweede-RP-demo; tweede domein/TLS niet geverifieerd. |
| 34 Sessierisico | Gedeeltelijk | Visuele basis; werkende geïsoleerde demo ontbreekt. |
| 35 Tweede browser | Gedeeltelijk | Statusweergave; geen fictieve browsersessie zonder nieuwe passkeyauthenticatie. |
| 36 Demoacties | Ontbrekend | Geen sessielab met read-only en optionele fictieve wijziging/bestand; telemetriebestanden hebben ander doel. |
| 37 Intrekken sessie | Gedeeltelijk | Einde/expiry/grantintrekking; geen specifieke revoke-lifecycle voor nieuw labartefact. |
| 38a MDR-respons | Ontbrekend | Geen alert/API-flow die uitsluitend demosessie intrekt. |
| 38b Containmentlab | Ontbrekend | Geen expliciete labtoestemming/echte EDR-containmentintegratie. |
| 39 Lablocatie | Gedeeltelijk | Lokaal Windows; externe testmachine/VM ontbreekt. |
| 40 Agentarchitectuur | Ontbrekend | Geen lichtgewicht externe Detection Lab-agent. |
| 41 Discovery | Ontbrekend | Geen mDNS/lokale pairinggoedkeuring. |
| 42 Agentmodus | Ontbrekend | Geen handmatige agent/Windows-service. |
| 43 Distributie | Ontbrekend voor agent | Bestaande app/hulpproces zijn geen portable agent of service-installer. |
| 44 Agentupdates | Ontbrekend | Geen ondertekende goedgekeurde update-/rollbackketen. |
| 45 Activiteiten | Gedeeltelijk | Vaste telemetriestappen/ATT&CK; geen uitbreidbare versieerbare bibliotheek met risico/cleanupcontract. |
| 46 Modulekwaliteit | Gedeeltelijk | Bestaande tests; geen formele modulegoedkeuring/ondertekening/vrijgavepoort. |
| 47 MDR-vergelijking | Gedeeltelijk | Productkeuze/beoordeling; geen bewijsgestuurde detail-/vergelijkingsmatrix. |
| 48 Rapportage | Gedeeltelijk | Dashboard, JSON, Navigator; geen drie doelgroep-PDF's. |
| 49 Bewaarbeleid | Gedeeltelijk | Idlecleanup/Prize-retentievelden; geen uitvoerend gescheiden bezoekers-/MDR-/bestandenbeleid. |
| 50 Ontwikkelmodel | Gedeeltelijk | Modulaire diensten/Prize-werkpakketten/regressierunners; nieuwe acht fasen niet gerealiseerd. |

### 3.1 Belangrijke afwijkingen en open issues

**Admin-YubiKey:** `PresenterAuthentication` vraagt resident key en user verification, maar gebruikt attestation `None` en valideert geen vertrouwde YubiKey-herkomst. UV, backupflags of sleutelnaam bewijzen geen fysiek merk. Eerst controleerbaar hardwaretrustbeleid kiezen, inclusief reservesleutel, herstel en privacy. Alleen UI of een ongeverifieerde AAGUID voldoet niet. Bestaande keys niet zonder herstelpad ongeldig maken.

**Objectrechten:** `PrizeAuthorization.RequireActorAsync` valideert rol/actor. `PrizeEventStore.ManagementListAsync/GetAsync` filteren niet op eigenaar/delegatie. Het oude dashboard leest alle actieve demo's. Samengestelde event-FK's voorkomen inconsistentie, geen onbevoegd lezen. Toets alle oude en nieuwe lees-, schrijf-, monitor-, export- en realtimepaden.

**Verwijdering:** `DemoService` verwijdert via owned/idle/end/presenter-end/cleanup; create/handover sluit de vorige demo. `SessionStatusService.GetAsync` verwijdert eveneens idle accounts, vóór grantcontrole. Toekomstige PrizeParticipants/VisitorSessions kunnen RESTRICT-fouten veroorzaken of behoud vereisen. Een gezamenlijke retentiebeslissing moet vóór deelname worden ingevoerd; huidige cleanup nu niet uitzetten.

**MDR:** bestaande Observed/Detected/NotDetected/NotEvaluated is geen volledige bewijsstatus. Voeg geen-alert-ontvangen/geblokkeerd/geïsoleerd, bron/validator, omgeving en versiegegevens toe. Houd simulatie, uitvoeringssucces, handmatige claim en extern gevalideerde detectie afzonderlijk.

| Open punt | Feitelijke status / vereiste validatie |
| --- | --- |
| LastPass | `Demo.cshtml` noemt provider/browser/extensieafhankelijkheid; specifieke werkende LastPass-login niet fysiek bewezen. |
| Herhaalde YubiKey-login | Ceremony/retry/cancel en discoverable presenterlogin bestaan; fysieke herhaalde login blijft handmatige acceptatie. |
| Windows-herkenningsmelding | Providerhulp legt Windows Hello uit; oorspronkelijke reproductie en exacte melding ontbreken. Oplossing onzeker. |
| Subdomeincertificaat | HTTPS/origins afgedwongen; SAN, ketenvertrouwen, DNS en mobiele bereikbaarheid niet operationeel onderzocht. |
| Prize-scope | 5.1/5.2a behouden; 5.2b–5.6 zijn aparte backlog. Tabellen niet als voltooide Prize Experience tellen. |
| Schermselectie | Oud Prize-plan beperkte tot venster openen/handmatig verplaatsen. Nieuw besluit 10 breidt uit met éénklik-monitorplaatsing. |
| Retentieconflict | Nieuwe standaard verwijderen bij sessie-einde versus langere Prize-claimbewaring: expliciete doelgebonden uitzondering/opt-in nodig, geen globale policy. |
| Toekomstwensen | Multi-presentator/Classroom/PIV niet automatisch MVP. Bestaande persoonlijke accounts behouden; delegatie en exclusieve bediening zijn wel requirements. |

## 4. Hergebruik en architectuurwijzigingen

### 4.1 Hergebruik

- Behoud `DemoService` en bestaande FIDO2-verifier: bouw wizard/uitleg rond limiet vijf, single-use challenges, counters/backupflags, Origin en CSRF.
- Breid `PresenterService.Actor`, policies, sessieintrekking, herauthenticatie en audit uit met objectrechten. Controleer rechten in dezelfde transactie als bedrijfsacties; geen tweede beheerauthenticatiesysteem.
- Hergebruik Prize-profielsnapshots, versies/CAS, idempotentie en transacties. Installatiebreed laatste profiel is iets anders dan persoonlijk laatste toegankelijk event.
- Hergebruik WPF-MVVM, commands, annulering, ATT&CK en export; haal scenario-orkestratie uit MainViewModel voordat meerdere clients dezelfde run bedienen.
- Gebruik begrensde `TelemetryService`-stappen als eerste modules; maak inputs, rechten, timeout, stop, cleanup, risico en output expliciet. Geen parametriseerbare remote shell maken.
- Hergebruik `RuntimePaths`, `TimeProvider`, NL/EN-resources en bestaande runners. Monitorstatus is bruikbaar, loopbackgeheim/transport niet als extern beheerkanaal.
- PrizePresentationGrants/Outbox zijn opslagpatronen; publicatie, intrekking en reconnect zijn nog te bouwen.

### 4.2 Voorgestelde grenzen

Voorstel: behoud zelfstandig WPF en zelfstandig portal. Scheid intern identiteit/toegang, events, demo-orkestratie, passkeyervaring, session lab, Detection Lab, agent en rapportage/retentie. Deel versieerbare contracten, geen databasebestand, beheerkeyring of presentatorcookie.

| Trust boundary | Benodigde verandering |
| --- | --- |
| Bezoeker → portal | Visitorbinding behouden; demotoken geeft geen beheerrechten. Tweede RP is eigen WebAuthn-context, geen versoepeling van bestaande origins. |
| Beheerder → event/sessie | Eigenaar/delegatie en objectautorisatie op queries/acties; intrekking raakt verbindingen en wachtende opdrachten. |
| WPF ↔ portal | Standaard uit, lokale/externe pairing met kortlevende eenmalige codes, beperkte scopes en intrekking; TLS valideren, secrets beschermd opslaan. |
| Bedienaar → sessie | Exclusieve lease met eigenaar, expiry, versie/fencing en auditeerbare overdracht; commando's idempotent. Netwerkverlies begrenst remote bediening; WPF blijft zelfstandig bruikbaar. |
| Intern → publiek | Aparte read-only capability/projectie met expiry/revoke en allowlist-DTO's; geen interne accountmodellen, tokens, PII of verborgen prijsuitslagen. Aliassen/IDs kunnen nog herleidbaar zijn. |
| Session lab → echte identiteit | Eigen purpose/audience/store voor fictieve demoartefacten; nooit echte cookies/profielen lezen. Artefact geeft geen beheer- of Prize-claimrechten. |
| Controller → agent | mDNS is discovery, geen vertrouwen. Lokale toestemming plus versleutelde geauthenticeerde pairing; whitelist module-ID/versie/parameters, geen scriptstrings. |
| Agent/MDR → bewijs | Verifieer bron, tijd, product/configuratie en runrelatie. Ontbrekend/vertraagd bewijs is geen bewezen niet-detectie. Containment alleen toegestane labmachine. |

Realtime via bijvoorbeeld SignalR of polling is nog een ontwerpkeuze: beoordeel CSP, Origin, scopes, reconnect, revocation en offlinegedrag. SQLite voor één portalhost blijft uitgangspunt. Geen meerdere hosts of netwerkshared database impliciet ondersteunen; eerst belasting meten voordat een andere database wordt gekozen.

## 5. Datamodel en migratie-impact

Onderstaande entiteiten zijn ontwerpvoorstellen; namen en migratienummers pas vaststellen na besluiten. Volgende migratie bouwt voort op volledig v6-contract; v6 niet achteraf aanpassen.

| Gebied | Voorstel en impact |
| --- | --- |
| Eventidentiteit | Kies overkoepelend demo-event met expliciete ExperienceEvent/PrizeEvent-relaties, of wijs na ontwerp één identiteit als canoniek aan. Niet op naam samenvoegen. Oude IDs/tellingen, één-actief-statistiekevent en aparte Prize-lifecycle behouden tot gecontroleerde overgang. |
| Rechten | EventOwner/EventDelegations met actor, rechten, expiry, revoked-at, versie en audit; scope op sessies/queries. Legacy-event zonder eigenaar niet publiek maken; admin laat eigendom expliciet toewijzen. Echte tenantstructuur pas na scopebesluit, eventisolatie direct. |
| Demosessies | DemoSessions met SessionId, optioneel EventId, bron/client, lifecycle en policysnapshot; expliciete koppeling naar bestaande DemoRunId's. PresentationLeases en command receipts voor fencing/idempotentie. |
| Tijdlijn | Getypeerde DemoEvents met scope, bron, event-/servertijd, sequence, activiteit/resultaat. Indexen op scope+tijd/type; begrensde payloads zonder secrets. WPF-/portal-/agent-ID's expliciet correleren. |
| Policies | Versieerbare centrale policies en event-snapshots; persoonlijke laatste-eventkeuze. WPF-thema/monitor/uitleg lokaal; lokale voorkeur mag centrale veiligheidsgrens niet versoepelen. |
| Session lab | Demoartefacten met purpose/audience, tokenhash, expiry/revoke, toegestane acties, sessie/eventscope en audit; fictieve accountstaat apart van beheersleutels. |
| Detection Lab | Activiteitdefinities/versies, signatures/approval; scenario's/versies/stappen met gevalideerde parameters; runs/results/cleanup/bewijs. Gepubliceerde versies immutable. |
| Agent | Identiteiten, pairings/grants, opdrachten/receipts, heartbeat, versies en updateapprovalhistorie; secrets beschermd opslaan of secure-storeverwijzing. Discoverydata niet vertrouwen. |
| MDR/historie | Testomgeving, product-/configuratiesnapshot, observaties, bewijsstatus, detectie-/responstijd en validator. Geanonimiseerde historie zonder koppeling die bezoekersidentiteit in leven houdt. |
| Retentie | Aparte bezoekers-/sessiepolicy, doelgebonden Prize-bindingen en technische historie; restartbare purgejobs en bestand/exportmanifest. Bestaande append-only audit ook op PII/bewaarbeleid toetsen. |

Latere migratievolgorde:

1. Na aparte uitvoeringsopdracht consistente database/keyringback-up en herstelcontext vastleggen; migraties eerst op synthetische testkopieën.
2. Eventmapping en legacyrechten besluiten; additieve tabellen/kolommen, geen credentialverlies of RP-ID-wijziging.
3. Gedeelde retentie/lifecycle invoeren vóór deelnamebinding/nieuwe sessieartefacten. Alle deletepaden testen, inclusief status-GET, worker en startupcleanup.
4. Alleen bewezen relaties backfillen; geen eigenaar of tijd verzinnen. Losse demo's behouden; niet-toegewezen legacy-events admin-only beheren.
5. Herhaalbaarheid, atomair falen, schemaherkenning, foreign_key_check en bytebehoud van keys/handles/counters testen; concurrency met onafhankelijke verbindingen.
6. Routes pas activeren na rechten/retentieacceptatie; featuregates en synthetische data tot volledige lifecycle werkt.
7. Rollback: module uit; bij incompatibel schema bijpassende code/database/keyring als consistente set herstellen met beleid voor writes sinds back-up. Geen oude binary blind op nieuw schema; herstel op testkopie bewijzen.

## 6. Risico's en afhankelijkheden

| Prioriteit | Risico / afhankelijkheid | Maatregel / vrijgavevoorwaarde |
| --- | --- | --- |
| Hoog | Rollen zonder eventisolatie | Objectrechten vóór gedeelde/publieke functies; negatieve tests met twee eigenaren, delegatie, intrekking en oude handlers. |
| Hoog | Delete versus Prize-RESTRICT/PII-behoud | Gezamenlijke retentieservice, revoke los van purge, bindingen en restartbare cleanup; geen uitzonderingspad via monitor-GET. |
| Hoog | Schijnbare YubiKey-afdwinging | Trust-/attestationbeleid plus fysieke tests, bootstrap/recovery vóór aanscherping. |
| Hoog | Tweede domein/RP verkeerd | Eigen RP/TLS en gescheiden challenges; verkeerde RP weigert bestaande credential; oorspronkelijke RP niet wijzigen. |
| Hoog | Demoartefact geeft echte rechten | Purpose/audience/store-isolatie, fictieve accounts en allowlistacties; tests tegen visitor/presenter/Prize-identiteiten. |
| Hoog | Agent wordt remote shell | Alleen goedgekeurde modules, lokale toestemming, minimale rechten, stop/timeout/cleanup, signatures. Servicecontext apart testen. |
| Hoog | Onterechte detectie/containmentclaim | Bewijs en omgeving verplicht; missing alert apart; containment niet impliciet activeren. |
| Hoog | PII in logs/back-ups/rapporten | Manifest, retentie/rotatie/herstel, anonimisering; grens voor extern gedownloade rapporten expliciet maken. |
| Middel/hoog | Dubbele bediening/crash/retry | Lease/fencing/idempotentie en reconnectsnapshot; crash na commit vóór response testen. |
| Middel | SQLite-contentie | Begrensde events/batching, korte transacties, synthetische loadtests; geen nieuwe lange netwerkstappen in writertransacties. |
| Middel | UI breekt bestaande demo's | WebAuthn/Prize/Monitor/Navigator-regressie, NL/EN, toetsenbord/contrast en offlineacceptatie. |
| Middel | Nieuwe libraries/distributie | PDF/realtime/QR/mDNS/updatepakket eerst op API, onderhoud, licentie, CSP en offlinegebruik beoordelen; hier niets installeren. |

Voor afhankelijke bouw nog te besluiten: vraag 26; scope limiet vijf versus beheerkeys; retentie per doel/Prize-uitzondering; canonieke eventidentiteit en tenantmodel; hardwaretrust/herstel; domeinen/certificaten; MDR-API/scopes/rate limits; toegestane labmachines/containment; signing-keybeheer en approval; exacte Infinigate-stijlmiddelen.

## 7. Implementatievolgorde en acceptatiecriteria

Dit zijn de acht nieuwe UX-fasen. **UX-fase 5 is niet Prize-werkpakket 5.1/5.2a.** Houd Prize 5.2b–5.6 apart traceerbaar en activeer deelname niet voordat de bestaande retentie-/veiligheidsvoorwaarden zijn ingevuld. Beheer/session lab/rapportage zijn grote pakketten; Detection Lab/agent zeer groot. Zonder scopebesluiten en testomgeving is geen betrouwbare tijdschatting mogelijk.

Voor iedere bouwfase: code review, passende automatische tests, regressie van bestaande functies, expliciete veiligheidsacceptatie, geregistreerde handmatige beperkingen en getest rollbackplan. Hardware-, visuele en operationele acceptatie apart aantonen; mocks volstaan daarvoor niet.

| Fase | Wijzigingspakket en afhankelijkheid | Meetbare acceptatiecriteria |
| --- | --- | --- |
| **1 Architectuur/impact** | Deze inventarisatie, gevolgd door besluiten over rechten, eventmodel, retentie en trust vóór bouw. | Alle eisen traceerbaar; code/schema/runtimebewijs onderscheiden; migratie-, trust- en risicoplan aanwezig; open keuzes zichtbaar; uitsluitend analysedocument toegevoegd. Dit document maakt open besluiten niet definitief. |
| **2a Beheerfundament** | Eerst objectrechten en gedeelde retentie, dan eventmapping/policysnapshots; raakvlakken PresenterService, PrizeAuthorization/Store, dashboard, DemoService en SessionStatusService. | Presentator ziet/beheert uitsluitend eigen/gedelegeerde events, admin alles. Direct-ID/API/export/monitor/eindroutes lekken geen ander event. Delegatie herroepbaar/auditeerbaar. Alle deletepaden respecteren retentie/Prize-binding/expiry; niet-deelnemers behouden huidige eind-/idlewerking. Legacydata en credentials behouden. |
| **2b Beheerervaring** | Dashboard/navigatie, laatste event, uitnodiging/wizard, hardwarepolicy/thema's op 2a. | Laatste toegankelijk event opent; intrekking geeft veilige fallback. Snelle aanmaak gebruikt geldig laatste profiel/snapshot. Bootstrap eenmalig/lokaal begrensd; niet-toegestane authenticator krijgt geen adminlogin, toegestane fysieke YubiKey wel; herstel/reservesleutel getest. Link/code niet in logs/referrers. Adminmenu én serverrechten correct; licht/donker/NL/EN bruikbaar. E-mail pas met gekozen koppeling. |
| **3 Passkeyportal** | Wizard/OS-suggestie, eventphishingpolicy, tweede eigen RP; verifier behouden. | Handmatige Apple/Android/YubiKey-keuze; providerbeperkingen eerlijk. Vijf credentials werken, zesde ook bij race geweigerd. Registratie is geen login. Retry/cancel/herhaalde login en origin/challenge/counter/backupregressie slagen. Tweede RP weigert oorspronkelijke credential en toont publieke uitslag/techniek apart. Bestaande RP/keys intact. Feitelijke LastPass/mobiel/YubiKey/Windows/certificaatmatrix. |
| **4 WPF-ervaring** | Sessiemodel, voorkeuren, intro/uitleg, schema/tijdlijn, resultaten, monitorvenster. | Opstart maakt verse demosessie; optioneel event, selecteren/herstarten zonder datavermenging. Intro standaard/overslaan, bedoelde drie tabs en filters werken. Thema/monitor/uitleg onthouden. Monitorplaatsing getest met DPI-verschil en loskoppeling. Offline, stop/cleanup, telemetrie en Navigator behouden. Publieke projectie uitsluitend allowlistvelden. |
| **5 Session Exposure Lab** | Eerst pairing/lease; daarna demoartefact, tweede browser, read-only acties, optionele fictieve wijziging/revoke. Vraag 26 vooraf beslissen. | Koppeling standaard uit; toegestane lokale/externe portal met TLS en herroepbare scopes. Geen twee effectieve bedienaars; stale lease/retry geweigerd. Tweede browser gebruikt alleen fictieve geïsoleerde sessie zonder nieuwe passkeyauthenticatie. Geen echte cookies/credentials gelezen. Handmatige/automatische revoke blokkeert vervolggebruik, ook open browser. Simulatie en echt ingetrokken demoartefact herkenbaar; portaluitval breekt zelfstandige WPF niet. |
| **6 Detection Lab** | Veilige stappen naar goedgekeurde bibliotheek, editor, lokale engine en onafhankelijke log/endpointkeuze. | Alleen goedgekeurde geteste versies; unsigned/onbekend/ongeldige parameters geweigerd. Geen scripts/shellinput. Handmatige/automatische scenario's consistente getypeerde resultaten. Log-only veroorzaakt geen endpointactiviteit; opties onafhankelijk. Stop/timeout/cleanup/crash/herstart getest; rechten/risico/ATT&CK/telemetrie zichtbaar. Uitvoering is geen detectieclaim. |
| **7 Externe agent** | Transport en moduleapproval op 6; mDNS/lokale pairing, portable/service, updates. | Discoveryspoofing geeft geen toegang; zonder lokale toestemming geen opdrachten. Encryptie/authenticatie, expiry/revoke en replaypreventie bewezen. Portable/minimale-rechtenservice voeren alleen geautoriseerde modules uit. Netwerkverlies/cancel begrensd, cleanupstatus zichtbaar. Signature én expliciete updateapproval vereist, geen update tijdens demo, foutrollback getest. Geen remote shell/EDR-omzeiling. |
| **8 MDR/rapportage** | Eerst handmatig bewijs/historie; daarna toegestane API's, matrix, respons en drie PDF's. Lokaal op 6, remote bewijs op 7. | Niet getest/geen alert ontvangen/gedetecteerd/geblokkeerd/geïsoleerd apart, met bron/validator, product/configuratie, tijd, agent/moduleversie. Duplicaten, vertraging, API-fout en verkeerde tenant/run geven geen valse claim. MDR-revoke raakt uitsluitend demosessie; echte containment alleen expliciet toegestane machine. Drie doelgroep-PDF's met beperkingen/bewijs, geen algemene ranglijst. Sessieverwijdering laat geen bezoekersidentiteit achter in technische historie, artifacts, exports of back-ups binnen vastgesteld beleid; restore past expiry toe vóór toegang. |

Aanbevolen keten: **1 → 2a → 2b → 3 → 4 → 5 → 6 → 7 → 8**. Ontwerp retentie- en bewijscontracten van fase 8 al in 2a/6, zodat historie niet onomkeerbaar verweven raakt met bezoekersidentiteit. Lokale rapportagevoorbereiding kan na 6; externe/API-claims pas na echte integratieacceptatie.

Het eerste concrete bouwpakket na afzonderlijke opdracht is 2a: objectrechten, eventidentiteitsbesluit en gezamenlijke retentie met synthetische migratie-/regressietests. Dit document start of activeert geen implementatie.

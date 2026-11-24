# Fase 5 - Prize Experience: technische architectuur

Status: opslagfundament 5.1 en evenementbeheer 5.2a geïmplementeerd; overige onderdelen ontwerp. Datum: 30 oktober 2026.
[Eisen en besluiten](phase5-prize-experience-functional.md) | [implementatie](phase5-prize-experience-implementation.md) | [roadmap](LummaSimulator-v0.5.md).
E/O/T/V hebben dezelfde betekenis als in de functionele specificatie.

## A01 - Gecontroleerde baseline

Onderstaande conclusies volgen uit broninspectie; er is geen live database geïnspecteerd.

| Bestaand bestand | Gecontroleerd gedrag / gevolg |
| --- | --- |
| LummaSimulator.Web/LummaSimulator.Web.csproj | net10.0, versie 0.4.1; Fido2.AspNet 4.3.1, Microsoft.Data.Sqlite 10.0.14; geen EF Core |
| Services/DemoStore.cs | Singleton Gate; lokale SQLite, ForeignKeys=true, Pooling=false, secure_delete=ON, busy_timeout=5000, journal_mode=DELETE; InitializeAsync voegt kolommen toe |
| Services/DemoService.cs | Cookiegebonden Options/Verify; max vijf credentials; resident key en UV Required; anonieme attestation; challenges single-use vóór verificatie; harde accountcap 200 telt alle Demos |
| Services/DemoService.cs | OwnedCoreAsync, CleanupCoreAsync, EndOwnAsync en EndAsPresenterAsync verwijderen Demos; CreateAsync sluit vorige account; cascade verwijdert credentials |
| Services/ExperienceStatistics.cs | BEGIN IMMEDIATE; één actief ExperienceEvent via OneActiveExperienceEvent; VisitorCounts.DemoId uniek over accountlevensduur; EventPrizePolicies kan door CHECK nog niet aan |
| Services/PresenterAuthentication.cs, PresenterManagement.cs | Persoonlijke accounts/keys/sessies, server-side actorvalidatie; administrator krijgt ook Presenter-rolclaim; sessieintrekking; append-only PresenterAudit |
| Services/PresenterLoginMigration.cs | ExperienceMigrations versie 5 is al gebruikt voor discoverable presentatorlogin, niet voor Prize Experience |
| Program.cs | Razor Pages/minimal APIs; exacte HTTPS-origin, CSRF, no-store/CSP, DPAPI Data Protection; principal wordt buiten /Presenter en /api/presenter gewist; nog geen AddSignalR/MapHub |
| Configuration/ExperienceSettings.cs | PrizeEnabledByDefault en StartCodesRequired momenteel fail-closed; beide true verhinderen startup |
| Services/PresenterService.cs | CleanupWorker iedere 30 seconden; VisitorCookie is Secure/HttpOnly/Strict sessiecookie |
| LummaSimulator.App/*.cs, docs/LummaSimulator-v0.4.md | WPF net10.0-windows/MVVM, zelfstandig proces; SessionMetadataClient is beperkte lokale monitorclient, geen Prize API-client |
| tests/LummaSimulator.Web.Tests | Eigen executable assertions, SoftwareAuthenticator, TestClock, echte HTTPS-childserver, migratie- en multikeytests |
| tests/LummaSimulator.RegressionTests/*.csproj | Eigen VSTest-target start .NET-, Node-, smoke- en WPF-runners; geen xUnit/EF testinfrastructuur |

Fase-2/3/4-geschiedenis en recentere loginmigratie zijn gelezen in docs/LummaSimulator-v0.5.md, naast README, v0.4 en Passkey-Portal-v0.3(-plan). Historische testtotalen zijn geen nieuw uitgevoerde tests.

## A02 - Componentgrenzen en hergebruik

O: één ASP.NET Core-proces met één lokale persistente SQLite-database. Meerdere browsers/presentatoren/evenementen zijn ondersteund; geen meerdere applicatiehosts of netwerkshare-database. WPF blijft onafhankelijk en krijgt geen prijzenlogica, SignalR-client, databasekoppeling of gedeelde beheergeheimen.

Nieuw onder LummaSimulator.Web:
- Models/PrizeModels.cs: gevalideerde statussen, configuraties en gescheiden DTO's.
- Services/Prizes/PrizeStore.cs: geparametriseerde repositories via DemoStore.OpenAsync, één connection/transaction doorgeven; nooit losse ExecuteAsync-calls voor één bedrijfsactie.
- PrizeMigration.cs, PrizeEventService.cs, PrizeProfileService.cs, PrizeParticipationService.cs, PrizeDrawService.cs, PrizeInventoryService.cs, PrizeClaimService.cs, PrizeReportService.cs, PrizeRetentionService.cs.
- PrizeVisitorAuthentication.cs: aanvullende cookie-onafhankelijke herstellogin met bestaande IFido2; geen tweede eigen cryptografische verifier.
- PrizeAuthorization.cs, PrizeEndpoints.cs, PrizeOutboxWorker.cs, PrizeHub.cs.
- Pages/Presenter/Prizes/*, Pages/Prizes/*, wwwroot/js/prizes.js, prize-presentation.js, prize-claims.js; bestaande lokale resources/CSS uitbreiden.

O: gebruik bestaande DI, TimeProvider, DemoIdentity.RandomToken/Hash, exacte WebAuthnSettings, Origin/CSRF, presenter Actor-patroon en NL/EN-resources. Maak actorvalidatie transactioneel herbruikbaar zonder accountbeheerrechten te verbreden. Geen kopie van presentatorlogin voor bezoekers; alleen gedeelde gecontroleerde verificatiehelpers waar werkelijk identiek.

Onvermijdelijke toekomstige raakvlakken in DemoService: alle verwijder-/idle-/handoverpaden moeten rekening houden met deelnamebindingsretentie; accountcap scheidt bewaarde deelnemers van actieve tijdelijke demoplekken. Bestaande 200 tijdelijke-plekkengrens blijft, bewaarde inactieve deelnemers mogen die niet blokkeren; voeg afzonderlijke gevalideerde installatiegrens voor bewaarde accounts toe (O: 10.000). Migratie behoudt iedere bestaande credentialbyte. Niet-deelnemende demo's blijven ongewijzigd.

## A03 - Datamodelcontract

O: UTC Unix-milliseconden INTEGER, UUID/ondoorzichtige TEXT-identifiers, BOOLEAN INTEGER CHECK IN(0,1); nooit SQLite REAL voor kans of geld. Iedere statuskolom heeft CHECK met genoemde waarden. Tabellen hieronder zijn nieuw, behalve expliciet genoemde toevoegingen. NOT NULL tenzij '?' is vermeld. Elke eventgebonden tabel heeft EventId; alle betrokken parents hebben UNIQUE(EventId,Id), alle childverwijzingen gebruiken samengestelde FK(EventId,ParentId). Hiermee kan een claim nooit een prijs/groep/deelnemer van een ander evenement aanwijzen. Parent-EventId verwijst naar PrizeEvents(Id); accountbinding naar Demos(Id) is RESTRICT, geen CASCADE. Eventcleanup verwijdert kinderen gecontroleerd vóór parents.

| Tabel | Sleutels, velden, beperkingen |
| --- | --- |
| PrizeProfiles | Id PK, Name, BuiltIn, ConfigJson, ConfigVersion, Version, CreatedAt, UpdatedAt; JSON server-side typed valideren |
| PrizeInstallationSettings | Id PK CHECK(Id=1), LastProfileId? FK SET NULL, Version; één installatiebrede voorkeur |
| PrizeEvents | Id PK, Name, ProfileId? FK SET NULL, ProfileSnapshotJson, ConfigVersion, PrizeEnabled, Visibility, Mode, GroupSize, WinnersPerGroup, AssignmentMode, NextGroupMode, DrawMethod, RevealMode, AnimationSeconds, StartCodeRequired, PrizeSelection, FixedPrizeTypeId?, ChanceNumerator, ChanceDenominator, MaxInstantWins?, InstantWins, ReactionSeconds, RetentionDays, PostCloseClaimSeconds, ClaimFieldSchemaJson, Status, CreatedAt, ClosedAt?, PurgeAt?, Version, NextParticipantNumber; CHECK 1≤RetentionDays≤90, AnimationSeconds IN(5,10,15), public implies code required; typed snapshot bevat ook gekozen grenswaarden |
| PrizeGroups | Id PK, EventId, Number, Status, Capacity, Occupied, FrozenAt?, Version; UNIQUE(EventId,Number), CHECK 0≤Occupied≤Capacity; partial UNIQUE(EventId) WHERE Status='Open' |
| PrizeParticipants | Id PK, EventId, AccountId FK Demos RESTRICT, GroupId?, Number, State, JoinedAt, CancelledAt?, AuthenticationAt; UNIQUE(EventId,AccountId), UNIQUE(EventId,Number) |
| PrizeStartCodes | Id PK, EventId, TokenHash UNIQUE, ExpiresAt, State, ConsumedByParticipantId?, ConsumedAt?; State Available/Consumed/Revoked; verbruiker zelfde event, UNIQUE verbruiker |
| PrizeGroupCodes | Id PK, EventId, GroupId, TokenHash UNIQUE, ExpiresAt, RevokedAt?; herbruikbaar zolang groep open |
| PrizeTypes | Id PK, EventId, Name, Total?, Reserved, Issued, Active, Version; CHECK Total IS NULL OR Total≥Reserved+Issued; Reserved,Issued≥0; null onbeperkt |
| PrizeDraws | Id PK, EventId, GroupId?, Kind Initial/Replacement/Instant, Sequence, Status, StartedAt, CompletedRevealAt, CandidateHash, AlgorithmVersion, ConfigurationSnapshotJson, Version; UNIQUE(EventId,GroupId,Sequence); maximaal één Initial per GroupId via partial index |
| PrizeDrawCandidates | EventId, DrawId, ParticipantId, Ordinal; PK(DrawId,ParticipantId), UNIQUE(DrawId,Ordinal); immutable snapshot |
| PrizeInstantDecisions | EventId, ParticipantId PK, IsWinner, Reason, AlgorithmVersion, DrawId?; één vast besluit per deelname |
| PrizeAwards | Id PK, EventId, DrawId, ParticipantId, PrizeTypeId, Ordinal, AnimatesFrom, RevealAt, ReplacesAwardId?; UNIQUE(EventId,ParticipantId) geldt ook na verval; UNIQUE(DrawId,Ordinal); UNIQUE(ReplacesAwardId) WHERE niet null |
| PrizeClaims | Id PK, EventId, AwardId UNIQUE, State, ReactionDueAt, RedeemedAt?, ForfeitedAt?, ExpiredAt?, Version, FieldSchemaJson; één eigenaar uitsluitend via immutable award → participant |
| PrizeClaimSecrets | ClaimId PK, EventId, TokenHash UNIQUE, ProtectedToken, KeyVersion; nooit plaintext; verwijderd bij terminale status |
| PrizeClaimData | ClaimId PK, EventId, ProtectedPayload, KeyVersion, UpdatedAt; persoonsgegevens pas na bekendmaking |
| PrizeInventoryLedger | Id PK, EventId, PrizeTypeId, AwardId?, Kind Reserve/Release/Issue/Adjust, Quantity, At, ActorId?; UNIQUE(AwardId,Kind) voor Reserve/Release/Issue; onveranderlijk tot eventpurge |
| PrizePresentationGrants | Id PK, EventId, TokenHash UNIQUE, ExpiresAt, RevokedAt?, Version; uitsluitend read-only |
| PrizeVisitorSessions | Hash PK, AccountId FK RESTRICT, CreatedAt, ExpiresAt, AuthenticatedAt, RevokedAt?; beperkte herstel/claimsessie |
| PrizeVisitorCeremonies | Id PK, BrowserHash, OperationId UNIQUE, Options, ExpiresAt, ConsumedAt?; discoverable loginchallenge, account pas na verificatie bekend |
| PrizeOperations | Id PK, EventId, ActorScope, OperationKind, IdempotencyKey, RequestHash, ResultReference, CompletedAt; UNIQUE(EventId,ActorScope,OperationKind,IdempotencyKey) |
| PrizeAudit | Id PK, EventId, At, ActorId?, Action, TargetId?, OperationId, PreviousVersion?, NewVersion?; geen codes/PII; leesbaar voor beide rollen in Prize-module |
| PrizeOutbox | Id PK, EventId, Revision, Kind, SafeReference, DueAt, PublishedAt?; geen verborgen winnaars/PII in payload; UNIQUE(EventId,Revision,Kind,SafeReference) |

O: Indexeer event/status, claimdeadline, PurgeAt en outbox DueAt; geen onbegrensde dashboardscans. FixedPrizeTypeId gebruikt samengestelde FK naar hetzelfde event; initieel nullable tijdens eventcreatie, service valideert vaste prijs vóór commit. Door die cyclus event+prijs in één transactie invoegen en event daarna op prijs wijzen; gebruik nullable kolom, geen foreign_keys=OFF.

O: PrizeEvents.Status Draft/Open/Closed/Purging. Draft is uitsluitend onvoltooide beheerconfiguratie, geen publieke deelname; snelle start creëert direct Open. Purging is onomkeerbaar, API geeft 410, waarna verwijdering. Groep Open/Closed/Frozen: eerste draw zet Frozen; verdere onthullingsstatus zit in draw. Draw Scheduled/Revealing/Revealed is een afgeleide tijdsstatus van gecommitte schema; opgeslagen Status Committed, geen langdurig Running-slot. Claim Pending/Redeemed/Forfeited/Expired; RevealAt is een extra toegangsvoorwaarde, geen claimstatus. Reservering bestaat precies zolang Pending; terminale mutatie vrijgeeft of geeft uit.

Belangrijke invarianten via service plus SQL:
- cross-table regels niet in SQLite CHECK verstoppen; transacties en gericht geteste triggers bewaken capaciteit, verboden statusovergangen, snapshot/award-immutabiliteit en één reserveringterminal.
- ReplacesAwardId alleen zelfde event/groep, oude claim Forfeited; direct expiry is geen automatische vervangingsbevoegdheid.
- toekomstige awards blijven intern; databasebeheerder kan ze technisch zien, presentator-API niet.
- na eerste deelname staan lotings-, kans-, privacy-, capaciteits- en startcodevoorwaarden vast. Groepsindeling/animatie/bekendmaking vast vóór eerste draw. Prijsaanvulling en naamcorrectie toegestaan met audit/version; geen bestaande prijs/award verwijderen.

## A04 - Migratie en accountlevensduur

O: vaste ExperienceMigrations-markering 6; fase 5 is niet marker 5. Eén expliciete PrizeMigration na bestaande initialisatie en vóór startup-cleanup/verkeer. Valideer verwachte kolommen/FK/indexen/triggers en markeringen; onbekend schema stopt veilig. Create-if-not-exists alleen is geen voldoende upgradecontrole.

Upgradeprocedure voor latere implementatie:
1. Onderhoudsvenster; oude server stoppen, geen twee builds op één database.
2. Consistente backup via SQLite backup-API of gesloten bestand; bescherm bestaande Data Protection-keyring en DPAPI-machinecontext. Geen bestandskopie van actieve database aannemen als consistent.
3. Preflight schema, integrity_check en foreign_key_check; controleer schijfruimte en schrijfrechten.
4. BEGIN IMMEDIATE, nieuwe tabellen/indexen/triggers en profielen invoegen, optionele nieuwe sessieverificatiekolommen toevoegen. Niets aan account-ID, user handle, credential-ID/key/counter/backupflag wijzigen.
5. Schemainvarianten en foreign_key_check; marker als laatste; commit.
6. Cleanup pas met nieuwe retentieregels; bij fout startup stoppen en transactie rollback. Herhaling doet geen dubbele seeds/import.
7. Rollback naar oude applicatie uitsluitend met passende pre-upgradebackup; oude cleanup mag nieuwe bewaarde accounts niet openen/verwijderen. Hersteltest vooraf op geïsoleerde kopie.

O: bewaarbeveiliging is een EXISTS op PrizeParticipants gekoppeld aan niet-verwijderd evenement, niet alleen Pending claims. Dit geldt onder DemoStore.Gate voor OwnedCore, cleanup, eindigen en Create(previous). Bewaarde accounts mogen ClosedAt hebben voor de oude demo en zijn dan alleen via herstellogin bereikbaar; nooit oude sessie terug uitgeven. Herstel/claimsessies zijn afzonderlijk van VisitorCookie en tellen niet als actieve WPF-demonstratie. Een account dat vóór opt-in al volgens oud beleid is verwijderd kan niet worden hersteld; UI communiceert dit.

O: registratie blijft maximaal vijf keys op hetzelfde Demos-account via bestaande verificatie. Voor een bewaard gesloten account maakt een gecontroleerde add-keyflow dezelfde accountgebonden registration-options beschikbaar na verse herstelassertion; IFido2 en bestaande uniqueness/handlechecks worden hergebruikt. Dit is nodig om geen nieuw account voor een reservesleutel te maken; geen kopie naar PresenterKeys.

## A05 - Atomaire bedrijfsacties en concurrentie

O: SQL-transacties zijn de autoriteit; Gate beschermt ceremonies/levensduur maar is geen cross-connection lock. Alle mutaties gebruiken BeginTransaction(deferred:false), actor/sessievalidatie binnen die transactie en versievoorwaarden. Geen WebAuthn-dialogen, animatiewachten, netwerkverkeer, exports of SignalR-send tijdens de writertransactie.

Deelname:
1. Verifieer sessie/assertion, event Open/enabled en codebeleid. Zoek bestaand (EventId,AccountId); idempotente retry geeft bestaand resultaat, Cancelled geeft blijvende afwijzing.
2. Controleer startcodehash/event/expiry; reserveer capaciteit met UPDATE PrizeGroups SET Occupied=Occupied+1 WHERE Status='Open' AND Occupied<Capacity. Eén gewijzigde rij vereist.
3. Verhoog eventnummer, insert participant; UPDATE code WHERE State='Available' AND ExpiresAt>now, controleer één rij; directe winstbeslissing/reservering zo nodig in dezelfde transactie.
4. Audit, operationresultaat, revisie/outbox; commit. Fout/volle groep/stockfout rolt ook codeverbruik terug. Instant Niet gewonnen door stock is daarentegen een geldig vastgelegd besluit.

Groepstrekking:
1. Controleer event, verwachte groepversie Closed/Frozen, bestaand Initial/Replacement-doel, idempotency en geen ononthulde vorige draw in dezelfde groep.
2. Bevries eerste snapshot of gebruik die van Initial; sluit ooit gewonnen deelnemers uit; onvoldoende kandidaten geeft 409 zonder commit.
3. CSPRNG-selectie en prijskeuze; reserveer laatste prijs met UPDATE PrizeTypes SET Reserved=Reserved+1 WHERE Id=$id AND EventId=$e AND Active=1 AND (Total IS NULL OR Reserved+Issued<Total). Eén rij of volledig rollback.
4. Insert draw/awards/claims, ledger en revealplanning. Atomic commit voordat animatie begint.

Claimuitgifte versus vervallen:
- Hercontroleer servernow≥RevealAt, eventdeadline, Pending en versie. Uitgifte UPDATE Pending→Redeemed; Reserved-- en Issued++, ledger Issue, secret verwijderen, audit samen.
- Vervallen alleen na ReactionDueAt, Pending→Forfeited; Reserved--, ledger Release, geheim verwijderen. Redeemed kan nooit vervallen.
- Deadline Pending→Expired geeft ook Release, zonder hertrekking; worker én reads/mutaties handhaven tijd, zodat offline worker geen geldigheid verlengt.
- Twee presentatoren/races leveren één commit; tweede krijgt huidig resultaat bij dezelfde idempotencykey of 409 bij andere intentie.

O: elke mutatie heeft Idempotency-Key (UUID), payloadhash en verwachte Version/If-Match. Zelfde sleutel+andere payload → 409; gelijke retry retourneert oorspronkelijke resource/status zonder nieuwe RNG. Businessunique constraints voorkomen ook dubbele draw met verschillende sleutels. Idempotentiegegevens leven tot eventpurge; API geeft bij verwijderde event 410. Bounded retry bij SQLITE_BUSY (max drie pogingen met jitter binnen requestbudget) herhaalt volledige transactie, nooit gedeeltelijke writes. Na onzekere commit eerst operationrecord lezen. Na definitieve timeout 503+Retry-After, geen automatische nieuwe trekking door client.

## A06 - REST-contract

O: JSON-DTO's gescheiden per doelgroep, servernow en eventrevision op snapshots. Mutaties: exacte Origin, CSRF, authenticatie, rate limit, idempotency, versie. GET is vrij van side effects zoals uitgifte; verlopen claims worden server-side als ongeldig gerenderd en worker verwerkt administratie.

| Route | Methoden en toegang |
| --- | --- |
| /api/presenter/prizes/profiles | GET/POST; beheerpolicy; /{id} PATCH met versie |
| /api/presenter/prizes/events | GET/POST snelle start; /{e} GET/PATCH naam/config binnen grenzen |
| /api/presenter/prizes/events/{e}/close, /reopen, /enable, /disable | POST, beheerpolicy, bevestiging waar destructief |
| .../{e}/profiles | POST huidige configuratie als nieuw profiel |
| .../{e}/groups | GET/POST; /{g}/close, /reopen POST |
| .../{e}/start-codes | POST batchuitgifte, GET alleen metadata; /{c}/revoke POST |
| .../{e}/groups/{g}/codes | POST uitgifte/rotatie; oude capability intrekken |
| .../{e}/prizes | GET/POST; /{p} PATCH voorraad/naam/active met versie |
| .../{e}/groups/{g}/draws | POST eerste draw; /{draw}/replacements POST met expliciete Forfeited award-IDs |
| .../{e}/claims/resolve | POST code invoeren; alleen vrijgegeven metadata; nooit uitgifte door scan |
| .../{e}/claims/{c}/redeem, /forfeit | POST beheer, atomair; geen accountwijzigingsroute |
| .../{e}/presentation-grants | POST, /{grant}/revoke POST |
| .../{e}/snapshot, /audit, /reports | GET beheer; /exports POST met exportkeuze, resultaatdownload beperkt |
| /api/prizes/events/{e} | GET veilige bezoekersconfig/status, geen toekomstige awards |
| /api/prizes/events/{e}/participation | POST join, GET eigen, /cancel POST |
| /api/prizes/events/{e}/claims | GET uitsluitend eigen bekendgemaakte claims; /{c}/data PUT gevalideerde toegestane velden |
| /api/prizes/recovery/options, /verify, /cancel | POST afzonderlijke browser-/operatiegebonden herstelceremonie |
| /api/prizes/recovery/keys/options, /verify | POST verse herstelassertion, accountgebonden add-key |
| /api/prizes/recovery/logout | POST beperkte sessie intrekken |
| /api/prizes/presentation/exchange | POST capability → presentatiecookie |
| /api/prizes/presentation/{e}/snapshot | GET read-only eventgebonden presentatiepolicy |

O: 400 validatie; 401 niet aangemeld; 403 bevoegdheid; 404 onbekend of andermans object; 409 versie/status/capaciteit/conflict; 410 verwijderd evenement; 429 limiet; 503 database tijdelijk bezet. Publieke codefouten zijn uniform. Responses bevatten resource-ID, versie, veilige foutcode en correlation-ID, geen SQL/stacktrace/token.

## A07 - Autorisatie, codes en realtime

O: PrizeManage policy vereist bestaand Presenter-cookieschema, amr=webauthn en rol Presenter OF Administrator; servercontrole sessie/accountstatus bij iedere actie. Routes blijven onder /api/presenter en /Presenter zodat bestaande principal-scope past. Afzonderlijke read-only presentation scheme en visitor recovery scheme mogen nooit door die scope worden weggegooid of in presenter-principal veranderen. Wijzig scope expliciet op pad+scheme; behoud gelijktijdige bezoekers-/presentator-CSRF-test.

O: claimcode is 128 willekeurige bits, Crockford Base32 26 tekens plus optionele leesgroepering (hyphens/spaties normaliseren; geen caseverschil). RNG-bytes → eenduidige Base32, geen modulo-biased karakterkeuze. Hash SHA-256 over purpose|eventId|canonicalToken, unieke index, collision retry vóór commit. Startcodes en groepscodes minstens 128 bits; presentatiegrants 256 bits. DemoIdentity.RandomToken is bruikbaar voor 256-bit capabilities. Uniforme foutmeldingen, geen bearer-token in log/CSV/presentatie.

Hash alleen kan oorspronkelijke code niet opnieuw tonen. Daarom staat claimtoken aanvullend purpose-bound Data Protection-versleuteld in PrizeClaimSecrets; uitsluitend eigen account na RevealAt mag het ontsleutelen. Keyring buiten wwwroot, bestaand application name en DPAPI behouden. Keyringverlies maakt codes niet herstelbaar; geen stille nieuwe draw, beheerherstel vereist expliciete codeheruitgifte met oude hashintrekking als later voorstel. Claimuitgifte via bestaande hash blijft mogelijk als code nog beschikbaar is. PII eigen purpose, event/claimbinding en sleutelversie. Bescherm opslag/backup met OS-ACL; secure_delete is geen garantie tegen SSD/backups.

O: Rate limiter na authenticatie waar accountpartitionering nodig is; huidige globale demo 300/min blijft compatibel. Aanvullend voorstelwaarden: join/code 5/min per account/event, 60/min per vertrouwd remote IP, 600/min installatie; claim resolve 10/min presentator, 120/min installatie; recovery 8/min browser/IP, 120/min installatie; mutaties 60/min presentator. Queue=0, Retry-After, geen willekeurige account-ID als onbeperkte limiterpartition. NAT/100 deelnemers testen. Geen permanente IP-opslag, bounded partitions en geen brute-force uitgifte-orakel.

O: ASP.NET Core SignalR past bij huidig Razor/minimal API-hostmodel: AddSignalR en MapHub; server in shared framework. Nieuwe lokale JS-clientartifact nodig, geen CDN. Drie gescheiden hubs: /api/presenter/prizes/hub (beheer), /api/prizes/visitor-hub (eigen meldingen), /api/prizes/presentation-hub (alleen publiek DTO). Geen draw/redeem hubmethoden; mutaties uitsluitend REST. Eventgroepen server-side toekennen na autorisatie, nooit vrij client Join(eventId).

Presentatie-URL /Prizes/Presentation/{eventId}#capability. Fragment blijft uit HTTP-URL; exchange via same-origin POST+CSRF maakt Secure/HttpOnly/Strict presentation cookie met server-side gehashte grantbinding. Fragment verwijderen, geen localStorage. Grant beperkt tot één event, O: maximaal 12 uur en nooit voorbij PurgeAt; dashboard kan nieuw grant geven/intrekken. Geen presenterlogin nodig op scherm. Tweede scherm gebruikt dezelfde flow. Browser mag voor verschillende evenementen aparte tab/grants gebruiken: cookie bevat alleen sessie-ID, server bewaart per sessie geautoriseerde grants, zodat één eventcookie geen andere tab onbedoeld vervangt.

SignalR-events: EventChanged, GroupChanged, ParticipationChanged, DrawScheduled, RevealAvailable, ClaimChanged, InventoryChanged; envelope eventId/revision/serverNow/resource-reference. Publieke DrawScheduled bevat geen winnaar; RevealAvailable pas na serverdeadline. Persoonlijke ClaimChanged alleen naar gevalideerde AccountId-groep. Beheeraudit enkel beheerhub. SignalR is notificatie, niet opslag.

O: transactionele outbox voorkomt verlies tussen commit en notificatie. Worker verwerkt due-reveals, controleert geldigheid grant/sessie vóór doelgroeppublicatie, maximaal at-least-once. Clients dedupliceren revision/message-ID; bij gaten/reconnect eerst volledige bevoegde snapshot. Langlevende verbinding: periodieke serverrevalidatie max 15 seconden én bij iedere invocation/publicatie; intrekking sluit verbinding/groepslidmaatschap, geen verdere payload naar ingetrokken grant. Cookie-expiry sluit verbinding; alleen een eenmalige connectcontrole is onvoldoende. Exacte WebSocket Origin valideren, geen wildcard CORS. Verbindings-/messagegroottes en aantallen begrenzen. Geen token in querystring, geen access-token-logging.

## A08 - Retentie, audit, rapporten en herstel

O: PrizeRetentionService draait bij startup vóór verkeer en periodiek via bestaande workercoördinatie (30 seconden), met TimeProvider. Transacties begrensd per event/batch. Eerst einddatums toepassen en claims afhandelen, daarna Purging markeren en grant/sessietoegang weigeren; outbox stoppen; childtabellen in FK-volgorde verwijderen; event verwijderen. Bij crash hervat Purging. Accounts pas na laatste deelnamebinding verwijderen volgens idlebeleid; andere eventclaims en Presenter-tabellen nooit mee verwijderen.

PrizeAudit is eventgebonden en retentiegebonden met append-only bescherming voor gewone applicatieacties, gecontroleerde purgecontext voor cleanup. PresenterAudit-triggers niet uitschakelen. Schrijf geen tijdelijke participant/claimtargets in permanente PresenterAudit; bestaande algemene beheer-/accountaudit blijft onveranderd. Geaggregeerde permanente statistieken uit fase 3 blijven bestaan; geen nieuwe persoonlijke prijshistorie daarin kopiëren.

Rapporten: één SQLite-readsnapshot, sluit verborgen toekomstwinnaars uit, anonieme kolommen standaard. CSV UTF-8, correcte quoting, formule-injectie afvangen (ook voor tab/CR en =,+,-,@). .xlsx met echte tekst-/numerieke cellen, geen formules/macros/externe links; BCL ZipArchive+XmlWriter voor minimale OOXML, geen XML-stringconcatenatie. Downloadstream geen onbeperkte buffer, filename servergegenereerd, no-store, Content-Disposition attachment; tijdelijke serverexports uiterlijk 15 minuten en altijd vóór PurgeAt verwijderen. Exportrechten bij genereren én downloaden opnieuw valideren.

Herstart: SQLite commit/rollback bewaart loting; geen transient in-memory winstresultaten. Reads reconstrueren revealstatus uit UTC-tijden; outbox verwerkt achterstand zonder extra trekking. Serverklok via betrouwbare OS-tijd; significante klokachteruitgang detecteren, beheerwaarschuwing en kritieke tijdsacties tijdelijk blokkeren tot stabiel, geen eerdere reveal ongedaan maken (bewaar PublishedAt). Vooruitgang kan deadlines versnellen: acceptatie/monitoring nodig. Backuprestore is herstel naar vroeger tijdstip en kan reeds fysiek uitgegeven prijzen missen; inventaris reconciliëren vóór hervatten, nooit automatisch opnieuw uitgeven na disaster restore.

## A09 - Twaalf verplichte controles en risico's

| Controle | Bevinding en oplossing / bewijs bij implementatie |
| --- | --- |
| 1 Blijvende bezoekersclaims | Nu ongeschikt: cascade/idle/end. Bewaarde bindingen en herstelpad; test alle deletepaden |
| 2 Meerdere bezoekerspasskeys | Bestaand 1:N Credentials, max vijf; behoud, herstel met sleutel 1 én 2 |
| 3 Migratie | Handmatige ADO.NET, marker 5 bezet; migratie ≥6 met backup/preflight/rollbacktests |
| 4 SignalR | Nog afwezig, host geschikt; principal-scope, lokale client, outbox en reconnect toetsen |
| 5 Dubbele trekkingen | IMMEDIATE+unique Initial/replacement+CAS+idempotentie; twee onafhankelijke connections |
| 6 Claimcodes | 128-bit RNG, purpose/eventhash, versleutelde terugtooncopy; replay/lek/rotatietests |
| 7 Laatste prijs | Conditionele stock-UPDATE in drawtransactie; 50 gelijktijdige pogingen geeft één reservering |
| 8 Startcodes | Consume na insert in dezelfde tx; injected rollback houdt code Available |
| 9 Capaciteit | Conditionele Occupied-update plus constraints; 100 joins op 15 plekken maximaal 15 |
| 10 Eventisolatie | Samengestelde FK en scopechecks in REST/hubs/rapport/codehash; cross-event negatieve tests |
| 11 Bestaande auth | Geen RP/origin/schemanamen vervangen; gescheiden cookies en rolepolicy, volledige regressie |
| 12 Crash/herstart | Durable draws/tijdlijn/outbox, recovery-login en catch-up cleanup; kill vóór/na commit testen |

Overige concrete risico's: bewaarde accounts vullen huidige 200-cap; retentie faalt als een oud deletepad overblijft; .NET SQLite-schrijvers serialiseren en kunnen demo latency verhogen; keyringverlies; ongecontroleerde open evenementen; gecompromitteerde presentator kan voorraad aanpassen maar geen bestaande uitslag herschrijven; fragmenttokens blijven gevoelig voor toegang tot scherm/browser; backup kan secrets en persoonsgegevens blijven bevatten. Beperk writerduur, log alleen foutcodes/correlation, bewaak cleanup/outbox/stockinvarianten en toets herstel operationeel.

## Bronnen voor technische voorstellen

Geraadpleegd 30 oktober 2026; primaire documentatie, geen claim van runtimevalidatie:
- [Microsoft.Data.Sqlite transacties](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions): writerconcurrentie en deferred/immediatekeuze.
- [SignalR authenticatie/autorisatie](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0): gescheiden toegang en connectielifetime.
- [SignalR security](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/signalr/security.md): tokens/URL-logging en browser-originbeperkingen.
- [SQLite foreign keys](https://www.sqlite.org/foreignkeys.html) en [CREATE TABLE](https://www.sqlite.org/lang_createtable.html): samengestelde FK/UNIQUE/CHECK als integriteitsbasis.

## A10 - Definitief opslagcontract werkpakket 5.1 (versie 6)

Deze sectie preciseert en bij verschil vervangt zij A03/A04. Migratieversie is vast **6**; nooit MAX(Version)+1. De bronbaseline en consistente ontwikkeldatabasebackup staan buiten het project in C:/Example/Baselines/phase5-51-20261030-210247 (sha256.json, source.zip, database-0.db, keys-0, database-manifest.json, RESTORE.txt). De ontwikkeldatabase heeft markeringen 3/4/5. Herstel alleen met gestopte server, eerst huidige toestand apart bewaren, bronarchief in nieuwe map uitpakken en hashes controleren, passende database/keyring terugzetten in oorspronkelijke machine-/DPAPI-context. Geen live upgrade uitgevoerd.

### Presentatie en opslag

PrizeDraws bewaart Committed, AlgorithmVersion, kandidaatdigest, configuratiesnapshot, AnimationSeconds, StartedAt en CompletedRevealAt. PrizeAwards bewaart de onveranderlijke Ordinal, AnimatesFrom en RevealAt. AnimationSeconds is 5/10/15; tijden voldoen aan RevealAt=AnimatesFrom+AnimationSeconds*1000. Tegelijk delen awards hun tijdvenster; afzonderlijk volgen zij hun vaste ordinal. Uitslag en planning worden in dezelfde latere bedrijfsactie gecommit; geen trekkingalgoritme in 5.1. Iedere nieuwe verbinding leest de bevoegde snapshot met ServerNow, hervat dezelfde tijdlijn en toont verstreken bekendmakingen onmiddellijk. SignalR meldt uitsluitend wijzigingen en is nooit bron van uitslagen.

Nieuw: PrizePresentationSessions(Hash PK, CreatedAt, ExpiresAt, RevokedAt) en PrizePresentationSessionGrants(SessionHash, EventId, GrantId; samengestelde PK). Sessies zijn browsergebonden, grants evenementgebonden; meerdere schermen hebben eigen sessies, meerdere tabs mogen meerdere grants aan dezelfde sessie koppelen. FK(EventId,GrantId) voorkomt eventvermenging. Alleen sessie-grantkoppelingen mogen door sessie/grantverwijdering cascaderen. Vervallen/ingetrokken verbindingen worden later opnieuw gevalideerd. PrizeVisitorSessions en PrizeVisitorCeremonies zijn account-/browsergebonden, geen evenemententabellen.

### Statussen en overgangen

- Event: Draft -> Open -> Closed; Closed -> Open alleen voor PurgeAt; Closed/Open -> Purging alleen volgens bewaarbeleid. Purging is terminaal.
- Group: Open <-> Closed zolang FrozenAt null en geen draw bestaat; Closed -> Frozen; Frozen terminaal.
- Participant: Active -> Cancelled uitsluitend voor eerste trekking. Cancelled terminaal; UNIQUE(EventId,AccountId) blijft.
- Startcode: Available -> Consumed of Revoked; terminaal. Groepscodes/grants/sessies hebben timestamps voor expiry/revocation, geen extra statuskolom.
- Draw: uitsluitend Committed. Scheduled/Revealing/Revealed zijn tijdprojecties, geen mutabele databaseworkflow.
- Claim: Pending -> Redeemed/Forfeited/Expired; alle drie terminaal.
- InstantDecision is immutable; IsWinner boolean. Ledger-kind Reserve/Release/Issue/Adjust; append-only tot purge.
- Profiel heeft BuiltIn en Version; geen soft-delete-status. PrizeType Active boolean; geen hard-delete na referentie.
- Audit, outbox, idempotentie en presentatie-/recoverykoppelingen hebben geen extra domeinstatus; CompletedAt/PublishedAt/expiry dragen hun lifecycle.

5.1 legt waarden, CHECKs, unieke sleutels, FK's, voorraad/capaciteitsgrenzen en opslag-CAS vast. Operationele guards voor deelname, draw, claim, immutabiliteit/purge en tijdsovergangen worden pas in hun betreffende werkpakket geïmplementeerd en getest; deze tabellen mogen vóór die guards niet publiek beschrijfbaar zijn. Dit is een expliciete beperking van het opslagfundament, geen geleverde deelnamefunctionaliteit.

Elke event-child heeft EventId en een samengestelde FK naar UNIQUE(EventId,Id), of UNIQUE(EventId,ClaimId) voor ClaimData/Secrets. Accountbinding RESTRICT. Default delete NO ACTION/RESTRICT. FixedPrizeTypeId is nullable tijdens opslag van Draft; vóór latere Open-start wordt een geldige vaste prijs vereist. Purge moet eerst FixedPrizeTypeId NULL maken, daarna kinderen en replacement-awards in omgekeerde referentievolgorde verwijderen. RESTRICT nooit uitschakelen. Geen automatische purge in 5.1.

Version begint op 1 voor mutable aggregates; conditionele UPDATE WHERE Version=expected verhoogt met 1, anders conflict zonder neveneffect. PrizeEvents.Version is tevens eventbrede Revision; elke latere mutatie verhoogt die eenmaal in dezelfde transactie als audit/outbox. OutboxId is unieke message-ID; meerdere berichten per revision toegestaan. Readsnapshots leveren één consistente revision. Total null is onbeperkt; anders Total>=Reserved+Issued; Occupied tussen 0 en Capacity. Alle kritieke mutaties BEGIN IMMEDIATE, geen netwerk of animatiewacht in transactie. SQLITE_BUSY geeft een retrybaar opslagconflict; caller herhaalt uitsluitend met dezelfde idempotentiesleutel.

### Idempotente evenementcreatie op installatieniveau

Nieuw: PrizeCreationOperations(ActorId, OperationKind, IdempotencyKey, CanonicalPayload, RequestHash, EventId UNIQUE, CompletedAt), PK(ActorId,OperationKind,IdempotencyKey), FK EventId naar PrizeEvents. Scope is installatie+gevalideerde presentator, niet een nog niet bestaand evenement. Zelfde sleutel bij een andere actor is een afzonderlijke intentie. CanonicalPayload is de server-gevalideerde getrimde naam plus expliciet ProfileId, in vaste JSON-veldvolgorde. Profielsnapshot wordt bij eerste uitvoering opgeslagen; een retry vergelijkt dezelfde requestpayload en leest het oorspronkelijke event, ook als het profiel later wijzigt. SHA-256 dient als digest, exacte canonical string wordt eveneens vergeleken.

Autorisatie valideert echte accountsessie binnen BEGIN IMMEDIATE. Daarna bestaande sleutel lezen: gelijke payload geeft hetzelfde event, afwijkende payload opslagconflict. Anders profiel lezen, disabled Draft met snapshot invoegen, operationrecord invoegen, commit. Concurrerende verbindingen serialiseren aan SQLite-writerlock; unieke PK sluit dubbele intenties uit. Fout/annulering vóór commit rolt event én record terug. Alleen interne repository, geen HTTP-route of evenementstartservice. Dit minimale Draft-opslagpad dient om het contract in 5.1 daadwerkelijk te testen; snelle start/activering blijft 5.2.

Migratie controleert bestaande bekende objecten vóór legacy-initialisatie bij heropening, voert nieuwe DDL/seeds/manifest/marker transactioneel uit en controleert FK/integriteit. PrizeSchemaContract registreert versie 6 en schemafingerprint; heropening vergelijkt de werkelijke objectdefinities met de vaste implementatie. Marker zonder passende objecten/contract en objecten zonder marker worden afgewezen. Vier ingebouwde profiel-IDs zijn stabiel; LastProfileId begint NULL, zodat eerste selectie Cybertruck oplevert. Migratie overschrijft geen gewijzigde profielconfiguratie of voorkeur bij heropening.

### Verplichte acceptatiepoort 5.2

Vóór enige deelnemersregistratie moeten ALLE verwijderpaden dezelfde retentieguard gebruiken: DemoService.OwnedCoreAsync, CleanupCoreAsync (inclusief startup/dashboard/Create), EndOwnAsync, EndAsPresenterAsync, CreateAsync(previousToken) en **SessionStatusService.GetAsync** (monitorverwijdering). Monitor blijft verlopen sessies afwijzen zonder een behouden account te verwijderen. Ook accountcap, gesloten accounts en credential-cascade testen. Deze lifecyclewijzigingen worden nadrukkelijk niet in 5.1 gedaan. Niet-deelnemers behouden hun bestaande gedrag.

## A11 - Implementatiegrens en resultaat 5.1

A10 is gerealiseerd voor de opslaggrens: 26 nieuwe tabellen, 8 expliciete indexen, vaste marker 6, schemamanifest, vier profielseeds, interne defaultprofiellezer, disabled Draft-creatie met transactionele actorvalidatie/idempotentie en Draft-CAS. De lifecycletransities zijn als modellen/contracten vastgelegd; operationele deelname-, trekking-, claim-, immutabiliteits- en purgeregels volgen in hun werkpakketten vóór publiek gebruik. Geen nieuwe routes/hubs/DI-registratie; geen live migratie.

Bronnen: Models/PrizeModels.cs en Services/Prizes/PrizeMigration.cs, PrizeStore.cs, PrizeAuthorization.cs. De baseline-preflight bewaart strings in SQL letterlijk en normaliseert alleen layout/identifierquotes. Voor bestaande gevalideerde versies 5/6 wordt de oude initialisatie overgeslagen om onbedoelde historische seeds/cleanup te voorkomen. Voor verse/ondersteunde oude schemas blijft de bestaande upgrade beschikbaar.

Zie het implementatieplan voor het complete objecten-/bestandenoverzicht en feitelijke resultaten: 467 webchecks, 43 JS-tests, geslaagde smoke en WPF. Afwijking buiten de verwachte bestandslijst betreft uitsluitend de gerichte synthetische legacyfixture in ExperienceChecks.cs. WebAuthn-verificatie, RP/origins, WPF, packages en bestaande lifecyclecode zijn ongewijzigd.

## A12 - Definitief beheercontract en uitvoering 5.2a

Deze sectie actualiseert A10/A11 uitsluitend voor evenementbeheer. Migratieversie 6, DDL en schemafingerprint zijn ongewijzigd. Geen nieuwe migratie, tabel, index, package, authschema of WebAuthn-ceremonie.

### Aansluiting en routes

Nieuwe openbare C#-servicelaag PrizeEventService orchestreert de interne partial PrizeStore. Openbaar betekent hier programmeerinterface binnen de assembly; er is geen publieke HTTP-API. PrizeAuthorization hergebruikt ongewijzigd PresenterService.Actor: bestaande sid-hash, sessieverval/revocatie, actieve account, amr en echte DB-rol worden in dezelfde opslagtransactie gecontroleerd. Beiden hebben dezelfde Prize-beheerrechten; bestaande administratorrechten elders blijven ongewijzigd.

Alle nieuwe HTTP-routes vallen onder bestaande AuthorizeFolder("/Presenter", PresenterService.Policy):
- GET /Presenter/Prizes: overzicht; pageIndex, 25 resultaten.
- GET/POST /Presenter/Prizes/New: profiel laden, concept of atomair creëren en starten.
- GET /Presenter/Prizes/Event/{id}: details.
- POST dezelfde detailroute met handler Start, Close, Reopen, Rename, Configure of Profile.

Razor-antiforgery en bestaande presenter-management-limiter gelden; POST vereist daarnaast een Origin die exact in de bestaande WebAuthn-origins staat. Geen GET-mutaties. Geen nieuwe REST-/bezoekersroutes, hub of presentatiebevoegdheden. De bestaande middleware-cookiecontrole blijft voor de opslagautorisatie staan. De routes/verborgen ExpectedEventId worden gelijk gecontroleerd; alle SQL gebruikt parameters en EventId-scope. Dit is geen tenantautorisatie: iedere geldige presenter mag ieder evenement op deze installatie beheren.

### Creatie en profielsnapshot

Bestaand PrizeCreationOperations-contract blijft PK(ActorId,OperationKind,IdempotencyKey) met vaste OperationKind CreateDraft wegens de schema-6-CHECK. De naam is een opslaghistorie, geen beperking tot HTTP-concepten. De nieuwe canonical request heeft vaste veldvolgorde {ManagementVersion:1,Name,ProfileId,Configuration,Start}. Naam en configuratie worden vooraf gevalideerd/genormaliseerd; claimvelddefinitie is een alfabetisch geordend object van maximaal vijf bekende velden met Optional/Required (Hidden wordt weggelaten). PrizeEnabled normaliseert naar false. Exacte canonical string én SHA-256 moeten overeenkomen. Voor historische 5.1-aanroepen zonder configuratie en zonder start blijft de oorspronkelijke canonical payload {Name,ProfileId} behouden.

Binnen BEGIN IMMEDIATE: echte actor controleren, bestaande intentie opzoeken vóór profielopnieuwlezen, anders profiel en snapshot opslaan. Bij Start=true tevens status Open, één PrizeTypes-configuratieregel, LastProfileId, audit en EventChanged-outbox vastleggen. Creatie start op revisie 1. Alles commit samen; elke fout rolt event, prijsconfiguratie, voorkeur, audit/outbox en idempotentierecord terug. Retry retourneert dezelfde EventId zonder nieuwe bijwerkingen, ook na profielwijzigingen. Zelfde sleutel/andere payload geeft conflict; gelijktijdige schrijvers kunnen geen dubbel evenement maken. Een andere actor heeft bewust een eigen intentiescope.

Profielen zijn kopieën. LastProfileId blijft NULL tot eerste geslaagde start; COALESCE naar builtin-cybertruck bij eerste gebruik of verdwenen eigen profiel. De voorkeur wordt in de starttransactie bijgewerkt (laatste commit wint); GET, concept, SaveProfile, Reopen en retries veranderen hem niet. Profielseeds worden niet opnieuw overschreven.

### Mutaties, revisie en statussen

PrizeOperations gebruikt bestaande scope (EventId,ActorScope,OperationKind,UUID). Payloadhash omvat route-id, verwachte Version, actie en relevante naam/configuratie. Na actorvalidatie eerst intentiecontrole: gelijke retry geeft huidige toestand van hetzelfde event terug; het oorspronkelijke commitresultaat wordt niet opnieuw uitgevoerd. Verschil geeft conflict. Pas bij nieuwe intentie geldt WHERE Id=$id AND Version=$expected, met verhoging naar Version+1. Audit en EventChanged-outbox worden in dezelfde BEGIN IMMEDIATE-transactie geschreven. SaveProfile geeft bij retry hetzelfde nieuwe ProfileId. Dit geeft geen stale snapshot bij herhaling; eerdere andere wijzigingen blijven zichtbaar.

| Actie | Toegestane toestand | Effect |
| --- | --- | --- |
| Start | Draft, geldige configuratie | Open, prijsconfiguratie, laatste profiel |
| Close | Open, voor PurgeAt | Closed; ClosedAt=now; PurgeAt eenmaal vastleggen |
| Reopen | Closed; PurgeAt bestaat en ligt in toekomst | Open, datums/voorraad/historie behouden |
| Rename | Draft/Open/Closed; voor PurgeAt | Alleen naam, revisie/audit |
| Configure | Draft/Open; geen groep/deelnemer/trekking of Reserved/Issued | Snapshot en getypeerde kolommen samen; Open-prijstype synchroniseren |
| SaveProfile | Draft/Open/Closed; voor PurgeAt | Nieuw BuiltIn=0-profiel; eventrevisie, geen settingswijziging |

Purging en verstreken PurgeAt wijzen nieuwe mutaties af. Lifecycleacties vereisen expliciete bevestiging. Een hernieuwde Close overschrijft ClosedAt, maar nooit eerdere PurgeAt. Ontbrekend event is op detailroute 404; fout formulier/configuratie 400, echte ongeautoriseerde actor geweigerd, stale/ongeldige overgang/afwijkende intentie 409. SQLITE_BUSY/LOCKED in mutaties geeft retrybaar PrizeBusy-conflict (409); onomgezette busy bij profielopslag geeft 503 met Retry-After. Caller bewaart dezelfde sleutel en payload; geen verborgen automatische SQL-retries.

### Prijsconfiguratie, isolatie en grenzen

Eén voorbereid prijstype wordt bij eerste start aangemaakt; Total mag NULL zijn, Reserved/Issued blijven 0, geen ledgerregels. FixedPrizeTypeId verwijst via bestaande samengestelde FK naar dezelfde EventId. Ook Random heeft in 5.2a slechts dit voorbereide type; uitbreiding naar meerdere prijstypen/selectie volgt 5.3. Bij herconfiguratie alleen update met Reserved=Issued=0; bestaande gereserveerde voorraad kan niet worden overschreven. Bij onverwachte reeds aanwezige prijstypen zonder pointer stopt start veilig.

Alle oude constraints blijven gelden (groep 1..200, winnaars <= groepsgrootte, voorraad niet negatief, animatie 5/10/15, publiek verplicht startcodes, retentie 1..90). Reactieseconden maximaal 86400; postclose maximaal 90 dagen; de UI gebruikt hele minuten. Er wordt uitsluitend een schema voor latere persoonsgegevens geconfigureerd, geen persoonsgegevens verzameld.

PrizeEnabled is in nieuwe beheerconfiguraties en alle gewijzigde events 0; de ingebouwde historische sjabloonwaarden blijven intact. Er is geen opname van deelnemers, codeverbruik, trekking, claim, SignalR-publisher of automatische purge. Outbox blijft opslag voor later; er is geen client die verborgen uitslagen kan ophalen.

### Herstel, behoud en acceptatiepoort

Bestaande 5.1-baseline gecontroleerd. Nieuwe baseline vóór wijzigingen: C:/Example/Baselines/phase5-52a-20261030-221240, met geverifieerd bronarchief/hashes, consistente SQLite-onlinebackup en keyring/restore-instructie. Live ontwikkel-database is niet gemigreerd of door de nieuwe build gestart. Losse tests gebruiken tijdelijke databases en gescheiden output. De database blijft leidend; na browserherladen komt actuele revisie uit SQLite, nooit uit clientstaat.

DemoStore, PrizeMigration, bestaande WebAuthn-implementatie, accountverwijderpaden en statistiekservices zijn in 5.2a ongewijzigd. Alle accountverwijderpaden uit A10 blijven een harde acceptatiepoort vóór deelnemersregistratie in 5.2b, inclusief monitorverwijdering SessionStatusService.GetAsync. Herstel-login, duurzame claimbinding en purge zijn nog niet geleverd. Historische securitydocumentatie beoordeelt bron na 5.1 en is geen security-assuranceclaim over deze uitbreiding.

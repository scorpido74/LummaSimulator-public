# Codex-rapport - LummaSimulator v0.5 werkpakket 5.2a

Datum: 30 oktober 2026. Status: **5.2a afgerond; 5.2b niet gestart**.

## Uitgevoerde werkzaamheden

Evenementbeheer en herbruikbare evenementprofielen toegevoegd aan de bestaande Passkey Demo Portal. Overzicht, details, concept, snelle start, basis/geavanceerde instellingen, sluiten, heropenen en expliciete profielkopie zijn beschikbaar voor bevoegde admin/presenter. Eerste standaardprofiel is Cybertruck; laatst succesvol gestart profiel wordt installatiebreed onthouden. Alleen een gecommitte eerste start wijzigt die voorkeur.

De bestaande autorisatie, WebAuthn, repositories en vaste migratie 6 zijn het uitgangspunt. Creatie en mutaties gebruiken servervalidatie, transactionele actorcontrole, idempotentie, revisies, audit en outbox. Een verschil tussen route-event en formulier-event wordt afgewezen. Meerdere presentatoren mogen dezelfde installatie-evenementen beheren; er is geen eigenaar-/tenantmodel toegevoegd.

Iedere beheerstart houdt PrizeEnabled=0. Er zijn geen publieke Prize-routes of bezoekersregistratie, startcodeverbruik, groepsdeelname, trekking, claims, SignalR, QR of prijsuitgifte. Eén voorbereid prijstype is uitsluitend configuratie: geen reservering, uitgifte of voorraadledger. Configuratieopties voor toekomstige functies activeren die functies niet.

## Herstelbaseline en behouden gegevens

De 5.1-baseline is beschikbaar en gecontroleerd:
C:/Example/Baselines/phase5-51-20261030-210247.

Nieuwe baseline vóór wijzigingen:
**C:/Example/Baselines/phase5-52a-20261030-221240**.

136 oorspronkelijke bron-/documentatiebestanden, source.zip met geverifieerde SHA-256-hashes, consistente SQLite-onlinebackup database-0.db, database-manifest.json, keys-0 en RESTORE.txt. Geen database-upgrade of bestaande serverrestart. Herstel: server stoppen, actuele toestand apart veiligstellen, bron naar nieuwe map uitpakken en hashes controleren; zo nodig bijbehorende DB/keyring onder oorspronkelijke DPAPI-context volgens manifest terugplaatsen. Backups bevatten authenticatiemateriaal; niet verspreiden.

Eindcontrole op live DB via uitsluitend read-only SQLite-verbinding: **alle 18 tabelinhouden en het schema zijn gelijk aan de baseline**. Markeringen blijven 3/4/5, integrity_check=ok en geen FK-overtredingen. Accounts, credentials, sessies en statistieken zijn behouden. PrizeMigration.cs, DemoStore.cs, DemoService.cs, SessionStatusService.cs en PresenterAuthentication.cs zijn byte-identiek met de baseline. Geen nieuwe databaseobjecten of migratie.

## Gewijzigde bestanden

Onderstaande lijst volgt uit SHA-256-vergelijking tegen de baseline; de projectmap is geen Git-working-tree. Gegenereerde bin/obj-uitvoer en testlogs tellen niet als bronbestanden. Geen bronbestanden verwijderd.

Bestaand gewijzigd:

- LummaSimulator.Web/Models/PrizeModels.cs
- LummaSimulator.Web/Pages/Presenter/Index.cshtml
- LummaSimulator.Web/Program.cs
- LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx
- LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx
- LummaSimulator.Web/Resources/Localization/UiText.resx
- LummaSimulator.Web/Services/Prizes/PrizeStore.cs
- LummaSimulator.Web/wwwroot/css/site.css
- docs/LummaSimulator-v0.5.md
- docs/phase5-prize-experience-architecture.md
- docs/phase5-prize-experience-functional.md
- docs/phase5-prize-experience-implementation.md
- tests/LummaSimulator.Web.Tests/HttpChecks.cs
- tests/LummaSimulator.Web.Tests/Program.cs

Nieuw:

- LummaSimulator.Web/Models/PrizeEventModels.cs
- LummaSimulator.Web/Pages/Presenter/Prizes/Event.cshtml
- LummaSimulator.Web/Pages/Presenter/Prizes/Event.cshtml.cs
- LummaSimulator.Web/Pages/Presenter/Prizes/Index.cshtml
- LummaSimulator.Web/Pages/Presenter/Prizes/Index.cshtml.cs
- LummaSimulator.Web/Pages/Presenter/Prizes/New.cshtml
- LummaSimulator.Web/Pages/Presenter/Prizes/New.cshtml.cs
- LummaSimulator.Web/Pages/Presenter/Prizes/PrizeEventInput.cs
- LummaSimulator.Web/Pages/Presenter/Prizes/PrizePageModel.cs
- LummaSimulator.Web/Pages/Presenter/Prizes/_Settings.cshtml
- LummaSimulator.Web/Services/Prizes/PrizeEventService.cs
- LummaSimulator.Web/Services/Prizes/PrizeEventStore.cs
- docs/codex-report.md
- tests/LummaSimulator.Web.Tests/PrizeEventChecks.cs
- tests/LummaSimulator.Web.Tests/PrizeEventHttpChecks.cs

## Feitelijke tests

| Uitvoering | Resultaat |
| --- | --- |
| dotnet build regressieproject --no-restore, aparte output, definitief -m:1 | Geslaagd; 0 waarschuwingen, 0 fouten |
| Volledige Web.Tests-run inclusief HTTPS en software-WebAuthn | **547 checks geslaagd**, exit 0 |
| node --test passkeys/localization/presenter | **43 geslaagd**, 0 mislukt, exit 0 |
| Volledige SmokeTests-run | Geslaagd, exit 0; o.a. 24 receiverchecks en 2778 ATT&CK/status/exportasserties |
| WPF Verification-run | **20 PASS-controles**, exit 0 |

De nieuwe 5.2a-tests omvatten profielselectie/last-used/terugval, profielkopie en snapshotbehoud, creatie, identieke/afwijkende intenties, rollback bij startfout, onafhankelijke gelijktijdige verbindingen en presentatoren, autorisatie/intrekking, lifecycle/bevestiging, bewaartermijn, configuratiefreeze, cross-event-isolatie, HTTP-CSRF/Origin, HTML-encoding, instellingen en ontbrekende publieke routes. Bestaande migratie-/behoud-/statistiek-/WebAuthn-tests zijn volledig mee uitgevoerd.

De eerste parallelle build had vier tijdelijke MSB3026-kopieerwaarschuwingen door gedeelde helperoutput; definitieve sequentiële build heeft nul waarschuwingen. Geen projectwijziging, package-installatie of restore. Geen fysieke authenticator- of visuele browseracceptatie geclaimd.

Bewijs in verification/bin:
- phase5-52a-final-build.log
- phase5-52a-web-tests.log
- phase5-52a-js-tests.log
- phase5-52a-smoke-tests.log
- phase5-52a-wpf-tests.log
- phase5-52a-source-diff.json

Build-output: verification/bin/phase5-52a-regression. Tests gebruiken gescheiden tijdelijke databases; simulator/WPF gebruiken aparte phase5-52a-runtime-paden. Het zijn expliciete executable-runners, geen extra dotnet test-claim.

## Problemen, ontwerpgrenzen en risico's

- Geen live deployment: het bestaande ontwikkelschema is nog 5. Start de nieuwe app daar uitsluitend na expliciete toestemming voor de bestaande schema-6-upgrade en backup.
- SQLite op één host blijft de opslagarchitectuur. Conflicten leveren een zichtbare fout; herhaal met dezelfde intentiesleutel/payload. De bestaande managementlimiter is gedeeld.
- Alle nieuwe of gewijzigde beheerconfiguraties blijven disabled. De oorspronkelijke ingebouwde enabled-sjabloonwaarden zijn niet gewijzigd; dit is geen activering.
- Instellingen wijzigen alleen Draft/Open zonder groepen/deelnemers/trekkingen/gereserveerde of uitgegeven voorraad. Closed is beperkt tot naam/profielkopie/heropenen; verlopen/Purging is alleen-lezen.
- Post-close-claimperiode gebruikt in de UI hele minuten, opslag blijft seconden. Effectieve claims moeten later altijd door PurgeAt worden begrensd; claims zijn nu niet geïmplementeerd.
- Multi-prijstypen, trekkingengine, claimbinding, herstellogin, automatische purge en realtime blijven latere werkpakketten.
- De security-assurancedocumenten zijn een historisch brononderzoek na 5.1. Dit rapport geeft functioneel regressiebewijs en is geen securitycertificering voor de nieuwe uitbreiding.
- Fysieke passkeys, visuele/mobiele bediening, meerdere echte machines en belastingacceptatie blijven open.

## Aanbevolen vervolgstap en stopgrens

Werkpakket 5.2a is afgerond. Geen deelnemersregistratie of 5.2b geïmplementeerd.

5.2b kan na expliciete opdracht beginnen met de retentieguard en bijbehorende lifecycle-tests. **Alle** verwijderpaden moeten vóór deelnemersregistratie worden aangepast: DemoService.OwnedCoreAsync, CleanupCoreAsync inclusief startup/dashboard/Create, EndOwnAsync, EndAsPresenterAsync, CreateAsync(previousToken), accountcap en **SessionStatusService.GetAsync**. Niet-deelnemers behouden bestaand gedrag. Publieke deelname mag pas actief worden na bewezen retentie-/herstel-/claimketen. De live database blijft zonder afzonderlijke toestemming ongemigreerd.

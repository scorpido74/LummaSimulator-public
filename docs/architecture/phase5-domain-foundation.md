# DemoEvent en DemoSession — eerste domeinbasis

> Vervolgstatus 1 november 2026: [het geconsolideerde lifecycleontwerp](phase5-event-lifecycle-design.md) en [het integratieplan](phase5-event-integration-plan.md) leggen de nu besloten doelarchitectuur vast. De hieronder genoemde open hoofdkeuzes zijn historische pakketcontext; de twee geïmplementeerde modellen en de beschreven tests zijn ongewijzigd. Lifecycle, opslag, capabilities, klokblokkades, auditintegriteit en retentieprofielen zijn nog niet in runtime aangesloten.

Datum: 31 oktober 2026. Branch: `feature/phase5-domain-foundation`, gestart vanaf analyserapportcommit `3597107`. Status: eerste in-memorydomeinmodellen toegevoegd; geïsoleerde volledige build en regressie geslaagd. Geen database- of runtime-integratie.

## Scope en broncontrole

Uitgangspunt: [architectuurreview](phase5-architecture-review.md), vooral §6 en open besluiten D01–D05, [functionele requirements](../functional-requirements.md), [hybride requirements](hybrid-deployment-requirements.md), Prize-architectuur A10/A12 en de securitygrenzen uit AGENTS.md.

Vooraf zijn de modellen, services en migratie-DDL onderzocht. DemoEvent/DemoSession bestonden niet. Wel aanwezig: DemoAccount/DemoRunId in DemoModels.cs, EventSummary/ExperienceEvents in ExperienceStatistics.cs, PrizeManagedEvent/PrizeEvents en aparte authenticatiesessies. Deze hebben andere verantwoordelijkheden en worden behouden; geen duplicerende account-, credential- of Prize-opslag toegevoegd. Gedeelde naamvalidatie volgt de bestaande grens van 100 tekens, trimmen en afwijzen van control characters uit Prize-eventbeheer, zonder een dependency vanuit de nieuwe modellen op die servicelaag.

De database is uitsluitend via `DemoStore.cs`, `ExperienceStatistics.cs`, `PresenterAuthentication.cs`, `PresenterLoginMigration.cs` en `Services/Prizes/PrizeMigration.cs` beoordeeld. Geen actieve SQLite-database geopend, gekopieerd, gequeryd of gewijzigd. De werkelijk geïnstalleerde schemaversie is niet vastgesteld. Migratiegeschiedenis en v6-schemafingerprint zijn ongewijzigd.

## Modellen

Beide modellen staan als onveranderlijke sealed records met get-only properties in `LummaSimulator.Web/Models`, conform de bestaande locatie voor portalmodellen. Geen nieuw project, package, linked-sourcecontract of afhankelijkheid vanuit WPF op Web.

| Model | Velden en invarianten |
| --- | --- |
| DemoEvent | Id: niet-lege Guid; Name: getrimde naam van 1–100 tekens zonder control characters; CreatedAt: expliciet aangeleverd tijdstip, genormaliseerd naar UTC. |
| DemoSession | Id: niet-lege Guid; CreatedAt: expliciet aangeleverd tijdstip in UTC; DemoEventId: optionele niet-lege Guid. Null betekent geen eventkoppeling. |

De constructor maakt geen ID aan, raadpleegt geen klok en voert geen I/O uit. De caller levert identiteit en tijdstip; daardoor zijn herstel en tests deterministisch. De optionele eventreferentie is alleen een domeinrelatie, geen gecontroleerde foreign key, bestaansbewijs of toegangsbevoegdheid. Een toekomstige service moet eventbestaan en actorrechten controleren.

DemoSession is een demonstratiecontext, geen login-/cookie-/bezoekerssessie. Het nieuwe Id vervangt geen bestaande portal- of WPF-DemoRunId. Er is nog geen conversie, automatische correlatie of import. Namen zijn eventlabels; gebruik uitsluitend synthetische labels in tests en neem geen persoonsnamen of geheimen op.

## Bewuste grens en compatibiliteit

De architectuur liet lifecyclewaarden, eigenaar/delegatie, opslagvariant A/C en backfill nog open. Dit pakket legt die keuzes niet vast: geen statusenum, lifecycletransities, retentieperiode, revisiecontract, ownerrol of databaseattributen toegevoegd. Ook geen automatische sessiestart, nieuwe route, DI-service, repository, schema, migratie of cleanup.

De modellen worden nog niet gebruikt door de bestaande runtimeflows. Authenticatie, bezoekersaanmaak/-verwijdering, statistiektellingen, PrizeEnabled-gates, monitor en WPF blijven hetzelfde gedrag houden. Bestaande eventmodellen worden niet hernoemd of vervangen. Een zuiver in-memorymodel vergt nog geen keuze tussen uitbreiding van ExperienceEvents en een nieuwe overkoepelende tabel.

## Verificatie

`DomainModelChecks.Run` is aangesloten op de bestaande dependencyvrije Web.Tests-runner. De nieuwe controles gebruiken alleen geheugen: optionele eventkoppeling, UUID-grenzen, naamvalidatie en het behoud van een tijdstip bij UTC-normalisatie. Geen nieuwe testdatabase of portal nodig voor de modelchecks zelf.

De volledige build en regressie zijn uitgevoerd in een nieuwe bronkopie onder genegeerde `artifacts`, met alleen Git-bronbestanden. TEMP/TMP, APPDATA/LOCALAPPDATA en testdata zijn voor de regressieprocessen binnen die nieuwe map ingesteld; `LUMMA_TEST_WEB_DLL` wees expliciet naar de eigen portalbuild. Bestaande regressies oefenden uitsluitend hun nieuwe synthetische fixtures en fixture-cleanup uit. Geen operationele cleanup, bestaande portalstart of bestaande databasehandeling.

Werkelijke resultaten op 31 oktober 2026:

| Controle | Resultaat |
| --- | --- |
| `dotnet restore LummaSimulator.slnx --locked-mode` | Geslaagd voor alle zeven projecten, exitcode 0. Geen lockfile- of packagewijziging. |
| `dotnet build LummaSimulator.slnx --no-restore` | Geslaagd voor alle zeven projecten, 0 waarschuwingen en 0 fouten. |
| `dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj --no-restore` | Volledige bestaande orchestrator geslaagd, exitcode 0. |
| Web-services, configuratie en HTTPS-integratie | 566 controles geslaagd, inclusief 13 nieuwe pure modelcontroles. |
| Node UX | 43 tests geslaagd; 0 mislukt, geannuleerd of overgeslagen. |
| SmokeTests | Geslaagd, inclusief 24 lokale receiver-lifecyclechecks en 2778 ATT&CK-mapping/status/exportasserties. |
| Windows WPF Verification | Geslaagd, inclusief volledige simulatie, Navigator, monitor en sidecar-shutdown. |

Logs: `artifacts/domain-foundation-be532b8f5d634888872dc471f222c39b/restore.log`, `build.log` en `regression.log`; bronkopie in de bijbehorende `work`-map. Deze artefacten blijven genegeerd en worden niet gecommit. Geen publishcontrole uitgevoerd: publish-/sidecarintegratie is niet gewijzigd. Geen hardware-/mobiele-/productie-TLS-/MDR-acceptatie geclaimd.

Gewijzigde bronbestanden: `LummaSimulator.Web/Models/DemoEvent.cs`, `DemoSession.cs`, `tests/LummaSimulator.Web.Tests/DomainModelChecks.cs` en één aanroep in `tests/LummaSimulator.Web.Tests/Program.cs`; documentatie in dit bestand. Bestaande services/migraties, configuratie, pagina's, WPF, shared en sidecar zijn via Git-diff ongewijzigd. Laatste kandidatencontrole vond dezelfde tien eerder beoordeelde configuratie-/fixturetreffers en geen nieuwe secrets of persoonsgegevens. Deze patrooncontrole is geen volledige securityaudit.

## Conflicten en volgende stap

Geen bestaande functie is omgezet naar het nieuwe model. De nog open semantiek van DemoEvent versus ExperienceEvent/PrizeEvent en DemoSession versus de twee bestaande DemoRunIds blijft een integratierisico voor later, geen opgelost migratieprobleem. De huidige eventautorisatie en verspreide retentiepaden zijn met deze modelslice niet veranderd.

Volgende stap: het minimale contract reviewen en D01–D05 expliciet vastleggen, inclusief hoe bestaande run-ID's aan de nieuwe sessiecontext worden gekoppeld. Ontwerp daarna de versiegestuurde migratiebasis en centrale retentieguards volgens R1, vóór opslag of publieke deelname wordt aangesloten. Geen directe migratie of koppeling van bestaande accounts op basis van alleen deze modellen. Terugdraaien van dit pakket vergt alleen het terugnemen van modelbestanden, testaanroep/-bestand en deze documentatie; geen dataherstel of passkeywijziging.

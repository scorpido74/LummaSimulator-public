# LummaSimulator — projectanalyse en opschoningsplan

Datum: 31 oktober 2026. Scope: `C:/src/LummaSimulator`. Status: uitsluitend analyse; geen toestemming tot opschoning.

## 1. Samenvatting en beslisregels

De projectmap bevat vóór dit rapport **2.317 bestanden, 910.178.165 bytes (868,01 MiB)**. Daarvan staat **908.858.514 bytes (866,76 MiB)** onder `bin`/`obj`. Dat is geen bedrag dat zonder meer veilig verwijderd kan worden: hieronder staan een actieve portalinstallatie, historische bewijsstukken en een databaseback-up met keyring.

De oplossing is functioneel samenhangend. Geen compleet bronproject is aantoonbaar overbodig. Vooral `verification` moet blijven: het bevat een actief WPF-acceptatieproject en een afzonderlijk publishverificatiescript. Het grootste opschoningspotentieel zit in herhaalde oude buildsets, niet in broncode.

**Prioriteiten:** bescherm eerst runtimegegevens en herstelmateriaal; behoud actieve bron en testinfrastructuur; archiveer historisch bewijs; valideer reproduceerbaarheid voordat oude binaries verdwijnen. Initialiseer Git pas na controle van de staginglijst en uitsluiting van gegevens/geheimen. De huidige `.gitignore` is onvoldoende voor alle aangetroffen soorten bewijs- en runtimebestanden.

| Categorie | Betekenis in dit rapport |
| --- | --- |
| A — veilig te verwijderen | Reproduceerbare intermediates zonder aangetroffen runtimefunctie. Alleen na afzonderlijke opschoningsopdracht en buiten een buildproces verwijderen. |
| B — waarschijnlijk overbodig | Output of voorbereide code waarvoor rebuild, procescontrole of inhoudscontrole nog nodig is. Nog niet vrijgegeven voor verwijdering. |
| C — archiveren | Historisch bewijs/documentatie; een nieuwe test kan een nieuw resultaat maken, niet het oorspronkelijke bewijs reconstrueren. |
| D — behouden | Actieve bron, configuratie, migratie, documentatie, tests en noodzakelijke distributiecomponenten. |
| E — beschermd | Database, keyring, back-up, credentials en gegevens waarvan gevoeligheid of gebruik nog niet voldoende is uitgesloten. Afzonderlijke gegevensopdracht vereist. |

Een beschermd bestand binnen een buildmap maakt de bovenliggende map ongeschikt voor blinde recursieve verwijdering. Een gitignore-regel is geen toestemming om bestanden te wissen.

## 2. Methode, controles en beperkingen

Recursieve inventarisatie met verborgen bestanden; bestandenaantallen en logische groottes; inspectie van solution/project/targets, bronnen, routes, testaanroepen, documentverwijzingen, configuratiepaden en bestandsduplicaten. Geen reparsepoint-mappen gevonden. Byte-identieke bestanden zijn met SHA-256 onderzocht. Twee open portal-logbestanden konden niet worden gehasht; daarom is geen volledige integriteitsbaseline van de hele map bewezen.

Alleen dit rapport wordt aangemaakt. Geen bestaande bron/configuratie gewijzigd, geen applicatie gestart of gestopt, geen database geopend of gemigreerd, geen dependencies geïnstalleerd, geen Git geïnitialiseerd en geen verwijder-/verplaatsactie uitgevoerd. De runtimecontrole betrof alleen bestandsmetadata en proces-executablepaden, niet database-inhoud, sleutelinhoud of volledige procescommandlines.

| Controle | Uitkomst / beperking |
| --- | --- |
| .NET SDK / Node | `dotnet --version`: 10.0.403; `node --version`: v24.20.0. Geen restore uitgevoerd. |
| JavaScript-regressie | `node --test tests/WebAuthn.Ux.Tests/passkeys.test.cjs tests/WebAuthn.Ux.Tests/localization.test.cjs tests/WebAuthn.Ux.Tests/presenter.test.cjs`: **43 geslaagd, 0 mislukt, 0 overgeslagen**, exitcode 0. Vooraf op schrijf-/procesacties onderzocht; leest bronnen en gebruikt gemodelleerde browser-API's. |
| Structurele parsing | Bron-JSON en XML-achtige `.csproj`, `.slnx`, `.targets`, `.resx`, `.xaml` buiten buildmappen geparsed: geen parsefouten. Dit vervangt geen compiler-/WPF-/Razorcontrole. |
| Builds/.NET-runners | Niet uitgevoerd: schrijven outputs/testdata, starten deels childservers/browser/WPF, terwijl uitsluitend het rapport aangemaakt mag worden. Ook `--no-restore` maakt een build niet read-only. |
| Portal | Niet gestart of herstart. Bron `Program.cs` roept initialisatie/migratie en startupcleanup aan; starten is ongeschikt als read-only controle. |
| Bestaand proces | `LummaSimulator.Web`, PID 39836, draaide bij inspectie vanuit `LummaSimulator.Web/bin/Debug/net10.0/LummaSimulator.Web.exe`. Een momentopname, geen toekomstige vrijgave. |
| Vergrendelde bestanden | `verification/bin/prize-http400-portal.stderr.log` en `.stdout.log` niet hashbaar wegens open gebruik. Niet verwijderen/archiveren zolang actief. |
| Hardware/operationeel | Geen YubiKey-, Windows Hello-, mobiel-, TLS-, MDR- of volledige browseracceptatie uitgevoerd. Geen nieuwe groene .NET-testclaim. |
| Git | Geen `.git` aangetroffen; geen commit/diff beschikbaar. Geen `AGENTS.md` aangetroffen. |

Groottes zijn ongecomprimeerde bestandslengtes, geen fysieke schijftoewijzing. Metingen kunnen door het reeds draaiende proces veranderen. Het nieuwe rapport is niet in de beginsom opgenomen. Buiten de projectmap zijn uitsluitend de door code aangewezen standaardruntimegegevens op metadata gecontroleerd; externe baselines zijn documentverwijzingen, geen opnieuw gecontroleerde herstelgarantie.

## 3. Projectstructuur, omvang en relevantie

| Rootonderdeel | Bestanden | Bytes | Functie / categorie |
| --- | ---: | ---: | --- |
| `LummaSimulator.App` | 59 | 863.059 | WPF-presentator/simulator; bron D, outputs nader classificeren. |
| `LummaSimulator.Web` | 188 | 49.875.955 | Zelfstandig passkey-/beheerportal; bron D, actieve uitvoermap behouden. |
| `tests` | 219 | 48.010.468 | .NET/Node-regressie en synthetische fixtures; bron D. |
| `verification` | 1.798 | 810.495.746 | Actieve acceptatiebron plus oude builds, logs, testdata en beschermde back-up; gemengd D/B/C/E. |
| `tools` | 26 | 465.795 | Productie-sidecar LocalTestServer; bron D. |
| `shared` | 3 | 4.586 | Gedeelde linked source en buildtargets; D. |
| `docs` | 21 | 448.791 | Functionele/technische bron, historie, referenties/security; D/C. |
| `README.md` | 1 | 13.087 | Bedienings-/buildinformatie, D; historische passages later verduidelijken. |
| `LummaSimulator.slnx` | 1 | 578 | Actieve solution, D. |
| `.gitignore` | 1 | 100 | Bestaande basisuitsluiting, D; voorstel hieronder. |

Belangrijke bronsubmappen: Web/Pages 77.482 bytes (waarvan Presenter 52.069), Web/Services 142.387 (waarvan Prizes 56.765), Web/Resources 166.755, Web/wwwroot 49.975, docs/security 86.053 en docs/references 36.909. Subtotalen overlappen en mogen niet bij de rootcijfers worden opgeteld. Exacte outputpaden/groottes staan in de actietabel.

Geen `TestResults`, `.vs`, `node_modules`, download/cache-root, `.git`, ZIP-back-up of installerproject binnen de inventaris aangetroffen. Wel SDK-caches in obj, native NuGet-runtimekopieën, gepubliceerde executables en historische artifacts. Geen bewijs dat er een aparte ondersteunde installer of complete formele release bestaat.

## 4. Projecten en afhankelijkheden

De `.slnx` bevat zes projecten. Het zevende, LocalTestServer, wordt transitief via gedeelde targets gebouwd. Geen orphan-project gevonden.

| Project | Framework / rol | Afhankelijkheden en bereikbaarheid |
| --- | --- | --- |
| `LummaSimulator.App/LummaSimulator.App.csproj` | net10.0-windows, WPF WinExe, versie 0.4.0 | Linked RuntimePaths/LocalTestServerProtocol; import LocalTestServer.Build.targets. Geen Web-projectreferentie. |
| `LummaSimulator.Web/LummaSimulator.Web.csproj` | net10.0, ASP.NET Core, versie 0.4.1 | Linked RuntimePaths; Fido2.AspNet 4.3.1/Microsoft.Data.Sqlite 10.0.14; geen WPF-referentie. |
| `tests/LummaSimulator.Web.Tests/LummaSimulator.Web.Tests.csproj` | net10.0 executable | ProjectReference Web; testassembly heeft InternalsVisibleTo. Verifieert echte FIDO2-library met softwareauthenticator en geïsoleerde HTTPS-fixtures. |
| `tests/LummaSimulator.SmokeTests/LummaSimulator.SmokeTests.csproj` | net10.0 executable | Compile-linkt actieve App-bestanden en resources; importeert sidecartargets. Geen vergeten kopieën. |
| `tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj` | eigen VSTest-orchestrator, Windowsconditionering | ProjectReferences naar Web.Tests en op Windows SmokeTests/Verification; roept vier runners aan. Geen lege nutteloze testassembly: EnableDefaultCompileItems=false is bewust. |
| `verification/Verification.csproj` | net10.0-windows, WPF executable | ProjectReference App, sidecartargets; in solution én Windows-regressie. |
| `tools/LummaSimulator.LocalTestServer/LummaSimulator.LocalTestServer.csproj` | net10.0 executable | Linked protocol; build/publish via shared targets. Nodig voor lokale HTTP-telemetrie en acceptatie. |

`shared/LocalTestServer.Build.targets` kopieert na Build de helper naar `$(TargetDir)LocalTestServer` en publiceert hem na Publish met dezelfde RID/deploymentmode. Verwijder geen helper uit een gebruikte distributie. Namespacegroepering App.Models/Services/ViewModels/Infrastructure en Web.Models/Services/Pages/Localization komt terug in constructors, DI, XAML, Razor en tests. WPF en portal communiceren via beperkt HTTPS-metadatacontract, niet door een gedeelde database.

Externe afhankelijkheden volgens huidige Web-lockfile: Fido2/Fido2.Models 4.3.1, libsodium 1.0.20.1, Microsoft.Bcl.Memory 9.0.14, Microsoft.Bcl.TimeProvider 8.0.1, Microsoft.Data.Sqlite.Core 10.0.14, IdentityModel-componenten 8.2.0, NSec.Cryptography 25.4.0, SQLitePCLRaw core/bundle/provider/native 2.1.13. Native assets voor meerdere platforms verklaren veel outputomvang; niet willekeurig platformbestanden verwijderen uit een actieve output. Geen netwerk-vulnerabilitycheck uitgevoerd. Bewaar alle drie `packages.lock.json`-bestanden (Web, Web.Tests, RegressionTests).

Runtime/testafhankelijkheden buiten NuGet: Windows/WPF voor visuele acceptatie, Node voor UX en echte browserfixture, Chrome of `LUMMA_TEST_BROWSER` voor PrizeBrowserChecks, .NET runtime of self-contained publicatie, TLS/OS-authenticators voor echte passkeys. Geen npm-installatie nodig voor de huidige Node-tests. SVG-logo's worden lokaal geserveerd; herkomst staat in `wwwroot/images/README.md`. Bewaar logo's, herkomst en ATT&CK-licentie.

## 5. Ongebruikte code, duplicaten en veroudering

Een tekstuele type-/methodereferentiescan over bron, XAML, Razor, project- en targetsbestanden is uitgevoerd. Dit is geen compilergebaseerde callgraph: reflectie, frameworkconventies, resources, markup en externe integratie maken 'geen C#-aanroep' onvoldoende bewijs.

| Onderdeel | Bewijs en interpretatie | Advies |
| --- | --- | --- |
| `Web/Models/PrizeModels.cs`: PrizeParticipantState, PrizeStartCodeState, PrizeDrawStatus, PrizeDrawKind, PrizeLedgerKind | In de onderzochte bron elk alleen als declaratie aangetroffen. Aantoonbaar geen tekstuele consumenten, maar onderdeel van voorbereid Prize-v6-model/volgende werkpakketten. Geen aantoonbaar waardeloze code. | B als onderzoekspunt, **behouden** met Prize-contract; niet verwijderen in bestandsopschoning. |
| `Web/Localization/LocalTextTagHelper.cs` | Type slechts één keer benoemd, maar via `@addTagHelper *, LummaSimulator.Web` en `<local-text>` framework-discovery actief. | D; geen dead code. |
| Razor `OnPost*Async`-handlers | Meerdere methoden slechts bij declaratie gevonden; Razor handlernamen/formulieren/router activeren ze. | D; niet verwijderen op referentieaantal. |
| `PrizeStore.DefaultProfileAsync` / `RenameDraftAsync` | Oudere repositoryfuncties naast nieuwe Management-methoden; nog expliciet aangeroepen door PrizeStoreChecks. Niet ongebruikt. | D; eventuele consolidatie is aparte refactoring met tests, geen opschoning. |
| `DemoStore.InitializeLegacyAsync`, PresenterLoginMigration, PrizeMigration | Gebruikt door migratieketen en fixtures; oude schemavormen nodig voor upgrades. | D; migratiecode behouden. |
| `SessionMetadata` in App en Web | Vergelijkbare records in onafhankelijke assemblies, bewust transportcontract zonder WPF/Web-projectkoppeling. Kans op toekomstige contractdrift, geen bewijs van overbodigheid. | D; later gedeeld contract overwegen, geen nu vereiste library. |
| PresenterAuthentication/Management en PrizeStore/EventStore | Partial classes in meerdere bestanden; gedeelde implementatie, geen dubbele complete service. | D. |
| `UiText.resx` en `UiText.nl-NL.resx` | Byte-identiek, SHA-256 `51C1C134B84F910D26AC674C8BE692A7D4DB00D779981C8CD6B342E2C9D08756`. Neutrale fallback versus expliciete NL-cultuur; lokalisatietests/resource lookup kunnen beide verwachten. | B voor mogelijke deduplicatie, voorlopig D/behouden. Eerst resource-/fallbackcontract testen. |
| Linked App-code in SmokeTests / shared files | Compile Include verwijst naar dezelfde bron, geen fysieke duplicatie. | D. |
| Oude passwordflag `--hash-presenter-password` | `Program.cs` weigert expliciet met verwijzing naar bootstrap. Geen actieve wachtwoordlogin of achtergebleven debugachterdeur. | D; compatibiliteits-/foutmelding behouden. |
| `--generate-monitor-key`, `--bootstrap-administrator` | Onderhoudspaden, geen tijdelijke debugfunctie. Bootstrap heeft lokale rechtencontrole; keygenerator schrijft op verzoek geheim naar console. | D; niet als test starten. |
| `--local-server-only` en `prize-browser-form.mjs` | Gericht testpad respectievelijk echte browserfixture; aantoonbare testconsumenten. | D. |
| `App.xaml.cs` met lege partial class | WPF Application en gegenereerde markupkoppeling; geen orphan. Eventuele ongebruikte using-directives zijn geen zinvolle ruimtewinst. | D. |

Geen aanvullende complete classes/methoden zijn als veilig verwijderbaar bewezen. Geen zelfstandige experimentele bronmap of nieuwe debugprojecten aangetroffen. Voorbereide Prize-modellen en fail-closed configopties zijn geen achtergelaten experiment. Volledige semantische dead-codeanalyse vergt een latere geïsoleerde compiler-/analyzerstap; daarover wordt geen zekerheid geclaimd.

De grootste exacte duplicaten zijn dependencybinaries: onder meer `e_sqlite3.dll` en native Linux/macOS-assets komen elk in 19 buildsets identiek voor. Dat bewijst herhaalde builds, geen recht om individuele native libraries uit een gebruikte deployment weg te halen. Bronduplicaatdetectie buiten bin/obj vond alleen het genoemde resourcepaar. DOCX/Markdown zijn functioneel verwante formaten, geen byte-identieke duplicaten.

## 6. Tests en speciale behandeling van verification

| Bestand/groep | Bereikbaarheid / relevantie |
| --- | --- |
| Web.Tests/Program.cs | Roept PrizeMigrationChecks, PrizeStoreChecks, PrizeEventChecks, PresenterChecks, LocalizationChecks, ExperienceChecks, MultiCredentialChecks, SessionStatusChecks en HttpChecks aan. Alle behouden. |
| PresenterChecks → DiscoverablePresenterChecks → PresenterMigrationChecks | Indirecte keten; losse bestanden zijn niet ongebruikt doordat ze niet direct in Program staan. |
| HttpChecks → PrizeEventHttpChecks / PrizeBrowserChecks → prize-browser-form.mjs | Actuele HTTP/browserregressie voor formulier-Origin en eventbeheer; browserfixture maakt eigen tijdelijk profiel en verwijdert dat zelf. Nodig voor toekomstige acceptatie. |
| SmokeTests/Program.cs | Sequencing/logging/cancel plus LocalServerChecks, TelemetryChecks, NavigatorChecks, SessionMonitorChecks. Compile-linkt productiecode bewust. |
| WebAuthn.Ux.Tests/*.cjs | Alle drie bestanden in RegressionTests-target; 43 tests nu geslaagd. Geen echte hardware/browserclaim. |
| `verification/Program.cs` | Echte WPF-window, Start/Stop/Reset/full simulation, logging, Navigator-export, tabs en helperstart/-shutdown. Windows-regressierunner, **D**. |
| `verification/Verification.csproj` | Solution + projectreference in RegressionTests op Windows; **D**. |
| `verification/Test-LocalHttpPublished.ps1` | Handmatig script, standaard `verification/bin/local-http-publish`. Start exacte gepubliceerde helper, synthetische POST, ongeldige payload en shutdown. Niet direct door huidige VSTest-target aangeroepen; **D**. |

`tests/LummaSimulator.SmokeTests/NavigatorChecks.cs` leest `docs/references/enterprise-attack-20.0-subset.json` en `navigator-layer-format-v4.5.md` rechtstreeks. Dat zijn actieve testfixtures, geen verwijderbare oude docs.

De regressie-orchestrator gebruikt standaard `tests/.../bin/...` en `verification/bin/Debug/net10.0-windows/Verification.dll`. `HttpChecks` ondersteunt `LUMMA_TEST_WEB_DLL`; historische instructies wijzen naar phase-buildsets. Het publishscript gebruikt een bestaande outputmap. Dus output is reproduceerbaar, maar na verwijderen moet vóór opnieuw uitvoeren worden gebouwd/gepubliceerd en moeten oude omgevingsverwijzingen gecontroleerd worden.

Historische logs en `phase5-51-source-diff.json` / `phase5-52a-source-diff.json` leggen eerdere resultaten/bronvergelijking vast. `docs/security/security-assurance-report.md` noemt hashes van 5.1-logs én binaries. `codex-report.md`, v0.5 en Prize-implementatieplan verwijzen naar meerdere outputpaden. Archiveer bewijs mét originele hash en padindex; een later groen resultaat vervangt dit niet. Testdata-/runtimefolders bevatten logs, exports, dummybestanden en markers; naam alleen bewijst geen volledige privacyveiligheid. Daarom B, eerst inhoud op synthetische herkomst beoordelen.

## 7. Documentatiebeleid en tegenstrijdigheden

`docs/functional-requirements.md` is de huidige functionele bron. Prize-documentatie en UX-impactanalyse blijven op hun huidige paden. Archiefadvies is een toekomstige actie, geen verplaatsing in deze opdracht.

| Exact pad / groep | Advies en reden |
| --- | --- |
| `docs/functional-requirements.md` | D, actuele uitgangspunten; open vraag 26 niet stilzwijgend invullen. |
| `docs/phase5-ux-modernization-impact-analysis.md` | D, behouden; actuele codevergelijking/volgorde. |
| `docs/phase5-prize-experience-functional.md` | D, actieve specifieke Prize-eisen naast nieuwe hoofdrequirements. |
| `docs/phase5-prize-experience-architecture.md` | D, v6-schema/veiligheidscontract en vervolgontwerp. |
| `docs/phase5-prize-experience-implementation.md` | D, afgeronde 5.1/5.2a én nog open 5.2b–5.6; niet als volledig afgerond plan archiveren. |
| `docs/Local-HTTP-testserver.md` | D, actuele helperbediening/publishacceptatie; historische logverwijzingen behouden/latere index. |
| `docs/LummaSimulator-v0.5.md` | D, gemengde roadmap/historie, 5.2a-sectie leidend. Titel noemt slechts 5.1: later verduidelijken. Niet heel document archiveren. |
| `docs/codex-report.md` | C, 5.2a-opleveringsbewijs; voorlopig behouden op pad, later archief met verwijzingen. |
| `docs/Passkey-Portal-v0.3-plan.md` | C, expliciet historisch uitgevoerd ontwerp; verouderde solution-/loginbeschrijvingen zijn geen huidige status. |
| `docs/Passkey-Portal-v0.3.md` | C, historische werking, deels nog referentie; later archief met linkindex. |
| `docs/LummaSimulator-v0.4.md` | C, vorige architectuur/bediening; huidige helper/monitor uitbreidingen maken sommige uitspraken onvolledig. |
| `docs/LummaSimulator_functionele_requirements_v0.1.docx` | C, oorspronkelijke bron van Markdown, behouden als herleidbare referentie. Geen automatisch gegenereerd wegwerpbestand. |
| `docs/security/dependency-inventory.md` | D, offline inventaris; versies/risico's later opnieuw beoordelen, geen automatische securityvrijgave. |
| `docs/security/secure-architecture.md`, `security-requirements.md`, `security-assurance-report.md`, `security-test-plan.md`, `threat-model.md` | D, zesdelige set inclusief dependency-inventory; snapshot na 5.1, niet opnieuw actueel verklaren voor alle 5.2a/UX-wijzigingen. Bewijslinks archiveren met provenance. |
| `docs/references/attack-navigator-LICENSE.txt` | D, licentie. |
| `docs/references/enterprise-attack-20.0-subset.json`, `navigator-layer-format-v4.5.md` | D, actieve Navigator-testreferenties. |
| `README.md` | D, ingang; later huidige instructies en historie duidelijk scheiden. |
| `LummaSimulator.Web/wwwroot/images/README.md` | D, assetherkomst/merkcontext, hoort bij twee gebruikte SVG's. |

Concrete verschillen: v0.3-plan meldt aanvankelijk alleen WPF; huidige solution bevat zes projecten. Oude v0.5-passages behouden passwordlogin, nieuwere fase-4/discoverable-code vervangt die. Nieuwe UX-eisen vragen eventeigendom/delegatie, fysieke admin-YubiKey en éénklik-monitorplaatsing; huidige rolgebaseerde Prize-beheerinterface/None-attestation/oude vensterinstructies voldoen daar nog niet aan. Nieuwe sessie-einderetentie conflicteert zonder doelgebonden uitzondering met Prize-claimbewaring. Opschoning mag deze verschillen niet 'oplossen' door oude functies, evidence of schema's weg te gooien.

## 8. Database, sleutel- en gegevensbescherming

### 8.1 Aangetroffen locaties

| Locatie | Bewijs / classificatie |
| --- | --- |
| `verification/bin/prize-http400-safety-20261030-232648/portal.db` | 577.536 bytes; back-up, **E**. Geen query uitgevoerd. |
| `verification/bin/prize-http400-safety-20261030-232648/keys/` | Eén XML-keybestand, 1.887 bytes, **E**; inhoud niet gelezen. |
| `verification/bin/prize-http400-safety-20261030-232648/manifest.json` | 1.343 bytes; manifest bij database/keyring, **E**. Samen als beschermde herstelset behouden. |
| `%LOCALAPPDATA%/LummaSimulator/PasskeyPortal/portal.db` | Standaardruntimepad aanwezig, 577.536 bytes; **E**, buiten opschoningsscope. Geen uitspraak over inhoud of live schema. |
| `%LOCALAPPDATA%/LummaSimulator/PasskeyPortal/keys/` | Sleutelbestand aanwezig; **E**, oorspronkelijke Windows/DPAPI-context behouden. |
| `%LOCALAPPDATA%/LummaSimulator/Backups/phase4-20261030-115614/` | portal.db 184.320 bytes plus keyring; **E**, buiten scope. |
| `%LOCALAPPDATA%/LummaSimulator/Logs/` en `DemoRuns/` | Runlogs/Navigator-exports en drie demo-runmappen op metadata aangetroffen; niet wissen, gegevensbeleid apart bepalen (**E** voorlopig). |
| `C:/Example/Baselines/phase5-51-20261030-210247` en `phase5-52a-20261030-221240` | Documentair beschreven bron-/DB-/keyringherstelsets; **E**, niet opnieuw geïnspecteerd of gewijzigd. Ook v0.4.1-baseline wordt in docs genoemd. |

Default wordt bepaald door `shared/RuntimePaths.cs`: `%LOCALAPPDATA%/LummaSimulator`. Overrides via `LUMMA_DATA_DIRECTORY`, `Storage:DataDirectory`, `Database:Path`; logs/exports via aparte environment/settings. Relatieve paden zijn executablegebonden. Het analyseproces heeft de onderzochte overrides niet gezet; dat bewijst niet de configuratie van het reeds draaiende portalproces. Andere expliciet geconfigureerde runtimepaden blijven operationeel te bevestigen. Geen user-secrets-store geopend; Web-project heeft een UserSecretsId.

### 8.2 Wat absoluut behouden blijft

WebAuthn-public keys/credential-ID's/counters/handles in SQLite zijn accountgebonden authenticatiegegevens, geen wegwerpoutput. Presenteraccounts, keys, sessies en audit staan eveneens in SQLite. PrizeMigration v6 bevat profielen/events en voorbereid deelnemers-/draw-/claims-/presentation-/outboxmodel. `DemoStore`, `PresenterLoginMigration`, `ExperienceStatistics` en `PrizeMigration` zijn noodzakelijke migratiebron; geen losse `.sql` nodig om essentieel te zijn.

Code bevat meerdere accountdeletepaden, inclusief `SessionStatusService.GetAsync`; portal starten kan migreren en opruimen. Geen live start als validatie. `secure_delete` maakt back-ups/keyrings niet overbodig. Sleutelverlies kan beschermde payloads onleesbaar maken; database en keyring als consistente set herstellen, met oorspronkelijke context. RP-ID niet wijzigen bij structuurwijziging. E-materiaal nooit in Git of in een openbare bron-ZIP.

## 9. Opschoningsstrategie, ruimte en risico

**A:** zeven project-obj-mappen, samen **5.363.503 bytes (5,12 MiB)**. SDK-intermediates, geen aangetroffen vereiste runtimegegevens; rebuild vergt bestaande/weer beschikbare SDK/packagecache. Geen installatie of verwijdering nu. Geen A-classificatie voor een volledige binmap.

**B:** oudere outputsets en synthetische testdata na rebuild/proces-/inhoudscontrole. **C:** historische documenten, logs, source-diffmanifesten en minimaal de expliciet gehashte 5.1-regressiebinaries. **D/E:** actieve bron, actieve deployment, beschermd herstelmateriaal en open operationele logs behouden.

Ruwe bovengrens aan uiteindelijk uit de werkmap te verwijderen/verplaatsen buildmateriaal: circa **822 MiB** wanneer alle andere checks slagen en de huidige Web/bin (44,53 MiB) en beschermde safetyset (0,55 MiB) blijven. Dit is inclusief A én B én C, geen veilige onmiddellijke besparing. Bron/documentatie circa 1,26 MiB; daarop optimaliseren levert weinig op. Archiveren op dezelfde schijf levert zonder compressie geen netto schijfruimte op. Werkelijke besparing hangt af van gekozen bewijsarchief en welke outputsets nog nodig zijn.

De afzonderlijke actietabellen aan het einde specificeren ieder voorgesteld output-/bewijsactiepad. 'Verwijderen na verificatie' is voorwaardelijk en blijft B. D/E krijgen expliciet geen verwijderadvies. Er is geen generieke actie `verification/bin` of `**/bin`.

## 10. Voorstel nieuwe structuur

Eerste opschoning: huidige bronpaden behouden. Dat geeft vrijwel alle ruimtewinst zonder breuk in csproj-links, repo-rootdetectie, docs-links, resourcebenamingen of publishscripts.

```text
LummaSimulator/
  LummaSimulator.slnx
  README.md
  .gitignore
  LummaSimulator.App/                 # behouden
  LummaSimulator.Web/                 # behouden
  shared/                            # linked source/targets behouden
  tools/LummaSimulator.LocalTestServer/
  tests/                             # vier huidige testgroepen
  verification/                      # Program.cs, csproj, publishscript behouden
  docs/                              # actuele functionele/Prize/UX-docs behouden
    references/                      # actieve fixtures/licentie
    security/
    archive/                         # alleen later goedgekeurde historie + index
  artifacts/                         # optionele latere build/test/publishoutput, gitignored
```

Database/keyring/back-ups blijven in bestaande beschermde runtime-/herstellocaties. Niet voor cosmetiek verplaatsen. Een latere `src/`-indeling of verhuizing van Verification naar tests is mogelijk, maar vergt aparte wijziging van solution, relative includes, imports, test-rootdetectie, scripts en docs. Nu geen voordeel dat dit risico rechtvaardigt. Ook centrale ArtifactsPath is een latere buildwijziging: de huidige custom VSTest-target gebruikt vaste binpaden en werkt niet vanzelf met een globale alternatieve outputstructuur.

## 11. Git-inhoud en voorstel .gitignore

In Git: alle actieve `.cs`, `.xaml`, `.cshtml`, `.resx`, JS/CSS/SVG, projects/solution/targets, drie lockfiles, test-/verificatiebron, migratiebron, geschoonde gedeelde instellingen, docs/referenties/licenties. Historische docs kunnen klein en waardevol blijven. Originele log-/binary-/DB-archieven buiten Git; eventueel geschoonde evidence-index met hashes in docs.

Bewaar de bestaande gedeelde `appsettings.json`, `appsettings.Development.json`, `LummaSimulator.App.settings.json` en `Properties/launchSettings.json`: de bekeken bron bevat normale defaults en lege geheimvelden, geen reden om alle settings te negeren. Lokale overschrijvingen apart houden; zo'n nieuw lokaal bestand wordt niet automatisch door de huidige app geladen. Voor eerste commit alsnog volledige staged secret-/PIIcontrole uitvoeren, ook op docs/logverwijzingen en DOCX. Geen secretwaarden in rapport of commando's publiceren.

Onderstaande tekst is uitsluitend voorstel; `.gitignore` is niet gewijzigd:

```gitignore
# Build/IDE/testoutputs
**/bin/
**/obj/
.vs/
.idea/
*.user
*.suo
*.sln.docstates
**/TestResults/
**/coverage/
*.trx
*.coverage
*.coveragexml
/artifacts/
/publish/
/dist/
**/node_modules/
**/.cache/

# Tijdelijke bestanden en lokale logs
*.tmp
*.temp
*.log
*.binlog
~$*
.DS_Store
Thumbs.db

# Runtime / gegevens: ook SQLite-sidecars en alternatieve extensies
**/App_Data/
**/Logs/
**/DemoRuns/
**/PasskeyPortal/
**/Backups/
**/keys/
**/*.db
**/*.db-journal
**/*.db-wal
**/*.db-shm
**/*.sqlite
**/*.sqlite3
**/*.sqlite-journal
**/*.sqlite-wal
**/*.sqlite-shm
**/*.sqlite3-journal
**/*.sqlite3-wal
**/*.sqlite3-shm
*.bak
*.backup

# Alleen lokale instellingen, niet alle gedeelde config
**/appsettings.Local.json
**/appsettings.*.Local.json
**/LummaSimulator.App.settings.local.json
.env
.env.*
!.env.example
**/secrets.json
**/credentials.json

# Cryptografisch materiaal (geen generieke *.xml-regel)
*.pfx
*.p12
*.pem
*.key
**/key-*.xml
```

Geen algemene `*.json`, `*.zip`, `*.exe` of `verification/`-uitsluiting: dat zou fixtures, toekomstige goedgekeurde distributiebronnen of testcode verhullen. Backup-ZIP's moeten buiten de repo blijven of een exact lokaal pad krijgen in ignore; '.zip' bewijst geen back-up. Exportmap kan configureerbaar/absoluut zijn: leg de werkelijk gekozen exportmap expliciet vast vóór staging. Het ontbreken van een bestand in Git beschermt het niet tegen handmatige verwijdering. Gitignore kan geen geheimen in een willekeurige andersgenoemde file herkennen.

## 12. Gecontroleerde uitvoering na afzonderlijke opdracht

1. Leg goedgekeurde exact-padenlijst vast met A/B/C/D/E, actuele bestandshashes/groottes en bewijsverwijzingen. Vergrendelde files niet forceren. Geen wildcard-verwijdering van bin/verification.
2. Bevestig gebruikte runtimepaden, actieve processen en herstelsets. Als een deployment moet worden aangeraakt: eerst expliciete onderhoudsafspraak, consistente DB/keyringback-up en herstelvalidatie. Niet stoppen/herstarten als impliciet onderdeel van opschoning.
3. Archiveer historische logs/source-diffs en gehashte binaries met originele padindex, bronbaseline, SDK-/packagecontext en SHA-256. Archiveer DB/keyrings alleen via aparte beschermde gegevensprocedure. Behoud bewijslinks of voeg later redirects/index toe.
4. Bewijs in een nieuwe disposable werkkopie dat bron/lockfiles plus bestaande toegestane packagecache alle projecten en sidecar kunnen opbouwen. Geen installaties impliciet. Testdata expliciet buiten echte runtimepaden; monitorgeheim leeg, test-origin localhost, eigen DB/keyring.
5. Voer huidige vier regressierunners op Windows uit; laat Web-tests alleen de eigen fixture-DLL starten. Publiceer App inclusief sidecar en voer het behouden publishscript met expliciete PublishDirectory uit. Registreer ontbrekende Chrome/hardwarevereisten als beperking, niet als geslaagde test.
6. Verwijder pas goedgekeurde A en daarna bewezen B per kleine batch. C pas na gecontroleerd archief uit werkmap halen. E nooit meenemen; D ongewijzigd houden. Bronrefactoring niet combineren met opslagopschoning.
7. Herhaal tests na iedere betekenisvolle batch en controleer bronhashes, linkpaden en beschermd materiaal. Ruimtewinst feitelijk opnieuw meten.
8. Pas na aparte opdracht ignorevoorstel implementeren en Git initialiseren. Controleer daarna eerst genegeerde paden en volledige staginglijst/secretcontrole; geen automatische publicatie of remote push.

## 13. Validatie en rollback

Vooraf bij latere uitvoering: bron-/config-/test-/dochashes en exact outputmanifest, consistente beschermde herstelset als aparte beheeractie, archief met toegangscontrole. Voor uitgevoerde JS-tests is geen productiegegeven gebruikt. Eindcontrole van deze analyse: alle **156 bestaande bestanden buiten bin/obj** zijn met SHA-256 vergeleken met de opgenomen bronbaseline; **nul gewijzigd, toegevoegd of verwijderd**, met uitzondering van dit bewust uitgesloten nieuwe rapport. Totale bestandentelling steeg uitsluitend van 2.317 naar 2.318. Dit is geen volledige live-database-integriteitstest; open logs konden niet volledig worden gehasht.

Succescriteria na latere opschoning: solution en zeven projecten rebuildbaar; vier regressierunners plus publishhelpercheck slagen; geen ontbrekende linked resources/assets/fixtures; oude bewijslinks blijven navolgbaar; DB, credentials, keyring, RP-ID en migratiebron onveranderd; bestaande offline WPF, passkeys, Prize-beheer, Session Monitor en Navigator behouden. Publieke hardwaredemo alleen na afzonderlijke operationele acceptatie, niet als bijeffect van tests.

Rollback bij ontbrekende output: rebuild uit exact behouden bron/SDK/lockfiles of herstel goedgekeurd binaryarchief. Bij beschadigde verwijzingen: gearchiveerde paden/bronset terugzetten, batch stoppen; geen reparatie van businesscode tijdens opschoning. Bij enig vermoeden van gegevensschade: werkzaamheden stoppen, huidige staat veiligstellen en aparte herstelopdracht gebruiken voor consistente database/keyring en oorspronkelijke DPAPI-context. Geen oude backup over een actieve DB kopiëren. Een Git-commit vervangt geen runtimeback-up.

## 14. Afzonderlijke tabel met voorgestelde bestand- en mapacties

Alle hierna genoemde paden zijn exact relatief aan `C:/src/LummaSimulator`, tenzij absoluut vermeld. Directoryacties omvatten hun kinderen; ze overlappen niet met andere directoryrijen. Bewijsbestanden onder verification/bin worden apart behandeld. Geen van deze acties is uitgevoerd. De tabellen zijn ingevuld vanuit de recursieve meting.


### 14.1 Mapacties en gemeten omvang

| Exact pad | Soort | Bytes | Cat. | Reden en bewijs | Risico | Mogelijke afhankelijkheden | Advies |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| `LummaSimulator.App/bin` | Map | 198631 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `LummaSimulator.App/obj` | Map | 563779 | A | SDK-intermediates (.g.cs/assets/caches); gegenereerd via project/targets, geen runtimeconsument aangetroffen. | Laag | Build/packagecache; niet verwijderen tijdens build. | Verwijderen na opdracht |
| `LummaSimulator.Web/bin` | Map | 46692183 | D | Actieve Web.exe op dit exacte binpad waargenomen. | Hoog | Draaiend portal; bijbehorende config/static assets/native deps. | Behouden |
| `LummaSimulator.Web/obj` | Map | 2709091 | A | SDK-intermediates (.g.cs/assets/caches); gegenereerd via project/targets, geen runtimeconsument aangetroffen. | Laag | Build/packagecache; niet verwijderen tijdens build. | Verwijderen na opdracht |
| `tests/LummaSimulator.RegressionTests/bin` | Map | 614759 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `tests/LummaSimulator.RegressionTests/obj` | Map | 88397 | A | SDK-intermediates (.g.cs/assets/caches); gegenereerd via project/targets, geen runtimeconsument aangetroffen. | Laag | Build/packagecache; niet verwijderen tijdens build. | Verwijderen na opdracht |
| `tests/LummaSimulator.SmokeTests/bin` | Map | 240012 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `tests/LummaSimulator.SmokeTests/obj` | Map | 580839 | A | SDK-intermediates (.g.cs/assets/caches); gegenereerd via project/targets, geen runtimeconsument aangetroffen. | Laag | Build/packagecache; niet verwijderen tijdens build. | Verwijderen na opdracht |
| `tests/LummaSimulator.Web.Tests/bin` | Map | 45362567 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `tests/LummaSimulator.Web.Tests/obj` | Map | 862706 | A | SDK-intermediates (.g.cs/assets/caches); gegenereerd via project/targets, geen runtimeconsument aangetroffen. | Laag | Build/packagecache; niet verwijderen tijdens build. | Verwijderen na opdracht |
| `tools/LummaSimulator.LocalTestServer/bin` | Map | 190238 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `tools/LummaSimulator.LocalTestServer/obj` | Map | 270099 | A | SDK-intermediates (.g.cs/assets/caches); gegenereerd via project/targets, geen runtimeconsument aangetroffen. | Laag | Build/packagecache; niet verwijderen tijdens build. | Verwijderen na opdracht |
| `verification/obj` | Map | 288592 | A | SDK-intermediates (.g.cs/assets/caches); gegenereerd via project/targets, geen runtimeconsument aangetroffen. | Laag | Build/packagecache; niet verwijderen tijdens build. | Verwijderen na opdracht |
| `verification/bin/Debug` | Map | 236133 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/discoverable-smoke-data` | Map | 93210 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/discoverable-suite` | Map | 47580918 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/discoverable-wpf-data` | Map | 39660 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/local-http-publish` | Map | 738738 | B | Gepubliceerde helper/App-output; reproduceerbaar via Publish-target. | Middel | Standaardinput Test-LocalHttpPublished.ps1; eerst republish/check. | Verwijderen na republishacceptatie |
| `verification/bin/local-http-publish-artifacts` | Map | 1750386 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/local-http-separated-artifacts` | Map | 100980684 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/local-http-smoke-data` | Map | 305177 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/local-http-smoke-separated-data` | Map | 21145 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/local-http-suite` | Map | 48071874 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/local-http-wpf-data` | Map | 79323 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/phase2-smoke-data` | Map | 93102 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/phase2-tests` | Map | 46037129 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase2-web` | Map | 45685251 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase3-smoke-data` | Map | 93106 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/phase3-suite` | Map | 47089271 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase3-tests` | Map | 46126317 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase4-smoke-data` | Map | 93112 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/phase4-suite` | Map | 47524706 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase4-tests` | Map | 46529088 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase4-web` | Map | 46085472 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase5-51-regression` | Map | 48251810 | C | Historische regressieset; 5.1-binaries gehasht in assurance, 5.2a-set genoemd in opleverrapport. | Middel | Bewijs/provenance; eerst compleet archief met padindex. | Archiveren |
| `verification/bin/phase5-51-smoke-data` | Map | 114207 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/phase5-51-suite` | Map | 46795920 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/phase5-51-wpf-data` | Map | 39663 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/phase5-52a-regression` | Map | 48744806 | C | Historische regressieset; 5.1-binaries gehasht in assurance, 5.2a-set genoemd in opleverrapport. | Middel | Bewijs/provenance; eerst compleet archief met padindex. | Archiveren |
| `verification/bin/phase5-52a-runtime` | Map | 154014 | B | Testlogs/exports/eigen dummy-runbestanden aangetroffen; fixtures maken nieuwe data, oude inhoud niet volledig gevalideerd. | Middel | Historische bewijswaarde/PII; eerst synthetische herkomst bevestigen. | Behouden tot inhoudscontrole; daarna archiveren of verwijderen |
| `verification/bin/phase5-52a-suite` | Map | 47244788 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/prize-http400-regression` | Map | 47302728 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |
| `verification/bin/prize-http400-safety-20261030-232648` | Map | 580766 | E | portal.db + keys + manifest; beschermde herstelset. | Kritiek | Database/DPAPI/credentials/back-upcontext. | Behouden |
| `verification/bin/web041-check` | Map | 45356068 | B | Build-/publishoutput; bronprojecten en targets aanwezig, nog geen schone rebuild bewezen. | Middel | Oude docs/env LUMMA_TEST_WEB_DLL, benodigde sidecars/assets en mogelijke procesreferenties. | Verwijderen na rebuild/procescontrole; bewijs zo nodig archiveren |

### 14.2 Historische bewijsbestanden: geen automatische verwijdering

Een log is opnieuw te genereren als nieuwe uitvoering, maar het historische resultaat is niet reproduceerbaar als hetzelfde bewijs. Alle niet-open bestanden hieronder krijgen daarom C, ook wanneer ze weinig ruimte kosten. De twee open portallogs zijn operationeel en mogelijk gevoelig: voorlopig E.

| Exact pad | Soort | Bytes | Cat. | Reden / bewijs | Risico | Mogelijke afhankelijkheden | Advies |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| `verification/bin/discoverable-build-results.log` | Bestand | 786 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/discoverable-js-results.log` | Bestand | 4439 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/discoverable-smoke-results.log` | Bestand | 1791 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/discoverable-web-results.log` | Bestand | 29692 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/discoverable-wpf-results.log` | Bestand | 612 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-build-results.log` | Bestand | 907 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-focused-results.log` | Bestand | 1882 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-focused-separated-results.log` | Bestand | 1967 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-js-results.log` | Bestand | 4439 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-publish-results.log` | Bestand | 996 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-published-check-results.log` | Bestand | 289 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-separated-build-results.log` | Bestand | 2042 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-smoke-results.log` | Bestand | 3754 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-web-results.log` | Bestand | 29692 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-wpf-results.log` | Bestand | 813 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/local-http-wpf-separated-results.log` | Bestand | 813 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase2-js-results.log` | Bestand | 3424 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase2-smoke-results.log` | Bestand | 1779 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase2-web-results.log` | Bestand | 18036 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase2-wpf-results.log` | Bestand | 612 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase3-js-results.log` | Bestand | 3628 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase3-smoke-results.log` | Bestand | 1779 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase3-web-results.log` | Bestand | 22037 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase3-wpf-results.log` | Bestand | 612 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase4-js-results.log` | Bestand | 4239 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase4-smoke-results.log` | Bestand | 1779 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase4-web-results.log` | Bestand | 25935 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase4-wpf-results.log` | Bestand | 612 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-51-build-results.log` | Bestand | 935 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-51-final-web-build.log` | Bestand | 320 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-51-js-results.log` | Bestand | 4437 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-51-smoke-results.log` | Bestand | 3752 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-51-source-diff.json` | Bestand | 1251 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-51-web-results.log` | Bestand | 33033 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-51-wpf-results.log` | Bestand | 813 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-52a-build.log` | Bestand | 6084 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-52a-final-build.log` | Bestand | 942 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-52a-js-tests.log` | Bestand | 4438 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-52a-smoke-tests.log` | Bestand | 3760 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-52a-source-diff.json` | Bestand | 2461 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-52a-web-tests.log` | Bestand | 37577 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/phase5-52a-wpf-tests.log` | Bestand | 813 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/prize-http400-after.log` | Bestand | 38209 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/prize-http400-before.log` | Bestand | 35983 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |
| `verification/bin/prize-http400-portal.stderr.log` | Bestand | 0 | E | Hashlezing geweigerd wegens open procesgebruik. | Hoog | Draaiende procesoutput, mogelijke gegevens. | Behouden; eerst eigenaar en inhoud vaststellen. |
| `verification/bin/prize-http400-portal.stdout.log` | Bestand | 1507 | E | Hashlezing geweigerd wegens open procesgebruik. | Hoog | Draaiende procesoutput, mogelijke gegevens. | Behouden; eerst eigenaar en inhoud vaststellen. |
| `verification/bin/v04-final-test-results.log` | Bestand | 12348 | C | Historisch resultaat/bronvergelijking; geen productiebron. Nieuwe run vervangt historisch bewijs niet. | Laag/middel | v0.5/HTTP-/Prize-/securitydocumentatie en opleverprovenance. | Archiveren met hash/padindex; daarna pas uit werkmap halen. |

### 14.3 Document- en bronkandidaten die niet worden verwijderd

| Exact pad | Soort | Cat. | Reden / bewijs | Risico | Mogelijke afhankelijkheden | Advies |
| --- | --- | --- | --- | --- | --- | --- |
| `docs/Passkey-Portal-v0.3-plan.md` | Bestand | C | Historische fase/bron/oplevering; zie documentanalyse. | Laag/middel | Bestaande Markdown-links en herleidbaarheid. | Later archiveren met linkindex; nu behouden. |
| `docs/Passkey-Portal-v0.3.md` | Bestand | C | Historische fase/bron/oplevering; zie documentanalyse. | Laag/middel | Bestaande Markdown-links en herleidbaarheid. | Later archiveren met linkindex; nu behouden. |
| `docs/LummaSimulator-v0.4.md` | Bestand | C | Historische fase/bron/oplevering; zie documentanalyse. | Laag/middel | Bestaande Markdown-links en herleidbaarheid. | Later archiveren met linkindex; nu behouden. |
| `docs/codex-report.md` | Bestand | C | Historische fase/bron/oplevering; zie documentanalyse. | Laag/middel | Bestaande Markdown-links en herleidbaarheid. | Later archiveren met linkindex; nu behouden. |
| `docs/LummaSimulator_functionele_requirements_v0.1.docx` | Bestand | C | Historische fase/bron/oplevering; zie documentanalyse. | Laag/middel | Bestaande Markdown-links en herleidbaarheid. | Later archiveren met linkindex; nu behouden. |
| `LummaSimulator.Web/Resources/Localization/UiText.resx` | Bestand | B/D | Exact duplicaat NL, maar fallbackresource. | Middel | ResourceManager, Razor/JS-lokalisatie, cultuurfallback. | Behouden; aparte semantische validatie. |
| `LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx` | Bestand | B/D | Exact duplicaat neutraal, maar expliciete NL-cultuur. | Middel | Lokalisatietests, satellite assembly en cultuurresolutie. | Behouden; geen byte-deduplicatie nu. |
| `LummaSimulator.Web/Models/PrizeModels.cs` | Bestand | D (enkele types B) | Vijf enums zonder tekstuele consumenten, overige model en v6-contract actief. | Hoog | PrizeStore/migratie/tests/toekomstige goedgekeurde werkpakketten. | Behouden; geen volledig bestand verwijderen. |
| `verification/Program.cs` | Bestand | D | Actieve Windows-regressierunner. | Hoog | Solution/RegressionTests/App. | Behouden. |
| `verification/Verification.csproj` | Bestand | D | Actieve projectreferences. | Hoog | Solution/Windows-target. | Behouden. |
| `verification/Test-LocalHttpPublished.ps1` | Bestand | D | Afzonderlijke publishacceptatie. | Middel | Gepubliceerde helper en PublishDirectory. | Behouden. |

Geen niet-genoemde bronmap is een verwijderkandidaat. Alle overige actieve bron-, configuratie-, migratie-, test-, asset- en huidige requirementbestanden vallen onder D. Alle aangetroffen database-/sleutel-/back-upbestanden blijven E ongeacht bovenliggende map of bestandsextensie. De afzonderlijke opdracht voor daadwerkelijke opschoning moet kiezen welke voorwaardelijke B/C-acties worden vrijgegeven.


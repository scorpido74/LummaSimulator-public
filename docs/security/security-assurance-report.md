# Security assurance report - LummaSimulator

Datum: 30 oktober 2026. Status: **offline bronbeoordeling en frameworkontwerp; geen securitycertificering of releasevrijgave**.
Project: C:/src/LummaSimulator, eigen bron na werkpakket 5.1.
[Security requirements](security-requirements.md) | [Threat model](threat-model.md) | [Secure architecture](secure-architecture.md) | [Dependency inventory](dependency-inventory.md) | [Security test plan](security-test-plan.md).

## Conclusie en juiste externe claim

In de onderzochte eigen productiebron is geen functionaliteit aangetroffen voor echte credential harvesting, browsercookie-/sessietokendiefstal, keylogging, malwarepersistentie, privilege-exploit, uitschakelen van beveiligingssoftware, remote command execution of verzending van werkelijke buit. De gesimuleerde buit komt uit vaste fictieve records en wordt vóór demonstratieverzending exact gevalideerd.

Dit is **geen bewijs dat iedere dependency/native library of iedere uitgeleverde/gewijzigde binary geen kwaadaardig gedrag bevat**. Runtime-, netwerk-, complete secrets-/CVE-/signing-/SBOM-controles ontbreken. Er zijn concrete gaten in opslag-/egressbegrenzing, helperintegriteit, publieke beschikbaarheid, privacy/retentie en bewijsintegriteit. Daarom is de applicatie nog niet gereed voor een onvoorwaardelijke klantclaim “veilig”, “malwarevrij”, “geen persoonsgegevens”, “uitsluitend fictieve data” of “kan niet worden misbruikt”.

Wel verdedigbaar: “De geïdentificeerde bronversie heeft deze gedocumenteerde simulatiegrenzen en bestaande controles; onderstaande resterende controles en risico's zijn nog open.” Een later rapport moet een werkelijk gesigneerde release en uitgevoerde testset noemen, niet deze bronreview als runtimecertificaat hergebruiken.

## Uitgevoerde analyse en beperkingen

- Volledige lokale inventaris van 130 niet-gegenereerde projectbestanden vóór deze documentatie. Productieruntime, process-/file-/netwerk-/auth-/configconsumenten, eigen JS en gedeelde buildpaden zijn brongebaseerd gevolgd; tests en resources betrokken bij veiligheidsclaims gericht geïnspecteerd en repository-wide op gevoelige API's/secretpatronen gezocht.
- Tests/frameworkmocks zijn geen productiefunctionaliteit. Testauthenticator genereert uitsluitend testprivésleutels; test-TLScallback pint het eigen tijdelijke certificaat. Node vm draait eigen lokale frontendtests, geen productie-eval.
- Geen bestaande database geopend of gemigreerd; geen applicatie of tests gestart, bestanden verwijderd, packages geïnstalleerd, beveiligingssoftware gewijzigd of externe verbindingen uitgevoerd.
- Alleen bron/metadata/bestaande logs gelezen en SHA-256 berekend. Bestaande ontwikkelbinaries zijn gehasht, niet uitgevoerd, gedecompileerd of cryptografisch gesigneerd gevalideerd.
- De formele Codex Security-preflighthelper kon niet starten: de lokale Python had noch tomllib noch tomli. Geen modules/configuratie toegevoegd. Geen voltooide georkestreerde scan, onafhankelijk tweede auditteam of nieuwe testresultaten geclaimd. Het framework gebruikt de bron-/threat-modelmethodiek met de expliciete offline-/documentatiebeperkingen als leidend.
- Geen Git-working-tree. Bronidentiteit en wijzigingsbewaking berusten op SHA-256, niet op git-diff.
- Bronreview van runtime-eigenaren plus brede bestands-/patrooninventaris is geen garantie dat iedere testregel, resourcetekst, frameworkimplementatie of native dependency integraal security-geaudit is. Black-boxgedrag, Windows-calltrace, releasepackaging en supply-chainprovenance blijven open.

## Functionele classificatie

| Klasse | Bevindingen | Bronbewijs / consequentie |
| --- | --- | --- |
| 1 - Veilige simulatie met fictieve gegevens | SAFE SIMULATION-berichten, fixed dummyrecords, in-memory exposure/replay, Navigatorlabels | LummaSimulator.App/Services.cs:12-23; LummaSimulator.App/TelemetryService.cs:43-44,75-83; LummaSimulator.App/SessionMonitorViewModel.cs:101-113; LummaSimulator.App/NavigatorExportService.cs:21-88. “Veilig” beschrijft deze datastroom, geen whole-productcertificaat |
| 2 - Legitiem, extra beveiliging nodig | whoami, owned files/cleanup, loopbackreceiver, echte passkeyportal/sessies, monitorsecret, logs/export/database/keyring, onderhouds-CLI | Expliciet inventaris in secure-architecture. Het hele product verwerkt dus niet uitsluitend fictieve gegevens |
| 3 - Kan misbruik of beleidsafwijking faciliteren onder voorwaarden | Config naar netwerkopslag, vervangbare helper zonder publisher/hashcontrole, forgeerbare publieke lokale serverherkenning, gedeelde account/requestcapaciteit, wijzigbare rapportage en ontbreken toestemmingsgate | R-01/R-02/R-03/R-05/R-06/R-12. Voorwaarden zijn beschreven; geen onbewezen remote RCE, privilege-escalation of bestaande credentialdiefstal afleiden |
| 4 - Onbekend/nader onderzoeken | Actuele advisories/licenties, Windows-DACL, native/runtime-gedrag, DNS/SMB/PKI/browser/providerverkeer, artifactsigning, volledige release-SBOM en reproduceerbaarheid | N/U-controles; zonder uitvoering geen groen oordeel |

## Risico's en aanbevolen wijzigingen

| Prioriteit | Geconstateerd of open | Aanpassing/verwijdering / reden |
| --- | --- | --- |
| P0 claimbeleid | whoami toont echte Windowsidentiteit; portal verwerkt echte eigen authmetadata | Voor letterlijke fictief-only eis whoami vervangen/uit. Eigen portalauthenticatie apart expliciet goedkeuren/classificeren of uitsluiten uit die demonstratiescope. Niet accounts/passkeys verwijderen |
| P0 releasegate | Geen volledig reproduceerbaar, ondertekend assurancepakket | Geen onvoorwaardelijke externe assuranceverklaring tot G0-G8 bewijs en formele sign-off beschikbaar zijn |
| P1 opslag/egress | HTTP-loopbackregels beperken netwerkfilesystem/DNS niet | Local-volume/final-path/ACL-beleid voor data/log/export/database/keyring; expliciet DNS/SMB/PKI/browserbeleid; deny-by-default waar vereist |
| P1 helper/omgeving | Vaste helperlocatie is geen uitgeverscontrole; child erft omgeving | Installatie-ACL, trusted manifest/publishercontrole, minimale childenv zonder monitor-key, normale uitvoering zonder elevatie |
| P1 publieke portal | 200 accounts en gedeelde limieten kunnen worden opgebruikt | Per-client/account plus globale quota/admission, monitoring; geen onbeperkte publieke demo aannemen als abusebestendig |
| P1 supply chain | Lockfiles zijn geen malware-/CVE-/provenance-/licentietoets | Locked restore, bronmapping, scanner/audit/SBOM, gepinde toolchain en ondertekende release |
| P1 privacy | Lokale paden, namen, keymetadata en historische audit/logs/backups zijn echt | Behoud minimaal noodzakelijke verwerking; DACL/encryptie/retentie/redactie en restorebeleid expliciet testen |
| P2 bewijs/IPC/files | Handmatige resultaten zijn niet onafhankelijk; nonce is geen authenticatie; padcheck is geen handle/racebewijs | Signed evidence, herkenbare manual/unsignedlabels, authenticated IPC indien nodig, final-handle/racetests |
| P0 vóór Prize-activering | 5.1 heeft nog geen operationele deelnemers-/claim-/retentieguards | Blijf dicht; verbind alle deletepaden incl. monitor en toekomstig claim/reveal/voorraadbeleid vóór vrijgave |

Geen echte malwarefunctie is geïdentificeerd die nu uit de bron verwijderd moet worden. Behoud bestaande WebAuthn, TLS, CSRF en rolcontrole; het uitschakelen hiervan zou assurance juist verslechteren. Een antivirusexclusie of onderdrukken van waarschuwingen is geen voorgestelde maatregel.

Implementatievolgorde: juiste scope/claim en artefactidentiteit -> opslag/egress en helper/secrets -> portalquota/privacy -> SAST/secret/dependency/SBOM/releasegates -> gecontroleerde runtime/securityregressie -> signing/klantpakket -> latere bedrijfsfunctionaliteit uitsluitend na eigen acceptatiepoorten. Prioriteit is geen vastgestelde CVSS-score.

## Traceability matrix

B = huidige bronvaststelling; H = historisch log; N = controle ontbreekt/niet uitgevoerd; U = onbekend. Geen H-resultaat geldt als nieuwe run of volledige eisdekking. E-02 verwijst naar de concrete bronankers en inventaris in secure-architecture; E-03 naar de dependency-inventory. Alle toekomstige T-tests zijn uitgewerkt in security-test-plan.

| Eis | Betrokken code | Technische maatregel / open gat | Test | Testresultaat nu | Bewijsstuk |
| --- | --- | --- | --- | --- | --- |
| SR-01 | LummaSimulator.App/Services.cs:12; LummaSimulator.App/TelemetryService.cs:43,75,107 | Fixed dummy equality; echte whoami blijft beleidsafwijking | T-01/T-06 | B/H gedeeltelijk; strikte whole-producteis niet voldaan | E-01/E-02/E-14 |
| SR-02 | LummaSimulator.App/TelemetryService.cs:75,187 | Eigen synthetic root/reparsecheck; final-handle/canarybewijs ontbreekt | T-02/T-13 | B; N voor complete trace/races | E-01/E-02 |
| SR-03 | LummaSimulator.App/SessionMetadataClient.cs:51; LummaSimulator.App/SessionMonitorViewModel.cs:125; LummaSimulator.Web/Services/DemoService.cs:134 | Monitor DTO weigert tokenvelden; geen keyloggingbron gevonden | T-03 | B/H monitor; N voor globale canary-/hooktest | E-02/E-12/E-14 |
| SR-04 | LummaSimulator.App/TelemetryService.cs:151; receiver tools/LummaSimulator.LocalTestServer/Program.cs:57 | Fixed loopback/DTO, redirects/proxy uit | T-04 | B/H positief lokaal transport; N voor volledige egressmeting | E-02/E-14 |
| SR-05 | shared/RuntimePaths.cs:6; LummaSimulator.App/SessionMetadataClient.cs:38 | Socketfilter wel, DNS/sharegrens niet | T-05 | B: controlgat; N/U runtime | E-01/E-02 |
| SR-06 | LummaSimulator.App/TelemetryService.cs:107; LummaSimulator.App/LocalTestServerManager.cs:95 | Vaste paths/args, geen shell; helpertrust/envgate ontbreekt | T-06 | B/H lifecycle; N publisher/env | E-02/E-14/E-15 |
| SR-07 | LummaSimulator.App/LocalTestServerManager.cs:105; receiver tools/LummaSimulator.LocalTestServer/Program.cs:14 | Retained handle en stdin-lease; geen bronautorun | T-07 | B/H processreaping; N registry/boottrace | E-02/E-14/E-15 |
| SR-08 | LummaSimulator.Web/Services/LocalMaintenance.cs:8; LummaSimulator.Web/Program.cs:83,159; test tests/LummaSimulator.Web.Tests/HttpChecks.cs:64 | Admincheck, productie-TLS/headers; testcertpin afgebakend | T-08 | B/H auth; N OS-trace | E-02/E-12 |
| SR-09 | LummaSimulator.App/MainViewModel.cs:102,120,180; LummaSimulator.Web/Program.cs:21 | Zichtbare modus/CLI; geen externe autorisatiegate | T-09 | B/H UI; N consentafdwinging | E-02/E-13/E-15 |
| SR-10 | App/Services.cs; .csproj/targets/lockfiles | Geen malwareloader in eigen code; binary/nativecontent niet integraal onderzocht | T-10/T-16 | B; U externe binaries/native | E-01/E-03/E-20 |
| SR-11 | LummaSimulator.Web/Services/DemoService.cs:84,130; LummaSimulator.Web/Services/PresenterAuthentication.cs:177; LummaSimulator.Web/Program.cs:83 | IFido2/UV/challenges/CSRF/cookies/identiteitsscheiding | T-11 | B/H 467 webchecks en JS; N nieuwe fysieke acceptatie | E-12/E-13 |
| SR-12 | LummaSimulator.Web/Services/PresenterAuthentication.cs:110,249; LummaSimulator.Web/Services/PresenterEndpoints.cs:24 | DB-actor/rol, verse herauth, lokale bootstrap | T-12 | B/H; N host-ACL/privilegetrace | E-02/E-12 |
| SR-13 | RuntimePaths.cs; LummaSimulator.App/TelemetryService.cs:187; LummaSimulator.App/Services.cs:34; LummaSimulator.Web/Program.cs:58 | Gedeeltelijke fileguards, geen universele final-local-rootgrens | T-13 | B/H cleanup; N/U ACL/link/UNC | E-02/E-14 |
| SR-14 | LummaSimulator.Web/Services/DemoService.cs:251; LummaSimulator.Web/Services/SessionStatusService.cs:73; LummaSimulator.Web/Services/PresenterAuthentication.cs:42 | Visitorcleanup, beperkte DTO; presenter-/log-/backupretentie open | T-14 | B/H deels; N volledige privacygate | E-02/E-12/E-14 |
| SR-15 | LummaSimulator.App/Models.cs:47; LummaSimulator.App/NavigatorExportService.cs:9; LummaSimulator.Web/Services/PresenterAuthentication.cs:60 | Handmatige bevestiging en append-only audit, geen signed bewijs | T-15 | B/H mapping; N cryptografische bewijsintegriteit | E-02/E-14 |
| SR-16 | LummaSimulator.Web/LummaSimulator.Web.csproj:12; drie lockfiles; buildtargets | Packagehashes aanwezig; advisory/licentie/SBOM/provenance niet getoetst | T-16 | B inventaris; N/U securityscan | E-03 |
| SR-17 | .csproj; shared/LocalTestServer.Build.targets | Losse bron-/binaryhashes; geen release signingpipeline aangetoond | T-17 | B hashes; N reproduceerbaarheid/signaturevrijgave | E-01/E-11/E-20 |
| SR-18 | LummaSimulator.Web/Services/DemoService.cs:36; LummaSimulator.Web/Program.cs:119 | Capaciteitscap en globale limiters; fairness/admission ontbreekt | T-18 | B; N belasting-/misbruiktest | E-02 |
| SR-19 | LummaSimulator.Web/Services/Prizes/PrizeStore.cs:10; LummaSimulator.Web/Configuration/ExperienceSettings.cs:33; LummaSimulator.Web/Program.cs:262 | Internal-only disabled Draft; geen Prize-routes | T-19 | B/H opslag; N toekomstige functionaliteit | E-02/E-12 |
| SR-20 | LummaSimulator.Web/Models/DemoModels.cs:18; LummaSimulator.Web/Program.cs:74; LummaSimulator.App/LocalTestServerManager.cs:100; .gitignore | Hash/DPAPI/gitignore; geen complete secret/release/childenvgate | T-20 | B gedeeltelijk; N systematische scan | E-01/E-02/E-03 |

Afgekorte App/Web-bestandsnamen verwijzen naar LummaSimulator.App of LummaSimulator.Web zoals in secure-architecture. De bronzoekactie vond geen hardcoded productiewachtwoord/privésleutel in de onderzochte code/config, maar dat is geen volwaardige of historische secretscan. Geen credentialinhoud gekopieerd of gebruikt. Er zijn geen .pfx/.p12/.pem/.key-bestanden aangetroffen in de geïnventariseerde projecttree bij de naamcontrole; dit sluit materiaal in andere opslagvormen of buiten de tree niet uit.

## E-01 - Identiteit en reproduceerbaarheid van deze bronbeoordeling

Snapshotdigest, vóór toevoeging van deze zes documenten:
`bf6bd686fd9e3a3a821f005c9139500379f20de85ae88a10674aa856b318147f`

Algoritme: inventariseer gewone bestanden onder projectroot, sluit padcomponenten bin, obj, .git en node_modules en de hele docs/security/-output uit; gebruik root-relatieve slashpaden, SHA-256 per ruwe filebytes; sorteer paden ordinaal; concateneer path=sha256 plus LF per rij; SHA-256 over UTF-8 van die tekst. Aantal: **130**. Documenten/referentiedata zijn inbegrepen; gegenereerde buildoutput niet. Dit is een lokale identiteit, geen gesigneerde herkomstattestatie.

Geselecteerde bronhashes (voor de overige bestanden is de bovenstaande reproduceerbare aggregate-identiteit leidend):

| Bestand | SHA-256 |
| --- | --- |
| LummaSimulator.App/TelemetryService.cs | `3660f773d1108fc096e1cd725358594734749a70148171ed03c77593e27f1c96` |
| LummaSimulator.App/LocalTestServerManager.cs | `b9c7893579d94060172a5b8110d334cbff7d2ec8b2499bdce38855678cfcefcc` |
| LummaSimulator.App/SessionMetadataClient.cs | `68a15170cd4b986d89b84a98bb586d741cd41090d1bea7eca81d4218ea2d2106` |
| LummaSimulator.App/SessionMonitorViewModel.cs | `6509d33658616ba2baeaae8d7985c9a40d56c8d341a3e9f16e5408d5ba842ed1` |
| LummaSimulator.App/Services.cs | `e691c47d7e6f1d35be7db9e6050126e76fe9f58173d60459eb119a28615c654a` |
| LummaSimulator.App/NavigatorExportService.cs | `5f391899c32ba459c773e3557cd0dd9542666f28e0d637ebc2d4e5e8d18ad9d3` |
| shared/RuntimePaths.cs | `93b66753007756aaddacc2e4869b01b3ad8d73846091f9a98ddb537df7e601d7` |
| shared/LocalTestServerProtocol.cs | `54caadcf71c393806ce0954fdcad1163cbb379db04ece195102402f329388044` |
| tools/LummaSimulator.LocalTestServer/Program.cs | `da547a38c8f96e874f3a63a92e4d671123e15f59bdf3032ee3a9b46999aaed59` |
| LummaSimulator.Web/Program.cs | `ea6368f74def11a49a5983cf9da483ad5ea013bbcbea89f6fd31dcdec5906ae8` |
| LummaSimulator.Web/Services/DemoService.cs | `40fc1ac9af8f9bf16dd44b7d6dc583e144e9394b14ac55346a7db041b78814cd` |
| LummaSimulator.Web/Services/PresenterAuthentication.cs | `9e7fb012e5d90b28c757584f9b17abd92118244864223b54cd143698477cbefa` |
| LummaSimulator.Web/Services/SessionStatusService.cs | `eaf6ae908e6a8f9c500579158f0dcad7f3b62d21984e8dacd63b3dbc8d51a683` |
| LummaSimulator.Web/Services/Prizes/PrizeMigration.cs | `d82102192cb52400767106af06c561c8ed4ef9273d8ccac17e0eb8389d2a2ed7` |
| tests/LummaSimulator.Web.Tests/HttpChecks.cs | `964736340467060c22e2b5a3bb96a2742204bdc3f17033232c6b35e0360f2caa` |

E-02: source-backed inventaris, categorieën en bronankers in secure-architecture/threat-model. E-03: alle 31 externe package-ID/versies en lockhashes in dependency-inventory. E-04: afrondingscontrole bevestigt dat alle 130 oorspronkelijke projectbestanden dezelfde SHA-256-identiteit behouden; uitsluitend de zes gevraagde docs/security-bestanden zijn toegevoegd. Alle lokale documentlinks zijn gecontroleerd.

## Historische testbewijzen E-11 t/m E-15

De aanwezige ontwikkellogs melden: build 0 warnings/errors; web 467 checks; JS 43 tests; smoke inclusief 24 receiverchecks/2778 mappingasserties; WPF 20 PASS-regels. Ze zijn gelezen/gehasht, **niet opnieuw uitgevoerd**. Bestandsmtime of een losse hash bewijst niet welke bron ze destijds heeft geproduceerd, noch dat ze niet vóór deze review veranderd zijn.

| Pad | Bytes | SHA-256 op reviewmoment |
| --- | --- | --- |
| verification/bin/phase5-51-build-results.log | 935 | `f5f20a745e50639ce66c6352f8df0c4900e2c7836a0853b50796cf4ec18ea084` |
| verification/bin/phase5-51-final-web-build.log | 320 | `203a3a7227b9dde719a0a4c727a59dbae1eda15177a1da5e353a33bd8bc93289` |
| verification/bin/phase5-51-web-results.log | 33033 | `4aa781d701c41345f2579755f8a82a19fc7406c400cb84ceffe8d6dbbdbe55ca` |
| verification/bin/phase5-51-js-results.log | 4437 | `cac064927a8eab1e05b2ba97b5cb44fff1e947270b04d9e4c39c2a503606dc38` |
| verification/bin/phase5-51-smoke-results.log | 3752 | `4507aeb4bfd270f6474425af654ff6ac80ccb6050f3f6db0ad3a9df140d68443` |
| verification/bin/phase5-51-wpf-results.log | 813 | `b707a827c9cacb1d78d3d2642763762f0a12e440de1947c457e6d0f080700e48` |

E-11 = buildlogs; E-12 = weblog; E-13 = JS-log; E-14 = smoke; E-15 = WPF. Er is geen actuele Procmon/ETW/pakket-/SAST-/secretscan-/SBOM-/signingrapportage aangetroffen waarmee de open gates kunnen worden gesloten.

## E-20 - Lokale ontwikkelbinaries: alleen file-identiteit

| Pad | Bytes | SHA-256 |
| --- | --- | --- |
| verification/bin/phase5-51-regression/LummaSimulator.Web.dll | 913920 | `811719ca6bb789ae244e6326ac5dd510ddc5d06fda4d97b7db80bd205df01f00` |
| verification/bin/phase5-51-regression/LummaSimulator.App.dll | 183296 | `ce08d554872553410e2404425ffbff0a95c9024b9d1e3c382a990b074652913f` |
| verification/bin/phase5-51-regression/LummaSimulator.LocalTestServer.dll | 24576 | `eea19d113c6c61c2c3224d0181ea01ad9d611489c3d9eddd634d12d713a2abb0` |

Dit zijn ontwikkel-/testartefacten, geen door deze opdracht vrijgegeven distributie. Hashen voert binaries niet uit. Authenticodesignatures, certificaattrust/revocation, native imports en overeenkomst met een reproduceerbare build zijn niet gevalideerd.

## Volgende stap en stopgrens

Vóór implementatie moeten product/security de data-/scopeclaim en prioriteiten accepteren. De eerste implementatie kan een afgebakend hardeningspakket voor lokale resourcegrenzen, helperintegriteit/childenv en release-evidencegates zijn, gevolgd door tests op een disposable VM met fictieve canaries. Bestaande authcontroles behouden. Werkpakket 5.2 blijft een aparte opdracht en mag niet impliciet met deze securityreview worden gestart.

Deze opdracht eindigt met zes beveiligingsdocumenten. Geen fixes, nieuwe packages, databasewijzigingen, externe scans of netwerkmonitoring uitgevoerd.

## Aanvullende beperkte broncontrole

Een lokale, alleen-lezen formatzoekactie controleerde dezelfde 130 bestanden op private-keyheaders en herkenbare AWS-/GitHub-/Slack-tokenvormen: nul hits. Alleen pad/regel/type zouden zijn gerapporteerd, nooit waarden; geen online tokenvalidatie. Dit is beperkte broninspectie (B), geen volwaardige secretscanner, geen historieonderzoek en geen bewijs dat nergens geheimen staan. De zes documenten, lokale links en bronregelnummers zijn gevalideerd; de oorspronkelijke brondigest bleef ongewijzigd.

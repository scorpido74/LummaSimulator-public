# GitHub-inrichting — eerste push uitgevoerd en gecontroleerd

Datum: 31 oktober 2026. Status: **eerste push naar de private repository geslaagd; remote main en alle 160 bronbestanden gecontroleerd**. Deze sectie is leidend. Onderstaande eerdere statussen blijven historische context.

De gebruiker heeft expliciet toestemming gegeven voor de eerste push naar `scorpido74/LummaSimulator`, inclusief het na dezelfde veiligheidscontrole committen en pushen van rapportwijzigingen. De toestemming voor bestaande publieke deploymenthost/RP-ID/origins blijft beperkt tot die configuratiewaarden; secrets en persoonsgegevens vallen daarbuiten.

Vóór de eerste push gecontroleerd:

- Repository-eigenaar/naam correct; REST API bevestigde `private=true` en `visibility=private`.
- Zowel fetch- als push-URL van `origin`: `https://github.com/scorpido74/LummaSimulator.git`.
- Lokale branch `main`, schone index/werkboom, HEAD `18b4d2436d3314b762fb5d09f2cb2f30405ec5fe`.
- Alle 160 huidige bestanden opnieuw gecontroleerd met de bestaande kandidaatscanner. Geen nieuwe secrets of persoonsgegevens vastgesteld; uitsluitend de zeven toegestane publieke configuratietreffers en drie bekende synthetische/codepatronen.
- Aanvullend alle bronblobs uit beide te pushen commits strikt als UTF-8 gecontroleerd op secrets, persoonsgegevens, WebAuthn-records, JWT/Authorization/connectionstringpatronen en beschermde paden. Geen database, keyring, beschermd archief, Office/binaire kandidaat of workflow aanwezig. Resultaat: nul onverwachte bevindingen; drie bekende fixturetreffers en 48 geverifieerde package-lockhashes. Git-auteursmetadata gebruikt de bestaande echte identiteit; geen persoonswaarden in broninhoud toegevoegd.

Uitgevoerd: `git push -u origin main`. Exitcode 0; nieuwe remote branch `main` aangemaakt en lokale upstream ingesteld op `origin/main`.

Na de eerste push gecontroleerd:

- `git ls-remote --heads origin refs/heads/main` bevestigde exact dezelfde volledige commit-ID als de lokale HEAD: `18b4d2436d3314b762fb5d09f2cb2f30405ec5fe`.
- Repository bleef private volgens de REST API.
- De volledige recursieve GitHub-tree was niet afgekapt. Alle 160 remote bronpaden en hun Git-blob-ID's kwamen exact overeen met de lokale commit, inclusief App/Web, sidecar/shared, migraties, tests, verification, lockfiles, resources, licenties en documentatie.
- Werkboom schoon; `main` volgt `origin/main`.

Deze rapportwijziging wordt afzonderlijk gecontroleerd en gecommit; de gebruiker heeft de daaropvolgende push expliciet mede toegestaan. Voor die push worden de volledige commitinhoud, remote/privacy en lokale status opnieuw gecontroleerd en na afloop remote HEAD en alle bronblobs opnieuw vergeleken. Controleadministratie blijft lokaal in `.git/precommit-audit`, waaronder `verify-push.py` en `push-history-scan.json`; deze bestanden worden niet gepubliceerd.

Geen applicatiecode, werkende deploymentconfiguratie, actieve portal, database, passkey, keyring of historisch archief gewijzigd. Geen nieuwe build nodig voor deze uitsluitend Git-/rapportwijziging; de eerdere geslaagde geïsoleerde build en regressies blijven de laatst uitgevoerde applicatieverificatie. Bestaande securityrisico's blijven open; patrooncontroles zijn geen volledige securitycertificering. Geen release, workflow, deployment of force-push uitgevoerd.

---

# GitHub-inrichting — historische status vóór pushtoestemming

Datum: 31 oktober 2026. Toenmalige status: **eerste lokale commit gemaakt, private GitHub-repository aangemaakt en origin ingesteld; niet gepusht**. De actuele status staat bovenaan. Onderstaande eerdere controles en rapporten zijn historische context.

- Eerste lokale commit op `main`: `803659e` — `Initial baseline after project cleanup`, met 160 gecontroleerde bestanden. De bestaande Git-auteursidentiteit is gebruikt en niet gewijzigd. Een aparte documentatiecommit legt deze eindstatus vast; geen geschiedeniswijziging.
- Repository: https://github.com/scorpido74/LummaSimulator. Aangemaakt met `gh repo create scorpido74/LummaSimulator --private`, zonder gegenereerde README/licentie, workflow, release of push.
- Private zichtbaarheid na aanmaak tweemaal gecontroleerd: REST API meldt `private=true` en `visibility=private`; `gh repo view` meldt `visibility=PRIVATE`, `isEmpty=true` en de juiste eigenaar/naam.
- `origin` voor fetch en push: `https://github.com/scorpido74/LummaSimulator.git`. Geen andere remote ingesteld.
- De expliciete toestemming betreft alleen de bestaande publieke deploymenthost, WebAuthn RP ID en toegestane origins in deze private repository. Geen toestemming afgeleid voor secrets, private keys, tokens, certificaatgeheimen of persoonsgegevens. Bestaande werkende configuratie is ongewijzigd.
- Laatste securitycontrole: alle 160 kandidaten strikt als UTF-8 gelezen en gehasht, geen Office/binaire kandidaat of beschermd runtimepad. Dezelfde tien bekende treffers: zeven nu toegestane publieke configuratievermeldingen en drie beoordeelde synthetische/codepatronen. Aanvullende JWT-/Authorization-/connectionstring-/lange-literalcontrole: uitsluitend 48 geverifieerde lockfile-`contentHash`-treffers. Geen nieuw vastgesteld secret of persoonsgegeven. Dit blijft een patrooncontrole, geen volledige securitycertificering.
- Werkelijke staginglijst vergeleken met het scanmanifest: exact 160 bestanden. Voor ieder bestand kwamen de ruwe SHA-256 en het met Git genormaliseerde blob-ID overeen met de gecontroleerde werkkopie. Geen runtime-/Office-/archiefbestand toegevoegd. Controleadministratie blijft lokaal in `.git/precommit-audit`.
- Aanvullende `git diff --cached --check` meldde bestaande trailing whitespace en lege slotregels in bron-/referentiebestanden. Deze niet-functionele formatteringsmeldingen zijn geen securitybevinding. Geen inhoud opgeschoond, beveiligingscontrole uitgezet of bronwijziging gedaan om deze controle te laten slagen.
- Alle bronkandidaten, behalve dit bijgewerkte rapport, kwamen overeen met de eerder succesvol geteste geïsoleerde kopie. De eerdere volledige build (alle zeven projecten, 0 waarschuwingen/fouten) en regressies (553 webcontroles, 43 Node-tests, SmokeTests en WPF) blijven geldig voor deze ongewijzigde bron. Geen nieuwe applicatiebuild nodig voor uitsluitend rapport-/Git-inrichting.
- Open securityrisico's uit het historische assurancerapport blijven open. Geen actieve portal, database, passkey, bestaande deploymentconfiguratie of historisch archief gewijzigd; geen applicatieontwikkeling uitgevoerd.

**Stopgrens: de eerste push is niet uitgevoerd. Afzonderlijke expliciete toestemming voor `git push -u origin main` is vereist.** Na toestemming eerst private zichtbaarheid, remote en lokale status opnieuw controleren; geen force-push of extra publicatie.

---

# GitHub-inrichting hervat — voorgaande controlestap

Datum: 31 oktober 2026. Toenmalige status: **publieke deploymentcontext expliciet toegestaan; laatste baselinecontrole vóór lokale commit en private repositoryaanmaak**. De actuele eindstatus staat bovenaan; onderstaande eerdere rapporten blijven historische context.

## Nieuwe controles

- Git: branch `main`, geen commits, index leeg, geen gevolgde bestanden en geen remotes. `git add --dry-run --all` slaagt voor 160 kandidaten. Bestaande auteursnaam/e-mail zijn aanwezig en ongewijzigd; waarden worden niet gepubliceerd. Geen ACL-, eigendoms- of safe.directory-wijziging toegepast.
- .NET SDK 10.0.403. De bestaande NuGet-configuratie is toegankelijk; nuget.org staat ingeschakeld. Locked restore van alle zeven projecten is geslaagd, zonder alternatieve configuratie of identiteit.
- GitHub CLI is via de bestaande keyring actief aangemeld; API-accountcontrole slaagt. Zowel repositorylookup als de lijst van eigen repositories leveren geen bestaande `LummaSimulator` op. Geen token opgevraagd of gekopieerd. Nog geen repository aangemaakt; private zichtbaarheid is daarom nog niet bevestigd.
- Alleen de 160 Git-kandidaten zijn naar een nieuwe geïsoleerde bronkopie onder `artifacts/github-resume-83b6dd8621604d09a2ea3f6aa4390b10/work` gekopieerd. Volledige solutionbuild, inclusief transitieve LocalTestServer: **geslaagd, 0 waarschuwingen, 0 fouten**.
- Volledige regressieorchestrator: **exitcode 0**, alle geconfigureerde suites geslaagd. Web: **553 controles**; Node UX: **43 geslaagd, 0 mislukt, 0 overgeslagen**; SmokeTests en Windows WPF Verification geslaagd. `LUMMA_TEST_WEB_DLL` wees expliciet naar de geïsoleerde Debug-build. TEMP/TMP en data/log/export zijn voor deze run binnen de nieuwe artifacts-map ingesteld; de HTTP-fixture gebruikt een nieuwe database, bijbehorende nieuwe keyring en tijdelijk testcertificaat op loopback.
- Logs: `restore.log`, `build.log`, `regression.log` onder bovengenoemde artifacts-runmap. Geen bestaande runtimegegevens of historische bewijssets gekopieerd, geopend of gewijzigd. Geen actieve portal gestart, gestopt of herstart. Geen deploymentconfiguratie aangepast. Geen publishcontrole uitgevoerd: publish/sidecarintegratie is niet gewijzigd. Fysieke authenticators, mobiel, productie-TLS en operationele MDR blijven buiten deze acceptatie.

## Security en commitkandidaten

Het bestaande `docs/security/security-assurance-report.md`, threat model en dependency-inventaris zijn beoordeeld. Dit is een beperkte historische bronreview, geen voltooide georkestreerde securityscan, actuele volledige CVE-audit of releasevrijgave. De open risico's rond opslag/egress, helperintegriteit/childomgeving, publieke capaciteit, privacy/retentie, signing/SBOM en bewijsintegriteit blijven bestaan. De eerdere Prize-status is historisch en bewijst niet de huidige implementatiestatus. Geen securityfixes of nieuwe functionaliteit uitgevoerd.

De bestaande kandidaatscanner is opnieuw uitgevoerd op alle 160 bestanden: strikt UTF-8, hashes, bestandsnamen en inhoud; geen Office- of andere binaire kandidaat. Resultaat: dezelfde tien eerder beoordeelde treffers, geen nieuw vastgesteld secret of persoonsgegeven. Zeven treffers betreffen de reeds gemelde deploymenthost, drie synthetische/codepatronen. Een aanvullende controle op JWT, Authorization, connectionstrings en lange literals vond uitsluitend geverifieerde `contentHash`-waarden in package-lockfiles. Dit zijn patrooncontroles, geen garantie dat ieder mogelijk geheim wordt herkend.

De oorspronkelijke Word-file blijft uitgesloten, inclusief de eerder vastgestelde persoonsgegevens/metadata; niet opnieuw opgenomen of gewijzigd. Databases, keyrings, archieven, artifacts en buildoutputs blijven genegeerd. Alle drie lockfiles, verification-bron/publishscript, gedeelde sidecartargets en ATT&CK-licentie blijven kandidaat. Geen workflow of beschermd runtimepad in de kandidaten. Geredigeerde scanresultaten en manifest staan in `.git/precommit-audit`; aanvullende treffers in de nieuwe artifacts-runmap. Werkelijke stagingcontrole bevestigt nog steeds een lege index; geen staging of commit uitgevoerd.

## Resterend besluit en vervolgstappen

De gebruiker heeft op 31 oktober 2026 expliciet toestemming gegeven voor opname van de bestaande concrete deploymenthost, WebAuthn RP ID en toegestane origins in de private repository `scorpido74/LummaSimulator`. Dit betreft uitsluitend publieke configuratiewaarden in `LummaSimulator.Web/appsettings.json:2,4,6`, `LummaSimulator.Web/Configuration/WebAuthnSettings.cs:13`, `LummaSimulator.App/SessionMetadataClient.cs:29–30` en `tests/LummaSimulator.Web.Tests/Program.cs:32`. Secrets, private keys, tokens, certificaatgeheimen en persoonsgegevens vallen buiten deze toestemming. RP-ID/origin en bestaande werkende deploymentconfiguratie blijven ongewijzigd.

Na dit besluit: rapport actualiseren, alle kandidaten opnieuw controleren, werkelijke indexinhoud vergelijken, eerste lokale commit `Initial baseline after project cleanup` maken, uitsluitend de gevraagde nieuwe private repository aanmaken en zichtbaarheid/remote controleren. **Daarna afzonderlijk expliciete toestemming vragen voor de eerste push van `main`.** Er is nu niets gepusht en geen publicatie, release of workflow aangemaakt.

---

# Git-baseline vervolgrapport

Datum: 31 oktober 2026. Status: **sanering en requirements afgerond; baselinecommit en GitHub-inrichting geblokkeerd door toegangsrechten en onvoltooide .NET-verificatie**.

## Uitgevoerde sanering

De auteursnaam in docs/functional-requirements.md is vervangen door Projectteam. In docs/project-cleanup-analysis.md zijn vier persoonlijke profielpaden vervangen door %LOCALAPPDATA%. In docs/project-cleanup-execution-report.md zijn de persoonlijke Windows-accountnaam en SID vervangen door een beschrijving van de oorspronkelijke gebruikersidentiteit. Technische historie, archieflocaties en herstelcontext zijn behouden; externe archieven zijn niet gewijzigd of geopend.

De concrete deploymenthost is in historische documentatie en het eerdere scanrapport vervangen door passkey.example.invalid. Deze documentaire host is uitsluitend een voorbeeld en geen werkende installatieconfiguratie. Dit geldt voor LummaSimulator-v0.4.md, LummaSimulator-v0.5.md, Passkey-Portal-v0.3-plan.md, Passkey-Portal-v0.3.md en security/secure-architecture.md. De eerdere tabel hieronder beschrijft historische bevindingen; de oorspronkelijke regels kunnen door sanering verschuiven.

Het oorspronkelijke Word-document docs/LummaSimulator_functionele_requirements_v0.1.docx blijft lokaal intact en is expliciet uitgesloten van Git. Read-only OOXML-inspectie bevestigde 17 parseerbare XML/relationship-onderdelen, één niet-leeg attributieveld in docProps/core.xml en geen ins/del/comment-markeringen. Er is een JPEG-thumbnail; geen ingebedde objecten of macro's in de pakketlijst. De eerder vastgestelde auteursnaam in body en metadata is niet verwijderd: volledige sanering met geverifieerd behoud van opmaak is hier niet aangetoond. Daarom geen opname. De gesaneerde Markdown-requirements blijven beschikbaar. Historische back-ups zijn ongemoeid gelaten.

Nieuw: architecture/hybrid-deployment-requirements.md. Dit document specificeert development, centrale hosting, zelfstandig offline gebruik, wifi/bezoekersapparaten, WebAuthn/TLS/DNS, toekomstige fysieke administrator-YubiKeys en optionele synchronisatie. Het is uitsluitend requirements; geen implementatie of definitieve keuze voor een domeinstrategie.

## Functionele uitzonderingen

Ongewijzigd behouden: LummaSimulator.Web/appsettings.json regels 2/4/6, Configuration/WebAuthnSettings.cs regel 13, LummaSimulator.App/SessionMetadataClient.cs regels 29/30 en tests/LummaSimulator.Web.Tests/Program.cs regel 32. De deploymenthost is respectievelijk AllowedHosts/RP ID/origin, default, validatie/fouttekst en bijbehorende fixture. Deze waarden zijn geen aangetroffen geheim, maar wel concrete omgevingsinformatie. Ze zijn niet vervangen omdat dit functioneel gedrag kan veranderen. Dit rapport geeft geen algemene vrijgave voor vertrouwelijke gegevens; beoordeel deze uitzondering bij hervatting vóór publicatie. Actieve runtimeconfiguratie, databases, passkeys, DNS en certificaten zijn niet gewijzigd.

## Nieuwe scan en ignorecontrole

160 huidige commitkandidaten, inclusief dit rapport. Alle kandidaten zijn strikt als UTF-8 gelezen en gehasht; onverwachte binaire/NUL-inhoud zou de controle stoppen. Bestandsnamen, inhoud, gevoelige patronen en de uitgesloten OOXML-pakketstructuur zijn gecontroleerd. Geen database, databaseback-up, private key, certificaatbestand, runtimebestand, workflow of binair bestand staat in de kandidatenlijst. Er zijn geen nieuwe echte secrets of onbedoelde persoonsgegevens vastgesteld met deze patrooncontrole. Dit is geen garantie dat ieder mogelijk geheim herkenbaar is.

Tien resterende patroontreffers: zeven functionele deploymenthostvermeldingen op de hierboven genoemde locaties en drie beoordeelde synthetische/code-treffers. HttpChecks.cs:36 genereert een tijdelijk willekeurig testcertificaatwachtwoord; Program.cs:33 bevat ongeldige synthetische URL-userinfo; Test-LocalHttpPublished.ps1:42 bevat expliciet fictieve payloads. Lockfilehashes en openbare ATT&CK-identificatoren blijven behouden. Geen productie-WebAuthn-records in kandidaten gevonden.

De bestaande ignorecategorieën voor build/test/runtime/database/keyring/geheimen/archieven zijn behouden. De expliciete Word-uitsluiting en artifacts-output zijn met git check-ignore bevestigd. Alle drie lockfiles, migratiebron, verification-bron/publishscript, gedeelde sidecartargets en ATT&CK-licentie zijn nog kandidaat. Geen brede JSON-/documentatie-/verification-uitsluiting toegevoegd. Scanmanifest en geredigeerde treffers staan lokaal onder artifacts/github-baseline/audit; testlogs onder artifacts/github-baseline. Deze bestanden zijn genegeerd.

## Werkelijke verificatie en blokkades

| Controle | Nieuw resultaat |
| --- | --- |
| Node UX-regressie | 43 geslaagd, 0 mislukt, 0 overgeslagen. |
| Geïsoleerde bronkopie | Alleen Git-kandidaten gekopieerd naar artifacts/github-baseline/work; geen bestaande outputs of runtimegegevens gekopieerd. |
| .NET locked restore | Mislukt: toegang geweigerd tot de bestaande gebruikers-NuGet.Config. Geen alternatieve identiteit/configuratie ingezet om deze blokkade te omzeilen. |
| Volledige build, WebAuthn/Prize, WPF, LocalTestServer | Niet uitgevoerd wegens restoreblokkade; geen nieuwe succesclaim. |
| Publish en Test-LocalHttpPublished.ps1 | Niet uitgevoerd wegens buildblokkade. |
| Git stagingvoorvertoning | git add --dry-run --all mislukt: index.lock kan niet worden gemaakt wegens toegang geweigerd tot .git. Geen rechten aangepast. |
| Git-identiteit | Bestaande auteursnaam/e-mail aanwezig; niet gewijzigd of in dit rapport gepubliceerd. |
| GitHub CLI-authenticatie | Niet verifieerbaar: toegang geweigerd tot bestaande GitHub CLI/config.yml. Geen token gelezen, gekopieerd of aangemaakt. |

De volledige regressie vereist bij hervatting expliciet LUMMA_TEST_WEB_DLL naar de geïsoleerde DLL en uitsluitend synthetische loopbackfixtures. De bestaande HTTP-tests maken tijdelijke testcertificaten; geen operationeel certificaat is aangemaakt of gewijzigd. Fysieke authenticators, mobiel, productie-TLS en operationele MDR zijn niet geaccepteerd.

## Git en GitHub

Branch main, staginglijst leeg, 0 gevolgde bestanden, geen commit/hash en geen remotes. Gewenste commitboodschap blijft Initial baseline after project cleanup. Geen commit gemaakt omdat de gevraagde verificaties niet zijn geslaagd en schrijven naar de Git-index geblokkeerd is.

GitHub-account kon niet opnieuw worden vastgesteld. Repositorynaam: LummaSimulator; bestaan en zichtbaarheid niet opnieuw verifieerbaar. Geen repository aangemaakt, origin ingesteld, workflow/release aangemaakt of push uitgevoerd. Private zichtbaarheid is dus nog niet bevestigd.

Voor hervatting is een toegestane uitvoeringscontext nodig met toegang tot de bestaande Git-metadata, NuGet-configuratie/packagecache en GitHub CLI-configuratie. Geen ACL-/eigendomswijziging of alternatieve identiteit toegepast. Voer daarna de ontbrekende geïsoleerde controles uit, herhaal kandidaten- en daadwerkelijke stagingcontrole, commit en verifieer de commitinhoud. Controleer repositorybestaan opnieuw; nooit overschrijven. Maak uitsluitend de gevraagde private repository aan na succesvolle baseline. Vraag pas daarna expliciete toestemming voor de eerste push. Push is nu niet aan de orde.

---

## Historisch rapport vóór deze vervolgstap

Onderstaande eerdere resultaten en blokkades zijn historische context; bovenstaande status is leidend.
# Git- en GitHub-inrichtingsrapport

Datum: 31 oktober 2026. Status: **lokale voorbereiding uitgevoerd; gestopt vóór staging, eerste commit en GitHub-publicatie wegens gegevensbevindingen**.

## Uitgangssituatie en lokale inrichting

- De drie gevraagde documenten zijn gelezen: opschoningsanalyse, uitvoeringsrapport en functionele requirements.
- Git: `2.56.0.windows.4`. GitHub CLI: `2.98.0`.
- GitHub CLI is via de bestaande keyring aangemeld met een persoonlijk GitHub-account. Geen nieuw token aangemaakt, opgevraagd of opgeslagen. De sandbox kon de bestaande CLI-configuratie niet lezen; de controle is na expliciete toestemming onder de normale gebruikerscontext uitgevoerd.
- De bestaande Git-auteursnaam en het bestaande e-mailadres zijn beschikbaar. Deze zijn niet gewijzigd; er is geen fictieve identiteit ingesteld. De waarden staan bewust niet in dit rapport.
- Vooraf geen Git-repository in de projectmap of bovenliggende werkmap gevonden. Git is lokaal geïnitialiseerd met `main`; er zijn nog geen commits of remotes.
- Een directe GitHub-query voor `LummaSimulator` onder het aangemelde persoonlijke account vond geen toegankelijke bestaande repository. De controle via de lijst met eigen repositories vond evenmin een match. Er is nog geen GitHub-repository aangemaakt.
- De oorspronkelijke projectstructuur, broncode, runtimegegevens en actieve installatie zijn niet gewijzigd.

Git signaleerde een eigendomsverschil tussen de projectmap en de sandboxidentiteit. Alleen het eigendom van de nieuw aangemaakte `.git`-metadata is na expliciete toestemming op de werkelijke gebruikersidentiteit gezet. Geen permanente `safe.directory`-uitzondering ingesteld; bestaande bron-/runtime-ACL's niet aangepast. Git-opdrachten voor deze repository zijn vervolgens onder die gebruikerscontext uitgevoerd.

## Ignore-regels en AGENTS.md

`.gitignore` is uitgebreid volgens de opschoningsanalyse: buildoutputs, tijdelijke bestanden, logs/testresultaten, SQLite en sidecars/back-ups, runtimegegevens, keyrings, cryptografisch materiaal, credentials, lokale geheime instellingen en expliciete archiefroots zijn uitgesloten. Geen algemene JSON-, ZIP- of verification-uitsluiting toegevoegd.

18 representatieve uitsluitingspaden zijn met `git check-ignore` en exacte padargumenten gecontroleerd. Daarnaast is bevestigd dat 14 belangrijke bron-/config-/lock-/migratie-/referentie-/Office-paden beschikbaar blijven voor beoordeling, waaronder alle drie de package-lockbestanden, de verification-bron, gedeelde settings en de ATT&CK-licentie. Geen database-/sleutel-/runtime- of workflowpad stond in de commitkandidatenlijst.

`AGENTS.md` beschrijft de architectuur, .NET 10, onafhankelijke WPF/portalonderdelen, database-/migratiebescherming, WebAuthn-beperkingen, security-by-design, geïsoleerde testdata, passende regressies, expliciete toestemming voor risicovolle acties en kleine controleerbare commits met documentatie.

## Volledige kandidatenlijst en inhoudscontrole

De eerste controle omvatte **159 bestanden, 1.409.217 bytes**. Dit rapport voegt één nieuw tekstbestand toe; de eindcontrole moet daarom **160 kandidaten** bevatten. De volledige lijst is expliciet vergeleken met de voorvertoning van `git add --dry-run --all`. Er is geen echte `git add` uitgevoerd.

Alle tekstkandidaten zijn gelezen en gecontroleerd op sleutel-/tokenpatronen, private keys, credentialhoudende URL's/connection strings, letterlijke geheimtoewijzingen, persoonsgegevens, lokale identiteiten, concrete deploymentconfiguratie en geserialiseerde WebAuthn-records. Aanvullend zijn JWT-, Azure-connectionstring-, Authorization-, private-netwerk- en lange-literalpatronen gecontroleerd. De bestaande JSON-configuratie en gevoelige-gerelateerde treffers zijn inhoudelijk beoordeeld. Lockfile-integriteitshashes en openbare ATT&CK-identificatoren zijn geen geheimen.

Het Word-bestand is als OOXML-pakket onderzocht: alle 17 XML-/relationships-onderdelen zijn gelezen, inclusief body, footer, custom XML, instellingen en core-/appmetadata. De enige niet-XML-inhoud was `docProps/thumbnail.jpeg`; deze is apart visueel gecontroleerd en bleek blanco. Geen embedded documenten of macrobestanden in het pakket aangetroffen. De auteur in body en metadata blijft wel een persoonsgegeven.

Dit is een inhouds- en patrooncontrole van de huidige commitkandidaten, geen garantie dat ieder mogelijk geheim automatisch herkenbaar is. De aangetroffen persoonsgegevens zijn op zichzelf al voldoende om de door de gebruiker gevraagde stop vóór commit toe te passen. Geen actuele API-key, werkelijk wachtwoord, werkelijk bearer-token, private key of productie-WebAuthn-record als zodanig vastgesteld in de kandidaten. De runtimebestanden zijn uitgesloten en niet voor deze controle geopend.

## Bevindingen die een besluit vereisen

Waarden worden niet weergegeven. Regels verwijzen naar de huidige, ongewijzigde betrokken bestanden.

| Bestand | Exacte locatie | Bevinding |
| --- | --- | --- |
| `docs/functional-requirements.md` | Regel 9 | Persoonsnaam in auteursvermelding. |
| `docs/LummaSimulator_functionele_requirements_v0.1.docx` | `word/document.xml`, paragraaf 4 | Dezelfde persoonsnaam in de documenttekst. |
| `docs/LummaSimulator_functionele_requirements_v0.1.docx` | `docProps/core.xml`, veld `creator` | Niet-lege auteurmetadata; beoordelen/saneren vóór opname. |
| `docs/project-cleanup-analysis.md` | Regels 161–164 | Gebruikersspecifieke absolute profiel-/runtimepaden. |
| `docs/project-cleanup-execution-report.md` | Regel 17 | Concrete Windows-accountidentiteit en bijbehorende SID. |
| `LummaSimulator.Web/appsettings.json` | Regels 2, 4 en 6 | Concrete deploymenthost/RP-ID/origin in gedeelde configuratie. Geen wachtwoord aangetroffen. Vertrouwelijkheid en toegestane opname bevestigen. |
| `LummaSimulator.Web/Configuration/WebAuthnSettings.cs` | Regel 13 | Dezelfde concrete deploymentcontext in een standaardinstelling. |
| `LummaSimulator.App/SessionMetadataClient.cs` | Regels 29–30 | Concrete deploymenthost in validatie-/fouttekst. |
| `tests/LummaSimulator.Web.Tests/Program.cs` | Regel 32 | Concrete deploymenthost als validatiefixture. |
| `docs/LummaSimulator-v0.4.md` | Regels 39, 76 en 80 | Concrete deploymenthost in documentatie. |
| `docs/LummaSimulator-v0.5.md` | Regel 308 | Concrete deploymenthost in documentatie. |
| `docs/Passkey-Portal-v0.3-plan.md` | Regel 48 | Concrete deploymenthost in documentatie. |
| `docs/Passkey-Portal-v0.3.md` | Regels 47, 105 en 110 | Concrete deploymenthost in documentatie. |
| `docs/security/secure-architecture.md` | Regel 57 | Concrete deploymenthost in architectuurdocumentatie. |

Een concrete deploymenthost is niet automatisch geheim of intern. Zonder een besluit over deze omgevingsgegevens wordt hier geen toestemming tot upload afgeleid. Verander RP-ID/origin of actieve standaardconfiguratie niet automatisch om een scan groen te maken.

## Beoordeelde onschuldige treffers

- `tests/LummaSimulator.Web.Tests/HttpChecks.cs:36`: willekeurig gegenereerd tijdelijk certificaatwachtwoord; geen vast opgeslagen geheim.
- `tests/LummaSimulator.Web.Tests/Program.cs:33`: bewust ongeldige URL met synthetische userinfo voor een afwijzingstest.
- `verification/Test-LocalHttpPublished.ps1:42`: expliciet fictieve payload voor helperacceptatie.
- `docs/references/enterprise-attack-20.0-subset.json:4`: openbare bronintegriteitshash; regels 21, 38, 57, 73, 89, 107, 125, 142, 160 en 176: openbare STIX-identificatoren.

Deze fixtures en referenties zijn behouden. Er zijn geen geheime waarden uit deze treffers in het rapport opgenomen.

## Staging-, commit- en GitHub-status

| Controle | Uitkomst |
| --- | --- |
| Branch | `main`, nog zonder commit. |
| Werkelijke index/staginglijst | Leeg: 0 bestanden. Expliciet gecontroleerd met `git diff --cached --name-only`. |
| Gevolgde bestanden | 0; gecontroleerd met `git ls-files`. |
| Eerste commit | Niet gemaakt; de gevraagde boodschap blijft `Initial baseline after project cleanup`. |
| Commit-hash | Niet van toepassing; er is nog geen commit. |
| Remotes | Geen. |
| GitHub-repository | Niet aangemaakt; geen upload, workflow, release, README/licentie/ignore-generatie op GitHub. |
| Productiegegevens/portal | Niet gestart, gestopt, gemigreerd of gewijzigd door deze Git-inrichting. |

Lokale controleadministratie staat uitsluitend onder `.git/precommit-audit/`: `candidate-manifest.csv` (paden, bytes, SHA-256), `staging-preview.txt`, `findings-redacted.csv`, `extended-findings-redacted.csv` en `summary.json`. Deze metadata worden niet door Git opgenomen. Het gebruikte controlescript wordt daar eveneens bewaard. De exacte waarden van gevoelige treffers zijn niet in de bevindingen opgeslagen.

## Benodigde vervolgbeslissing

De gebruiker moet aangeven of de hierboven gelokaliseerde auteur-/omgevingsgegevens eerst gesaneerd moeten worden, of expliciet in de **private** repository mogen worden opgenomen. Tot dat besluit blijven staging, commit, repositoryaanmaak en push geblokkeerd. Historische documenten en operationele configuratie zijn niet stilzwijgend gesaneerd of weggelaten.

Na dat besluit: eventuele overeengekomen sanering uitvoeren, alle kandidaten opnieuw controleren, uitsluitend goedgekeurde bestanden stagen, de werkelijke indexinhoud opnieuw controleren en dan pas de eerste commit maken. Controleer opnieuw of de persoonlijke GitHub-repository inmiddels bestaat; gebruik een geschikte bestaande private repository of maak anders de gevraagde lege private repository. Verifieer privacy vóór de eerste push en controleer daarna de remote en commit-hash. Geen extra workflows of releases toevoegen.

Geen applicatiebuild of regressierunner gestart voor deze uitsluitend Git-/documentatie-/ignorewijzigingen. De eerder gedocumenteerde geïsoleerde build-/testresultaten zijn niet als nieuwe resultaten geclaimd.

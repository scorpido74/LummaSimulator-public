# Event Edition v0.1 — distributievoorbereiding en verificatie

Datum: 2 november 2026. Branch: `feature/phase5-domain-foundation`. De werkboom had bij aanvang geen tracked wijzigingen; `codex-start.bat` en de eerdere `.setup-verification-*` waren ongetrackt en zijn niet gewijzigd of meegenomen. Geen commit, push of GitHub Release uitgevoerd.

**Status: distributiebron voorbereid; geen distributie-ZIP opgeleverd.** De geïsoleerde locked restore wordt door de omgeving geblokkeerd op toegang tot `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. Deze permissieblokkade is niet via een andere configuratie, locatie of identiteit omzeild. Zonder restore/build/publish is het pakket niet als werkend vrijgegeven.

## Actuele architectuur

| Project | Rol in distributie/verificatie |
|---|---|
| LummaSimulator.Web | Zelfstandige ASP.NET Core-host; Razor Pages, WebAuthn, SQLite en beheerwizard; publiceren naar Portal |
| LummaSimulator.App | Zelfstandige WPF-app inclusief Session Exposure Monitor- en Event Edition-tabs; publiceren naar Simulator |
| tools/LummaSimulator.LocalTestServer | Transitive sidecar; bestaand App-publishtarget neemt dezelfde win-x64/self-contained variant mee naar Simulator/LocalTestServer |
| tests/LummaSimulator.Web.Tests | FIDO2-, database-, HTTPS- en browserregressies; niet distribueren |
| tests/LummaSimulator.SmokeTests | Windows/WPF-, client- en sidecarregressies; niet distribueren |
| verification/Verification | Actieve WPF-verificatiebron; niet distribueren |
| tests/LummaSimulator.RegressionTests | Bestaande orchestrator voor bovenstaande en Node-tests; niet distribueren |

Geen nieuwe webhost, monitorservice, migratie of databasebeheerlaag. De bestaande DemoStore.InitializeAsync initialiseert/migreert alleen de expliciet door de starter geconfigureerde opslag bij portalstart. Bestaande PresenterService-bootstrap, IFido2, uitnodigingen, origin-/CSRF-/rate-limit-/loopbackcontroles en gescheiden enrollment/login blijven gebruikt.

De starters gebruiken Kestrels ingebouwde certificate-storeconfiguratie: een vooraf geprovisioneerd vertrouwd certificaat in CurrentUser/My, unieke subjectnaam en private key die op de laptop blijft. Lokale productieconfiguratie: `https://localhost`, RP-ID localhost, poort 443, uitsluitend loopback. Geen Development-user-secrets, geen TLS-uitzonderingen en geen gewijzigde WebAuthn-validatieregels. Setup voert geen certificaatcreatie/import, firewall- of ACL-wijzigingen uit. De handleiding beschrijft de bewuste handmatige provisioning.

De lokale installatie-ID bepaalt een eigen LocalAppData-opslag. Configuratieaanmaak gebruikt CreateNew en overschrijft ook bij gelijktijdige Setup geen bestaande identiteit. Childprocessen erven geen bestaande LUMMA-/database-/Kestrel-/hostingoverrides of monitorgeheimen. Offline WPF hoeft geen nog geldig portalcertificaat te hebben. Stop accepteert uitsluitend eigen package-executablepaden met passende PID en starttijd. WPF sluit eerst normaal; de bestaande sidecarlifecycle blijft behouden.

## Nieuwe en gewijzigde bestanden

- `packaging/Build-EventEdition.ps1`: geïsoleerde bronkopie, restore/build/regressie, self-contained win-x64-publicaties, bestaande sidecaracceptatie, pakketcontroles en ZIP/hashmanifest. Stopt bij fouten vóór archivering.
- `packaging/EventEdition/Demo.ps1`: setup-, start-, stop- en bootstrapimplementatie.
- `packaging/EventEdition/Setup-Demo.cmd`, `Start-Demo.cmd`, `Stop-Demo.cmd`, `Start-PasskeyPortal.cmd`, `Start-LummaSimulator.cmd`, `Bootstrap-Administrator.cmd`: eenvoudige starters zonder SDK of execution-policybypass.
- `verification/Test-EventEditionPackaging.ps1`: afzonderlijke checks zonder database- of certificaatmutaties.
- `docs/event-edition-installatie.md`: Nederlandse installatie-, certificaat-, bootstrap- en probleemoplossingsinstructies.
- Dit verificatieverslag.
- `Pages/Presenter/Setup.cshtml` en de drie UiText-resources: apphostopdracht in plaats van SDK-opdracht, plus aanwijzing om de pakketwrapper te gebruiken.
- `.gitignore`: expliciete uitsluiting van eerdere verificatiekopieën en lokale installatieconfiguratie.

## Werkelijke resultaten

| Controle | Resultaat |
|---|---|
| Git-branch en status vooraf | Gecontroleerd; juiste branch |
| PowerShell-parsing en launcherchecks | 22 geslaagd onder Windows PowerShell 5.1; helperchecks met synthetische procesrecords en padnamen met spaties |
| Node-regressie, alle vier UX-bestanden | 52 geslaagd, 0 mislukt |
| XML/resourcevalidatie, dubbele sleutels | Geslaagd |
| git diff --check | Geslaagd |
| Ignorecontrole tijdelijke kopieën/configuratie | Geslaagd |
| Locked restore in eigen bronkopie | Mislukt: NuGet.Config access denied voor alle zeven projecten |
| Volledige solutionbuild/.NET-orchestrator | Niet uitgevoerd na restoreblokkade |
| Self-contained App/Web/sidecar-publicatie | Niet uitgevoerd na restoreblokkade |
| Gepubliceerde sidecaracceptatie | Niet uitgevoerd; gekoppeld aan builder voor na publicatie |
| ZIP-inhoud, runtime/native SQLite en hashes | Niet uitgevoerd: geen ZIP/publicatie aanwezig |
| Werkelijke start vanuit pad met spaties | Niet uitgevoerd; uitsluitend launchargumenten en omgeving getest |
| Verse database, registratiebehoud na herstart, werkelijk stoppen | Niet uitgevoerd; vereist gepubliceerd pakket/eigen VM |

De eerste bouwpoging bewaart diagnostiek onder `artifacts/event-edition-package-80dfcff8210047d688c2c6e66bad813e/restore.log`; `node.log` bevat de UX-resultaten. Deze tijdelijke kopie is uitsluitend bouwuitvoer, geen alternatieve actuele bron. Latere bronverfijningen zijn met de launcherchecks en resourcevalidatie gecontroleerd, niet met een geslaagde .NET-build.

Na herstel van de toegestane bouwtoegang: voer `packaging/Build-EventEdition.ps1` opnieuw uit. Beoogde output is `dist/LummaSimulator-EventEdition-v0.1-win-x64.zip`; er is nu geen bestand op dat pad. Het script neemt alleen de benoemde starters uit de template op, nooit een eventueel lokaal aangemaakte installatieconfiguratie. Het kopieert uitsluitend verse publishoutput, vervangt appsettings door bekende niet-geheime defaults, controleert verboden runtime-/secretbestandstypen, vereiste runtimebestanden en native SQLite en houdt SDK/tests/broncode buiten het pakket. Bestaande Navigator-licentie wordt meegenomen. Controleer vóór daadwerkelijke externe vrijgave ook alle toepasselijke dependencylicenties/notices en de daadwerkelijke ZIP-inhoud; er is nog geen vrijgaveclaim.

## Open acceptatie en risico's

1. Schone Windows 11 x64-VM zonder SDK/runtime/SQLite: downloaden/uitpakken, pad met spaties, scriptvrijgave volgens lokaal beleid, certificaatprovisioning en vertrouwde HTTPS, eerste database en wizard.
2. Fysieke YubiKey: registratie en afzonderlijke authenticatie; herstart met behoud van accounts/keys; reservesleutel en laatste-adminbescherming. Attestation none bewijst geen hardwaretype.
3. Proceslifecycle: herhaald starten, bezette poort, WPF-sluitweigering, sidecar-parentverlies en uitsluitend eigen processen stoppen; geen gegevensverlies door stop tijdens normale idle toestand.
4. Geen mobiele ondersteuning geclaimd: localhost is lokaal. Extern DNS/TLS/netwerk vereist apart ontwerp en acceptatie; RP-ID niet cosmetisch veranderen.
5. Monitor blijft standaard ongekoppeld. Geen automatische sleutel-/grantprovisioning, werkelijke MDR-meting of overdracht van gebruikersdata. Windows-gebruikers-/DPAPI-context moet behouden blijven.
6. Nog geen digitale ondertekening van binaries/scripts. Windows-beleid/SmartScreen kan uitvoering blokkeren; de scripts schakelen bescherming niet uit. Self-contained runtimes moeten bij beveiligingsupdates opnieuw worden gepubliceerd.

Geen bestaande installatie/processen gestart of gestopt; geen gebruikersdatabases, passkeys, keyrings of certificaatvertrouwen gewijzigd.

## Correctie pakketpadvalidatie

Het oorspronkelijke suffixpatroon classificeerde `Microsoft.Data.Sqlite.dll` ten onrechte als database doordat het `.Sqlite.` midden in de naam matchte. De publishcontrole gebruikt nu de afzonderlijk geteste functie `Test-ForbiddenPackagePath`, met normalisatie van relatieve padseparators, segmentcontroles en volledige bestandssuffixen. Er is geen algemene DLL-uitzondering die beschermde mappen omzeilt: bijvoorbeeld `tests/Microsoft.Data.Sqlite.dll` blijft verboden. Absolute paden en parent-traversal worden ook geweigerd.

Regressiegevallen dekken Microsoft.Data.Sqlite, SQLitePCLRaw, native e_sqlite3, geneste Windows-/Unix-padnotatie, databases en SQLite-sidecars/backups, private keys/PFX, ontwikkelgeheimen, lokale installatieconfiguratie en tijdelijke verificatiekopieën. De beschermingscontrole vindt plaats vóór het overslaan van standaard publishconfiguratie. Bekende niet-geheime appsettings worden nog steeds door de builder gegenereerd.

Resultaat na deze correctie: **72 packaging-checks geslaagd onder Windows PowerShell 5.1**, inclusief de 22 bestaande launcherchecks. `git diff --check` geslaagd. Geen databases aangemaakt/geopend/gewijzigd. Geen volledige build gestart: de gebruiker voert die buiten de Codex-sandbox uit. Deze controle geeft geen nieuwe runtime-/VM-/ZIP-acceptatieclaim.

## Windows PowerShell 5.1 — gepubliceerd proces starten

`verification/Test-LocalHttpPublished.ps1` gebruikte de nieuwere .NET-property `ProcessStartInfo.ArgumentList`. Dit is vervangen door `Arguments`, uitsluitend samengesteld uit een integerpoort met bereik 1–65535 en de vaste switch `--managed`. Het executablepad en de werkmap blijven afzonderlijke properties, zodat spaties geen argumentgrenzen wijzigen. De bestaande HTTP-/nonce-/PID-/synthetische-payload-/shutdowncontroles blijven intact. `System.Net.Http` wordt expliciet geladen voor .NET Framework. De cleanup beschermt het childproces nu vanaf direct na de start, ook als initialisatie van de HTTP-client faalt.

Alle PowerShell-scripts in packaging en verification zijn gecontroleerd op parsing en vergelijkbare nieuwere proces-/pad-API's. Geen andere overeenkomstige API-incompatibiliteit gevonden; bestaande launcherargumenten zijn al vaste strings zonder geïnterpoleerde paden. Een nieuwe `verification/Test-PublishedProcessCompatibility.ps1` extraheert dezelfde starthelper, compileert met Windows PowerShell een eigen synthetische .NET Framework-consoleprobe en start die vanuit een nieuw pad met spaties. De probe bevestigt twee intacte argumenten, de werkmap, stdin-stop, stderr en exitcode. Ongeldige/geïnjecteerde poorten worden vóór starten geweigerd. Alleen de eigen childhandle wordt eventueel opgeruimd; geen gebruikersdatabase of actieve installatie wordt benaderd. De fixture blijft als eigen tijdelijke testuitvoer onder artifacts staan.

Resultaten onder **Windows PowerShell 5.1**: **72 packaging-checks plus 21 compatibiliteitschecks geslaagd**. De packaging-checks roepen deze compatibiliteitstest automatisch via powershell.exe aan, ook als de builder zelf onder PowerShell 7 draait. `git diff --check` geslaagd. Geen volledige build of daadwerkelijke gepubliceerde sidecar-HTTP-acceptatie uitgevoerd; de gebruiker voert de Event Edition-build opnieuw uit buiten de sandbox. Geen commit of push.

# Presenterhelper, centrale synchronisatie en updates

Status 11 november 2026: implementatiecheckpoint in de worktree, **nog niet volledig geverifieerd of geaccepteerd**. Een externe run heeft alle elf projecten gebouwd en 31 helpercontroles doorlopen. Die run stopte daarna op een fout in de lidmaatschapsfixture; de gecorrigeerde sessiedemosuite slaagt inmiddels met 67 controles. De volledige herhaling blijft open. De bestaande wijzigingen na d8da9ee blijven behouden. Geen deployment, gewijzigde actieve opslag, certificaatinrichting, commit of nieuw testpakket uitgevoerd.

## Vastgestelde scope

De portal blijft de autoriteit voor accounts, uitnodigingen, evenementnamen, vaste IDs, lidmaatschappen, rechten, rondes en toestemming. Bestaande registratie en WebAuthn-verificatie blijven ongewijzigd. Alleen de bestaande administratorfunctie maakt nieuwe presentatoruitnodigingen; de helper maakt geen lokale accounts of evenementen. Een presentator kan een volledige geldige uitnodigingslink openen; de browser voert registratie en daarna aanmelding/goedkeuring uit. Een normale login bewijst niet op zichzelf dat een fysieke YubiKey is gebruikt.

De opdrachtgever heeft bevestigd dat app-locatie en helperdownloadlinks bij installatie worden bepaald en later aanpasbaar moeten blijven. Er is daarom geen fictief vast lab-IP toegevoegd. De geïnstalleerde publieke instellingen bevatten portal-origin, HTTPS-app-origin op TCP 443, SHA-256-certificaatpin en release-uitgeverpin. Het koppeltoken wordt per Windows-gebruiker met DPAPI opgeslagen, gescheiden van programma-updates. Dagelijks gebruik vraagt geen handmatig overgetypte tokens, codes of run-IDs.

## Contracten in deze bron

| Contract | Autorisatie en beperking |
| --- | --- |
| `/api/local/participant-demo` | Bestaand loopbackcontract, niet verruimd |
| `/api/helper/participant-demo` | Alleen met `Helper:Enabled=true`; HTTPS, native protocolheader, kortdurende willekeurige app-capability en browsergoedkeuring. Iedere beschermde actie valideert de presentatorsessie en het evenement opnieuw |
| `/Presenter/HelperConnection` | Bestaande presentatorpolicy, exacte Origin, Razor antiforgery, expliciete browsergoedkeuring |
| `/api/helper/participant-demo/compatibility` | Ondersteunde protocolversies en webversie; geen account-/bezoekersgegevens |
| `/lumma-helper/v1/info` en `/selection` | Afzonderlijke appontvanger via HTTP.sys/HTTPS 443; intrekbaar koppeltoken, normale TLS-validatie plus certificaatpin. Selectie accepteert alleen het ingestelde portal-origin en laat de app het kortdurende portalrecht en event-ID opnieuw verifiëren |
| `/lumma-helper/v1/update-lease` en `/update-release` | Alleen ingerichte helper; begrensde installatiepauze. Actieve simulatie/toestemming weigert de lease; nieuwe simulatiestart is gedurende maximaal twee minuten geblokkeerd |
| `/lumma-helper/updates/v1/manifest` en `/package.zip` | Vaste updatecontracten, onafhankelijk van het demo-protocol, zodat ondersteunde oudere helpers nog kunnen bijwerken. Geen vrije bestandspaden |
| `/lumma-helper/download/helper.zip` en `/helper-release.json` | Publieke, vaste signed-releasebestanden voor initiële distributie; alleen wanneer manifest en pakket aanwezig zijn. Geen pairing, instellingen of bezoekersgegevens |

Helper en app gebruiken dezelfde DTO/clientimplementatie uit linked source in Helper.Core. De app haalt bij vernieuwen ook de actuele evenementen op: naamwijzigingen behouden IDs, nieuw geaccepteerde rechten verschijnen en ingetrokken rechten verdwijnen. Het ontbreken van toestemming is geen filter op de deelnemerslijst. De bestaande expliciete Start vraagt toestemming voor de status van uitsluitend de demosessie en het openen van een fictieve sessie; intrekking en accountverwijdering behouden hun bestaande server-side blokkade en anonieme rondehistorie.

## Installatie en beheergrens

`HelperReceiver.settings.json` naast de app bevat `Enabled` (standaard false), `Origin` (exacte HTTPS-authority, TCP 443) en `PairingTokenHash` (SHA-256 van het ingerichte token). Appinstellingen bevatten de bestaande `PortalOrigin` en optionele `UseRemotePortal=true` voor de nieuwe helperroute. Het tokenhashbestand wordt bij requests opnieuw gelezen: uitschakelen of vervangen trekt de betreffende koppeling in. Verplaatsen van de luisterlocatie vereist de beheerprocedure en een gecontroleerde appherstart.

De beheerder moet vooraf een geldig servercertificaat met private key en passende SAN/server-auth-EKU inrichten, de concrete HTTP.sys-HTTPS-binding en URL-reservering voor de standaardgebruiker toevoegen en alleen de benodigde netwerktoegang van de demo-pc toestaan. Geen generieke Everyone-reservering, wildcardfirewallregel of automatische trust-on-first-use. De code voert deze beheerstappen niet uit. Relevante platformdocumentatie: [HTTP.sys/HTTPS-configuratie](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http).

`Helper:DownloadUrl` en `Helper:InstallationGuideUrl` zijn instelbare HTTPS-443-links voor `/Presenter/Helper`. De helper bewaart instellingen onder `%LOCALAPPDATA%/LummaSimulator/PresenterHelper`, met een private gebruikers-ACL. Programma-updates gebruiken de submap `install/current`; instellingen en pairing blijven erbuiten. Het gecontroleerde stabiele startpunt wordt de updater met `--launch`, die vóór de helperstart een onderbroken transactie herstelt.

**Nog af te ronden na de eerste compile/testcontrole:** de concrete installatie-/beheer-scripts, integratie van helper/updater en releasebestanden in Build-EventEdition en het nieuwe distributiepakket. De bestaande pakketbuilder is nog niet aangepast en mag niet als volledig helperpakket worden gerapporteerd.

## Updates en signing

Eigen helperversie: `0.1.0`; demoprotocol: `1`; update-envelopschema: `1`. Een update bestaat uit een ZIP en `helper-release.json`. De envelop bevat de exacte base64-gecodeerde manifestbytes, RSA-SHA256-handtekening en het publieke uitgeverscertificaat. Het getekende manifest bevat versie, ondersteunde protocollen, exacte ZIP-grootte, SHA-256, het vaste updatepad, een afzonderlijke RSA-SHA256-pakkethandtekening en de complete bestandsinventaris met hashes. Standaard .NET-cryptografie wordt gebruikt; geen WebAuthn-key als automatische releasesigner.

De verifier vereist zowel de vooraf ingerichte certificaat-SHA256 als een geldige OS-certificaatketen met code-signing-EKU en revocationcontrole. Ook manifest, pakket en uitgepakte bestanden worden geverifieerd. Onbekende/oudere versies, verkeerde uitgever/protocol/signatuur/hash, ontbrekende bestanden, ZIP-traversal, alternatieve streams, symlinks, duplicate namen en te grote pakketten worden geweigerd. Verificatiebeleid: [X509ChainPolicy.ApplicationPolicy](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.applicationpolicy?view=net-10.0).

Er is geen bestaande release-signingvoorziening gevonden in de onderzochte bron. Voor een installeerbare release zijn nog nodig: een vertrouwd RSA-code-signingcertificaat met private signingkey buiten Git, beschikbaarheid van keten/revocation, uitrol van de publieke uitgeverpin en een gecontroleerde releasejob die helper/updater publiceert en beide handtekeningen maakt. De updater bevat een expliciete `--sign`-releasefunctie voor het CurrentUser/My-certificaatstore; die is niet uitgevoerd. Dit is pakket-/manifestondertekening, geen claim dat de losse PE-bestanden al Authenticode-ondertekend zijn. Zonder geldige signing wordt geen automatische installatie toegestaan.

Een aparte updater draait vanuit een eigen kopie buiten `current`, wacht op de oorspronkelijke helper-PID/starttijd en verifieert het pakket opnieuw. Exclusieve bestandslocks voorkomen gelijktijdige updates. Een write-through journal registreert staging, vervanging en startupcontrole. De vorige directory blijft bewaard. Een eigen nieuw gestart helperproces geeft na WPF-start een transactiegebonden healthbevestiging. Bij mislukking wordt uitsluitend dat eigen proces gesloten en de vorige werkende directory teruggezet. Het journal ondersteunt herstel na onderbreking tussen directorymoves; er is geen recursieve opschoning. De hoogst geaccepteerde versie en een mislukte versie worden apart bijgehouden.

Deze herstel- en ondertekeningscode is nog niet runtime-gevalideerd. Met name standaardaccountinstallatie, werkelijk vervangen van executables, stroomuitval/health-timeout, leasegedrag en het stabiele recovery-startpunt vereisen de gerichte controles en normale-flow-acceptatie vóór release.

## Verificatiecheckpoint

`packaging/Verify-HelperDevelopment.ps1` maakt één nieuwe synthetische bronkopie en unieke outputconfiguratie, voert maximaal één build tegelijk uit en start geen geïnstalleerde portal. Het herstelt expliciete testenvironmentwaarden na afloop. Het bouwt sidecar en Web eerst afzonderlijk, controleert hun deps/runtimeconfig/reference-output, bouwt de volledige projectketen en zet `LUMMA_TEST_WEB_DLL` op diezelfde portal-DLL. Daarna volgen helpertests, sessiedemo-/ronde-/HTTPS-controles, deelnemers-/WPF-/helper-UI-controles en de volledige bestaande regressieorchestrator. Exitcodes en logs worden buiten Git bewaard; gegenereerde lockfiles worden afzonderlijk voor review gemeld.

Nieuwe tests omvatten synthetische signature-/hash-/protocol-/downgrade-negatieven, bounded extraction, transactioneel herstel, bestaande centrale event-ID’s na delen/hernoemen/intrekken, aparte helperbrowsergoedkeuring en een werkelijk WPF-helpervenster dat offline blijft starten. Er wordt geen fysieke YubiKey, daadwerkelijke netwerkreceiver-inrichting of volledige hardware-/productieacceptatie geclaimd.

De eerdere melding van een lege deelnemerslijst is nog niet via de draaiende normale WPF-flow gereproduceerd: venstertoegang was geweigerd. De diagnostische foutweergave is behouden. De nieuwe helperflow en centrale scopes zijn aanvullend; ze zijn geen bewezen verklaring voor die eerdere runtimewaarneming.

## Deelnemers-QR in dashboard en gekoppelde helper (11 november 2026)

Implementatie in de worktree; geen deployment of volledige runtimeacceptatie. Bestaand lokaal werk is behouden.

### Weergave en vaste URL

Bij het geselecteerde open Prize-rondenevenement tonen zowel Evenementbeheer als de bestaande Prize-evenementpagina een QR-code, de evenementnaam, de volledige deelnemers-URL en **Kopieer deelnemerslink**. De payload gebruikt precies de bestaande vaste route `/Index?eventKind=prize&eventId=<id>`. De route staat nu eenmalig in linked source `ParticipantLink.EventPath`; `PrizeRoundsService.FixedJoinPath` blijft hetzelfde contract aanbieden. Een ronde sluiten verandert deze evenementlink niet. Een evenement sluiten verwijdert de QR-aanbieding.

Het dashboard accepteert voor de absolute URL uitsluitend de actuele request-origin die exact voorkomt in de bestaande WebAuthn-originallowlist, met de werkelijke PathBase. De helper gebruikt het ingerichte portal-origin en controleert dat dit overeenkomt met het origin van de daadwerkelijke WPF-client en de inrichting. Er worden geen domein, IP-adres, poort, RP-ID, toegestane origins, certificaattrust, firewallregels of HTTP.sys-bindingen automatisch gewijzigd.

Bij localhost/loopback wordt expliciet getoond dat de URL alleen op de demo-laptop werkt en een telefoon er niet mee kan aanmelden. Een andere hostname is op zichzelf nog geen bewijs van telefoonbereikbaarheid: daarvoor moeten DNS, netwerkroute en vertrouwd HTTPS werkelijk vanaf de telefoon werken. De code vervangt localhost niet stilzwijgend door een LAN-IP.

De QR bevat geen koppeltoken, app-/bezoekerscookie, sessie-ID, runrecht of deelnemersalias. Generatie gebeurt lokaal in `shared/ParticipantQr.cs`: begrensde QR Model 2 byte-codering, versies 1–10, foutcorrectie L, masker 0 en een witte rand van vier modules. Lange of ongeldige invoer wordt geweigerd, nooit afgekapt. Er is geen externe QR-dienst, pakketupgrade of nieuwe NuGet-afhankelijkheid toegevoegd. De code ondersteunt maximaal 271 ASCII-payloadbytes; normale vaste deelnemerslinks worden automatisch in de kleinste passende versie geplaatst.

### Verversen en verwijderen

Het dashboard gebruikt de beschermde Razor-pagina `/Presenter/ParticipantQr` met handlers `State` en `Image`. Iedere aanvraag controleert opnieuw de echte presentatorsessie, evenementtoegang, toegestane origin en open Group-status. Beide responses zijn `no-store`; gesloten/verwijderde of niet-toegankelijke evenementen leveren geen QR op. De leesroute heeft een afzonderlijke begrensde limiter van 120/minuut, zodat de periodieke controle niet het bestaande budget voor beheeracties verbruikt. Bestaande authenticatie-/beheerlimieten zijn ongewijzigd.

Het dashboard valideert elke twee seconden en wist bij sluiting, autorisatiefout, netwerkfout of ongeldige response de afbeelding, naam en kopieerbare link. Een request heeft een timeout van 1,5 seconde. Bij verlaten/verbergen van de pagina verdwijnt de code; late responses herstellen geen oude weergave. Zonder JavaScript verschijnt geen statische QR die ongemerkt kan blijven staan nadat het evenement sluit.

De bestaande beveiligde receiver heeft een additieve **GET `/lumma-helper/v1/selection`**. De POST voor selectie blijft bestaan. De GET heeft dezelfde TLS-, protocol-, pairing-, Origin-/Forwarded-weigeringen en een begrensde deadline. Hij geeft alleen de actuele gevalideerde WPF-selectie met publieke evenementmetadata terug. WPF toetst verbinding, succesvolle roster-/selectieversie en actuele centrale evenementtoegang; bij een gewijzigde of ongeldige selectie volgt `Unavailable`. Er worden geen tijdelijke apprechten aan de helper teruggestuurd.

De helper haalt de selectie iedere twee seconden op en controleert daarnaast de eigen actuele portalbevoegdheid voor hetzelfde evenement. Hij gebruikt de centrale evenementnaam en de exacte vaste route. Een andere WPF-selectie vervangt de QR automatisch. Sluiten, intrekken, verlopen, incompatibiliteit of een verbroken koppeling wist de afbeelding, evenementnaam en kopieerbare URL. Een aparte watchdog controleert iedere 250 ms een weergavelease van maximaal vier seconden, ook tijdens een trage aanvraag. Gezonde controles laten de afbeelding staan zodat de code stabiel kan worden gescand. De knop voor koppelen heet nu **Evenement in WPF selecteren**; de helper opent geen bezoekersaanmelding automatisch. De bezoeker scant en meldt zich zelf aan. De bestaande browserflow voor presentatorlogin en expliciete appgoedkeuring blijft behouden.

### Verificatie en beperkingen

Nieuwe geïsoleerde bronkopie/logs: `artifacts/participant-qr-47ff413a615449c085308c329c000aaf`. De finale bron is hierin gesynchroniseerd.

- Volledige expliciete Node-regressieset uit de orchestrator: **97 tests, 95 geslaagd, 2 bestaande PE-/publishiconcontroles overgeslagen, 0 fouten** (`node-complete.log`). Nieuwe QR-tests zijn ook opgenomen in de bestaande orchestrator.
- De echte C#-generator en displaypolicy zijn zonder projecten/pakketten in geheugen gecompileerd en uitgevoerd. Een aparte decoder controleert alle versies 1–10, exacte payload, finder-/alignment-/timingpatronen, formaat- en versiebits, Reed-Solomon-syndromen en de exacte SVG-matrix met witte rand. De lifecycleproef controleert selectie A → B, sluiten en verbreken. Dit zijn synthetische in-memory tests, geen actieve app of portal.
- De daadwerkelijke pure C# receiver/client, protocol-DTO's, QR-generator en displaypolicy zijn daarnaast samen gecompileerd met standaard PowerShell-referenties (`contract-check.log`). Er is geen listener gestart. Deze gerichte controle is geen solution-/WPF-/Razor-build.
- Dashboardtests controleren expliciet kopiëren, localhost-waarschuwing, NL/EN-resources, ontoelaatbare eventwisseling, sluiting/toegangsverlies en het blokkeren van late responses na navigatie. XML-/JavaScript-syntax- en `git diff --check`-controles slagen.
- De HTTPS-fixture bevat extra controles op de exacte vaste URL, beschermde/ongecachete SVG, directe onbevoegde links en sluiten van uitsluitend een nieuw synthetisch testevenement. De bestaande QR → passkey → SignedIn → dezelfde alias in herhaalde rosterresponses blijft onderdeel van die fixture. Helper-, WPF- en smokecontroles zijn uitgebreid voor de huidige selectie en het ontbreken van oude codes wanneer offline. **Deze volledige .NET-/HTTPS-/WPF-runners zijn niet uitgevoerd.**
- Locked solution-restore met .NET SDK 10.0.403 is geprobeerd en faalt op ontbrekende leestoegang tot `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config` (`restore.log`). Geen configuratie-, ACL- of identiteitsomweg gebruikt. Er is daarom geen geslaagde nieuwe solution-build of nieuwe appstart via de normale launcher.
- De echte QR van Test Remco, scannen vanaf een fysieke telefoon, bereikbare DNS/netwerk/TLS vanaf die telefoon, handmatige passkey-aanmelding en dezelfde alias na **Lijst vernieuwen** op de demo-laptop zijn **nog niet live bevestigd**. De eerder geweigerde toegang tot Chrome/WPF is niet via een andere tool omzeild. De bestaande draaiende processen en databases zijn niet gewijzigd of herstart.

### Bestanden in deze wijziging

- Nieuw: `shared/ParticipantQr.cs`, `LummaSimulator.Web/Services/ParticipantQrService.cs`, `Pages/Presenter/ParticipantQr.cshtml` en `.cshtml.cs`, `Pages/Shared/_ParticipantQr.cshtml`, `wwwroot/js/participant-qr.js`, `LummaSimulator.Helper.Core/HelperQrDisplay.cs`.
- Dashboard: `Pages/Presenter/Events/Index.cshtml` en `.cshtml.cs`, `Pages/Presenter/Prizes/Event.cshtml` en `.cshtml.cs`, `wwwroot/css/site.css`, de drie `UiText*.resx`-catalogi, `Program.cs` en `Services/Prizes/PrizeRoundsService.cs`.
- Koppeling/helper: `LummaSimulator.App/ParticipantSessionClient.cs`, `EventParticipantsViewModel.cs`, `HelperReceiver.cs`; `LummaSimulator.Helper.Core/HelperProtocol.cs`, `ReceiverHost.cs`; beide `LummaSimulator.PresenterHelper/MainWindow`-bestanden; de Web- en Helper.Core-projectbestanden voor linked source.
- Verificatie: `tests/WebAuthn.Ux.Tests/participant-qr.test.cjs`, `participant-qr-encoding.test.cjs`, `Export-ParticipantQr.ps1`; de bestaande regressieorchestrator; `tests/LummaSimulator.Web.Tests/SessionDemoHttpChecks.cs`, `PrizeRoundsChecks.cs`; `tests/LummaSimulator.Helper.Tests/Program.cs`, `tests/LummaSimulator.SmokeTests/EventParticipantsChecks.cs`, `verification/PresenterHelperUiChecks.cs`.

Voor fysieke acceptatie: open het echte actieve evenement Test Remco, vergelijk de zichtbaar gekopieerde URL met de gedecodeerde QR en controleer de localhost-waarschuwing. Laat de bezoeker op de eigen telefoon scannen en via de bestaande passkeyflow aanmelden. Vergelijk de alias op SignedIn met **Lijst vernieuwen** bij hetzelfde evenement in WPF. Zonder sessiedemotoestemming moet de alias zichtbaar zijn, maar Open blijft geblokkeerd. Test sluiten en verbreken afzonderlijk met een synthetisch testevenement en een eigen testkoppeling.

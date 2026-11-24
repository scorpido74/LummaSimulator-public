# LummaSimulator Event Edition v0.7.0-beta.1 installeren

Zie [branding en talen](branding-and-languages.md) voor lokale resellerlogo's, NL/EN en het neutrale applicatie-icoon. Publicatie en werkelijke eerste-installatieacceptatie zijn afzonderlijke vrijgavevoorwaarden; deze handleiding bewijst die niet.

Deze handleiding beschrijft een lokaal Windows 11 x64-demopakket. De ontwikkelaar bouwt het pakket met .NET 10; de demomachine heeft geen Visual Studio, .NET SDK, runtime of SQLite-installatie nodig. Gebruik uitsluitend fictieve demo-identiteiten. Een self-contained pakket bevat zijn eigen runtime en moet voor beveiligingsupdates opnieuw worden gebouwd.

## Downloaden en uitpakken

Download na vrijgave `LummaSimulator-EventEdition-v0.7.0-beta.1-win-x64.zip` uitsluitend van de publieke distributierepository `scorpido74/LummaSimulator-Releases`. De bronrepository blijft prive. Controleer de ZIP-hash tegen het afzonderlijke `.zip.sha256`-bestand. Er is nog geen publieke release geclaimd. Pak de ZIP volledig uit op een lokale vaste schijf, bijvoorbeeld `C:\Demo pakketten\LummaSimulator Event Edition v0.7.0-beta.1`. Start niets rechtstreeks vanuit de ZIP, OneDrive-synchronisatie of VMware Shared Folders. Paden met spaties worden ondersteund.

Gebruik een eigen Windows-account met Edge of Chrome en Windows PowerShell 5.1. De scripts veranderen geen execution policy. Blokkeert Windows gedownloade scripts, controleer dan herkomst en hashes en laat een bevoegde beheerder het gecontroleerde pakket volgens lokaal beleid vrijgeven; schakel geen beveiliging generiek uit. SmartScreen/antivirus kan niet-ondertekende binaries waarschuwen. Er is nog geen ondertekende installer.

## HTTPS voorbereiden — verplicht, handmatig

De lokale configuratie gebruikt alleen `https://localhost`, RP-ID `localhost` en een directe loopbackverbinding. Poort 443 moet vrij zijn. Er worden geen firewallregels, DNS-records, hostsregels of certificaatvertrouwensinstellingen automatisch gewijzigd. HTTP, certificaatwaarschuwingen negeren en browserflags zijn geen alternatief.

Gebruik `Start-Demo.cmd` als interactieve Windows-gebruiker om bij de standaardrol `Both` portal en LummaApp samen te starten. De rollen `Portal` en `App` starten alleen het eigen onderdeel. `Start-PasskeyPortal.cmd` is specifiek voor de portal. Start bij een portalservice de desktopapp apart vanuit de gebruikerssessie via `Start-LummaSimulator.cmd`. De launcher hergebruikt de geregistreerde processen en meldt of het App-venster gereed is. Bij een bezette poort of ontbrekend App-venster geeft de launcher een herstelactie.

De App gebruikt dezelfde ingestelde `PortalOrigin` als de launcher. Bij standaard lokale installatie is dit loopback (`https://localhost`, in de ontwikkelstarter `https://localhost:7243`). De App maakt een uitgaande HTTPS-verbinding naar de portal. `App-IP` in Demonetwerk is een LAN-luisteradres in het beheerplan voor de optionele, gescheiden App-receiver. Een lokale installatie heeft daarvoor geen luisteradres of firewallregel nodig. Een expliciete remote portal of receiver behoudt de bestaande origin-, listener-, certificaat- en pairinginstellingen.

Laat een bevoegde operator een geldig servercertificaat met SAN `localhost`, private key en unieke CN installeren in **Cert:\CurrentUser\My** van het Windows-account dat de demo draait. De keten moet vertrouwd zijn. Voor een uitsluitend lokale, tijdelijke test kan de operator bewust onderstaande stappen in Windows PowerShell uitvoeren. Dit maakt één lokale, niet-exporteerbare sleutel en vertrouwt uitsluitend het bijbehorende publieke certificaat voor de huidige gebruiker. Controleer deze vertrouwenswijziging vooraf volgens het eigen beleid. De meegeleverde Setup voert deze stappen niet uit.

```powershell
$demoCert = New-SelfSignedCertificate -Subject ('CN=LummaEvent-' + [guid]::NewGuid().ToString('N')) -DnsName localhost -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddMonths(3) -Type SSLServerAuthentication
$demoPublicFile = Join-Path $env:TEMP ('LummaEvent-' + $demoCert.Thumbprint + '.cer')
Export-Certificate -Cert $demoCert -FilePath $demoPublicFile
Import-Certificate -FilePath $demoPublicFile -CertStoreLocation Cert:\CurrentUser\Root
$demoCert.Thumbprint
```

Het `.cer` bevat alleen het publieke certificaat. Exporteer of deel geen private key/PFX. Bewaar de thumbprint voor Setup en later gecontroleerd opruimen; de starters verwijderen geen certificaten. Bedrijfsbeleid kan zelfondertekende certificaten verbieden. Gebruik dan een door de organisatie uitgegeven localhost-testcertificaat met unieke CN en correcte SAN/EKU. Zonder vertrouwde TLS-keten stopt Setup.

De starters gebruiken Kestrels bestaande certificate-storeconfiguratie, geen eigen TLS-verifier. Zij controleren geldigheid, private key, DNS-naam, ketenvertrouwen en unieke subjectnaam. De private key blijft in het Windows-certificaatarchief. Er zitten geen certificaten of private keys in de distributie.

## Eenmalige setup en eerste administrator

1. Draai `Setup-Demo.cmd` onder het Windows-account dat voortaan de demo gebruikt. Vul de certificaatthumbprint in. Setup maakt `event-edition.local.json` met installatie-ID en thumbprint; geen wachtwoord of private key. Een bestaande configuratie wordt niet overschreven.
2. Setup toont de opslagmap: `%LOCALAPPDATA%\LummaSimulator\EventEdition\<installatie-ID>`. Database, Data Protection-keyring, logs, exports, procesadministratie en eventueel Event Edition-manifest blijven hier op deze machine. Setup opent geen database; de portal initialiseert die bij de eerste start met de bestaande migraties.
3. Draai `Start-PasskeyPortal.cmd`. De browser opent `/Presenter` en bij een nieuwe installatie automatisch `/Presenter/Setup`.
4. Gebruik op `/Presenter/Setup` de expliciete lokale eerste-administratorregistratie. Deze vereist een werkelijk nieuwe installatie, loopback, HTTPS, Origin en antiforgery; er is geen open presenterregistratie. Vul een fictieve login en weergavenaam in en bevestig de handeling.
5. Registreer de passkey in dezelfde browserflow. Deel de tijdelijke enrollmentcode niet via URL, screenshot of log. Volg de user-verificationinstructies van de browser. Een code verloopt na tien minuten.
6. Meld je na registratie afzonderlijk aan via de presenterlogin. Registratie is geen aanmelding. Rond vervolgens `/Presenter/Onboarding` af met organisatie, hostingbevestiging, eerste evenement en verse herbevestiging voor afronding.
7. Registreer zo mogelijk een tweede authenticator via Mijn account. `Bootstrap-Administrator.cmd` blijft uitsluitend beschikbaar als bestaande expliciete onderhoudsprocedure met OS-beheerrechten onder dezelfde Windows-gebruiker; niet als normale installatiestap of accountreset gebruiken.

Een eerder geactiveerd account houdt bootstrap dicht, ook na uitschakeling of rolwijziging. Een nieuwe bootstrapuitgifte vervangt eerdere nog niet afgeronde codes/grants. Dit is geen accountreset of herstelroute. Gewone uitnodigingen blijven via `/Presenter/Enroll` werken. Registreer zo mogelijk een reservesleutel via het bestaande beveiligingsmenu met verse herauthenticatie.

## Starten en stoppen

| Bestand | Werking |
|---|---|
| `Setup-Demo.cmd` | Vereisten/certificaat controleren, installatieconfiguratie en opslag voorbereiden |
| `Start-Demo.cmd` | Portal gereed laten komen, browser openen en WPF starten |
| `Start-PasskeyPortal.cmd` | Alleen portal starten en dashboard openen |
| `Start-LummaSimulator.cmd` | Alleen WPF starten |
| `Stop-Demo.cmd` | Alleen door deze installatie geregistreerde processen stoppen |
| `Bootstrap-Administrator.cmd` | Expliciete bestaande lokale beheerprocedure uitvoeren |

De starters gebruiken absolute paden vanuit hun eigen map. Ze voeren geen `dotnet run`, package-installatie of SDK-detectie uit. Er wordt geen bestaande portal op basis van procesnaam overgenomen. Een bezette poort geeft een fout. Start dezelfde installatie niet onder meerdere Windows-gebruikers tegelijk.

Stop controleert proces-ID, starttijd en executablepad. WPF krijgt eerst een normaal sluitverzoek; bij weigering moet je het venster zelf sluiten. De portal wordt uitsluitend via zijn gecontroleerde eigen proceshandle beëindigd. SQLite-transacties beschermen tegen gedeeltelijke transacties, maar stop bij voorkeur buiten een registratie/beheerhandeling. De door WPF beheerde LocalTestServer stopt via de bestaande stdin-/parentlifecycle. Stop wist geen runtimebestanden; oude procesrecords verlenen bij PID-hergebruik geen stoprecht.

## Session Exposure Monitor en Event Edition

De Session Exposure Monitor zit in een WPF-tab; er bestaat geen apart monitorprogramma. De portalkoppeling blijft standaard uit. Het starten van beide apps geeft de monitor geen toegang: daarvoor blijven een apart monitorgeheim en expliciete server-side grant nodig. De fase-6.4-broncode voegt administratorbeheer en expliciete handmatige invoer in WPF toe; zie [beheerprocedure en verificatiestatus](phase6-4-monitor-integration.md). Een bestaand gepubliceerd pakket krijgt deze functie pas na een geslaagde nieuwe build/publicatie. Presenteer een ontbrekende verbinding nooit als gemeten sessiestatus of MDR-detectie.

De bestaande Event Edition-tab en portalpagina behouden hun eigen expliciete runkoppeling/leescode; de launcher maakt geen evenementen of grants aan. De LocalTestServer wordt uitsluitend op verzoek door WPF gestart voor synthetische lokale HTTP-telemetrie.

## Bewaren en verplaatsen

Verwijder of wijzig `event-edition.local.json` niet als hersteltruc. Een nieuwe installatie-ID verwijst naar andere opslag en migreert bestaande accounts niet. Kopieer geen ingerichte pakketmap naar een andere laptop: download daar opnieuw de schone ZIP en voer Setup uit. Een kopie van gebruikersdata, database of keyring is geen ondersteunde overdracht. Behoud database, keyring en oorspronkelijke Windows-DPAPI-context samen; backups/herstel vereisen een aparte gecontroleerde procedure.

## Mobiele toegang en YubiKey-beperkingen

`localhost` op een telefoon is de telefoon zelf. Dit pakket bindt aan loopback en ondersteunt daarmee geen externe mobiele bezoeker. Mobiele toegang vereist een afzonderlijk ontworpen configuratie met beheerde DNS-naam, geldig vertrouwd HTTPS-certificaat, bijpassende RP-ID/origin en expliciete netwerkautorisatie. Verander een bestaande RP-ID niet: reeds geregistreerde credentials worden daardoor niet automatisch bruikbaar op het nieuwe domein.

Een fysieke YubiKey kan lokaal worden gekozen waar de browser dat ondersteunt. De bestaande `attestation: none` bewijst geen hardwaretype en dwingt geen YubiKey af. Ook een door het OS aangeboden andere passkey kan worden geaccepteerd. Fysieke YubiKey-, mobiele/hybrid- en productie-TLS-acceptatie zijn niet als uitgevoerd geclaimd.

## Probleemoplossing

- **Setup faalt op TLS:** controleer hetzelfde Windows-account, thumbprint, SAN localhost, geldigheidsduur, serverauthenticatie-EKU, ketenvertrouwen en unieke CN. Start niet door met een browserwaarschuwing.
- **Poort bezet:** sluit alleen de bekende andere toepassing volgens lokaal beleid, of stop deze demo via zijn eigen Stop. De starter beëindigt geen onbekende processen.
- **Bootstrap geweigerd:** controleer bestaande OS-beheerrechten en of al ooit een account is geactiveerd. Gebruik geen lege andere database als herstelmethode.
- **Sleutel ontbreekt na herstart:** controleer Windows-account, ongewijzigde installatie-ID, oorspronkelijke opslag en RP-ID. Verwijder geen database of keys om opnieuw te beginnen.
- **Scripts geblokkeerd:** vraag vrijgave volgens lokaal beleid; de starters gebruiken geen ExecutionPolicy Bypass.
- **Portal sluit direct:** controleer certificaattoegang en schrijfbaarheid van de getoonde lokale opslag. Er wordt bewust geen bootstrapuitvoer naar een log omgeleid. Laat de beheerder de bestaande configuratie controleren zonder geheimen te delen.

## Bouw- en VM-acceptatie

De ontwikkelaar gebruikt `packaging/Build-EventEdition.ps1` met .NET 10 SDK, Git, Node en de bestaande browsertestvereisten. Het script kopieert actuele bronbestanden naar een nieuwe geïsoleerde werkmap, voert locked restore, solutionbuild, regressieorchestrator, twee self-contained win-x64-publicaties en de bestaande gepubliceerde sidecartest uit. De sidecar wordt door het bestaande App-publishtarget meegenomen. Bij fouten wordt geen ZIP gemaakt. De output gaat naar `dist/LummaSimulator-EventEdition-v0.1-win-x64.zip`; `SHA256SUMS.txt` bevat bestandshashes, geen digitale ondertekening.

Op een schone Windows 11-VM zonder SDK/runtime blijven minimaal te controleren: volledig uitpakken op lokale schijf met spaties; expliciete certificaatprovisioning en Windows-/browservertrouwen; setup en verse database; fysieke sleutelregistratie en afzonderlijke login; herstart met behoud van dezelfde administrator en keys; sluiten van WPF/sidecar/portal zonder andere processen te raken; WPF offline; monitor zonder grant gesloten; tweede nieuwe laptop zonder dataoverdracht. Leg browser-/OS-/authenticatorversie en resultaat vast. Een succesvolle build vervangt deze acceptatie niet.

Technische basis: [self-contained publiceren](https://learn.microsoft.com/en-us/dotnet/core/deploying/) en [Kestrel-certificaatconfiguratie](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).
## Lokale DNS bij een nieuwe Cybertruck-installatie

Voor het eigen demonetwerk kan een nieuwe installatie expliciet met een vaste lokale DNS-origin worden ingericht:

```powershell
.\Demo.ps1 -Action Setup -PortalOrigin 'https://<lokale-dns-naam>'
```

Vul de echte DNS-naam in. Richt DNS/DHCP op het demonetwerk zo in dat die naam naar de demo-laptop verwijst. Het certificaat moet die DNS-naam bevatten en door laptop én deelnemerstelefoons worden vertrouwd. De launcher controleert momenteel de primaire DNS-naam van het certificaat en gebruikt HTTPS 443. De nieuwe instelling stemt deelnemers-origin, RP-ID en AllowedHosts op elkaar af; een vooraf vast laptop-IP is niet vereist. Dit commando is niet op de actieve installatie uitgevoerd.

Oudere instellingen zonder PortalOrigin blijven localhost gebruiken. Een afwijkende origin meegeven aan een bestaande installatie wordt geweigerd: bestaande passkeys zijn aan de oorspronkelijke RP-ID gebonden. Deze optie is geen automatische migratie, certificaatprovisioning of firewallinstallatie. De standaard localhost-start blijft uitsluitend voor de laptop. Zie [netwerk en acceptatie](architecture/cybertruck-local-network-acceptance.md) voor de afzonderlijke beheerprocedure en beperkingen.

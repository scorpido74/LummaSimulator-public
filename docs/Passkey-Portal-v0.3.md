# Passkey Demo Portal v0.3

## Architectuur

`LummaSimulator.Web` gebruikt ASP.NET Core 10 Razor Pages en lokale JavaScript-WebAuthn-aanroepen. Het bevat geen WPF-referentie en start geen processen of simulatoractiviteiten. De bestaande WPF-app blijft ongewijzigd. Een willekeurige DemoRunId is voorbereid voor toekomstige correlatie; deze verleent geen toegang en er is nog geen koppeling met de simulator.

| Onderdeel | Verantwoordelijkheid |
| --- | --- |
| `Configuration/WebAuthnSettings.cs` | Strikte startupvalidatie van RP ID en expliciete HTTPS-origins. |
| `Models/DemoModels.cs` | Willekeurige identifiers, bezoekersnamen en minimale modellen. |
| `Services/DemoStore.cs` | SQLite, geparametriseerde queries, foreign keys, cascadeverwijdering en gedeelde concurrency-lock. |
| `Services/DemoService.cs` | Tijdelijke accounts, eigenaarscontrole, eenmalige challenges, FIDO2-libraryverificatie en opschoning. |
| `Services/PresenterService.cs` | Afzonderlijke presentatorsessies, bezoekerscookie en periodieke opschoning. |
| `Pages` | Welkomstpagina, begeleide demo, privacy, afscheid en geautoriseerd presentatordashboard. |
| `wwwroot/css`, `wwwroot/js` | Eigen responsive opmaak en browserinteractie; geen CDN of externe frontendbibliotheken. |
| `Program.cs` | DI, authenticatie, CSRF, rate limiting, securityheaders en HTTPS/API-routes. |

De FIDO2-service en DemoService zijn scoped; de SQLite-store met semaphore is singleton. Verificatie, challengeconsumptie en accountmutaties worden binnen dezelfde lock uitgevoerd. Deze versie is bedoeld voor een enkele applicatie-instance, maximaal 200 actieve demonstraties. SQLite biedt geen echte asynchrone disk-I/O; de servicecontracten en browser-/serverflows gebruiken wel async/await en CancellationToken.

## Dependencies en installatie

Vereist: .NET 10 SDK; voor uitvoering zonder SDK de ASP.NET Core 10-runtime. Voor WebAuthn is een moderne browser met een geschikte platformauthenticator of FIDO2-sleutel nodig.

Goedgekeurde directe packages: `Fido2.AspNet` 4.3.1 en `Microsoft.Data.Sqlite` 10.0.14. De eerste brengt de bestaande `Fido2`-verificatie-implementatie mee. Cryptografie van WebAuthn wordt niet door deze applicatie geimplementeerd. De volledige resolved packageboom, inclusief native SQLite/libsodium-componenten, staat in het lockbestand. Tests voegen geen directe NuGet-packages toe.

```powershell
dotnet restore LummaSimulator.slnx --locked-mode
dotnet build LummaSimulator.slnx --no-restore
dotnet dev-certs https --trust
dotnet run --project LummaSimulator.Web --launch-profile https
```

`--trust` kan een lokale vertrouwensbevestiging vereisen. De applicatie zelf vereist geen administratorrechten. De server draait op **https://localhost:7243**, uitsluitend HTTPS. Er wordt geen HTTP-listener of browserwaarschuwing-omzeiling toegevoegd. De ontwikkelingsterminal toont gestructureerde JSON-logs; het dashboard wordt niet automatisch geopend.

## Eerste passkeyregistratie en authenticatie

1. Open `https://localhost:7243` met een vertrouwd localhost-certificaat.
2. Kies telefoon, demolaptop, YubiKey of de afzonderlijke iPad-route. Er wordt automatisch een willekeurige Visitor-gebruikersnaam gemaakt; vul geen persoonlijke gegevens in.
3. Kies **Passkey registreren**. Volg de browserdialoog en bevestig met apparaat-PIN/biometrie of een geschikte FIDO2-sleutel en PIN. Registratie vraagt om een discoverable credential en gebruikersverificatie.
4. Kies **Inloggen met mijn passkey**. Pas na geldige serververificatie verschijnt een succesmelding. Dit is een echte WebAuthn-assertion, geen gesimuleerde knopstatus.
5. Kies **Demonstratie beëindigen**. De bedankpagina verschijnt alleen na bevestigde verwijdering van de servergegevens. De opgeslagen passkey op het apparaat moet de gebruiker eventueel zelf verwijderen.

De apparaatroute geeft instructies en advisory browserhints; die keuze bewijst niet welk apparaat of welk merk authenticator is gebruikt. Registratie van meerdere passkeys is mogelijk tot maximaal vijf per account. De app toont geen onbevestigde claims over synchronisatie, hardwarebinding of attestatie van het merk.

Windows Hello moet op de laptop geconfigureerd zijn. YubiKey vereist FIDO2/passkey-ondersteuning, voldoende credentialruimte en zo nodig een ingestelde FIDO2-PIN. Bij ondersteunde cross-device-combinaties biedt de browser een telefoon-/QR-flow aan; de app maakt zelf geen QR-overdracht van sessiecookies of authenticatietokens.

Een iPad of telefoon kan niet rechtstreeks de localhost-origin van een andere computer bezoeken. Voor een demonstratie in de browser van dat apparaat is de publieke origin met vertrouwd certificaat nodig. Developmentcredentials onder `localhost` zijn niet bruikbaar onder `passkey.example.invalid`: daar is een nieuwe tijdelijke demo en registratie nodig.

## Presentatorauthenticatie

Er is geen standaardwachtwoord en geen publiek registratie-/beheeraccount. Zonder `Presenter:PasswordHash` blijft de login gesloten. Genereer interactief een PBKDF2-hash met de ingebouwde ASP.NET Core PasswordHasher:

```powershell
dotnet run --project LummaSimulator.Web -- --hash-presenter-password
```

Het wachtwoord van 16-256 tekens wordt zonder echo gelezen; alleen de hash wordt getoond. Configureer die hash via user-secrets, een omgevingsvariabele of de secretvoorziening van de host. Sla geen echt wachtwoord of hash op in Git. Voor een lokale terminal:

```powershell
$env:Presenter__PasswordHash = Read-Host 'Plak de hash'
dotnet run --project LummaSimulator.Web --launch-profile https
```

Open `/Presenter` en gebruik het oorspronkelijke wachtwoord. Het dashboard is beveiligd door een afzonderlijk cookieschema, presentatorrol en server-side sessiecontrole. Sessies vervallen absoluut na 30 minuten, zonder sliding renewal; opnieuw inloggen maakt een nieuwe sessie. Uitloggen trekt de serverregistratie in. Een configuratiewijziging van de hash maakt bestaande presentatortickets ongeldig zodra de configuratie beschikbaar is voor het proces (een gewijzigde omgevingsvariabele vereist herstart).

Het dashboard toont actieve gebruikers, credentialaantallen, geslaagde/mislukte serverauthenticaties en verstreken tijd. Vernieuwen haalt actuele waarden op en verlengt bezoekerssessies niet. Client-side annuleringen zonder verificatieresponse tellen niet als mislukte serververificatie. Tellingen horen bij actieve accounts en verdwijnen bij verwijdering. Een presentator kan geselecteerde demonstraties beeindigen; een bezoeker uitsluitend de eigen demonstratie.

## WebAuthn en sessiebeveiliging

- Interne account-ID, sessiegeheim en user handle zijn afzonderlijk cryptografisch willekeurig. De gebruikersnaam is een willekeurig label; DemoRunId is een aparte GUID. Geen hiervan wordt als openbaar toegangstoken gebruikt.
- De server bewaart alleen een hash van het bezoekerssessiegeheim. De cookie is Secure, HttpOnly, SameSite=Strict, host-only en een browser-sessioncookie. Cookies met de `__Host-`-prefix hebben geen Domain-attribuut en gelden vanaf `/`.
- Elke options-aanvraag maakt een nieuwe challenge die de vorige voor die demo vervangt. Challenges zijn gekoppeld aan account, sessie en operatietype, verlopen na twee minuten en worden atomair verwijderd voor verificatie. Ook een afgewezen response kan niet opnieuw worden gebruikt. Een syntactisch ongeldige HTTP/JSON-aanvraag kan al voor de verificatiehandler worden geweigerd.
- De library verifieert challenge, origin, RP-ID-hash, gebruikersverificatie, handtekening en protocolstructuur. De applicatie controleert daarnaast credentialeigenaarschap, unieke credential-ID en de permanente backup-eligibilityflag. Een geldig account van een andere bezoeker kan geen credential overnemen.
- User verification is `required`, resident key is `required` en attestation is `none`. Alleen anonieme `none`-attestation wordt geaccepteerd. Daarmee zijn geen certificaat-, fabrikant- of metadata-opvragen nodig. De server configureert geen externe MDS-service en ontvangt nooit de private sleutel.
- Mutaties gebruiken antiforgery. WebAuthn-API's eisen ook een exact toegestane HTTP Origin. Er is geen CORS-toegang voor andere sites en geen iframe-inbedding. Requestbodies zijn begrensd op 64 KB.
- Rate limiting is procesbreed: 300 demo-aanvragen per minuut en acht loginpagina-/login-aanvragen per minuut. De limieten gebruiken geen opgeslagen IP-adressen; bij een drukke demo delen bezoekers deze capaciteit. Dit is geen vervanging voor infrastructuurbeveiliging bij een publieke productieomgeving.

## Database, privacy en verwijdering

Standaarddatabase: `%LOCALAPPDATA%\LummaSimulator\PasskeyPortal\portal.db` op Windows. Op andere platforms gebruikt de app de lokale applicatiedatamap. Configureer desgewenst een absoluut pad buiten `wwwroot`:

```powershell
$env:Database__Path = 'C:\DemoData\PasskeyPortal\portal.db'
```

Schema-initialisatie gebeurt automatisch; er zijn geen EF-migraties nodig. Verwijdering van een demo is een atomair SQLite-statement met foreign-key-cascades voor credentials en challenges. De bezoekerssessiehash zit in dezelfde accountregel en verdwijnt mee. `secure_delete=ON` en de DELETE-journalmodus beperken achterblijvende database-inhoud; de app maakt geen backups. Dit is geen garantie voor fysieke verwijdering uit snapshots, bestandssysteemhistorie of externe backups.

Data Protection-sleutels staan in de submap `keys` naast de database. Windows beschermt deze met DPAPI voor het uitvoerende account. Op Unix krijgen nieuw gemaakte datamappen mode 0700 en de database mode 0600; beheer bestaande maprechten en eventuele encryption-at-rest via de host. Verleen de hostgebruiker schrijfrechten op de eigen datamap, geen administratorrechten. Database, sleutels, PFX-bestanden en lokale geheimen worden door Git genegeerd. Maak geen externe backups van de tijdelijke demoaccounts zonder afzonderlijk retentiebeleid.

Inactieve accounts verlopen na 29 minuten. Een cleanup-worker draait elke 30 seconden met marge tot 30 minuten, en requestvalidatie weigert verlopen accounts ook direct. Een druk, stilgelegd of gestopt proces kan geen harde wall-clock-verwijderingsgarantie bieden; verlopen gegevens worden voor de volgende startup verwijderd en falende opruiming geeft een generieke operationele foutmelding. Dashboardrequests houden bezoekers niet actief. Afzonderlijke verlopen challenges en presentatorsessies worden eveneens opgeruimd.

Na beeindiging blijven geen accountgebonden applicatielogs achter: de applicatie logt geen gebruikersnamen, DemoRunIds, user handles, credential-ID's, IP-adressen, user-agents, requestbodies of authenticatietokens. Operationele JSON-logs bevatten uitsluitend generieke events en geaggregeerde opruimaantallen. De pagina's worden met `Cache-Control: no-store` geserveerd. Noodzakelijke cookies en publieke credentialgegevens kunnen onder privacywetgeving vallen; er wordt niet beweerd dat nergens persoonsgegevens worden verwerkt. Beoordeel reverse proxy-, Cloudflare-, browser- en hostlogs afzonderlijk.

## RP ID, origins en publieke hosting

Developmentprofiel:

```json
{"WebAuthn":{"RpId":"localhost","Origins":["https://localhost:7243"]}}
```

Production gebruikt standaard:

```json
{"WebAuthn":{"RpId":"passkey.example.invalid","Origins":["https://passkey.example.invalid"]}}
```

De validator staat bewust alleen de afgesproken RP ID per omgeving toe. Een ander domein vereist een expliciete code-/configuratiebeleidswijziging, geen willekeurige runtime-wildcard. Development kan meerdere expliciete HTTPS-poorten hebben. Origins moeten canonical zijn zonder trailing slash, wildcard, credentials, query, fragment of pad; productie gebruikt poort 443. De app controleert ook de requesthost/scheme tegen deze origins. Developmentwaarden zijn geen productiefallback.

Voor toekomstige publieke hosting: publiceer het webproject naar een ASP.NET Core 10-host met een eigen beschrijfbare datamap. Gebruik een instance en een vaste `passkey.example.invalid`-host. Zet `ASPNETCORE_ENVIRONMENT=Production`, configureer het presentatorgeheim en gebruik vertrouwd HTTPS.

Cloudflare kan later als DNS/reverse proxy of Tunnel worden ingericht. Deze implementatie maakt geen tunnel, DNS-record of externe netwerkverbinding aan. Bij een lokaal tunnelproces kan de app bijvoorbeeld op `http://127.0.0.1:5000` luisteren, mits de proxy de originele Host behoudt en `X-Forwarded-Proto: https` doorgeeft. Configureer alleen de werkelijk vertrouwde proxy-IP's:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5000'
$env:ReverseProxy__KnownProxies__0 = '127.0.0.1'
dotnet run --project LummaSimulator.Web --no-launch-profile
```

Deze laatste configuratie werkt pas met de passende proxy; directe HTTP-aanvragen worden geweigerd. De code vertrouwt forwarded headers uitsluitend wanneer proxy-IP's expliciet zijn geconfigureerd en alleen van die adressen. Stel bij Cloudflare geen caching in voor accounts, authenticatie of dashboard, beoordeel logretentie en voorkom dat onbevoegden de originhost direct kunnen beheren. Het ASP.NET Core-proces en SQLite worden niet als een statische Cloudflare Pages-site gehost. Publieke uitrol is niet uitgevoerd of geverifieerd.

## Verificatie en beperkingen

```powershell
dotnet restore LummaSimulator.slnx --locked-mode
dotnet build LummaSimulator.slnx --no-restore
dotnet run --project tests/LummaSimulator.Web.Tests
dotnet run --project tests/LummaSimulator.SmokeTests
dotnet run --project verification/Verification.csproj
```

De webtests gebruiken de echte Fido2-library met een test-only software-authenticator en ECDSA-testkeys. Ze verifiëren geldige registratie/assertion, ongeldige origin/RP-ID-hash/challenge/handtekening/UV/user handle, backupflag, eenmalige challenges, gelijktijdige replay, sessie-isolatie, verval en verwijdering. HTTPS-integratietests starten een eigen Kestrel-proces en een tijdelijke SQLite-database, gebruiken een uitsluitend voor die test gegenereerd certificaat met thumbprintpinning, en controleren CSRF, dashboardautorisatie, login/logout, eigenaarschap en privacy van logs. Er worden geen certificaatvertrouwensinstellingen aangepast. Testdatabasebestanden kunnen in `%TEMP%\LummaPortalTests` achterblijven; ze bevatten uitsluitend testidentiteiten.

De WPF-verificatie opent kort het bestaande dashboard. De simulator-smoke-tests starten echte whoami-processen en lokale loopbackrequests zoals beschreven in de bestaande WPF-documentatie.

Niet bevestigd door deze tests: visuele rendering in een echte browser, fysieke Windows Hello/YubiKey-tests, NFC, iOS/Android/iPad, passkeymanagersynchronisatie en cross-device-QR. Deze moeten met de gebruikte browser/apparaatcombinaties worden gecontroleerd. De portal bevat geen ClickFix-delivery, sessieonderschepping, exfiltratie of EDR-koppeling. Er wordt geen EDR-detectie geclaimd.

## Bestandenoverzicht van deze uitbreiding

Gewijzigd: `LummaSimulator.slnx`, `.gitignore`, `README.md` en het historische `docs/Passkey-Portal-v0.3-plan.md`. Toegevoegd: deze gebruiksdocumentatie en de onderstaande bestanden. Er zijn geen bronbestanden van `LummaSimulator.App`, de bestaande simulatortests, de WPF-verificatie of de lokale testserver gewijzigd.

```text
LummaSimulator.Web/
  LummaSimulator.Web.csproj
  Program.cs
  appsettings.json
  appsettings.Development.json
  packages.lock.json
  Configuration/WebAuthnSettings.cs
  Models/DemoModels.cs
  Services/DemoStore.cs
  Services/DemoService.cs
  Services/PresenterService.cs
  Properties/launchSettings.json
  Pages/_ViewImports.cshtml
  Pages/_ViewStart.cshtml
  Pages/Shared/_Layout.cshtml
  Pages/Index.cshtml                  + Index.cshtml.cs
  Pages/Demo.cshtml                   + Demo.cshtml.cs
  Pages/Complete.cshtml               + Complete.cshtml.cs
  Pages/Privacy.cshtml                + Privacy.cshtml.cs
  Pages/Error.cshtml                  + Error.cshtml.cs
  Pages/Presenter/Login.cshtml        + Login.cshtml.cs
  Pages/Presenter/Index.cshtml        + Index.cshtml.cs
  wwwroot/css/site.css
  wwwroot/js/passkeys.js
  wwwroot/js/dashboard.js
tests/LummaSimulator.Web.Tests/
  LummaSimulator.Web.Tests.csproj
  Program.cs
  SoftwareAuthenticator.cs
  HttpChecks.cs
  packages.lock.json
```

De tijdelijke scaffoldbestanden voor Bootstrap/jQuery, templatevalidatie, favicon en ongebruikte templateopmaak zijn verwijderd uit het nieuwe webproject; zij worden niet gebruikt of meegeleverd als frontenddependency.

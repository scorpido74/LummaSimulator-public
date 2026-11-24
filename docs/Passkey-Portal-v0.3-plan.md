# Passkey Demo Portal v0.3 — implementatieplan en packagevoorstel

Status: historisch ontwerp, inmiddels goedgekeurd en uitgevoerd. De gebruiker heeft Fido2.AspNet 4.3.1, Microsoft.Data.Sqlite 10.0.14 en de noodzakelijke transitieve dependencies goedgekeurd. Zie [de implementatie- en gebruiksdocumentatie](Passkey-Portal-v0.3.md) voor de actuele werking, verificatie en beperkingen. De onderstaande tekst beschrijft het oorspronkelijke plan en packageonderzoek.

## Inspectie

- `LummaSimulator.slnx` bevat uitsluitend `LummaSimulator.App`, een WPF-project op `net10.0-windows`, versie 0.2.0, zonder externe PackageReferences.
- De app scheidt modellen, MVVM-infrastructuur, ViewModel en simulatie-/telemetrieservices. De webportal krijgt geen projectreferentie naar deze Windows-specifieke code.
- Buiten de solution staan dependencyvrije smoke-tests, een WPF-verificatieproject en de lokale HTTP-testserver. Deze blijven behouden.
- Geinstalleerd: .NET SDK 10.0.403 en ASP.NET Core-runtime 10.0.14. Er is nog geen webproject, SQLite-database of presentatorauthenticatie.

## Kort implementatieplan

1. Voeg na packagegoedkeuring `LummaSimulator.Web` toe als zelfstandig ASP.NET Core Razor Pages-project op `net10.0`. Gebruik ingebouwde DI, logging en configuratievalidatie; geen WPF-referentie. Voeg het project toe aan de solution.
2. Bouw lokaal geserveerde, responsive Razor-pagina's met lichte kaarten, groene accenten, tekstuele Infinigate/Yubico-co-branding en de expliciete melding dat dit een Infinigate-demonstratieomgeving is. Geen externe fonts, scripts, analytics of onbevestigde logo-assets. Neem de aangeleverde welkomst-, privacy- en afscheidsteksten over. Geef telefoon, demolaptop, YubiKey en iPad afzonderlijke instructieroutes.
3. Implementeer SQLite-opslag en tijdelijke accounts met cryptografisch willekeurige interne account-ID, een afzonderlijke willekeurige WebAuthn-user-handle en een willekeurige gebruikersnaam `Visitor-...`. Voeg een afzonderlijke DemoRunId toe voor een toekomstige koppeling; dit is geen autorisatiemiddel. Geen persoonsgegevensvelden.
4. Implementeer echte registratie en authenticatie met de goedgekeurde FIDO2-library en browser-WebAuthn. Koppel eenmalige challenges aan de juiste bezoekerssessie, het account en de operatie. Voeg verval, CSRF-bescherming, concurrentiebeveiliging en servervalidatie toe.
5. Bouw een afzonderlijk geauthenticeerd presentatordashboard met actieve demo's, tijdelijke gebruikersnamen, credentialaantallen, succes-/faaltellingen, tijdsduur en een geautoriseerde beeindigingsactie. Voeg eigenaarsgebonden beeindiging door bezoekers en automatische verwijdering toe.
6. Voeg dependencyvrije controles toe voor generatie, configuratie, verval, challengehergebruik, accountisolatie, opschoning en dashboardautorisatie. Voer restore/build uit en test WebAuthn met echte browser/authenticatorcombinaties. Leg de start- en testprocedure vast zonder handmatige hardwaretests als geslaagd te presenteren voordat ze zijn uitgevoerd.

## Packages waarvoor toestemming nodig is

| Direct package | Vast te zetten versie | Reden en compatibiliteit |
| --- | --- | --- |
| `Fido2.AspNet` | `4.3.1` | ASP.NET Core-integratie van de bestaande FIDO2/WebAuthn-library. De gepubliceerde dependencies bevatten een `net10.0`-groep. Brengt `Fido2` en `Fido2.Models` vanaf 4.3.1 transitief mee. |
| `Microsoft.Data.Sqlite` | `10.0.14` | Lichte Microsoft ADO.NET-provider voor SQLite, zonder een volledige EF Core-laag. Richt zich op .NET Standard 2.0, ondersteund door .NET 10. |

De bronrepository van de FIDO2-library is `passwordless-lib/fido2-net-lib`; NuGet publiceert stabiele 4.3.1 en een afzonderlijke 5.0-prereleasereeks. Het voorstel gebruikt de stabiele versie. Compatibiliteit is op package/frameworkmetadata gecontroleerd; daadwerkelijke restore-, compileer- en browserverificatie van deze packages volgt pas na toestemming.

Toestemming omvat ook hun transitieve dependencies. Gepubliceerde dependencies noemen onder meer `Fido2`, `Fido2.Models`, `Microsoft.Extensions.Http`, `Microsoft.IdentityModel.JsonWebTokens`, `NSec.Cryptography`, `System.Formats.Cbor`, `Microsoft.Data.Sqlite.Core` en SQLitePCLRaw-componenten, inclusief native cryptografie-/SQLite-runtimecomponenten. Dit is geen claim dat de volledige resolved dependencyboom al is vastgesteld. Na goedkeuring wordt die boom met een lockbestand vastgelegd en gecontroleerd; er worden geen andere directe packages stilzwijgend toegevoegd.

Geen aparte frontendpackages, CDN, testframeworkpackages of externe authenticatiediensten zijn nodig voor het voorgestelde ontwerp. ASP.NET Core-cookieauthenticatie, antiforgery en wachtwoordhashing voor de presentator komen uit het geinstalleerde shared framework.

## Authenticatie, opslag en privacy

- Bezoekers krijgen een willekeurige, ondoorzichtige Secure/HttpOnly-cookie met een afzonderlijk sessiegeheim; alleen een hash daarvan wordt server-side bewaard. Een gebruikersnaam, URL-parameter of DemoRunId verleent nooit toegang tot een demoaccount. Accounts en credentials kunnen uitsluitend via de eigen sessie of een geautoriseerde presentator worden gewijzigd/verwijderd.
- Bewaar alleen demo-identificatoren, noodzakelijke credentialgegevens (credential-ID, publieke sleutel, user handle, relevante tellers/flags), sessieverval, tijdelijke challenges en dashboardtellers. Geen bezoekerswachtwoorden, privésleutels of volledige browserfingerprints. Browserrouting is begeleiding, geen bewijs van het gebruikte authenticator- of opslagtype.
- Kies gebruikersverificatie `required`, attestation `none` en een expliciet beleid voor resident credentials. Laat challenge-, origin-, RP-ID-hash-, handtekening- en credentialvalidatie door de library uitvoeren. Controleer account/credential-eigenaarschap daarnaast in de applicatielaag. Gebruik geen automatische externe metadata- of certificatendownloads.
- Challenges verlopen na twee minuten, zijn per operatie/sessie gekoppeld en worden atomair verbruikt, ook bij een mislukte validatie. Gelijktijdige responses mogen niet allebei slagen. Registratie en authenticatie accepteren uitsluitend credentials voor de gekoppelde gebruiker.
- Presentatorauthenticatie krijgt een afzonderlijk cookieschema en server-side sessiecontrole. Een presentatorgeheim wordt buiten de repository ingesteld; zonder configuratie blijft toegang gesloten. Bescherm login tegen herhaalde pogingen. Alle dashboardpagina's en mutaties vereisen de presentatorrol en CSRF-bescherming.
- Plan een idle-verval van 29 minuten met een opruimronde elke 30 seconden, zodat er operationele marge is voor de grens van 30 minuten. Verlopen toegang wordt ook bij requests geweigerd. Achtergrondpolling van het dashboard verlengt een bezoekerssessie niet. Bij opstart worden verlopen gegevens verwijderd voordat de portal verkeer accepteert. Een gestopt proces kan geen achtergrondopschoning uitvoeren; dit wordt een expliciete hostingbeperking.
- Beeindigen verwijdert account, credentialregistraties, sessies, challenges en herleidbare tijdelijke demogegevens in een transactie. De presentatie van een bedankpagina volgt pas na bevestigde verwijdering. De server kan geen passkey uit een apparaat of passkeymanager verwijderen.
- Structurele operationele logs bevatten geen gebruikersnamen, volledige IP-adressen, user-agents, credentialdata, requestbodies of authenticatietokens. Eventuele herleidbare demo-events blijven uitsluitend in de tijdelijke database en worden mee verwijderd. Reverse proxy-, Cloudflare-, hostinglogs en backups vereisen een eigen privacy-/retentiecontrole; geen absolute claim dat nergens persoonsgegevens worden verwerkt.

## Configuratie en toekomstige hosting

- Development: RP ID `localhost`, origin uitsluitend `https://localhost:7243` (of een expliciet geconfigureerde andere HTTPS-poort). Valideer configuratie bij startup: geen wildcard, credentials in URI, query, fragment of ongewenst pad; originhost moet passen bij de toegestane RP ID. Weiger HTTP en onbedoelde hosts.
- Production: RP ID `passkey.example.invalid`, origin `https://passkey.example.invalid`. Developmentwaarden mogen niet stilzwijgend als productiefallback dienen.
- SQLite staat buiten `wwwroot`, op een schrijfbare lokale locatie. Gebruik geparametriseerde queries, foreign keys, transacties en een expliciete tijdelijke-dataretentie. Databasebestanden en lokale geheimen horen niet in Git of webresponses.
- Toekomstige Cloudflare-hosting betekent een ASP.NET Core-host achter een passende reverse proxy of Tunnel; er wordt nu geen deployment, tunnel of DNS-wijziging uitgevoerd. Vertrouw forwarded headers alleen van expliciet bekende proxies. Configureer HTTPS, de vaste publieke origin, uitsluiting van caching voor auth/demo/dashboardpagina's en privacybeleid voor infrastructuurlogs. SQLite vereist voor deze eerste versie een enkele applicatie-instance met lokale persistente opslag.
- Een telefoon/iPad kan de localhost-origin van een andere computer niet rechtstreeks gebruiken als zijn eigen localhost. Voor afzonderlijke apparaten is een bereikbare, vertrouwde HTTPS-origin nodig. Cross-device-authenticatie gebruikt waar beschikbaar de browser-/authenticatorflow, geen zelfgemaakte overdracht van accountcookies of sessies.

## Beoogde start en eerste registratie — nog niet uitvoerbaar

Na implementatie: vertrouw een ontwikkelcertificaat, configureer RP/origin en een afzonderlijk presentatorgeheim, voer `dotnet restore` en `dotnet build` uit en start `dotnet run --project LummaSimulator.Web --launch-profile https` op `https://localhost:7243`.

Eerste test: open de portal in een WebAuthn-browser, kies demolaptop of YubiKey, controleer de automatisch toegewezen Visitor-naam, registreer met Windows Hello of een FIDO2-sleutel en voer daarna een afzonderlijke authenticatie uit. Controleer vervolgens beeindiging, ingetrokken sessie en verwijderde serverregistratie. Test telefoon/iPad afzonderlijk op een geschikte HTTPS-origin. Hardware-, browser- en synchronisatieondersteuning worden alleen beschreven voor daadwerkelijk geverifieerde combinaties.

## Bronnen — gecontroleerd op 29 oktober 2026

- [Fido2.AspNet 4.3.1: frameworks en dependencies](https://www.nuget.org/packages/Fido2.AspNet/4.3.1)
- [Fido2 4.3.1: frameworks en dependencies](https://www.nuget.org/packages/Fido2/4.3.1)
- [FIDO2-librarybronrepository](https://github.com/passwordless-lib/fido2-net-lib)
- [Microsoft.Data.Sqlite 10.0.14](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14)
- [.NET Standard-ondersteuning, inclusief .NET 10](https://learn.microsoft.com/en-us/dotnet/standard/net-standard)
- [Microsoft: passkeys in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0)
- [Cloudflare Tunnel-documentatie](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/)

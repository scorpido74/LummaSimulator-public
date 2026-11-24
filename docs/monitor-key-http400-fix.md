# Monitorsleutelgeneratie: HTTP 400 bij Origin null

Datum: 5 november 2026. Bronanalyse en gerichte correctie; geen actieve portal onderzocht of gestart.

## Oorzaak en afwijzingspad

`Pages/Presenter/Integrations/Monitor.cshtml.cs`, methode `NoStore`, overschreef het algemene responsebeleid met `Referrer-Policy: no-referrer`, op zowel GET als POST. De beheeractie is een gewone Razor-formuliernavigatie. `presenter.js` onderschept uitsluitend `form[data-presenter-action]`, dus wel de herbevestiging maar niet het Change-formulier. `monitor-keys.js` verwijdert alleen de eenmalige sleutelweergave bij navigatie; het verstuurt het formulier niet.

Voor een POST-navigatie kan `no-referrer` de geserialiseerde Origin op `null` zetten, ook bij een same-origin bestemming. `Sec-Fetch-Site` beschrijft een andere eigenschap en kan dus tegelijk `same-origin` zijn. Dit gedrag volgt het [WHATWG Fetch-algoritme voor de Origin-header](https://fetch.spec.whatwg.org/#append-a-request-origin-header). `fetch` gebruikt zonder expliciete mode standaard `cors`; daarbij blijft de echte Origin bij deze POST behouden. Daarom kan de bestaande WebAuthn-herbevestiging via `fetch` wel slagen.

De afwijzingspaden zijn onderscheiden:

1. De algemene middleware in `Program.cs` controleert HTTPS en de origin van de **bestemmings-URL**. Die weigering heeft een tekstbody; zij vergelijkt hier niet de aangeleverde Origin-header.
2. Razor Pages valideert voor deze POST standaard antiforgery voordat de handler wordt uitgevoerd. Een aanwezige token bewijst op zichzelf niet dat token, cookie en identiteit overeenkomen; bij een fout kan ook deze laag een lege 400 geven.
3. Wanneer antiforgery geldig is, weigert `MonitorModel.OnPostChangeAsync` de opgegeven `Origin: null` deterministisch: de exacte vergelijking met `WebAuthnSettings.Origins` faalt en retourneert `BadRequest()` zonder body, vóór `MonitorKeyStore.ChangeAsync` en vóór consumptie van verse herbevestiging. Dit verklaart de waarnemingen en de status Disabled zonder sleutelwijziging.
4. Het endpointfilter van `/api/presenter` valideert Origin en antiforgery voor de herbevestiging. Het hoort niet bij de Razor Change-handler. Succes van die API bewijst geen geslaagde autorisatie van een later formulierverzoek. Herbevestiging vervangt de presenteridentiteit niet; de verse bevestiging wordt server-side vastgelegd voor één beheerwijziging.

Er is geen referrer-meta-element of CSP-sandbox op deze beheerpagina aangetroffen. Het bestaande CSP staat eigen scripts en formulierbestemmingen toe (`form-action 'self'`, `frame-ancestors 'none'`). Geen iframe-, file-URL-, redirect- of afwijkende browsercontext is nodig om dit probleem te verklaren. De eigen `no-referrer`-responseheader is voldoende. Zonder actieve requesttrace wordt niet beweerd dat een mogelijk tweede antiforgery-probleem operationeel is uitgesloten.

## Kleinste correctie

Alleen het lokale beleid in `MonitorModel.NoStore` is gewijzigd naar `Referrer-Policy: same-origin`, inclusief toelichtend commentaar. Daardoor kan de native same-origin POST de vereiste exacte Origin meesturen. Naar een andere origin wordt nog steeds geen Referer gestuurd. Sleutels staan niet in URL's; browsercache blijft uitgeschakeld met `Cache-Control: no-store` en `Pragma: no-cache`.

De exacte Origin-allowlist, afwijzing van ontbrekende/null/andere Origin, antiforgery, expliciete bevestiging, adminpolicy, verse WebAuthn-controle, revisiecontrole, opslag en audit zijn ongewijzigd. Er is geen terugval op Referer of Sec-Fetch-Site toegevoegd. Het algemene middlewarebeleid, formulier, CSP en JavaScript van de applicatie zijn niet aangepast.

## Tests en resultaten

- Nieuwe `tests/LummaSimulator.Web.Tests/MonitorManagementHttpChecks.cs`, aangeroepen vanuit de bestaande geïsoleerde `HttpChecks`: GET/POST-headerbeleid en no-store; synthetische legacy-intrekking en nieuwe sleutelgeneratie na echte software-WebAuthn-herbevestiging; geauthenticeerde HEAD met de uitgegeven sleutel; eenmalige weergave; verbruik van verse proof; intrekking. Negatieve tests sturen een ontbrekende, letterlijke `null` of externe Origin met `Sec-Fetch-Site: same-origin`, een ontbrekende/ongeldige antiforgery-token, ontbrekende bevestiging en een bezoekerssessie. Geweigerde pogingen mogen proof/revisie niet verbruiken. Deze .NET-tests zijn nog niet uitgevoerd.
- Nieuwe `tests/WebAuthn.Ux.Tests/monitor-origin.browser.test.cjs`: echte headless-browserregressie met eigen profiel en synthetische HTTP-loopbackserver, zonder portal/database/certificaten. Vergelijkt originele native POST onder no-referrer, fetch onder no-referrer en native POST onder het gecorrigeerde paginaresponsbeleid. Dit test uitsluitend browserheaders, niet ASP.NET-autorisatie of productie-TLS. De poging eindigde bij de eerste browser met exitcode `2147483651` (`0x80000003`); geen geslaagde browserreproductie of acceptatie geclaimd. Geen alternatieve identiteit, sandboxuitschakeling of browserbeveiligingsflags geprobeerd. De test staat apart van de bestaande Node-unitregressie en kan expliciet met `node --test tests/WebAuthn.Ux.Tests/monitor-origin.browser.test.cjs` worden uitgevoerd.
- Bestaande Node-regressie (passkeys, localization, presenter, ClickFix, branding, monitor-keys): **64 geslaagd, 0 mislukt, 2 PE-icooncontroles overgeslagen**.
- Locked restore opnieuw geprobeerd op alle zeven projecten in `artifacts/monitor400-verify-87443d46e64d4e94ab3e8db68eebe049/source`: **exitcode 1**, toegang tot `C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config` geweigerd. Geen workaround toegepast. Build en .NET-regressie konden daarom niet worden uitgevoerd; geen compilatiesucces geclaimd.

Na een geverifieerde nieuwe build moet de monitorpagina volledig opnieuw worden geladen; een reeds geopende documentcontext behoudt haar oude referrerbeleid. Herbevestig daarna en voer de beheeractie opnieuw uit. De actieve installatie is niet vervangen of herstart. Test de volledige route in een geïsoleerde omgeving met nieuwe fictieve data voordat een operationele release wordt gebruikt.

Gewijzigd voor deze correctie: `LummaSimulator.Web/Pages/Presenter/Integrations/Monitor.cshtml.cs`, `tests/LummaSimulator.Web.Tests/HttpChecks.cs`, nieuwe `tests/LummaSimulator.Web.Tests/MonitorManagementHttpChecks.cs`, nieuwe `tests/WebAuthn.Ux.Tests/monitor-origin.browser.test.cjs`, dit rapport en een verwijzing vanuit het fase-6.4-rapport. Eerdere niet-gecommitteerde wijzigingen zijn behouden. Geen bestaande sleutel, database, certificaat, configuratie of actieve applicatie gewijzigd; geen commit of push.

# Monitorgrant: Toestaan bleef Toestaan

5 november 2026. Bronanalyse en correctie; geen actieve portal/database geopend of gestart.

## Daadwerkelijke oorzaak

De oude monitorhandler in `Pages/Presenter/Index.cshtml.cs` had **geen expliciete request-Origin-validatie**. `RecordDemoActionAsync` controleert de actuele presentatorautorisatie en het bestaan van de gekozen demo, maar controleert geen Origin. Het `/api/presenter`-endpointfilter geldt niet voor dit Razor-formulier. De algemene middleware controleert HTTPS en de bestemmings-origin, niet deze requestheader. De waargenomen 302 was dus niet aantoonbaar een Origin-afwijzing door deze handler.

De primaire fout zat in `Index.cshtml`: `value="@(!Model.MonitorGrants.Contains(demo.Id))"` leverde een boolean op in een conditioneel Razor-attribuut. Bij Toestaan is die boolean true: Razor kan dan de attribuutnaam als waarde schrijven (`value="value"`), precies zoals in DevTools is vastgesteld. Dit is geen tekstuele `true`. De oude parameter `bool enabled` kon `value` niet binden; de bindingfout werd in ModelState opgenomen en de parameter bleef false. Razor Pages stopt een handler niet automatisch wegens deze ModelState-fout. De handler controleerde ModelState niet en riep `SetGrantAsync(id, false)` aan.

`SetGrantAsync` verwijdert bij false een eventuele grant. Zonder bestaande grant verwijdert dat nul rijen, zonder fout. De handler stuurt vervolgens altijd een 302 terug naar het dashboard. `GrantedDemoIdsAsync` vindt terecht geen grant en de knop blijft Toestaan. Bij een bestaande grant kon foutieve invoer deze juist stilzwijgend intrekken.

De bestaande `TempData["MonitorNotice"]` werd alleen gevuld bij een `DemoRequestException`. Een bool-bindingsfout is geen dergelijke exception, en nul verwijderde rijen evenmin. Daarom ontbrak de zichtbare melding; bestaande meldingen stonden bovendien pas onder de uitgebreide dashboardsecties.

Het dashboard erfde daarnaast `Referrer-Policy: no-referrer`. Dit verklaart de combinatie native POST, `Origin: null` en `Sec-Fetch-Site: same-origin`, zoals bij de eerdere [sleutelbeheerfout](monitor-key-http400-fix.md). Daar bestond wél een strikte Origin-controle die een lege 400 gaf. De symptomen lijken verwant, maar het daadwerkelijke grantprobleem was de booleaanse formulierwaarde plus genegeerde bindingfout. Een geverifieerde monitorsleutel bevestigt uitsluitend de aparte sleutelauthenticatie, nooit een grant.

## Correctie

- Het hidden enabled-veld produceert expliciet de strings `true` en `false`.
- De parameter is nu `[FromForm] bool? enabled`. ModelState, aanwezigheid, formulierinhoud en precies één enabled/id worden gecontroleerd vóór audit en wijziging. Missing/ongeldige/dubbele waarden mogen niets veranderen; er is geen terugval naar false.
- De monitorhandler valideert nu expliciet de exacte request-Origin tegen de bestaande `WebAuthnSettings.Origins`. Ontbrekend, letterlijk null, anders of meervoudig blijft geweigerd. Er is geen uitzondering op basis van Sec-Fetch-Site/Referer toegevoegd.
- Alleen het dashboard zet zijn GET-response en monitor-POST-response op `Referrer-Policy: same-origin`, zodat native same-origin formulieren de echte Origin behouden. Geen extern Referer; no-store, CSP, Razor-antiforgery en de bestaande Presenter-policy blijven behouden. De actie blijft Post/Redirect/Get, inclusief weigering, maar fouten krijgen nu een zichtbare, vertaalde melding bovenaan met `role="alert"`.
- Na een wijziging wordt de bestaande grantquery gebruikt om de gewenste toestand te bevestigen. Bij een afwijkende uitkomst of SQLite-fout verschijnt een veilige melding zonder interne details. Een 302 op zichzelf geldt niet als succesbewijs.
- Intrekken blijft beschikbaar als nog een grant bestaat maar de sleutelconfiguratie inmiddels ontbreekt. Een nieuwe toestemming blijft dan uitgeschakeld.

Geen wijziging aan `SessionStatusService` of SQL/schema. Bestaande opslag blijft INSERT/UPDATE op MonitorGrants en DELETE bij intrekking, gekoppeld aan de interne demo-account-ID. De API gebruikt afzonderlijk de portal-DemoRunId. De duur blijft `DemoService.IdleLifetime`, 29 minuten; metadata blijft afhankelijk van geldige sleutel, loopback, grant en actieve geauthenticeerde sessie. Grantreadback en dashboardbezoek verlengen de bezoekerssessie niet.

## Bestanden

- `LummaSimulator.Web/Pages/Presenter/Index.cshtml` en `Index.cshtml.cs`.
- Drie `LummaSimulator.Web/Resources/Localization/UiText*.resx`: Nederlandse/Engelse foutmeldingen.
- Nieuw `tests/LummaSimulator.Web.Tests/MonitorGrantHttpChecks.cs`; aangesloten via `HttpChecks.cs`.
- `tests/LummaSimulator.Web.Tests/SessionStatusChecks.cs`: expliciete opslag/readback-, intrekking- en 29-minutenassertions.
- Dit verslag en een verwijzing vanuit `docs/phase6-4-monitor-integration.md`.

## Tests en bewijsgrenzen

Nieuwe HTTP-tests gebruiken uitsluitend de bestaande geïsoleerde HTTPS-fixture met fictieve passkeygebruiker. Ze lezen de werkelijk gerenderde Razor-formulieren (geen handmatig verzonnen enabled bij Toestaan/Intrekken), controleren true/false en de knoplabels, volgen de redirect en controleren de monitor-API. Negatieve cases: ontbrekende/null/externe Origin ondanks Sec-Fetch-Site same-origin, ongeldige antiforgery, enabled value/lege waarde/ontbrekend/1/dubbel en een ongeldige Origin bij intrekking. Een bestaande grant moet bij die afwijzingen intact blijven. Bezoekersautorisatie blijft ook via de bestaande negatieve HTTP-test gedekt. De database-unitchecks controleren de bestaande grantquery en expiry zonder actieve gegevens aan te spreken.

Werkelijke resultaten:

- Node-regressie passkeys/localization/presenter/ClickFix/branding/monitor-keys: **64 geslaagd, 0 mislukt, 2 bestaande PE-icooncontroles overgeslagen**. Dit is geen uitvoering van de nieuwe C#/Razor-granttests.
- Resource-XML en unieke keys gecontroleerd; diff-whitespacecontrole geslaagd.
- Nieuwe geïsoleerde bronkopie: `artifacts/monitor-grant-verify-9cb7f610b4a84bb08542c055b6198582/source`. Locked restore voor alle zeven projecten faalt met exitcode 1: toegang geweigerd tot `C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config`.
- Daarom **geen bevestigde build of uitvoering van de nieuwe .NET-/Razor-/HTTP-tests**. Geen workaround voor de toegangsblokkade. Geen nieuwe echte browseracceptatie uitgevoerd; de eerder vastgelegde browserstartfout is niet als opgelost beschouwd.

Na een geverifieerde geïsoleerde build: voer de bestaande regressieorchestrator uit met `LUMMA_TEST_WEB_DLL` expliciet naar de eigen testportal-DLL. Controleer daarna native browser-Toestaan → knop Intrekken → metadata toegestaan → Intrekken → metadata geweigerd, met nieuwe fictieve data. Herlaad het dashboard volledig zodat het nieuwe referrerbeleid geldt. De actieve installatie is niet herstart of vervangen.

Bestaande sleutels, databases, certificaten, configuratie en accounts zijn ongemoeid gelaten. Geen migratie, endpoint-/MDR-test, commit of push. Eerdere niet-gecommitteerde wijzigingen blijven behouden.

# Fase 6.4 — beveiligde monitorintegratie

Ontwerp vastgelegd vóór implementatie, 5 november 2026. Bestaande niet-gecommitteerde wijzigingen blijven behouden. Geen actieve installatie wordt gestart of gemigreerd.

Vervolgcorrectie: [HTTP 400 bij native sleutelbeheerformulier](monitor-key-http400-fix.md). De monitorpagina gebruikt `Referrer-Policy: same-origin` zodat een gewone POST de strikte Origin-controle kan doorlopen; no-store en alle autorisatie-/antiforgerycontroles blijven behouden. Zie het vervolgverslag voor de nieuwe tests en uitvoeringsbeperkingen.

Vervolgcorrectie dashboard: [Toestaan/Intrekken en monitorgrant](monitor-grant-dashboard-fix.md). Expliciete true/false-formulierwaarden, gecontroleerde binding, exacte Origin-validatie, gericht same-origin-referrerbeleid en zichtbare foutmeldingen. De oorspronkelijke 302 was geen bewijs van een geslaagde grantwijziging. Build-/testbeperkingen staan in het vervolgverslag.

## Bron van waarheid en compatibiliteit

Geen SQLite-schemawijziging. Een Data Protection-beschermd `monitor-integration.state` naast de werkelijk geconfigureerde database bewaart alleen versie/revisie, huidige SHA-256-hash, sleutel-ID, aanmaak-/rotatiedatum, Active/Revoked-status en een gestructureerd auditrecord zonder sleutelwaarden. Het gebruikt de bestaande keyring en Windows-/DPAPI-context. Een blijvende managed-marker en eventuele vorige status voorkomen automatische terugval bij een ontbrekend/beschadigd statusbestand. Geen automatische herstelprocedure die een oude sleutel opnieuw activeert.

Zonder beheerd bestand/marker blijft de bestaande Monitor:KeyHash-configuratie werken. Overname is expliciet en admin-geautoriseerd. In managed-modus wordt configuratie genegeerd, inclusief na intrekking. De metadata uit legacyconfiguratie bewijst geen oorspronkelijke aanmaakdatum; overname verzint die niet. Generatie/rotatie/intrekking vereisen bestaande adminautorisatie, verse WebAuthn-herauthenticatie, exacte Origin en antiforgery. Slechts één actieve sleutel; geen overlap of stille fallback. Opslagfouten falen gesloten.

Bestand en SQLite kunnen niet gezamenlijk committen. Daarom bevat de atomaire statuswijziging een niet-geheim pending-auditrecord. Bestaande PresenterAudit ontvangt dat record idempotent (actie + unieke operationele referentie). Tot auditbevestiging verleent de nieuwe status geen toegang. Een crash kan tijdelijk onbeschikbaarheid veroorzaken; hij mag geen ongeauditeerde toegang of herstel van de oude sleutel veroorzaken. Een verloren eenmalige uitgifte vraagt expliciete nieuwe rotatie, geen uitleesroute.

## Transport en WPF

Geen browser→WPF-listener en geen browserlogin die lokale apprechten geeft. Een bevoegde administrator krijgt de nieuw gegenereerde sleutel eenmaal in de POST-response; geen TempData, opslag, URL of automatisch klembord. No-store en verwijderen van de uitgifteweergave bij navigatie beperken browserretentie. Daarna handmatige overdracht naar een PasswordBox in WPF.

WPF bewaart de credential met Windows DPAPI CurrentUser in `%LOCALAPPDATA%\LummaSimulator\MonitorCredentials\<SHA256-van-canonieke-data-root>`, met uitsluitend de huidige gebruikers-SID op de nieuw aangemaakte map. Bestaande runtime-/database-/keyring-ACL's worden niet gewijzigd. Paden moeten lokaal zijn en mogen geen reparsepunten bevatten. Bestaande afwijkende credentialmaprechten worden niet stilzwijgend herschreven. Verwijderen betreft uitsluitend het eigen credentialbestand na expliciete bediening.

Een expliciete LUMMA_MONITOR_KEY-override heeft voorrang; een ongeldige override valt niet terug op een opgeslagen sleutel. De UI maakt deze modus zichtbaar. Geheimen worden niet getoond na opslaan, niet gelogd en niet in procesargumenten gezet. Plaintext bestaat noodzakelijk kortstondig in browser-/procesgeheugen bij uitgifte, invoer en HTTP-authenticatie. Applicatiegestuurde rapporten blijven geredigeerd; absolute uitsluiting uit door OS/beheer afgedwongen volledige geheugendumps kan niet worden gegarandeerd zonder aanvullende, afzonderlijk goedgekeurde Windowsmaatregelen.

## Beperkte contractuitbreiding

De bestaande `/api/local/session-status/{demoRunId}` krijgt HEAD als authentieke verbindingstest. Dezelfde HTTPS-, loopback- en sleutelcontrole blijft gelden. HEAD geeft uitsluitend een vaste protocolbevestiging, nooit een bezoekersstatus, en raakt geen demo-opslag. GET behoudt run-/grant-/sessiecontrole. Dit maakt koppelen vóór bezoekersregistratie mogelijk zonder een tweede monitor-API of parallel authenticatiesysteem.

Voor gegevens gebruikt WPF de portal-DemoRunId, nooit automatisch de WPF-run-ID. Event Edition-pairing blijft onafhankelijk. Intrekking geldt voor nieuwe autorisatiebeslissingen; reeds geautoriseerde HTTP-verzoeken kunnen nog afronden. Een verbindingsstatus is een momentopname, geen permanente garantie.

## Launchers en risico's

Builders krijgen geen monitorcredentials. Portal krijgt hoogstens de legacyhash; WPF hoogstens een expliciete legacy-raw-key. Normaal starten gebruikt de persistente beheerde status en DPAPI-credential. Een browseractie voert geen endpointtest uit. Geen nieuwe netwerklistener. Netjes stoppen moet de bestaande WPF-shutdown en portal-host-stop gebruiken; geen kill op procesnaam en geen geforceerde datareset.

Belangrijkste risico's: verkeerde bron/installatie/Windowscontext, herstel van verouderde sleutelstatus, ongeauditeerde file/DB-tussenstand, lek via browsercache/childomgeving/dumps, verkeerde portalrun en misleidende verbindingsstatus. Tests en opleverstatus worden hieronder aangevuld na implementatie. Geen claim van runtimeacceptatie vóór werkelijk uitgevoerde checks.

## Bestaande situatie en hergebruik

Voor deze uitbreiding werden de configuratieproviders van ASP.NET Core gebruikt voor `Monitor:KeyHash` (appsettings, development-user-secrets en omgevingsvariabele `Monitor__KeyHash`); WPF las `LUMMA_MONITOR_KEY`. Er was geen beheerpagina voor deze sleutel en geen lokale versleutelde opslag. De bestaande generator is een ontwikkelhulpmiddel, geen beheerinterface. De nieuwe pagina blijft binnen de bestaande PresenterService-adminpolicy, account-/sessiecontrole, verse WebAuthn-bevestiging en PresenterAudit. Geen tweede gebruikers- of authenticatiestelsel.

`SessionStatusService`, `SessionMetadataClient`, bestaande MonitorGrants en de server-side portal-DemoRunId blijven het datacontract bepalen. De grantduur blijft `DemoService.IdleLifetime` (29 minuten). GET geeft voortaan ook bij een grant geen metadata zonder actieve geauthenticeerde demosessie (404). De eerdere negatieve DTO-statussen blijven leesbaar in WPF voor compatibiliteit met oudere portals. Bestaande accountretentie-/verwijdercode is niet veranderd. De identity-auditexporter, Event Edition-code/pairing, Prize, SQLite-schema, RP-ID, origins en TLS-validatie zijn ongewijzigd.

## Beheerprocedure na een geverifieerde build

1. Start vanuit de projectroot met `Start-LummaDev.bat`, of gebruik de bestaande Event Edition-starter van het nieuw gebouwde pakket. Gebruik de bestaande Windows-gebruiker, opslag en certificaten.
2. Meld je met een bestaande administratorpasskey aan op `/Presenter`. Kies **Beveiligde integraties → Session Exposure Monitor** (`/Presenter/Integrations/Monitor`). Gewone presentatoren en bezoekers hebben geen toegang.
3. Voer de expliciete WebAuthn-herauthenticatie op de beheerpagina uit. Bevestig de beheerhandeling en kies **Genereren**. Bij een bestaande legacyhash: kies expliciete overname of rotatie. Een nieuwe sleutel vervangt de vorige zonder overgangsperiode.
4. De nieuwe sleutel verschijnt alleen in deze uitgifte. Geen automatische overdracht of browseropslag. Breng hem eenmalig over naar het afgeschermde invoerveld in WPF; laat geen kopieën in notities, opdrachten of configuratie achter. Bij handmatig kopiëren via het systeemklembord: houd rekening met eigen klembordgeschiedenis/synchronisatie; de applicatie schrijft of wist het systeemklembord niet.
5. Kies in WPF **Instellingen → Monitorintegratie**, controleer het exacte bestaande HTTPS-portaladres, voer de sleutel in, bevestig en kies **Opslaan**. Er is geen browserlogin die dit invoerveld automatisch vult of lokale toegang geeft.
6. Kies **Koppeling testen**. Alleen de geauthenticeerde HEAD-bevestiging van de portal geeft **Verbonden**. Deze controle bevestigt geen demo-grant, passkey of MDR-detectie. Laatste succesvolle verbinding is een runtime-momentopname en wordt bij lokale sleutelvervanging/verwijdering gewist.
7. Doorloop met een fictieve bezoeker registratie, afzonderlijke authenticatie en ClickFix. Selecteer de juiste gekoppelde portalrun in de presentatie. De onafhankelijke Event Edition-koppeling verleent geen monitorrechten.
8. Geef in de presentatorportal de expliciete monitorgrant voor die demo. Gebruik in Session Exposure Monitor de **portal-DemoRunId**; een WPF-run-ID is hiervoor geen vervanging. Vernieuw vóór iedere gesimuleerde exposure/replay. Er wordt uitsluitend toegestane servermetadata opgehaald.
9. Trek de grant in: volgende metadatarequests geven 404. Trek de sleutel in na verse adminherauthenticatie: volgende sleuteltests en metadatarequests worden geweigerd. WPF toont bij de volgende controle dat de sleutel is geweigerd of ingetrokken. Er is geen achtergrondpush die onmiddellijk een geopend scherm bijwerkt.
10. Voor rotatie: herauthenticeer, roteer, voer de nieuwe eenmalige sleutel in WPF in en test opnieuw. Geen overlap: de oude sleutel stopt direct bij nieuwe autorisatie. **Koppeling verwijderen** verwijdert alleen de lokale versleutelde credential; serverintrekking is een aparte beheerhandeling.
11. Stop ontwikkeling met `Stop-LummaDev.bat` (of `Start-LummaDev.bat stop`). Deze sluit alleen geregistreerde processen met overeenkomend PID, starttijd en uitvoerpad. WPF sluit normaal inclusief zijn eigen sidecar. Het lokale stopbestand vraagt normale ASP.NET-hostafsluiting. Bij geweigerde/uitblijvende afsluiting volgt een melding, geen geforceerde kill. Voor Event Edition blijft `Stop-Demo.cmd` de ingang.

### Voorrang en herstel

- Server: zodra managed-status/marker bestaat, is die de enige autoriteit. Alleen vóór overname wordt de bestaande `Monitor:KeyHash` geraadpleegd. Een configuratiewijziging achteraf kan een ingetrokken managed-sleutel niet reactiveren.
- WPF: een niet-lege expliciete `LUMMA_MONITOR_KEY` heeft voorrang op DPAPI. Het adres komt dan uit de bestaande `DesktopSettings.PortalOrigin`/`LUMMA_PORTAL_ORIGIN`. Ook een ongeldige override blokkeert terugval; de invoerbediening is uitgeschakeld. Zonder override geldt de opgeslagen credential inclusief zijn exacte origin. Veranderde adressen moeten expliciet worden opgeslagen; er is geen verruiming naar LAN/andere hosts.
- De private credentialmap is gescheiden per canonieke runtime-data-root en ligt buiten build-/publishmappen. Andere Windowsgebruikers/installaties koppelen afzonderlijk. Een ontbrekend bestand betekent ongeconfigureerd; corrupte bestanden/afwijkende ACL's worden geweigerd, niet automatisch gerepareerd.
- Bij pending audit: bezoek de adminpagina opnieuw. Zij probeert uitsluitend het opgeslagen, niet-geheime auditrecord idempotent af te leveren. Bij verloren eenmalige response: na succesvolle auditreconciliatie expliciet roteren. Bij beschadigde managed-status: gesloten houden en herstel apart beoordelen. Verwijder geen marker, oude status of keyring om legacy terugval af te dwingen.
- Het `.previous`-bestand is geen automatisch herstelpad en bevat alleen beschermde hashstatus. Een consistente back-up/herstelprocedure moet altijd ook managed-status, marker, database en oorspronkelijke Data Protection-context beoordelen; deze wijziging voert geen back-up of herstel uit.
- Bestaande gestarte portalprocessen zonder stopregistratie worden niet overgenomen. Sluit hun eigen console normaal. Nieuwe stopbestanden bevatten alleen een vast stopsignaal; procesregistraties bevatten geen credential. Controlebestanden blijven behouden, zonder automatische opschoning.

## Gewijzigde bestanden voor fase 6.4

Eerdere niet-gecommitteerde fase-6.3-, branding- en crashfixwijzigingen staan daarnaast in de werkmap. Onderstaande lijst beschrijft uitsluitend deze uitbreiding.

- Portal: `Program.cs`, `Services/MonitorKeyStore.cs` (nieuw), `Services/PresenterMonitorKeys.cs` (nieuw), `Services/SessionStatusService.cs`, `Services/LauncherStopService.cs` (nieuw), `Pages/Presenter/Integrations/Monitor.cshtml` en `.cshtml.cs` (nieuw), `Pages/Presenter/Index.cshtml`, `wwwroot/js/monitor-keys.js` (nieuw), drie `Resources/Localization/UiText*.resx`, `LummaSimulator.Web.csproj`.
- WPF: `MonitorCredentialStore.cs` en `MonitorIntegrationSettings.cs` (nieuw), `SessionMetadataClient.cs`, `SessionMonitorViewModel.cs`, `MainWindow.xaml` en `.xaml.cs`, `DesktopSettings.cs`, beide `DesktopText*.resx`, `LummaSimulator.App.csproj`, `LocalTestServerManager.cs` (geen monitorgeheimen doorgeven aan de sidecar); gedeeld: `shared/UserPrivateFiles.cs` (nieuw).
- Start/publicatie: `Start-LummaDev.bat`, `Stop-LummaDev.bat` (nieuw), `packaging/Start-LummaDev.ps1`, `packaging/EventEdition/Demo.ps1`, `packaging/EventEdition/LauncherControl.ps1` (nieuw), `packaging/Build-EventEdition.ps1`, `.gitignore`. `codex-start.bat` is ongemoeid gelaten.
- Tests: Web `MonitorKeyChecks.cs` (nieuw), `Program.cs`, `PresenterChecks.cs`, `SessionStatusChecks.cs`, `HttpChecks.cs`; Smoke `MonitorIntegrationChecks.cs` (nieuw), `Program.cs`, `.csproj`; Node `monitor-keys.test.cjs` (nieuw); regressie-`.csproj`; `verification/Program.cs`, `Test-MonitorLaunchers.ps1` (nieuw), `Test-EventEditionPackaging.ps1`.
- Documentatie: dit rapport, `README.md`, `docs/development-start.md`, `docs/event-edition-installatie.md`.

## Werkelijke verificatie op 5 november 2026

| Controle | Uitkomst |
|---|---|
| Node passkeys/localization/presenter/ClickFix/branding/monitor-keys | 64 geslaagd, 0 mislukt, 2 PE-icooncontroles overgeslagen zonder build/publishoutput |
| `Test-MonitorLaunchers.ps1` | 14 geslaagde synthetische controles, geen appstart |
| `Test-LummaDevLauncher.ps1` | 10 geslaagde synthetische preflightcontroles |
| `Test-EventEditionPackaging.ps1` | 76 pakketcontroles geslaagd; aansluitend procescompatibiliteit 33 geslaagd (eigen synthetisch proefproces, geen portal/WPF) |
| PowerShell-parser, XAML als XML, unieke resourcekeys, `git diff --check` | Geslaagd; Git meldt alleen bestaande LF/CRLF-conversiewaarschuwingen |
| Geïsoleerde locked restore van alle zeven projecten | Geblokkeerd: toegang geweigerd tot `C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config`, exitcode 1 |
| .NET-build, Web/Smoke/WPF-regressie, nieuwe DPAPI-/beheer-/HTTP-tests | Niet uitgevoerd wegens restoreblokkade; geen compilatiesucces geclaimd |
| Publish en `Test-LocalHttpPublished.ps1` | Niet uitgevoerd wegens dezelfde buildblokkade |
| Volledige acceptatieroute, fysieke passkey, visuele WPF, echte tweede start/stop/herstart | Nog handmatig/operationeel te testen na geverifieerde geïsoleerde build |

Geïsoleerde bronkopie van de restorepoging: `artifacts/phase6-4-verify-6afb10436c094173a0a1d9dca1a44bd7/source`. Latere bron-/testcorrecties staan in de werkmap; de mislukte restorekopie is geen actueel bouwbewijs. Geen workaround voor de geweigerde NuGet-configuratie uitgevoerd.

Nieuwe .NET-tests dekken hash-only opslag, malformed/wrong key, generatie/rotatie/intrekking, nieuwe store-instantie met dezelfde bescherming, legacyovername, managed-fallbackblokkade, stale revisions, pending audit, anonieme/verse adminautorisatie, antiforgery/Origin, geldige sessie/grant versus ingetrokken sleutel, geauthenticeerde HEAD en afwijzing van generieke HTTP-success. Smoke-tests bevatten DPAPI-reload/ACL, ongeldige vervanging, herhaald testen, environmentvoorrang, verwijdering en onbereikbare portal met synthetische transporten. Deze assertions zijn geschreven maar nog niet uitgevoerd.

Volg voor vervolgverificatie de bestaande regressieorchestrator in een nieuwe geïsoleerde bronkopie, met `LUMMA_TEST_WEB_DLL` expliciet naar haar eigen gebouwde portal-DLL. Daarna App inclusief LocalTestServer publiceren en de bestaande gepubliceerde sidecartest met expliciete geïsoleerde `PublishDirectory` uitvoeren. Gebruik uitsluitend nieuwe fictieve data. De gebruikersroute met 15 stappen uit de opdracht blijft de operationele acceptatiecheck; daarvoor zijn nog geen geslaagde resultaten beschikbaar.

Er zijn geen actieve apps gestart/gestopt, databases geopend/gemigreerd, bestaande gebruikers/credentials/certificaten/keyrings gewijzigd, endpoint-/MDR-tests uitgevoerd, commits gemaakt of pushes gedaan. De broncode is nog geen geverifieerde release. Resterende risico's zijn de onuitgevoerde .NET-/Windowsacceptatie, onvermijdelijke tijdelijke plaintext bij handmatige overdracht en volledige OS-dumps, en operationeel herstel van file/audit-tussenstanden. Geen netwerkverruiming of Windowsmaatregel toegevoegd om die beperkingen te maskeren.

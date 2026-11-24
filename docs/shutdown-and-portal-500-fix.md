# WPF-afsluitcrash en afzonderlijke portal-500

Datum: 4 november 2026. Geen commit/push, deployment, actieve processtop of gegevens-/certificaat-/authenticatieconfiguratiewijziging.

## WPF: oorzaak

Het oude `async void OnClosing` annuleerde het eerste Closing-event, startte shutdown en riep daarna direct `Close()` aan. `await` garandeert geen uitstel: wanneer de awaited Task al voltooid is, loopt dezelfde stack direct verder. Idle shutdown kan volledig synchroon afronden: geen actieve run, geen monitorwerk en geen eigen sidecar. WPF is dan nog bezig met het oorspronkelijke Closing-event. De geneste Close wordt geweigerd met de gemelde InvalidOperationException; een exception uit async void kan als ongedekte dispatcherexception in .NET Runtime-event 1026 eindigen.

Herhaalde verzoeken tijdens een werkelijk asynchrone shutdown werden wel geannuleerd, maar de oude taakguard beschermde niet tegen dit synchronous-completion-pad. Bovendien was MainViewModel.ShutdownAsync zelf niet idempotent: een tweede externe/re-entrante aanroep kon resources opnieuw disposen. Een fout bij annuleren, wachten of disposen sloeg resterende cleanup over. De Window-handler had geen catch en verwijderde niet alle abonnementsrelaties.

## WPF: herstel

- `OnClosing` is een override die de volledige eventafhandeling omvat. Eerste en herhaalde verzoeken worden geannuleerd totdat de eenmaal aangevraagde shutdown geslaagd is.
- Een onvoorwaardelijke dispatcher-hop vindt plaats vóór shutdown, ook bij een reeds voltooide Task.
- De laatste Close wordt afzonderlijk ingepland. De closing-depthguard voorkomt Close terwijl andere Closing-subscribers nog actief zijn, inclusief subscribers met een geneste DispatcherFrame. De finally van de override plant het resterende verzoek pas opnieuw in nadat alle handlers zijn teruggekeerd.
- MainViewModel publiceert één completion-task onder een lock **vóór** command-, cancellation- of disposecallbacks. Ook herintreding uit een Dispose-callback krijgt exact dezelfde Task.
- Annulering en taakvoltooiing worden afzonderlijk afgehandeld. De daadwerkelijke runtask wordt afgewacht, inclusief het bestaande laatste logcheckpoint; ook na een cancellation-/notificationexception. Monitorwerk wordt eveneens afgewacht wanneer een cancellationcallback faalt.
- De overige cleanupstappen worden ook na fouten geprobeerd. Fouten worden verzameld en aan de Window-handler doorgegeven, niet als async-void-exception losgelaten. Nieuwe uitvoering, cleanup en reset worden tijdens shutdown geblokkeerd.
- Bij onbevestigde cleanup blijft het venster uitgeschakeld/open met een Nederlandse/Engelse foutmelding. Er volgt geen automatische retry van dezelfde disposals en geen geforceerde exit. Dit is een bewuste fail-safe: een mislukte cleanup is geen bewijs dat alle achtergrondtaken gestopt zijn. Het venster kan dan niet als normale demo worden hervat; de mislukte cleanup vraagt afzonderlijk onderzoek.
- `OnClosed` verwijdert model-, log-, password-, Loaded- en SizeChanged-handlers. De modelshutdown verwijdert zijn monitor-/sidecarabonnementen.

De bestaande sidecarmanager en zijn exclusieve eigendom via behouden proceshandle zijn ongewijzigd. Geen procesnaamlookup/nieuwe killroute, verwijderactie, databasehandeling of accountreset toegevoegd. De bestaande beperkte sidecar-stopprocedure blijft gelden. Een niet-coöperatieve taak kan de veilige afsluiting laten wachten; er is geen nieuwe timeout die data of onbekende processen geforceerd afbreekt.

## Portal-500: afzonderlijk onderzoek

Beide genoemde pagina's gebruiken `_Layout.cshtml`. De vorige brandingcode gaf `IWebHostEnvironment.WebRootPath` zonder nullcontrole door aan `Path.Combine`. Een fysiek webrootpad kan ontbreken bij development/buildoutput; statische assets kunnen ondertussen via de static-web-assets-fileprovider beschikbaar zijn. Met de standaard Infinigate-configuratie leidt een nullpad in die layout aantoonbaar tot ArgumentNullException. Een custom logo had dezelfde foutgrens.

`ResellerBranding.LogoUrl` verdraagt nu null/lege paden en valt terug op organisatietekst. Het standaardlogo wordt waar beschikbaar via de bestaande WebRootFileProvider gevonden, zonder nieuwe netwerk- of opslagtoegang. De layout geeft deze provider door. Geen wijziging van listeners, origins, RP-ID, sessies of credentials.

**Bewijsgrens:** dit is een concrete bronfout die het gemeenschappelijke 500-symptoom kan verklaren. De exception/stacktrace van de werkelijk draaiende portal is nog niet aangeleverd en de actieve routes zijn niet opgevraagd. Daarom is niet geclaimd dat de gemelde live 500 hiermee al operationeel is opgelost; een andere fout in PageModel/opslag kan eveneens een 500 geven.

Het bestaande middlewarelog onderdrukte alle exceptiondetails, waardoor de oorspronkelijke console mogelijk alleen `Portal request failed` bevat. Het log vermeldt nu aanvullend uitsluitend de exceptiontypenaam, bijvoorbeeld ArgumentNullException of SqliteException. Geen exceptionbericht, stacktrace, routeparameters, cookies of configuratiewaarden. De generieke vertaalde browserfout blijft behouden. Deze wijziging betreft het gewone requestlog, niet de fase-6.2-identity-exporter.

## Tests en werkelijke resultaten

- **Geslaagd:** onafhankelijke WPF-frameworkreproductie via `verification/Test-WpfClosingRepro.ps1` onder Windows PowerShell/STA. Een completed-task-awaiter is synchroon voltooid en WPF weigert een geneste Close binnen Closing. Geen vensters getoond, Lumma-app geladen, endpoints aangeroepen of gegevens gewijzigd. Dit bevestigt het frameworkmechanisme; het is geen test van de nieuw gebouwde .NET 10-app.
- **Geslaagd:** bestaande vijf Node UX-bestanden: 61 geslaagd, 0 mislukt, 2 bestaande exe-/publishicoonchecks overgeslagen.
- **Geslaagd:** XAML-/resource-XML, resourcesleutel-uniciteit en `git diff --check`.
- **Toegevoegd, niet uitgevoerd:** Smoke ShutdownChecks voor taakidentiteit, re-entrante disposecallbacks, eenmaal disposen, cancellationfouten, doorlopende cleanup, een synthetische actieve fase plus laatste geheugenlogcheckpoint en late commandcallbacks. Geen echte endpointactiviteit.
- **Toegevoegd, niet uitgevoerd:** WindowShutdownChecks in WPF Verification voor directe/asynchrone completion, herhaalde sluitverzoeken, geneste DispatcherFrame, handlerafkoppeling, sync/async shutdownexceptions en de echte idle MainViewModel-route.
- **Toegevoegd, niet uitgevoerd:** BrandingChecks voor null/lege/whitespace-WebRootPath en provider-backed standaardlogo. De bestaande HTTP-suite blijft de gate voor daadwerkelijk renderen van publieke en presentatorpagina's met uitsluitend synthetische testopslag.
- **Geblokkeerd:** locked restore van alle zeven projecten in `artifacts/shutdown-fix-e85dd5cb31c841458df13f7bb9b3758f/source`: toegang geweigerd tot de bestaande gebruikers-NuGet.Config. Geen configuratie-/identiteitsomweg. Daardoor zijn compilatie, de nieuwe .NET-tests, volledige regressie en HTTP-rendering hier niet bevestigd. `restore.log` en `node.log` staan één niveau hoger. De bronkopie is na de laatste wijzigingen bijgewerkt; de restoreblokkade ging vooraf aan compilatie.

Na een toegestane isolated build zijn uitsluitend de nieuwe tests apart te draaien met de SmokeTests-DLL `--shutdown-only` en Verification-DLL `--shutdown-only`. De volledige regressie blijft `dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj`, met expliciet LUMMA_TEST_WEB_DLL naar de eigen testbuild. Gebruik geen actieve portalinstallatie als testtarget. De volledige suite bevat bestaande synthetische sidecar-/telemetriechecks; deze sessie heeft geen echte endpoint-/MDR-tests uitgevoerd.

Open acceptatie: nieuw gebouwde WPF herhaald sluiten bij idle/SAFE/gekoppeld wachten; de bestaande sidecaracceptatie in een eigen fixture; renderen van `/` en `/Presenter/Login` met nieuwe synthetische opslag; exceptiontype van de gemelde live portal-500. Een bestaande portal wordt niet automatisch gestart, herstart of gemigreerd.

## Bestanden van deze reparatie

WPF: MainWindow.xaml.cs, MainWindow.xaml (foutmelding), MainViewModel.cs, SessionMonitorViewModel.cs, DesktopText.resx/en-GB.resx en AssemblyInfo.cs (interne testtoegang voor Verification).

Portal: Services/ResellerBranding.cs, Pages/Shared/_Layout.cshtml en Program.cs (alleen veilige exceptiontype-diagnostiek).

Tests: nieuw SmokeTests/ShutdownChecks.cs, verification/WindowShutdownChecks.cs en Test-WpfClosingRepro.ps1; SmokeTests/Program.cs, verification/Program.cs en Web.Tests/BrandingChecks.cs bijgewerkt. De gerichte tests zitten in de bestaande runners; geen nieuw testframework.

Documentatie: dit verslag en README. Eerdere niet-gecommitteerde launcher-, fase-6.3- en brandingwijzigingen blijven behouden; `codex-start.bat` is ongemoeid.

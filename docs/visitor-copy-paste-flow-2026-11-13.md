# Bezoekersflow: kopiëren en plakken binnen de pagina

13 november 2026. Implementatiecheckpoint; geen browser- of releaseacceptatie. Werkboom bevestigd met `git rev-parse --show-toplevel`. Bestaand lokaal werk, startdiagnostiek van de browserfixture en historische migraties zijn behouden. Geen actieve portal, bestaande database, browserprofiel of installatie gebruikt.

## Gedrag

De startknop toont Bekijk je prijskans → bij een eigen evenement met ingeschakelde groepsloting, anders Bekijk je demoresultaat →. Dit hangt af van de opgeslagen evenementcontext, ook als de ronde vol is, en claimt geen daadwerkelijke inschrijving. De bestaande echte deelname-/uitslaglink en de oranje uitslagkaart zijn ongewijzigd. De geverifieerde authenticatierespons bevat aanvullend `prizesEnabled`, afkomstig van een beperkte server-side read voor het eigen aangemelde account en de bestaande evenementconfiguratie. Daarmee wordt de nog niet herladen pagina na aanmelden meteen bijgewerkt; dit is geen autorisatiebeslissing in de browser. Herladen en de compatibele SignedIn-pagina gebruiken dezelfde read.

De eerste kaart toont Prijscontrole mislukt of Resultaat laden mislukt, de gevraagde urgente tekst, Kopiëren en Win + R. Een subtiel demolabel blijft zichtbaar. Het kruisje heeft een vertaalde toegankelijke naam; Escape en de kruisjes sluiten alleen deze oefening. Er is geen echte Win + R-toetsregistratie.

Kopiëren bewaart de vaste onschuldige voorbeeldregel uitsluitend in een lokale JavaScript-variabele. Win + R opent de illustratie met een leeg alleen-lezen invoerveld. Plakken is alleen beschikbaar als in deze oefenpoging gekopieerd is; daarna verschijnt de regel en wordt OK actief. Zonder kopiëren blijft het veld leeg en OK uitgeschakeld. Er is geen systeemklembord, browseropslag, download, processtart of arbitraire opdrachtinvoer toegevoegd.

Uitvoeren heeft een smalle titelbalk met icoon, titel, subtiel demolabel en kruisje; Openen staat naast het veld, Plakken bij het veld en OK/Annuleren compact rechtsonder. De inhoud is licht. De grote kop, uitlegregel en tekstknop Oefening sluiten zijn verwijderd. Het in de opdracht genoemde screenshot was niet zichtbaar in deze sessie; de indeling volgt de uitgeschreven referentie, zonder geclaimde pixelvergelijking.

OK opent de bestaande nabespreking. Titel, uitleg en primaire terugactie van dat laatste scherm zijn behouden. De oude automatische 10+22-secondenpresentatie is vervangen door de expliciete knoppen. De server vereist nog steeds eigen geldige authenticatie, Origin/CSRF en een bestaande startregistratie; de oude minimale 32-secondenleestijd is vervallen om de gevraagde directe OK-actie mogelijk te maken. Een toekomstige StartedAt wordt nog steeds geweigerd. De test controleert weigering zonder start en succesvolle voltooiing na expliciet starten, zonder timer. Dit is uitsluitend browseroefenevidence, geen bewijs van malware-uitvoering of MDR-detectie.

Sluiten, Annuleren, Escape en pagehide wissen de gekopieerde waarde, het veld, de actieve oefenreferentie en de plak-/OK-status. De focus gaat terug naar de startknop. Een nieuwe documentload begint met lege markup; bij een reload van een aangemelde bezoeker krijgt de startknop focus. De server bewaart een eerdere echte oefenvoltooiing; replay verandert die niet en verstuurt geen tweede Choice-/Complete-POST. Er is geen nieuwe aanroep voor lotingsinschrijving toegevoegd.

## Verificatie

Nieuwe bronkopie: `artifacts/dev-start-66c0e6ce0a424327828df83d61009930/source`. Alle laatste correcties zijn naar deze kopie gesynchroniseerd.

- Volledige expliciete Node-set van de regressieorchestrator: 124 tests, 122 geslaagd, 2 bestaande PE-iconchecks overgeslagen, 0 fouten. Inclusief NL/EN-context, kopiëren/openen/plakken, leeg veld zonder kopie, wissen/sluiten, herlaadfocus en replay zonder extra voltooiingsverzoek. Finale uitvoer: `node-final.log` naast de bronkopie.
- XML-resources, JavaScript-syntax en `git diff --check` slagen.
- Locked restore faalt op ontbrekend leesrecht voor `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. Release-build en rechtstreekse webtestrunner falen daardoor op NETSDK1004, ontbrekende project.assets.json. Logs: `restore.log`, `build.log`, `web-direct.log`. Geen configuratie-, ACL- of identiteitsomweg gebruikt. Volledige .NET-regressie niet gestart.
- De bestaande statische browserpreview is met een eigen nieuw profiel geprobeerd: `CDP timeout: Page.enable`, vastgelegd in `responsive-browser.log`. Geen geslaagde screenshot- of visuele acceptatie geclaimd.
- De echte browserfixture is bijgewerkt voor de expliciete knoppen, lege start, Plakken, wissen/focus na reload en afzonderlijke Complete-/Choice-tellingen. Bereikbaarheidchecks gebruiken onder meer 640/390/320/160 CSS-pixels en scrollen elke knop binnen het venster in beeld. Dit omvat reflow-equivalenten van 200% op desktop en smal scherm; het is geen uitgevoerde fysieke browserzoomtest. De fixture is door de buildblokkade niet uitgevoerd.
- De bestaande synthetische C#-regressies voor behoud van deelnemer-/ronde-inschrijvingsaantallen, CompletedAt en voltooiingsaudit zijn behouden, maar niet uitgevoerd.

Nog open: Razor-/C#-build, echte HTTP-/databaseketen, browseracceptatie van beide talen en beide prijscontexten, werkelijke 200%-zoom en fysieke mobiele bediening. Authenticator- en productie-TLS-acceptatie zijn niet door deze UI-wijziging afgedekt.

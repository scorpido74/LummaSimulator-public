# Bezoekersflow, rondevoortgang en CBOR — 13 november 2026

Implementatie en beperkte verificatie; geen releaseacceptatie. `git rev-parse --show-toplevel` gaf `C:/src/LummaSimulator`. Reeds aanwezige lokale wijzigingen zijn behouden. Geen actieve portal gestart, database geopend, proces gestopt of runtimeconfiguratie gewijzigd.

## Wijzigingen

- `DemoService.OptionsAsync` gebruikt `visibleName` voor zowel WebAuthn `user.name` als `user.displayName`. Zonder Prize-alias is dat `account.Name`. Alleen de verouderde verwachting in `SessionStatusChecks` is aangepast; Prize-aliastests blijven behouden.
- Het groene blok houdt Je bent aangemeld, één zin over phishingbescherming en Start de oefening →. Technische verificatie staat ingeklapt. Bezoekersfelicitatie en dubbel authenticatielabel zijn verwijderd; de oranje winnaarskaart houdt haar felicitatie.
- De groene kaart blijft zichtbaar en bedienbaar tijdens de oefening. De fictieve melding staat rechtsonder; Uitvoeren staat linksonder. Beide blijven binnen de viewport, met interne scroll en afbrekende opdrachttekst. Geen blauw paneel, voortgangsbalk of WPF-uitleg. De exacte gevraagde voorbeeldopdracht is uitsluitend tekst in een alleen-lezen browserillustratie. Geen clipboard, download of systeemactie.
- De bestaande minimale serverleestijd van 32 seconden blijft behouden: melding 10 seconden, daarna Uitvoeren; OK komt 22 seconden later beschikbaar. OK opent de nabespreking in dezelfde browserpagina. Sluiten/Escape/terug herstellen focus naar de startknop. Replay behoudt dezelfde aanmelding en deelname en verstuurt geen tweede Choice-/Complete-verzoek. NL en EN zijn bijgewerkt.
- `GET /Presenter/Events?handler=Progress&id=…&roundId=…` veroorzaakt de generieke voortgangsmelding bij een niet-OK respons, timeout of verwerkingsfout. Gesloten rondes gebruiken hun servergerenderde opgeslagen uitslag zonder voortgangsverzoeken. Een open ronde die sluit toont de ontvangen uitslag en stopt verdere verzoeken. Het serverendpoint blijft geautoriseerd beschikbaar voor expliciete reads. Geen foutmelding verborgen bij open rondes.
- Het eerder genoemde SQLite-log is niet aangeleverd en in de doorzochte projectdocumentatie/logbestanden is geen bijbehorende exception gevonden. Een tijdsverband of identieke oorzaak is daarom **niet vastgesteld**. De pollingwijziging bewijst geen reparatie van een onbekende SQLite-fout.

## Dependency en blokkade

Web.Tests → projectreferentie Web → Fido2.AspNet 4.3.1 → Fido2 4.3.1 → System.Formats.Cbor 9.0.0. De tests-lockfile toont deze transitieve dependency. De bestaande portal-lockfile bevatte die CBOR-node niet en vereist eveneens echte hergeneratie.

De portal heeft nu een expliciete packageverwijzing naar **9.0.24**. Dit houdt de bestaande 9.x-lijn aan en voldoet aan [Microsofts gepatchte minimumversie](https://github.com/dotnet/runtime/security/advisories/GHSA-vmv3-wvjp-wqpj). Geen andere packageversie is aangepast.

**Nog niet voltooid:** lockfilegeneratie, package-audit en publicatiecontrole. Locked restore stopt op ontbrekend leesrecht voor `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. Een normale aanvraag van openbare NuGet-hashmetadata werd ook door sockettoegangsrechten geblokkeerd. Geen configuratie-, ACL-, netwerk- of identiteitsomweg gebruikt. Er zijn geen hashes verzonnen en geen lockfiles handmatig aangepast. De packageverwijzing en huidige locks zijn dus nog niet consistent. NU1902 kan nog niet als verdwenen worden aangemerkt; er is geen nieuwe publicatie waarvan een meegeleverde CBOR-versie is gemeten.

## Werkelijk uitgevoerd

Bronkopie en logs: `artifacts/dev-start-a556bca62ea04fcc8a2216e1af8d4e07`. De kopie bevat de actuele lokale bron, inclusief ongetrackte projecten en benodigde packaging-bronnen; geen actieve runtimegegevens.

| Controle | Resultaat |
| --- | --- |
| Locked restore | Geblokkeerd op NuGet.Config-leesrecht; `restore.log` |
| Release-build, geen restore | NETSDK1004: ontbrekende project.assets.json; `build.log` |
| Rechtstreekse Web.Tests-runner via dotnet run, geen restore | NETSDK1004 voor Web.Tests/obj/project.assets.json; `web-tests.log` |
| Volledige dotnet test-regressie, geen restore | NETSDK1004; `regression.log` |
| Volledige expliciete Node-set van regressieorchestrator | 117 tests: 115 geslaagd, 2 bestaande PE-iconchecks overgeslagen, 0 fouten; `node-final.log` |
| Resource-XML, gewijzigde JavaScript-syntax, git diff --check | Geslaagd |
| Statische browserpreview | Mislukt: `CDP timeout: Page.enable`; `browser-final.log`. Geen visuele of 200%-zoomacceptatie geclaimd. |

De webtestrunner bereikte geen test: er is geen eerste runtime-exception, stacktrace of testnaam beschikbaar. De eerste fout is de genoemde NETSDK1004-buildfout, niet een mislukte `SessionStatusChecks`-assertie. Oudere Node-pogingen zijn behouden in de logs; de tijdelijke fixturefouten (ontbrekende packaging-bronnen en oude EN-knoptekst) zijn hersteld.

De Node-tests controleren NL/EN, aanmeldlogica, sluiten, Escape, terugkeerfocus, replay zonder extra voltooiing, opgeslagen winnaars/verliezers en geïsoleerde in-memory SQLite-data voor open en gesloten rondes. Een aparte test controleert nul requests voor een reeds gesloten ronde, ook bij pageshow. Dit zijn geen geslaagde live portal-, Razor- of mobiele browseracceptaties.

## Opdrachten voor gewone Windows PowerShell

Voer onderstaande opdrachten uit met normale NuGet.Config-leestoegang. Ze gebruiken de eigen bronkopie en publiceren uitsluitend naar een nieuwe geïsoleerde map; ze starten geen bestaande portal. Controleer na lockgeneratie de wijzigingen: uitsluitend CBOR en de daardoor benodigde dependencyregistratie mogen veranderen. Neem pas daarna de betrokken lockfiles terug naar de hoofdwerkboom.

```powershell
$repo = 'C:/src/LummaSimulator'
$source = Join-Path $repo 'artifacts\dev-start-a556bca62ea04fcc8a2216e1af8d4e07\source'
Set-Location $source
$ErrorActionPreference = 'Stop'
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Vorige opdracht mislukt: $LASTEXITCODE" } }
# Eén expliciete hergeneratie vanwege de nieuwe packageverwijzing.
dotnet restore LummaSimulator.slnx --configfile "$source\NuGet.Config" --force-evaluate -p:RestoreLockedMode=false -p:RuntimeIdentifiers=win-x64
Check-Exit
dotnet restore LummaSimulator.slnx --configfile "$source\NuGet.Config" --locked-mode -p:RuntimeIdentifiers=win-x64
Check-Exit
dotnet build LummaSimulator.slnx -c Release --no-restore
Check-Exit
$env:LUMMA_TEST_WEB_DLL = "$source\LummaSimulator.Web\bin\Release\net10.0\LummaSimulator.Web.dll"
dotnet "$source\tests\LummaSimulator.Web.Tests\bin\Release\net10.0\LummaSimulator.Web.Tests.dll" 2>&1 | Tee-Object "$source\artifacts\web-direct.log"
Check-Exit
dotnet test tests/LummaSimulator.RegressionTests/LummaSimulator.RegressionTests.csproj -c Release --no-restore
Check-Exit
dotnet list LummaSimulator.Web/LummaSimulator.Web.csproj package --vulnerable --include-transitive
Check-Exit
dotnet list tests/LummaSimulator.Web.Tests/LummaSimulator.Web.Tests.csproj package --vulnerable --include-transitive
Check-Exit
$publish = Join-Path $source ('artifacts\portal-publish-' + [guid]::NewGuid().ToString('N'))
dotnet publish LummaSimulator.Web/LummaSimulator.Web.csproj -c Release -r win-x64 --self-contained false --no-restore -o $publish
Check-Exit
$deps = Get-Content "$publish\LummaSimulator.Web.deps.json" -Raw | ConvertFrom-Json
$deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'System.Formats.Cbor/*' }
Get-Item "$publish\System.Formats.Cbor.dll" | Select-Object FullName, @{Name='ProductVersion';Expression={$_.VersionInfo.ProductVersion}}
```

De volledige regressie voert ook de expliciete Node-set, SmokeTests en Windows Verification uit. Leg bij een runtimefout de eerste exception inclusief stacktrace en naam van de falende check vast uit `web-direct.log`. De echte ClickFix-browserfixture is bijgewerkt voor OK, NL/EN, focus en kleine viewports; run die na een geslaagde build ook expliciet:

```powershell
dotnet "$source\tests\LummaSimulator.Web.Tests\bin\Release\net10.0\LummaSimulator.Web.Tests.dll" --clickfix-browser-checks
Check-Exit
```

Nog open: echte aanmelding → sluiten → terug → herhalen → ronde-uitslag in NL/EN op de geïsoleerde portal; werkelijke browserzoom 200%; fysieke telefoon/iPad en YubiKey/native passkeydialoog; productie-TLS en operationele MDR-acceptatie. Een virtuele authenticator, CSS-reflow of deviceScaleFactor bewijst deze fysieke eigenschappen niet.

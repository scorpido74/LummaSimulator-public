# /Demo: terugkeer, begroeting en visuele herhaling — 12 november 2026

## Trace en herstel

AGENTS.md en de bestaande sessie-/oefenimplementatie zijn gelezen. Lokaal werk is behouden; geen actieve runtimegegevens, database, proces, credential, origin of RP-ID zijn gewijzigd.

De huidige afronding navigeert naar /ClickFixComplete en beëindigt geen IdentitySession of bezoekerscookie. De oude oefeningsterugknop verwees naar /SignedIn; die gaat nu rechtstreeks naar /Demo. De legacy /ClickFix-GET en de afvang voor een niet-voltooide bedankpagina verwijzen eveneens naar /Demo. De bedankknop verwees al naar /Demo.

Een tweede concrete fout was de browserafhandeling van BeginExercise.Completed=true: deze navigeerde direct weer naar het bedankscherm in plaats van de oefening te tonen. BeginAsync gaf de bestaande voltooiing terecht terug. Opnieuw beginnen via Nieuwe bezoeker zou juist wél de vorige demo sluiten en een nieuwe registratie vereisen; die actie wordt niet meer gebruikt als oefenherstart. Er is geen live bewijs verzameld dat het afronden zelf een geldige aanmelding introk.

Na herstel:

- Vóór aanmelding blijft de titel Welkom bij [evenement], of de bestaande zelfstandige fallback. Na serverbevestiging verandert kop én browsertitel naar Welkom [alias]. SSR na terugkeer/herladen gebruikt dezelfde OwnerAsync-bevestiging en de bestaande deelnemeralias. De groene Je bent aangemeld-status blijft staan zolang dezelfde aanmelding geldig is.
- De knop toont na voltooiing Demo-oefening opnieuw starten. De server leest hiervoor alleen de bestaande CompletedAt van het eigen demoaccount. Oefenstatus is geen vervanging voor authenticatie.
- BeginAsync valideert de eigen IdentitySession opnieuw. Voor een reeds voltooide oefening retourneert het read-only replaytoestemming, zonder nieuw ticket, nieuwe rij, nieuwe sessie of ronde-inschrijving. De browser speelt daarna alleen de bestaande visuele tijdlijn opnieuw af. Er volgt geen tweede Choice-/Complete-POST, voltooiingsaudit, lotingsinschrijving of prijsbepaling. De uiteindelijke bedankpagina controleert opnieuw de bestaande aanmelding en voltooiing.
- First-run voltooiing, bestaande Join-/Close-/voorraadregels, afgesloten ronde-uitslagen, sessieverloop en intrekking blijven behouden. Replay roept JoinAuthenticatedAsync niet aan en maakt geen nieuwe deelnemer in een gesloten of open ronde. Een vervallen aanmelding wordt door OwnerAsync geweigerd; geen sessieverlenging of automatische passkeyceremonie is toegevoegd.
- De melding luidt Beveiligingsverificatie verbroken / Er is iets niet goed gegaan tijdens je laatste aanmelding. Daarna verschijnt Uitvoeren, Openen: en LUMMA-DEMO-ONLY. Beide zijn statische browserillustraties met Demo-oefening; er worden geen OS-vensters, klembord of systeemacties gebruikt.
- Na afloop staat: De melding deed alsof er iets mis was met je aanmelding en probeerde je onder tijdsdruk een herstelstap te laten volgen. NL en EN zijn bijgewerkt.

## Uitgevoerde verificatie

- Volledige expliciete Node-orchestratorset: **106 tests, 104 geslaagd, 2 bestaande iconcontroles overgeslagen, 0 fouten**. Nieuw: Echovalk-begroeting uit de geverifieerde respons, visuele replay zonder tweede voltooiings-/presentatoractie en weigering van een verlopen sessie. Bestaande registratie-/aanmeldscheiding, minstens tien seconden zonder automatische navigatie, QR-, consent- en lokalisatiecontroles blijven slagen.
- JavaScript-syntax, XML-resources, gewijzigde C#-syntax (Roslyn, geen typecontrole) en git diff --check zijn gecontroleerd.
- De bestaande echte browserfixture is uitgebreid met afronden → de werkelijke terugknop naar /Demo → alias/groen blok → herladen → replay. Hij controleert dat geen tweede WebAuthn-API-aanvraag en geen tweede Complete-/Choice-aanvraag plaatsvindt. De eigen synthetische browser/profiel gebruikt een virtuele authenticator; dit vervangt geen fysiek Windows-passkeyvenster. De begrensde fixturetermijn is 150 seconden voor twee visuele tijdlijnen.
- De C#-regressies controleren ook behoud van CompletedAt, dezelfde authenticatie, één deelnemer/één afronding en afwijzing bij het verlopen van authenticatie. Deze volledige .NET-/browserchecks zijn niet uitgevoerd door de buildblokkade hieronder.

## Buildblokkade en nog open

Start-LummaDev.bat verify met locked restore maakte artifacts/dev-start-7ae0a193a7df42ada6be1f43a21a3253. Restore faalt op geweigerde leestoegang tot %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. De geïsoleerde regressieorchestrator en Web.Tests-runner zijn met --no-restore en expliciet geïsoleerde LUMMA_TEST_WEB_DLL geprobeerd: NETSDK1004, ontbrekende project.assets.json. Geen toegangsrechten-/configuratieomweg gebruikt en geen bestaande app herstart.

Logs: artifacts/demo-replay-5578e34d64584211ada653fc3db8d3d0 (node.log, regression.log, web-tests.log, syntax.log); restore.log in de dev-start-map. De eigen bronkopie wordt met de laatste correcties gesynchroniseerd; dit is geen geslaagde buildclaim.

Nog uit te voeren na herstel van de normale buildomgeving: de volledige keten in één browser met de echte portal, inclusief dezelfde alias (zoals Echovalk), terugknop, herladen, replay, gelijkblijvende ronde-inschrijving en verlopen sessie. Er is geen nieuwe live browser- of fysieke authenticatoracceptatie geclaimd.

## Gewijzigde bestanden en regels

| Bestand | Regel |
|---|---|
| LummaSimulator.Web/Pages/Demo.cshtml.cs | 13 |
| LummaSimulator.Web/Pages/Demo.cshtml | 12 |
| LummaSimulator.Web/Pages/Shared/_Layout.cshtml | 10 |
| LummaSimulator.Web/wwwroot/js/localization.js | 19 |
| LummaSimulator.Web/wwwroot/js/passkeys.js | 132 |
| LummaSimulator.Web/Services/VisitorExerciseService.cs | 12 |
| LummaSimulator.Web/wwwroot/js/clickfix.js | 35 |
| LummaSimulator.Web/Pages/Shared/_ClickFixExercise.cshtml | 10 |
| LummaSimulator.Web/Pages/ClickFix.cshtml.cs | 13 |
| LummaSimulator.Web/Pages/ClickFixComplete.cshtml.cs | 14 |
| LummaSimulator.Web/Resources/Localization/UiText.resx | 1294 |
| LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx | 1294 |
| LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx | 1294 |
| tests/WebAuthn.Ux.Tests/passkeys.test.cjs | 421 |
| tests/WebAuthn.Ux.Tests/clickfix.test.cjs | 76 |
| tests/LummaSimulator.Web.Tests/ClickFixChecks.cs | 75 |
| tests/LummaSimulator.Web.Tests/PrizeRoundsChecks.cs | 93 |
| tests/LummaSimulator.Web.Tests/RoundVisitorBaseline.cs | 72 |
| tests/LummaSimulator.Web.Tests/ClickFixBrowserChecks.cs | 21 |
| tests/LummaSimulator.Web.Tests/clickfix-browser.mjs | 119 |

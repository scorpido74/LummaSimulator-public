# Beoordelingsknop en QR bij bezoekerslink — 11 november 2026

AGENTS.md en de requirements/QR-architectuur zijn gelezen. De bestaande working tree is behouden; geen actieve database of bestaande app is geopend, gestopt of gemigreerd.

## Gedrag

- Naast Scenario staat Beoordeling vastleggen / Record assessment. Het bestaande formulier start gesloten. Dezelfde knop sluit het weer zonder de velden te vernietigen. Het formulier opent onder de fasekaarten, over de beschikbare breedte. Er is geen gereserveerde rechterkolom. De bestaande ConfirmAssessmentCommand, bevestigingsvalidatie, SaveAsync en beoordelingsstatus blijven in gebruik.
- De gedeelde QR-partial is bij de eerste serverrender zichtbaar en staat boven het label Bezoekerslink voor dit evenement. De klikbare volledige URL en kopieerknop blijven beschikbaar. QR-state en SVG worden op iedere aanvraag opnieuw per evenement geautoriseerd; bij sluiten/verlies van toegang wist de bestaande polling de uitnodiging.
- De QR-service gebruikt de eerste expliciet geconfigureerde WebAuthn HTTPS-origin, niet de Host van het beheerscherm. De bestaande originlijst en RP-ID worden niet gewijzigd. Bij meerdere toegestane origins is de eerste de bezoekersorigin; PathBase en de volledige eventKind/eventId-query blijven behouden.
- Naast Prize ondersteunt dezelfde uitnodiging nu ook actieve experience- en edition-evenementen. De bestaande LiveAsync- en toegangscontroles bepalen beschikbaarheid; de ongekoppelde/quarantaine-scope krijgt geen QR.
- De bestaande lokale C# QR-encoder wordt hergebruikt; geen internetdienst of nieuwe dependency.

## Verificatie

- Relevante Node-set: **61 tests geslaagd, 0 fouten, 0 overgeslagen**. Inclusief passkeys, lokalisatie, presentator, QR-lifecycle, evenementtypecontrole en onafhankelijke decoder voor de werkelijk gecompileerde C#-generator. Twee synthetische evenementen decoderen exact naar afzonderlijke HTTPS-URL's inclusief /portal/Index en eventKind/eventId. Dit zijn synthetische generatorcontroles, geen live evenementbezoek.
- WPF-verificatie uitgebreid voor dicht/open/dicht, behoud van veldselectie, geen rechterkolom, effectieve DIP-afmetingen waaronder 100/150%, en opslaan via het bestaande bevestigingscommando. Web-tests uitgebreid voor beheertoegang via localhost met geconfigureerde netwerk-origin en twee verschillende evenementen. Deze .NET-tests zijn **niet uitgevoerd**.
- XAML en beide WPF-resourcebestanden als XML gecontroleerd. git diff --check gecontroleerd.
- `Start-LummaDev.bat verify` creëerde artifacts/dev-start-7f50db601ad64533911426afa6f4b4e1 met locked restore. Restore faalt op geweigerde leestoegang tot %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. Geen configuratie-, ACL- of identiteitsomweg gebruikt.
- De bestaande dotnet-regressieorchestrator is in die geïsoleerde bronkopie geprobeerd met --no-restore en expliciet LUMMA_TEST_WEB_DLL binnen die kopie. Hij stopt op NETSDK1004: project.assets.json ontbreekt door de mislukte restore. Er is geen geslaagde solution-/WPF-/Razor-build en geen nieuwe app gestart.
- Open: handmatig openen/sluiten/opslaan in draaiende WPF; fysieke Windows-schaal 100/150%; QR-scan met telefoon op Cybertruck-netwerk en vergelijking met de echte bezoekerslinks van twee evenementen. Eerdere geweigerde computer-/netwerktoegang is niet omzeild.

## Bestanden en regels van deze wijziging

| Bestand | Regel |
|---|---|
| LummaSimulator.App/MainWindow.xaml | 61 |
| LummaSimulator.App/MainWindow.xaml.cs | 63 |
| LummaSimulator.App/DesktopText.resx | 340 |
| LummaSimulator.App/DesktopText.en-GB.resx | 340 |
| LummaSimulator.Web/Services/ParticipantQrService.cs | 10 |
| LummaSimulator.Web/Services/EventAccess.cs | 18 |
| LummaSimulator.Web/Pages/Presenter/ParticipantQr.cshtml.cs | 13 |
| LummaSimulator.Web/Pages/Presenter/Events/Index.cshtml.cs | 46 |
| LummaSimulator.Web/Pages/Shared/_ParticipantQr.cshtml | 2 |
| LummaSimulator.Web/wwwroot/js/participant-qr.js | 26 |
| LummaSimulator.Web/wwwroot/css/site.css | 115 |
| shared/ParticipantQr.cs | 11 |
| verification/Program.cs | 72 |
| tests/LummaSimulator.Web.Tests/PrizeRoundsChecks.cs | 41 |
| tests/WebAuthn.Ux.Tests/Export-ParticipantQr.ps1 | 16 |
| tests/WebAuthn.Ux.Tests/participant-qr.test.cjs | 13 |
| tests/WebAuthn.Ux.Tests/participant-qr-encoding.test.cjs | 73 |

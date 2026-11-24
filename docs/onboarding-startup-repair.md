# Onboarding en lokale startup

## Vastgestelde oorzaak

- De onboarding gebruikte een geordende lijst én nummers in de vertaalsleutels voor meerdere koppen. De lijst telt nu elke stap één keer.
- De hostinghandler zette `HostingConfirmed` duurzaam en stuurde terug naar dezelfde pagina. De Razor-pagina toonde de vlag niet en de handler zette alleen bij een geweigerde actie een melding. Een POST met 302 en een GET met 200 past dus ook bij een geslaagde opslag. Ongeldige bevestigingen konden via een kale 400 eindigen. De wizard valideerde bovendien opnieuw met een omgeving afgeleid uit de RP-ID, terwijl startup de effectieve configuratie al met de echte hostomgeving valideert.
- Evenementbeheer blijft achter de bestaande poort totdat `CompletedAt` is opgeslagen. Een `FirstEvent` is op zichzelf geen afronding. Alleen afronden vereist de bestaande recente WebAuthn-bevestiging. De server consumeert het bewijs bij de geslaagde transactie. De nieuwe knop vraagt het bewijs bij een expliciete serverrespons en herhaalt de oorspronkelijke, antiforgery-beveiligde POST één keer. Annuleren laat de formulierwaarden staan.
- `PresenterInstallation.Existing` komt uit de bestaande migratie. De lokale Setup-wizard en CLI-bootstrap openen geen bestaande installatie opnieuw. Een verse database via een directe portalstart bevat de normale onboardingstatus. Die startmethode heeft geen launcher voor de desktopapp.
- De ontwikkelstarter behandelde alleen HTTP 200 op `/Presenter/Login` als gereed. Een verse database retourneert daar tijdelijk 302 naar `/Presenter/Setup`, zodat de launcher vóór App-start strandde. De gereedheidscheck accepteert nu alleen die specifieke 302 op dezelfde origin, naast 200. `Start-Demo.cmd` start beide onderdelen bij rol `Both`. `Start-PasskeyPortal.cmd` is de portalstarter.

Zie `LummaSimulator.Web/Pages/Presenter/Onboarding.cshtml`, `Onboarding.cshtml.cs`, `Services/PresenterOnboarding.cs`, `Services/PresenterPageAccessFilter.cs`, `Services/EventAccessMigration.cs`, `wwwroot/js/presenter.js`, `packaging/Start-LummaDev.ps1` en `packaging/EventEdition/LauncherControl.ps1`.

## Update van een bestaande installatie

Er is geen nieuwe schemamigratie of reset voor deze reparatie. Volg de bestaande [stop-, upgrade- en herstelprocedure](helper-network-upgrade.md). Behoud dezelfde installatieconfiguratie, database, keyring, Windows-identiteit, HTTPS-origin en certificaten. Gebruik onboarding opnieuw vanuit de bijgewerkte portal als de status nog open is. De wizard toont het al opgeslagen evenement bij naam en creëert bij hervatten geen tweede evenement.

Gebruik de interactieve `Start-Demo.cmd` voor een gecombineerde installatie. Gebruik bij een portalservice `Start-LummaSimulator.cmd` onder de interactieve gebruiker. Loopback is het verbindingsadres voor de App naar de lokale portal. `App-IP` in Demonetwerk blijft een voorgesteld LAN-luisteradres voor een afzonderlijke App-receiver. Dat plan verandert geen actieve listeners, firewallregels of pairings. Expliciete remote instellingen blijven gelden.

Voor een via de ontwikkelstarter gebruikte installatie met een expliciete `Database__Path` zet je diezelfde gecontroleerde override in de terminal vóór `Start-LummaDev.bat`. Behoud ook de eventuele bestaande opslag-, TLS- en origininstellingen van die startomgeving. De ontwikkelstarter bouwt de Release-sidecar vóór de solutionbuild, zodat de bestaande kopieertaak hem naast de App kan plaatsen.

## Verificatie

- `artifacts/dev-start-23bcd958bb3544a8bd9fb674f0e5299a/onboarding.log` bevestigt beide ontbrekende checkboxwaarden, opgeslagen hosting, hervatten met het eerste evenement, de servercontrole op recent bewijs, Chrome op 360 px, annulering met behouden invoer, afronding en een nieuwe beheerdersaanmelding. De Chrome-fixture simuleert alleen de authenticatorannulering. De succesvolle toets gebruikt de bestaande synthetische WebAuthn-authenticator.
- De Web- en Helper-regressies passeerden de orchestrator. Een volgende stap strandde op ontbrekende packagingscripts in de geïsoleerde ontwikkelkopie. De ontbrekende scripts en referentiebestanden zijn aan de betreffende testkopieën toegevoegd en de overige controles zijn afzonderlijk uitgevoerd. Node gaf 124 geslaagd en twee overgeslagen. De verse Release-ontwikkelbuild, 24 lokale sidecarcontroles, de volledige smoke-test en WPF-verificatie staan in `artifacts/dev-start-09a1e2b6a1804a3c88aa1d1118c99a2e`.
- De launchercontrole valideerde standaard- en gescheiden rollen, hergebruik van een geregistreerd App-proces, een echt bezette loopbackpoort, de juiste Setup-redirect en het foutpad bij een gestopte App. Op de demoapparatuur staan de gecombineerde launcherstart, productie-TLS en fysieke passkeycontrole open. De live installatie is buiten de test gebleven.

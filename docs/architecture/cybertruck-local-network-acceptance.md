# Cybertruck: lokaal demonetwerk en scenarioverificatie — 11 november 2026

## Inrichtingskeuze

De normale inzet is een eigen lokaal demonetwerk. Het laptop-IP hoeft vooraf niet bekend te zijn: configureer een vaste DNS-naam en laat DNS/DHCP op dat netwerk naar het actuele laptopadres verwijzen. Gebruik dezelfde HTTPS-origin voor deelnemerslink, QR, portalconfiguratie en de toegestane WebAuthn-origin; de RP-ID is de bijbehorende hostnaam. Een DNS-naam maakt de dienst niet publiek bereikbaar. Verander RP-ID niet voor bestaande credentials zonder afzonderlijk migratieplan.

Ook lokaal vereist WebAuthn op telefoons HTTPS met een passend, vertrouwd certificaat. Een eigen CA vereist expliciet vertrouwen op de telefoons; een certificaat van een publiek vertrouwde CA kan ook bij lokale DNS worden gebruikt. Publieke hosting is optioneel en vereist daarnaast bereikbaarheid, passende DNS/TLS en beheer. Er is hiervoor geen configuratie van een actieve installatie gewijzigd.

Ontwikkelprofiel: `https://localhost:7243`, alleen op de laptop bruikbaar. Een nieuwe pakketinstallatie ondersteunt nu `Demo.ps1 -Action Setup -PortalOrigin https://<lokale-dns-naam>`: origin, RP-ID en AllowedHosts blijven gelijkgericht; bestaande instellingen worden niet omgezet. De verpakte lokale standaard is `https://localhost` (443), eveneens alleen lokaal. Productie-WebAuthn valideert momenteel uitsluitend de standaard HTTPS-poort. Daardoor is **7243 geen automatisch vereiste demonetwerkpoort**. Voor een lokale netwerkuitrol moet eerst de daadwerkelijke hostnaam, binding en het certificaat worden ingericht; neem niet aan dat de aanwezige standaardconfiguratie al telefoons ondersteunt.

## Verbindingscontracten uit de broncode

| Bron | Bestemming | Richting op bestemming | Protocol/poort | Voorwaarde |
|---|---|---|---|---|
| Telefoon/deelnemersbrowser | Portal | Inkomend | HTTPS/TCP, originpoort; standaard 443, dev 7243 | Alleen werkelijk ingerichte netwerklistener, demonetwerk-CIDR en Private/Domain-profiel |
| WPF en Presenter Helper | Portal | Inkomend op portal / uitgaand op client | HTTPS/TCP, geconfigureerde originpoort | Optionele portalkoppeling en bestaande toegangscontrole |
| Presenter Helper / HelperUpdater | WPF-helperontvanger | Inkomend op demo-laptop / uitgaand op helper | HTTPS/TCP 443, HTTP.sys | Alleen ingeschakelde, gepaarde ontvanger met werkelijk provisioned listener; certificaatpin blijft gelden |
| HelperUpdater | Updatebestanden bij AppReceiver | Uitgaand | HTTPS/TCP 443 | Bestaande vaste releasepaden; geen generieke updatehost of aparte inkomende updaterpoort |
| WPF | LocalTestServer | Loopback | HTTP/TCP 8080 standaard, configureerbaar | Geen inkomende demonetwerkregel |
| Clients | Lokale DNS/DHCP-infrastructuur | Uitgaand/clientverkeer | DNS/DHCP volgens netwerkbeheer | Geen projectregel voor een DNS-/DHCP-server op de laptop toegevoegd |

De daadwerkelijke listeners, actieve profielen en firewallregels konden niet worden vastgesteld: `Get-NetTCPConnection`, `Get-NetConnectionProfile` en `Get-NetFirewallRule` gaven toegang geweigerd. Geen alternatieve inventarisatieroute, ACL-wijziging, firewallwijziging, beveiligingsuitschakeling of processtop toegepast. DNS, certificaatvertrouwen, WithSecure en bereikbaarheid vanaf telefoon zijn nog operationeel te toetsen.

## Beheerprocedure

`packaging/EventEdition/Demo.ps1 -Action Firewall -FirewallAction Audit` gebruikt de bestaande onderhoudsingang. De nieuwe `Firewall.ps1` ondersteunt Audit, Plan, Install en Remove. Plan wijzigt niets. Install vereist beheerrechten, expliciete CIDRs/profielen en verifieert de echte listener en het eigen executable-pad; er wordt geen blinde regel voor 443 aangemaakt. Remove verwijdert alleen regels met projectnaam, groep en eigenaarmarkering. Een bestaande blokkaderegel wordt niet verwijderd.

Voorbeeld met uitsluitend placeholders voor de beheerder (eerst echte waarden invullen, geen letterlijk uit te voeren netwerkkeuze):

```powershell
.\packaging\EventEdition\Firewall.ps1 -Action Audit
.\packaging\EventEdition\Firewall.ps1 -Action Plan -PortalOrigin 'https://<lokale-dns-naam>' -PortalProgram 'C:\<installatie>\Portal\LummaSimulator.Web.exe' -DemoSubnet '<demonetwerk-CIDR>' -NetworkProfile Private
# Na controle van binding, certificaat en plan, in verhoogde PowerShell:
.\packaging\EventEdition\Firewall.ps1 -Action Install -PortalOrigin 'https://<lokale-dns-naam>' -PortalProgram 'C:\<installatie>\Portal\LummaSimulator.Web.exe' -DemoSubnet '<demonetwerk-CIDR>' -NetworkProfile Private
.\packaging\EventEdition\Firewall.ps1 -Action Remove -WhatIf
```

Alleen bij een werkelijk gebruikte helperontvanger: voeg `-HelperReceiverSettingsPath '<absoluut instellingenpad>' -HelperSubnet '<helpernetwerk-CIDR>'` toe. De demo zelf blijft standaardgebruiker. Er is geen inkomende regel nodig om alleen uitgaand updates op te halen.

## Bevestigde correcties in deze werksessie

- Bouwkopie selecteert de drie helperprojecten naast App/Web; controle tegen alle directe solutionprojecten. Ook de EventEdition-builder selecteert die bronnen. Uitsluitingen en locked restore blijven behouden. `Start-LummaDev.bat verify` maakt zonder runtime-start een nieuwe bronkopie, ook als het ontwikkelstartvenster bezet is.
- `/SignedIn` start de fictieve oefening pas na de knopklik, op dezelfde pagina. Alias en bevestiging blijven staan. Herhaalde klikken starten geen tweede tijdlijn. Legacy `/ClickFix` verwijst terug naar dit scherm.
- Het bedankscherm toont de bestaande handmatige groepsuitslag of 'uitslag volgt'. De deelnemer wordt gevraagd de alias te onthouden; uitloggen verlengt geen account. De presentator kan winnaars per ronde terugvinden en uitreiken eenmalig registreren zonder tweede voorraadboeking. Er is geen automatische individuele trekking toegevoegd.
- Presentatorweergave wordt ook bij sluiten van de groepsronde ongeldig. Deelnemers blijven waar nodig voor overzicht/uitslag beschikbaar; toestemming verleent geen blijvende weergavetoegang.
- De gekoppelde helper kan uitsluitend voor de actuele, opnieuw gevalideerde WPF-selectie de vaste deelnemerslink op de gekoppelde werkplek openen. Geen willekeurige URL of opdracht wordt geaccepteerd.
- WPF-log en fasedetails zijn standaard ingeklapt; scrollen en loggegevens blijven beschikbaar. Evenementnaam/status/statistieken en acties staan bij het actieve evenement.

## Werkelijk uitgevoerd en nog open

Normale start `Start-LummaDev.bat`: geblokkeerd door een actief ontwikkelstartvenster. Geen processen beëindigd. Ondersteunde geïsoleerde route `Start-LummaDev.bat verify` creëerde:

`C:/src/LummaSimulator\artifacts\dev-start-9e7d0566748b438195a31869f84a89c1`

Alle directe solutionprojectbestanden, inclusief Helper.Core, HelperUpdater en PresenterHelper, zijn in die kopie gecontroleerd. Locked restore faalde op ontbrekende leestoegang tot `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`. Geen omleiding of wijziging van toegangsrechten/configuratie gebruikt. De kopie bevat de bronnen van het moment van die poging, niet alle latere correcties.

Geen geslaagde .NET-build, regressieorchestrator, publishacceptatie of nieuwe WPF-/portalstart. C#-tests voor afronding, consent na rondesluiting en idempotente uitreiking zijn aangepast maar nog niet uitgevoerd. XAML/XML en PowerShell/JavaScript-syntaxcontroles en een uitsluitend synthetisch firewall-Plan zijn uitgevoerd. De Node-regressies: **97 totaal, 95 geslaagd, 2 bestaande PE-iconcontroles overgeslagen, 0 fouten**, log `artifacts/scenario-node-check.log`. De pakketverificatie slaagt met **104 packaging checks**, plus monitorlauncher- en **43 procescompatibiliteitscontroles** (synthetische kindprocessen, geen actieve installatie), log `artifacts/scenario-packaging-check.log`. De netwerk-origincontroles gebruiken uitsluitend synthetische DNS-namen, inclusief afwijzen van HTTP, IP-adressen, alternatieve poorten en URL-paden.

**Geen volledig scenario is als geslaagd aangemerkt.** Open: fysieke passkeyregistratie en terugkerende aanmelding, QR-scan op telefoon, Test Remco met dezelfde alias in WPF na vernieuwen, tweede tab/logout/verloop, helperroute, prijsafronding/-uitreiking, NL/EN liveflow, Windows-schaal 100/150%, mobiel, standaardgebruiker met WithSecure en netwerk/TLS. Hiervoor zijn een werkende toegestane buildomgeving en de concrete lokale netwerkinrichting nodig. Actieve databases, keyrings en historische gegevens zijn niet geopend of gewijzigd.

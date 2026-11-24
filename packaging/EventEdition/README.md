# LummaSimulator v0.7 — Windows x64

Testversie voor geautoriseerde demonstraties met synthetische gegevens. Controleer de ZIP met het afzonderlijke SHA-256-bestand en pak alles uit op een lokale schijf. De self-contained programma's hebben geen SDK nodig. Binaries zijn niet digitaal ondertekend; schakel geen beveiliging uit.

## App en portal

De beheerder richt vooraf vertrouwd HTTPS, de juiste origin/RP-ID, opslag en eventuele netwerktoegang in. Het lokale startprofiel gebruikt https://localhost:443 en een vooraf vertrouwd localhost-certificaat met private key in CurrentUser/My van het demo-account. Het pakket bevat geen certificaatprivésleutel of demoaccounts. Een server-/LAN-installatie vereist expliciete beheerconfiguratie; wijzig RP-ID/origins niet als cosmetische aanpassing.

Start bij een nieuwe lokale installatie Setup-Demo.cmd, daarna Start-Demo.cmd. De browser begeleidt eerste beheerderregistratie en onboarding. Start-PasskeyPortal.cmd en Start-LummaSimulator.cmd starten één onderdeel. Stop-Demo.cmd stopt uitsluitend de eigen installatieprocessen, zonder gegevens te wissen. Behoud database, Data Protection-keyring en oorspronkelijke Windows-DPAPI-context samen.

## Eén Presenter Helper

Start-PresenterHelper.cmd start de bediening op de presentator-pc als gewone gebruiker. De portal biedt hetzelfde portable pakket aan onder Presenter Helper voor Windows downloaden (ZIP). Voor een andere pc: download het pakket, pak het volledig uit en gebruik daarin hetzelfde startpunt.

PresenterHelper/ bevat één helperprogramma, plus de interne Helper.Core-library en HelperUpdater. Verwijder of start die dependencies niet afzonderlijk. De WPF-app-pc bevat zijn eigen optionele HTTPS-ontvanger; dit is geen tweede gebruikershelper. De beheerder verstrekt app-origin, certificaatpin en pairing. Alleen een goedgekeurde portalverbinding, ingeschakelde evenementknop en actuele QR bevestigen bruikbare koppeling. De portable versie wijzigt geen firewall of TLS-inrichting. Ondertekende automatische updates vereisen de afzonderlijke beheerde gebruikersinstallatie en vertrouwde release-uitgever.

## Netwerk en beheer

De serverportal gebruikt ingericht HTTPS 443 of expliciet 7243. De WPF-helperontvanger gebruikt HTTPS 443 op de app-pc (HTTP.sys), uitsluitend vanaf interne presentator-hosts/subnetten. De helper luistert zelf niet. TCP 7243 in het ontwikkelprofiel is loopback en heeft geen inkomende LAN-regel nodig. Kestrel en de HTTP.sys-ontvanger kunnen niet zonder extra inrichting hetzelfde IP/poortpaar 443 delen.

Firewall.ps1 is voor de beheerder: begin met -Action Audit, daarna -Action Plan met expliciete -HostRole Portal of AppPc, -LocalAddress, -NetworkProfile Private/Domain en concrete bron-CIDRs. Installatie vereist beheerrechten en controle van de actieve listener. Geen Public/Any-regel, internet-portforwarding of firewallwijziging door de presentator. Certificaatbinding, HTTP.sys-URLACL en bereikbaarheid moeten op de werkelijke hosts apart worden gecontroleerd.

Hosts kunnen per installatie verschillen. Gebruik bij nieuwe inrichting `powershell -NoProfile -File .\Demo.ps1 -Action Setup -Component Portal` op de server, of `-Component App` op de app-pc. Geef `-PortalOrigin` op; de app-only variant vraagt geen portalprivésleutel. Met `-AppOrigin`, `-PortalListenAddress`, `-AppListenAddress`, `-DemoSubnet`, `-HelperSubnet` en `-NetworkProfile` geeft de beheerder een volledig netwerkplan mee. De lokale standaard gebruikt Component Both. De pakketlauncher ondersteunt als actieve publieke portal-origin HTTPS 443; een bestaande 7243-/reverse-proxyinstallatie blijft afzonderlijke beheerinrichting.

Het plan is later door een opnieuw geauthenticeerde administrator te wijzigen onder Configuratie → Demonetwerk. Opslaan bewaart een gecontroleerd voorstel en wijzigt geen actieve Windows-firewall, certificaat of RP-ID. De beheerder moet de juiste hosts controleren en het plan expliciet toepassen; bestaande passkeys en installatiegegevens worden niet automatisch gemigreerd.

Oude builds, testbronnen, logs, lokale instellingen, databases en sleutels horen niet in dit pakket. Licenties, runtimes en LocalTestServer zijn wel nodig. Een ZIP-checksum bewijst geen praktijkacceptatie: test na uitpakken met nieuwe synthetische opslag, standaardgebruikerspairing, mobiele TLS en fysieke authenticators.

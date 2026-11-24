# Presenter Helper voor Windows x64

Pak het volledige ZIP-bestand uit op een lokale schijf. Start **Start-PresenterHelper.cmd** als gewone gebruiker op de presentator-pc. Een SDK, beheerrechten of inkomende firewallregel op deze pc zijn niet nodig. Dit portable pakket is niet digitaal ondertekend; controleer herkomst en het afzonderlijk verstrekte SHA-256-bestand.

Presenter Helper is de bediening voor een afzonderlijke Lumma WPF-app-pc. Hij meldt de presentator via de portalbrowser aan, vraagt expliciete goedkeuring en laat een toegestaan evenement in WPF selecteren. Een ingeschakelde selecteerknop en een actuele evenement-QR bevestigen de bruikbare koppeling; alleen een gestart venster doet dat niet.

Laat de beheerder eerst portal-origin, app-origin (HTTPS 443), certificaatpin en pairing inrichten. Vul die in bij Installatie-instellingen; log of deel de pairingwaarde niet. Instellingen blijven per gebruiker onder LocalAppData en buiten dit pakket. Bij offline status blijven nieuwe gekoppelde demoacties geblokkeerd. Gebruik uitsluitend synthetische sessies.

`PresenterHelper/` bevat één programma met zijn interne dependencies: Helper.Core is een library, HelperUpdater is voor gecontroleerde ondertekende updates. Start die dependencies niet afzonderlijk en verwijder ze niet. Deze portable eerste installatie installeert niet automatisch naar de beheerde gebruikersmap. Automatische updates vereisen de bestaande beheerde installatie, vertrouwde release-uitgever en ondertekende updatebestanden; een portable start omzeilt die controles niet.

De WPF-ontvanger draait op de app-pc en wordt door WPF gestart als de beheerder hem expliciet heeft ingericht. Dat is geen tweede Presenter Helper. Het helperkanaal blijft op het demonetwerk; publiceer of forward de receiverpoort niet naar internet.

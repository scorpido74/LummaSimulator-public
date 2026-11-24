# v0.7.0-beta.1 — pre-release

Eerste beoogde publieke Windows 11 x64-distributie. Testversie voor gecontroleerde demonstraties met fictieve gegevens; geen productieondersteuning.

## Functionaliteit

- Zelfstandige WPF-presentator met zes gesimuleerde aanvalsfases en optionele Session Exposure Monitor.
- Passkeyportal met verplichte user verification, presenterbeheer, eventautorisatie, uitnodigingen en eerste-installatiebegeleiding.
- Interactieve browser-ClickFix-simulatie zonder uitvoerbare payload of daadwerkelijke installatie.
- Fase 6.5e: nieuwe bezoekers behouden hun server-side eventcontext; eigendomsoverdracht trekt ongebruikte event-enrollmentautorisaties in.
- Self-contained Windows x64-publicatie met portal, WPF en LocalTestServer; geen afzonderlijke .NET-installatie nodig.

## Vereisten en bekende beperkingen

- Vooraf een vertrouwd localhost-certificaat onder dezelfde Windows-gebruiker configureren. Geen certificaten meegeleverd, geen TLS-bypass of automatische firewallwijzigingen.
- Pakketstarters gebruiken uitsluitend `https://localhost`, poort 443. Telefoon- of externe hosting is geen kant-en-klare optie van dit pakket.
- Niet-ondertekende binaries kunnen een SmartScreen-/beveiligingswaarschuwing opleveren. Geen advies om beveiliging uit te schakelen.
- Fysieke authenticator-, mobiele-, DPI- en werkelijke MDR-tests blijven afzonderlijke acceptatietaken.
- Historische redeemed enrollmentgrants zonder uitnodigingsprovenance kunnen niet betrouwbaar achteraf worden geidentificeerd; bestaande korte vervaltijden blijven gelden. Geactiveerde accounts en credentials worden niet verwijderd.

## Verificatiestatus

Voor v0.7.0-beta.1 is de geïsoleerde nulmeting geblokkeerd door geweigerde leestoegang tot de gebruikers-NuGet.Config. Er is nog geen nieuwe .NET-build of distributiezip. Eerder aangeleverd testbewijs voor de vorige versie valideert deze wijzigingen niet.

De volledige Node-suite heeft 79 geslaagde tests en twee overgeslagen PE-iconchecks wegens ontbrekende build-/publishoutput. De pakket-, monitor-, procescompatibiliteits- en ontwikkelstarterchecks slagen. Nieuwe versiebron, responsieve WPF-aanpassingen en `<<`/`>>`-navigatie vereisen nog .NET- en fysieke acceptatie. Zie `docs/v0.7-testbuild-status.md` in de bronrepository voor de blokkade en resterende controles.

**Vrijgave nog geblokkeerd totdat de daadwerkelijke self-contained publicatie, pakketcontrole en eerste-installatie/start/stopacceptatie geslaagd zijn.** Deze conceptnotitie mag niet als bewijs van uitgevoerde installatieacceptatie worden gebruikt.

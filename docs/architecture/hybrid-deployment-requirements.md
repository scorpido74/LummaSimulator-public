# Requirements voor hybride deployment

Datum: 31 oktober 2026. Status: toekomstige requirements, geen implementatieopdracht of bewijs van huidige ondersteuning. Bestaande functionele requirements en open keuzes blijven gelden.

## Eén codebase, drie omgevingen

Dezelfde codebase moet development op localhost, centrale publiek bereikbare hosting en een zelfstandige offline Cybertruck-installatie ondersteunen. WPF en portal blijven onafhankelijk; hun optionele HTTPS-koppeling staat standaard uit. Installaties delen geen database. Dit document wijzigt geen actieve configuratie, passkeys, certificaten, DNS of schema's.

De Cybertruck biedt een eigen lokaal wifi-netwerk en moet zonder internet functioneren. Bezoekers gebruiken eigen telefoon, laptop of YubiKey; demo-laptops bieden een fallback. Portal, statische bestanden en noodzakelijke diensten moeten lokaal beschikbaar zijn. Bereikbaarheid via wifi, clientisolatie, lokale naamresolutie en browserondersteuning moeten op echte apparaten worden geaccepteerd. Cloudsync en telefoon-naar-laptop-authenticatie mogen geen voorwaarde zijn voor de offline basiservaring; beschikbare authenticatormethoden moeten vooraf worden getest.

## Deployment en WebAuthn

Elke installatie vereist een expliciet configureerbare publieke URL, WebAuthn RP ID en beperkte lijst toegestane origins. Deze requirements verruimen huidige hostvalidatie niet. De huidige code bevat reeds configuratiepunten en een beperkte WPF-origincontrole; volledige ondersteuning van drie omgevingen is nog niet aangetoond.

Credentials zijn aan hun RP ID gebonden; de server moet de origin controleren. HTTPS en certificaatvertrouwen moeten op bezoekersapparaten werken. Een wijziging van RP ID vereist vooraf een afzonderlijk migratie- en herstelplan, beoordeling van herregistratie, veilige accountrecovery, compatibiliteitstests en behoud van bestaande toegang. Bestaande credentials niet herschrijven of stilzwijgend hergebruiken voor een andere RP ID. Zie de [W3C WebAuthn-specificatie](https://www.w3.org/TR/webauthn-3/).

Onderzoek later een vaste publieke domeinnaam met lokale DNS-resolutie naar de Cybertruck en een vooraf uitgegeven vertrouwd certificaat. Dit is een onderzoeksrichting, geen gekozen ontwerp. Domeineigendom, certificaatketen, sleutelopslag, browservertrouwen, offline geldigheid, juiste systeemtijd en conflicten tussen installaties moeten worden beoordeeld. Lokale DNS alleen creëert geen certificaatvertrouwen. Certificaatvernieuwing vereist internettoegang of een vooraf geregelde procedure; plan vervaldatumbewaking, veilige distributie en acceptatie vóór vertrek. Privésleutels horen buiten Git. Gebruik geen fictieve RP ID als vervanging van een werkende installatieconfiguratie.

## Toekomstig administratorportaal

Een beveiligd administratorportaal moet installatie-instellingen, evenementen, presentatoren, retentiebeleid, integraties, domeinconfiguratie en monitoring/status ondersteunen. Administrators gebruiken een fysieke YubiKey. Het toekomstige ontwerp moet bepalen hoe dit betrouwbaar wordt afgedwongen en welke recoveryprocedure geldt. Een browserkeuze, AAGUID zonder verdere validatie of attestation none bewijst geen fysieke YubiKey. Gebruik de bestaande FIDO2-library, server-side rollen en eventgrenzen, CSRF-/Origin-controles, audit en verse herauthenticatie voor gevoelige wijzigingen.

Deploymentinstellingen omvatten publieke URL, RP ID, origins, listeners/proxytrust, DNS, certificaten en opslaglocaties. Applicatie-instellingen omvatten eventbeleid, presentatievoorkeuren, presentatorrechten en retentie. Kritieke deploymentwijzigingen vereisen aanvullende autorisatie, impactanalyse op bestaande credentials, validatie, expliciete bevestiging, audit, rollback en zo nodig gecontroleerde herstart. Geen onbeperkt instellingenformulier dat deze grenzen omzeilt. De exacte beheer- en recoveryprocedures zijn nog open.

## Optionele synchronisatie

Toekomstige synchronisatie tussen zelfstandige en centrale installaties is optioneel; offline werken blijft zelfstandig mogelijk. Ontwerp later expliciete eigendomsgrenzen, stabiele identifiers, versies, herhaalde overdracht zonder dubbele verwerking en hervatting na verbindingsverlies. Leg conflictregels vast voor offline wijzigingen, verwijderingen en retentie; overschrijf wijzigingen niet ongemerkt.

Tenant- en evenementisolatie, beperkte server-side autorisatie en controleerbare auditlogs gelden aan beide kanten. Synchroniseer uitsluitend noodzakelijke gegevens; geen automatische overdracht van passkeys, keyrings, secrets of bezoekersgegevens zonder vastgesteld doel en gegevensbeleid. Retentie en verwijderverzoeken moeten ook na offline perioden doorwerken. Vereis wederzijds geautoriseerde beveiligde communicatie, sleutelbeheer, replaybescherming en intrekbaarheid. Conflictenbeleid, gegevenscontracten en operationele acceptatie blijven open. Er wordt nu geen synchronisatie geïmplementeerd.

## Latere acceptatie

Test elke omgeving afzonderlijk, inclusief volledig offline opstarten en gebruik, eigen mobiele apparaten en demo-laptops, fysieke administrator-YubiKeys, certificaatverloop, RP-ID-migratie en herstel. Geautomatiseerde regressies vervangen deze acceptatie niet. Geen volgende ontwikkelfase starten zonder afzonderlijke goedkeuring.

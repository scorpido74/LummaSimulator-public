<!-- Bron: LummaSimulator_functionele_requirements_v0.1.docx; inhoud overgenomen zonder nieuwe ontwerpkeuzes. -->

# LummaSimulator

> Aanvullende vastgestelde architectuurbesluiten, 1 november 2026: [geconsolideerd lifecycleontwerp](architecture/phase5-event-lifecycle-design.md), K01–K23, en [gefaseerd integratieplan](architecture/phase5-event-integration-plan.md). Bij verschil met onderstaande eerdere requirements/ontwerpstatus zijn deze expliciete besluiten leidend: DemoEvents-parent; verplichte automatische einddatumsluiting/catch-up; afzonderlijke klokblokkade; hybride Prize-autorisatie zonder impliciete systeembeheer-PII-/claimrechten; geregistreerde beheerders-YubiKey; cryptografisch controleerbare audit; terminale Archived; configureerbare retentieprofielen Demo 7, Workshop 14, Onderwijs 90, audit 90, geanonimiseerde statistieken 365 en backups 14 dagen, met Prize afzonderlijk. Dit zijn doelbesluiten, geen claim van implementatie. Tijdelijke bezoekersauthenticatie en sessie-eindverwijdering worden niet automatisch naar deze metadataretentietermijnen verlengd. Geen automatische bezoekersaccount-/passkeysynchronisatie.

> Functioneel requirementsdocument | besluitregister en acceptatiekaders

Versie 0.1 – 31 oktober 2026  |  Status: concept voor architectuur- en code-impactanalyse

Auteur: Projectteam

## 1. Doel en uitgangspunten

LummaSimulator is een gecontroleerd demonstratieplatform voor phishing, passkeys, sessierisico’s en MDR-detectie. Het bestaat uit een Windows WPF-presentatorapplicatie, een zelfstandig ASP.NET Core Passkey Demo Portal, een Session Exposure Monitor en later een Detection Lab met optionele externe agent. Het platform moet bruikbaar zijn tijdens Cybertruck-demonstraties en in geïsoleerde labs.

- Veilige simulatie: geen echte credentials of browsercookies verzamelen of exfiltreren; alleen fictieve accounts, bestanden en sessieartefacten.
- WebAuthn/passkeys beschermen tegen phishing bij authenticatie, maar vormen geen algemene bescherming tegen malware op een al ingelogd endpoint.
- Het portal werkt onafhankelijk van de Windows-app; de Windows-app werkt ook zonder portal. Portalkoppeling staat standaard uit.
- Werkelijke MDR-detectie en blokkering uitsluitend rapporteren als deze in de testomgeving aantoonbaar zijn vastgesteld.
- Rechten, privacy, tenant-/eventscheiding en server-side autorisatie zijn leidend; publieke schermen tonen alleen geschoonde informatie.

## 2. Bestaande situatie en ontwikkelgrens

Projectlocatie: C:/src/LummaSimulator. Technologie: .NET 10, WPF, ASP.NET Core en SQLite. Volgens de huidige projectstatus zijn databasefase 5.1 (migratie v6) en fase 5.2a (presenter event management) geïmplementeerd en getest; fase 5.2b is nog niet gestart. Deze status moet Codex in de repository verifiëren.

Bestaande functies, waaronder Prize Experience, passkeyregistratie en -login, Session Exposure Monitor, MITRE-export en bestaande verwijderpaden, mogen niet stilzwijgend verdwijnen. Accountverwijdering (onder meer SessionStatusService.GetAsync) moet vooraf op bewaarbeleid worden getoetst.

## 3. Besluitregister – functionele keuzes

### 01  Beheerinterface

Eén gedeelde admin-/presentatorinterface; administrators zien extra menu’s. Autorisatie server-side.

### 02  Portalkoppeling

Lokale én externe portalondersteuning; veilige pairing en standaard uitgeschakeld.

### 03  Eventregistratie

Volledige demo-eventtijdlijn met filters.

### 04  Visualisatie

Visueel aanvalsschema én filterbare eventtijdlijn.

### 05  Sessiestart

Automatisch een nieuwe demosessie; handmatig selecteren/herstarten mogelijk.

### 06  Eventrelatie

Demosessies kunnen optioneel aan een event gekoppeld worden.

### 07  Privacy

Instelbare bewaartermijn; standaard verwijderen bij sessie-einde.

### 08  Resultaten

Resultaten in WPF, beheerportal en geschoonde publieke presentatieweergave.

### 09  Presentatiebediening

Bediening via WPF of portal; per sessie één actieve eigenaar.

### 10  Schermselectie

Handmatig of met één klik presenteren op gekozen monitor.

### 11  Introductie

Interactieve, geanimeerde uitleg van Lumma Stealer.

### 12  Uitlegniveau

Omschakelen tussen eenvoudige en technische uitleg.

### 13  Navigatie

Intro standaard tonen, overslaan mogelijk; tabbladen Intro / Simulator / Resultaten.

### 14  Instellingen

Lokale voorkeuren onthouden; nieuwe sessie bij opstart.

### 15  Configuratie

Centraal beheerde policies naast lokale WPF-voorkeuren.

### 16  Dashboard

Presentator ziet laatste toegankelijke event; administrator alle events.

### 17  Rechten

Presentator beheert eigen en gedelegeerde events.

### 18  Event aanmaken

Snelle aanmaak met laatst gebruikte profiel; geavanceerde opties apart.

### 19  Uitnodigen en bootstrap

Presentatoruitnodiging via kopieerbare link/e-mail; admin verplicht fysieke YubiKey; veilige eerste-adminwizard.

### 20  Vormgeving

Licht/donker thema in Infinigate-huisstijl.

### 21  Impactanalyse

Eerst read-only Codex-impactanalyse, daarna gefaseerde bouw.

### 22  Detection Lab

Log- en veilige endpointactiviteiten onafhankelijk instelbaar; geen echte credentials.

### 23  MDR-resultaten

Handmatige validatie en optioneel uitlezen via MDR-API.

### 24  Scenario’s

Handmatige en geautomatiseerde scenarioreeksen.

### 25a  Scenario-editor

Herbruikbare stappen uit een veilige bibliotheek; geen willekeurige scripts.

### 25b  Sessiedemo

Zowel visuele uitleg als werkende, geïsoleerde demosessie; geen echte cookies stelen.

### 26  Startmethode sessiedemo

OPEN: niet expliciet beantwoord in het eerdere gesprek.

### 27  Authenticatorselectie

OS-afhankelijke suggestie met handmatige keuze Apple / Android / YubiKey.

### 28  Onboarding

Korte visuele uitleg plus optionele interactieve wizard.

### 29  Passkeys

Maximaal vijf credentials per account; onderscheid sync/hardware alleen waar technisch vaststelbaar.

### 30  Phishingdemo

Optionele interactieve demonstratie na passkeyregistratie.

### 31  Phishing per event

Per event automatisch aanbieden of optioneel maken.

### 32  Uitleg phishing

Eenvoudige bezoekersuitslag plus technische toelichting voor presentator.

### 33  WebAuthn-domeinen

Visuele én echte WebAuthn-test op tweede eigen RP-ID/domein.

### 34  Sessierisico

Visuele én optioneel werkende geïsoleerde sessiedemonstratie.

### 35  Tweede browser

Statusweergave plus optionele tweede browser met fictief account zonder nieuwe passkeyauthenticatie.

### 36  Demoacties

Standaard read-only; optioneel fictieve profielwijziging of demobestand.

### 37  Intrekken sessie

Handmatig en automatisch; gesimuleerde en echte respons expliciet onderscheiden.

### 38a  MDR-respons

Gesimuleerd alert en optioneel echte MDR-detectie die uitsluitend demosessie intrekt.

### 38b  Containmentlab

Echte EDR-blokkering/quarantaine/isolatie alleen in expliciet toegestaan lab.

### 39  Lablocatie

Lokaal of op externe Windows-testmachine/VM.

### 40  Agentarchitectuur

Volledige LummaSimulator lokaal; externe lichtgewicht Detection Lab-agent.

### 41  Discovery

Automatische LAN-discovery via mDNS; pairing goedkeuren op testmachine.

### 42  Agentmodus

Handmatige uitvoering of Windows-service.

### 43  Distributie

Portable self-contained executable en installer voor service.

### 44  Agentupdates

Centraal beheer, maar alleen expliciet goedgekeurde updates; ondertekening, rollback, niet tijdens demo.

### 45  Activiteiten

Uitbreidbare bibliotheek met metadata, MITRE-mapping, telemetrie, risico en opschoning.

### 46  Modulekwaliteit

Losse modules met automatische tests en formele goedkeuring vóór beschikbaarstelling.

### 47  MDR-vergelijking

Leveranciersdetail én vergelijkingsmatrix; alleen gemeten resultaten als detectie claimen.

### 48  Rapportage

Dashboard én PDF-rapporten voor engineers, resellers en eindklanten.

### 49  Bewaarbeleid

Gescheiden instelbare bewaartermijnen voor bezoeker-/sessiegegevens en geanonimiseerde MDR-testhistorie.

### 50  Ontwikkelmodel

Eén architectuur, gefaseerde modulaire ontwikkeling en acceptatietests per fase.

## 4. Niet-functionele eisen en veiligheidsgrenzen

### Identiteit en toegang

- Admin login vereist fysieke YubiKey; veilige bootstrap eerste administrator.
- Event- en sessierechten afdwingen op server, niet alleen via zichtbaarheid van menu’s.
- Gedelegeerde toegang is expliciet, herroepbaar en auditeerbaar.

### Privacy en gegevens

- Geen echte credentials, sessiecookies, browserprofielen of vertrouwelijke klantdata in demoartefacten.
- Willekeurige bezoekersaliassen; uitleg over anonimiteit en opschoning op portal.
- Gegevensverwijdering omvat database, bestanden, back-ups en afhankelijke records volgens vastgesteld beleid.

### Agent en modules

- Authenticatie en versleuteling voor agentcommunicatie; pairing op testmachine.
- Geen generieke remote shell, willekeurige code-uitvoering of EDR-omzeiling.
- Veilige activiteiten hebben beperkte rechten, stopcondities, cleanup en auditeerbare uitvoering.
- Ondertekende, geteste modules; expliciete goedkeuring en rollback voor updates.

### Rapportagekwaliteit

- Onderscheid niet getest / geen alert ontvangen / gedetecteerd / geblokkeerd / geïsoleerd.
- Leg productversie, configuratie, tijdstip, agent- en moduleversie vast.
- Rapporten benoemen simulatiecontext en beperkingen; geen algemene vendor-ranglijst.

## 5. Rapportages en historie

### Technisch

Activiteiten, telemetrie, ATT&CK-mapping, detectie- en responstijden, configuratie, bewijsstatus.

### Reseller

MDR-mogelijkheden in deze testopstelling, verschillen, operationele en dienstverleningskansen.

### Eindklant

Begrijpelijke aanvalsketen, passkeywaarde en -grenzen, sessierisico en MDR-respons.

Historische MDR-resultaten mogen zonder bezoekersidentiteit worden bewaard binnen een apart beleid. Verwijderen van een bezoekerssessie mag geen persoonsgegevens achterlaten in de technische historie.

## 6. Fasen en oplevercriteria

### 1. Architectuur en impactanalyse

Repository-inventarisatie, datamodel, trust boundaries, afhankelijkheden, risico’s, migratieplan; geen codewijzigingen.

### 2. Beheerportal

Navigatie, dashboard, rollen, eventdelegatie, bootstrap, thema’s; autorisatietests.

### 3. Passkey Demo Portal

Platformkeuze, wizard, maximaal vijf credentials, phishing op eigen tweede domein; WebAuthn-regressie.

### 4. WPF-presentatorervaring

Intro, aanvalsschema, eventtijdlijn, monitorselectie, resultaatweergave; offline bruikbaar.

### 5. Session Exposure Lab

Demo-only sessieartefact, tweede browser, expliciete revoke, koppeling standaard uit; geen echte cookies.

### 6. Detection Lab

Veilige activiteiten, telemetrie, scenario-editor, modulegoedkeuring en audit.

### 7. Externe agent

mDNS-discovery, lokale toestemming, portable/service, beveiligde pairing, gecontroleerde updates.

### 8. MDR en rapportage

Handmatige/API-detectieverificatie, leveranciersmatrix, drie PDF-sjablonen, historie en retentie.

Voor elke fase: code review, geautomatiseerde tests, regressie van bestaande functies, expliciete veiligheidsacceptatie en rollbackplan.

## 7. Open punten en te valideren aannames

- Vraag 26 over de precieze startmethode van de sessiedemonstratie is niet beantwoord.
- De exacte eisen voor Prize Experience zijn niet volledig opgenomen in dit gesprek; eerst bestaande code en eerdere specificaties inventariseren.
- Voor echte phishingdemonstratie is een tweede beheerd domein met eigen RP-ID, TLS en juiste origins vereist; domeinconfiguratie nog te valideren.
- LastPass-loginoptie, herhaalde YubiKey-login, Windows-herkenningsmelding en subdomeincertificaat zijn eerder als open issues gemeld; actuele status verifiëren.
- Multi-presentator-login en Classroom Mode met PIV zijn toekomstwensen, niet automatisch onderdeel van MVP.
- Retentieperioden, toegestane MDR API’s, testmachines, testcriteria en beheer van moduleondertekening moeten nog concreet worden vastgesteld.
- Codex moet de werkelijke database-/codebasis controleren; de hierboven genoemde implementatiestatus is nog niet opnieuw geverifieerd.

## 8. Opdracht voor de eerste Codex-analyse

Voer uitsluitend een read-only analyse uit van de repository. Maak een inventaris van componenten, databasen, routes, rollen, bestaande tests, bestaande functionaliteit en afhankelijkheden. Vergelijk de implementatie met dit document; label eisen als aanwezig, gedeeltelijk aanwezig, ontbrekend of onzeker. Beschrijf concrete wijzigingen per fase, inclusief beveiligingsrisico’s, migraties, regressierisico’s, acceptatietests en uitvoeringsvolgorde. Wijzig geen code of configuratie zonder afzonderlijke opdracht.

Voorgestelde analyse-output: docs/phase5-ux-modernization-impact-analysis.md. Het requirementsdocument blijft de functionele bron, maar onopgeloste keuzes en feitelijke codeafwijkingen moeten zichtbaar blijven.

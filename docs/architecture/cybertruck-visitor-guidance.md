# Cybertruck — zelfstandige bezoekersflow en duurzame lotingsdeelname

12 november 2026. Dit beschrijft de huidige wijziging, inclusief de aansluitende opdracht over lotingsstatus. Oudere rapporten beschrijven hun toenmalige implementatie; de eerdere tekst dat actieve, onafgeronde inschrijvingen meetellen is hiermee achterhaald.

## Gedrag

- /Demo heeft één gedeelde, links uitgelijnde contentkolom en uitleg met maximaal circa 65 tekens leesbreedte. Vooraf staan de handelingsuitleg en alias/privacy-uitleg in afzonderlijke alinea's.
- Stap 1 toont Passkey aanmaken als primaire actie. Alleen de YubiKey-route geeft de instructie Kies Beveiligingssleutel. De browserkeuze wordt niet technisch afgedwongen. Na serverbevestigde registratie blijft Registratie voltooid een afzonderlijke status; stap 2 wordt zichtbaar en krijgt focus, maar begint alleen na een klik. De extra registratieknop is secundair. Bij een niet te onderscheiden browseronderbreking volgt een neutrale fout met een concrete herhaalinstructie voor de huidige stap.
- Alleen server-side bevestigde authenticatie toont Welkom [alias] en het groene blok. Registratie/inlogblokken en de sessiekaart verdwijnen dan uit beeld; Demonstratie beëindigen blijft secundair beschikbaar. De toelichting legt de websitebinding van passkeys en de beperking tot phishing bij het inloggen uit.
- De oefening start uitsluitend via Bekijk wat er na het inloggen kan gebeuren. Deze laatste opdracht vervangt de tussentijds gevraagde prijsclaim-knoptekst. De bestaande blauwe overlay, fictieve inhoud en browserstappen blijven behouden. Na de laatste stap volgt binnen de overlay een nabespreking en Terug naar mijn demo. Deze knop, sluiten en Escape herstellen het groene blok en de focus. Herhalen gebruikt dezelfde geldige sessie en schrijft geen tweede voltooiing of inschrijving.
- De rondekaart staat direct onder de titel, zonder herhaalde alias. De server levert de berichtsleutel en of een trekking aantoonbaar heeft plaatsgevonden. Alleen een ronde met opgeslagen winnaars krijgt de oranje kaart. Winnaar, verliezer, uitgesloten deelnemer, prijzen uit, geen rondeprijs en nog open hebben afzonderlijke teksten. Een open ronde toont alleen wachten op de uitslag bij een geldige afgeronde inschrijving. Onbekende historische individuele deelname blijft onbekend; een ontbrekende prijs wordt niet als bewijs van deelname gebruikt.
- De echte YubiKey-uitslaglink blijft afzonderlijk van de browseroefening. Naam/origin/RP-ID, autorisatie, WebAuthn-verificatie, cookies en bestaande opschonings-/bewaartermijnen zijn niet veranderd.

## Lotingsdeelname en opslag

De oude CloseAsync-query selecteerde alleen actieve PrizeParticipants met nog bestaande accounts. Normale verwijdering verplaatst de minimale alias/inschrijving naar PrizeRoundHistory, waardoor afgeronde deelnemers wegvielen. De nieuwe selectie combineert live ronde-inschrijvingen met deze bestaande historie en vereist een voltooide demo. De bestaande RoundDemoCompleted-audit of de eigen VisitorExercises-voltooiing is daarvoor het bewijs. Vóór normale accountverwijdering wordt de voltooiing zo nodig als auditfeit behouden. Een expliciet geannuleerde inschrijving blijft bij overdracht uitgesloten via RoundEntryWithdrawn. Credentials en sessietoegang worden niet naar de historie gekopieerd; de normale accountopschoning blijft ongewijzigd.

Nieuwe trekkingen leggen RoundEnteredDraw/RoundDidNotEnterDraw en bekende uitsluitingsredenen vast in het bestaande evenementgebonden auditmodel. De winnaar wordt in dezelfde transactie in de toepasselijke live- of historieregel opgeslagen. Ook prijzen uit en ontbrekende rondevoorraad worden als rondefeit vastgelegd. Dit voegt geen tabellen, kolommen, schemafingerprints of migratieversies toe. De bestaande serialiserende transactie, opgeslagen-resultaatcontrole, voorraadberekening en evenementretentie blijven gelden. Afgesloten rondes keren terug vóór nieuwe selectie of feitenregistratie: bestaande uitslagen worden niet herschreven of opnieuw getrokken.

## Gewijzigde bestanden en ingangen

| Bestand | Regel | Verandering |
|---|---:|---|
| LummaSimulator.Web/Pages/Demo.cshtml | 12 | Gedeelde kolom, rondekaart, uitleg en afzonderlijke stappen |
| LummaSimulator.Web/wwwroot/js/passkeys.js | 31 | Stapstatus, nadruk, herhaalinstructie en geldige aanmeldstatus |
| LummaSimulator.Web/wwwroot/css/site.css | 138 | Uitlijning, leesbreedte, primaire acties en uitslagkaart |
| LummaSimulator.Web/Pages/Shared/_ClickFixExercise.cshtml | 14 | Nabespreking en terugknop binnen overlay |
| LummaSimulator.Web/wwwroot/js/clickfix.js | 24 | Terugkeren en eenmalige voltooiing/replay |
| LummaSimulator.Web/wwwroot/js/participation-status.js | 3 | Servergestuurde uitslagkaart en verversen |
| LummaSimulator.Web/Pages/Participation.cshtml.cs | 11 | Eigen, geautoriseerde rondepresentatie bij polling |
| LummaSimulator.Web/Pages/Participation.cshtml | 5 | Individuele bezoekerstekst |
| LummaSimulator.Web/Pages/ClickFixComplete.cshtml.cs | 20 | Dezelfde opgeslagen bezoekeruitslag |
| LummaSimulator.Web/wwwroot/js/exercise-result.js | 11 | Toegestane nieuwe uitslagsleutels |
| LummaSimulator.Web/Models/PrizeModels.cs | 46 | Gescheiden opgeslagen deelnamestatus en bezoekerpresentatie |
| LummaSimulator.Web/Services/Prizes/PrizeRoundsService.cs | 190 | Selectie, momentopname van deelname en opgeslagen uitslag |
| LummaSimulator.Web/Services/Prizes/PrizeRoundsService.cs | 289 | Rondekaart uit server-side ronde-/deelnemergegevens |
| LummaSimulator.Web/Services/Prizes/PrizeRoundHistoryMigration.cs | 47 | Alleen runtime-overdracht aangepast; schema ongewijzigd |
| LummaSimulator.Web/Pages/Shared/_EventRoundProgress.cshtml | 7 | Individuele status en uitsluitend bekende reden |
| LummaSimulator.Web/wwwroot/js/event-round-progress.js | 19 | Dezelfde status en reden na verversen |
| LummaSimulator.Web/Resources/Localization/UiText.resx | 1287 | NL-teksten, ook sleutels vanaf 1341 |
| LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx | 1287 | NL-teksten |
| LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx | 1287 | EN-teksten |
| tests/LummaSimulator.Web.Tests/PrizeRoundsChecks.cs | 71 | Gewijzigde voltooiingsvoorwaarde en echte uitslagteksten |
| tests/LummaSimulator.Web.Tests/RoundAccountEndChecks.cs | 159 | Drie aliassen met echte synthetische cleanup en trekking |
| tests/LummaSimulator.Web.Tests/clickfix-browser.mjs | 85 | Zelfstandige stap 2 en expliciet terugkeren na nabespreking |
| tests/WebAuthn.Ux.Tests/passkeys.test.cjs | 438 | Acties per toestand en afbreken/herhalen |
| tests/WebAuthn.Ux.Tests/clickfix.test.cjs | 44 | Nabespreking, terugknop, Escape, sluiten en herhalen |
| tests/WebAuthn.Ux.Tests/phase65e.test.cjs | 146 | SQL-selectie, uitslagstatus en NL/EN-rondekaart |
| docs/architecture/cybertruck-visitor-guidance.md | 1 | Deze implementatie- en verificatienotitie |

## Werkelijk uitgevoerd

- AGENTS.md, git status, requirements en relevante Prize-/flowdocumentatie gelezen. De bestaande dirty working tree behouden; niets gestaged of gecommit. Geen actieve portal of database geopend, gestart of gewijzigd.
- De expliciete Node-regressieset: 116 tests, 114 geslaagd, 2 overgeslagen, 0 mislukt. Log: artifacts/visitor-guidance-checks/node-current.log. De eerste runs signaleerden verouderde verwachtingen over de dubbele alias, knoptekst en actieve deelname; deze verwachtingen zijn aan de nieuwe eisen aangepast en aangevuld met afzonderlijke toestandstests.
- Werkelijke productie-SQL met in-memory SQLite uitgevoerd voor drie fictieve aliassen: een opgeruimde afgeronde inschrijving, een actieve afgeronde inschrijving en een onafgeronde inschrijving. Alleen de eerste twee tellen mee; expliciete intrekking blijft uitgesloten.
- Werkelijke JavaScript met nagebootste DOM/serverresponses uitgevoerd voor registratie versus aanmelden, afbreken/herhalen, de oefening inclusief nabespreking/terugkeer/replay, en NL/EN-uitslagkaarten voor winnaar, verliezer, niet meegeloot, geen prijs, prijzen uit en onbekende historie. De pollingweergave blijft bij herhaald ophalen gelijk. Dit is geen echte browser- of server-trekkingstest.
- JavaScript-syntax, C#-syntaxparse, resource-XML/unieke sleutels en git diff --check geslaagd. Syntaxparse is geen compilatie of Razor-build.
- Start-LummaDev.bat verify met locked restore daadwerkelijk uitgevoerd. Definitieve bronkopie: artifacts/dev-start-105b0a830d8b48fa840efa827f6e0f9c/source. Restore faalt op toegang geweigerd tot %USERPROFILE%/AppData/Roaming/NuGet/NuGet.Config. Geen configuratie-, permissie- of locatie-omweg toegepast.
- De .NET-regressieorchestrator met expliciete geïsoleerde LUMMA_TEST_WEB_DLL en de Web.Tests-runner daarna daadwerkelijk aangeroepen met --no-restore. Beide stoppen vóór tests met NETSDK1004 omdat project.assets.json door de mislukte restore ontbreekt. Logs: dotnet-regression.log en dotnet-web.log in artifacts/visitor-guidance-checks.

## Open acceptatie

De volledige .NET-test met software-authenticators, normale sessieopruiming, echte trekking, herladen en stabiele prijsuitkomst is geschreven maar nog niet uitgevoerd. Ook de echte browsertests (registreren, afbreken, opnieuw proberen, inloggen, minimaal tien seconden blijvend succes, overlay, terugkeer en herhalen) zijn niet gestart: de vereiste geïsoleerde portal-DLL kon niet worden gebouwd. Na herstel van de NuGet-configuratietoegang via de normale beheerroute moeten verify, de regressieorchestrator en Web.Tests opnieuw draaien. Controleer daarna NL/EN op desktop en mobiel, inclusief de rondekaarten en de werkelijke Windows-/telefoonpasskeydialoog. Geen fysieke telefoon-, authenticator- of DPI-acceptatie is geclaimd.

# Fase 5 - Prize Experience: functionele specificatie

> Aanvullende architectuurbesluiten 1 november 2026: [lifecycleontwerp](architecture/phase5-event-lifecycle-design.md), K11/K15/K18–K23, is leidend waar het oude autorisatie-/auditcontract afwijkt. Gekoppelde PrizeEvents volgen eventcapabilities; ongekoppelde houden gewone bestaande beheerrechten. Systeembeheer geeft in beide paden geen automatische toegang tot PII of claims. Parent- en Prize-lifecycle blijven gescheiden; het bestaande afzonderlijke Prize-bewaarbeleid en de oorspronkelijke PurgeAt-bovengrens blijven gelden. Dit is doelarchitectuur; de hieronder beschreven 5.2a-bron en huidige implementatiestatus zijn niet gewijzigd.

Status: definitieve eisenbasis en technisch ontwerp voor implementatie; **opslagfundament 5.1 en beschermd evenementbeheer 5.2a gereed; publieke Prize-functionaliteit niet geïmplementeerd**.
Datum: 30 oktober 2026. Baseline: huidige broncode na fase 4 en gebruikersnaamloze presentatorlogin.
Gerelateerd: [roadmap](LummaSimulator-v0.5.md), [architectuur](phase5-prize-experience-architecture.md), [werkpakketten](phase5-prize-experience-implementation.md).

## Leeswijzer en besluitstatus

**Eis (E)** is een vastgestelde opdrachtvoorwaarde. **Ontwerp (O)** is de gekozen technische/functionele uitwerking om die eis uitvoerbaar te maken; dit is geen claim over bestaande code. **Open (T)** vereist nog een technische keuze of operationele acceptatie. Extra mogelijkheden zijn uitsluitend **Voorstel (V)** en vallen buiten de verplichte implementatie. De eisen hieronder zijn definitief; ontwerpkeuzes zijn het implementatie-uitgangspunt. Open punten staan expliciet onderaan.

Bij de oorspronkelijke specificatie zijn uitsluitend bronnen gelezen. Geen applicatie gestart, live database geopend, migratie uitgevoerd, package geïnstalleerd of hardwaretest gedaan. Documenten over fase 2, 3 en 4 staan als opeenvolgende secties in de roadmap; oudere checkpoints beschrijven niet overal het huidige gedrag.

## F01 - Optionele uitbreiding en evenementen

E: standaard blijft Prize Experience uit; bestaande demo's, accounts, credentials en verificatie blijven bruikbaar zonder deelname of trekking. Aanmelden is geen deelname. Inschakeling gebeurt expliciet per evenement. Een nieuwe demo, tellers resetten, account verwijderen of prijzen uit/aan zetten creëert geen nieuwe prijzenpot.

O: de bestaande statistiekevenementen blijven intact. Nieuwe PrizeEvents zijn zelfstandige prijsevenementen; meerdere mogen gelijktijdig open zijn. De bezoekerslink bevat altijd het specifieke PrizeEventId. Een bestaande VisitorNumber is niet het deelnemersnummer: een account kan aan verschillende evenementen deelnemen en krijgt per evenement een eigen nummer. Er is geen globale actieve prijsgroep of globale actieve prijzenpot.

O: Prize Experience is een aparte dashboardsectie. Nieuwe prijsevenementen starten alleen via de expliciete snelle-startflow. Bestaande evenementen/policies worden niet stilzwijgend geactiveerd of omgezet. Bestaande EventPrizePolicies met CHECK(Enabled=0) blijven historische voorbereiding, niet de nieuwe bron van waarheid. Globale PrizeEnabledByDefault blijft false. Een per-evenement PrizeEnabled bepaalt beschikbaarheid; uitschakelen stopt deelname/nieuwe trekkingen, maar bewaart geldige claims, voorraad en historie. In-/uitschakelen is geen afsluiten.

## F02 - Profielen en snelle start

E: profielen zijn kopieerbare sjablonen. Het laatst succesvol gebruikte profiel is installatiebreed standaard geselecteerd; bij eerste gebruik Cybertruck. Profielwijzigingen wijzigen geen bestaande evenementen. Een configuratie kan expliciet als nieuw profiel worden opgeslagen.

O: selectie wordt opgeslagen bij succesvol Start evenement, in dezelfde transactie. Alleen selecteren of een mislukte start verandert de voorkeur niet. Bij concurrente starts is de laatst gecommitte start leidend. Verwijdering van een geselecteerd eigen profiel laat Cybertruck als terugval gelden. Meegeleverde profielen hebben vaste identifiers en worden niet bij iedere startup overschreven; wijzigingen aan meegeleverde waarden worden opgeslagen als eigen profiel.

Snelle start: Nieuw evenement → laatst gebruikt profiel → naam invullen → basisinstellingen controleren → Start evenement. Daarna dashboard, bezoekerslink, QR en knop Presentatiescherm openen. QR bevat dezelfde evenementlink, geen beheerrechten. De knop opent een browservenster via een gebruikersklik; de presentator verplaatst het naar scherm twee. Automatische OS-schermplaatsing valt buiten scope.

| Basisinstellingen | Validatie |
| --- | --- |
| Naam | 1-100 tekens, platte tekst |
| Profiel | Bestaand profiel; volledige configuratiesnapshot wordt gekopieerd |
| Groepsgrootte | Positief geheel getal; ontwerpgrens 1-200 |
| Winnaars per groep | 1 tot groepsgrootte; opnieuw toetsen aan werkelijke geldige deelnemers bij trekking |
| Prijsselectie | Vast prijstype of uniform willekeurig beschikbaar prijstype |
| Startcode-instelling | Openbaar: verplicht; besloten: optioneel |

Geavanceerd: openbaar/besloten, directe winst/groepstrekking, winstkans en maximum, bekendmaking tegelijk/afzonderlijk, animatie 5/10/15 seconden, reactietermijn, claimperiode na sluiten, bewaartermijn, groepscodebeleid, automatische groepswisseling, optionele afhandelingsvelden en beveiligingslimieten. Openbaar/besloten staat ook zichtbaar in de samenvatting; ongeldige combinaties blokkeren Start.

| Profiel | Ingevulde waarden |
| --- | --- |
| Cybertruck | Onderstaande exacte defaults |
| Direct winnen | O: besloten; optionele startcode; winstkans 1/10; maximum 10 eerste winmomenten; overige toepasselijke Cybertruckwaarden |
| Evenement met loting | O: besloten; groep 15; 1 winnaar; handmatig; overige Cybertruckwaarden |
| Aangepast | O: kopie van Cybertruck als veilige invulbasis, volledig aanpasbaar vóór start |

O: een nieuw profiel bevat geen fictieve echte voorraad. Cybertruck gebruikt een configureerbaar prijstype met displaynaam Cybertruck-demoprijs, onbeperkte voorraad als sjabloonwaarde. De startweergave vraagt controle van prijsnaam/voorraad; een werkelijk voertuig wordt hiermee niet toegezegd. Direct winnen met vaste kans is geen gegarandeerde uitgifte van tien prijzen.

### Cybertruck: vastgestelde defaults

| Instelling | Waarde |
| --- | --- |
| Prize Experience | Ingeschakeld na expliciet Start evenement |
| Groepsgrootte / winnaars | 15 / 1 |
| Trekking | Handmatig |
| Startcode / groepscode | Niet verplicht |
| Groepsindeling / volgende groep | Automatisch / handmatig |
| Prijsselectie | Vaste prijs |
| Animatieduur / bekendmaking | 10 seconden / na animatie |
| Reactietermijn winnaar | 2 minuten vanaf eigen bekendmaking |
| Niet reagerende winnaar | Alleen handmatig laten vervallen |
| Persoonsgegevens bij directe uitreiking | Geen verplichte velden |
| Bewaartermijn | 30 dagen na afsluiten |
| Claimperiode na afsluiten | 24 uur, begrensd door bewaartermijn |
| Presentatiescherm | Beschikbaar |

O: Cybertruck is standaard **besloten** zodat codevrije deelname verenigbaar is met E: openbare evenementen vereisen startcodes. Publiek maken dwingt startcodes af. Bij circa 100 deelnemers ontstaan circa zeven rondes, laatste ronde eventueel kleiner; een presentator sluit die expliciet.

## F03 - Deelname, identiteit en startcodes

E: geldige passkey-authenticatie → expliciet Doe mee aan de winactie → eventuele code → transactionele deelname. Geen persoonsgegevens. UNIQUE(evenement, account) blijft bestaan, ook na annulering; annulering is onomkeerbaar voor dat evenement. Een code wordt alleen met de geslaagde deelname definitief verbruikt, nooit teruggegeven.

O: servercontrole vereist een geldige bezoekerssessie plus succesvolle assertion in die sessie, niet alleen een registratie of een ooit opgehoogde Successes-teller. Bestaande recent geverifieerde sessie kan worden gemigreerd met gecontroleerde LastAuthenticatedAt/sessionbinding; anders eerst opnieuw authenticeren. Browserheaders en deelnemersnummers zijn geen eigendomsbewijs.

O: startcodes zijn evenementgebonden, eenmalig, vervallend en servergegenereerd. Vooraf controleren is hoogstens advies; verbruik vindt plaats met accountcontrole, capaciteit en deelname in één commit. Verlopen, ingetrokken, verkeerd evenement, gebruikt of onbekend geeft dezelfde publieksfout. Geen code in URL, QR-bezoekerslink of log. Groeps-QR is een andere, herbruikbare groepscapability en vervangt geen startcode.

E: een startcode bewijst niet dat één natuurlijk persoon slechts één account gebruikt. Rate limiting en beperkte code-uitgifte verkleinen misbruik, maar lossen dit niet op. Publieke uitrol vereist gecontroleerde uitgifte; captcha of identiteitscontrole zijn geen vastgestelde uitbreiding.

O: annuleren mag tot de groep voor het eerst trekt, of bij Direct winnen vóór besluitvorming; directe besluitvorming gebeurt meteen bij registratie en laat daarom geen tussenliggende annuleringsfase toe. Na eerste trekking is de deelnemerslijst bevroren. Een privacyverzoek blijft mogelijk via afzonderlijke verwerking, maar wijzigt nooit reeds vastgelegde uitslagen. Afgehandelde of vervallen winnaar blijft als winnaar aangemerkt en kan niet opnieuw winnen.

## F04 - Groepsrondes

E: automatische toewijzing aan de open ronde óf verplichte groepscode/QR per evenement. Server dwingt capaciteit af, ook bij gelijktijdige aanmeldingen. Sluiten vóór trekking; heropenen mag zolang nog geen trekking bestaat.

O: maximaal één open inschrijfronde per evenement, maar oudere rondes kunnen nog claims hebben. Vol is een toelatingsgrens en sluit bij handmatige wisseling niet automatisch af. Annulering vóór bevriezen maakt een plek vrij; verbruikt geen nieuw deelnemersnummer. Bij automatische wisseling sluit de server de volle ronde en maakt maximaal één volgende ronde in dezelfde transactie, mits evenement open. Bij handmatige wisseling krijgt de volgende bezoeker Groep vol; codes blijven ongebruikt.

O: gesloten ronde blijft administratief dezelfde ronde bij heropenen. Eerste trekking bewaart een onveranderlijke geldige deelnemerssnapshot. Hertrekking gebruikt diezelfde snapshot minus geannuleerden vóór bevriezen en alle accounts die ooit wonnen. Annuleringen achteraf herschrijven de snapshot niet.

Automatische groepswisseling wacht niet op claims: oude claims blijven aan hun oorspronkelijke ronde en reservering gekoppeld; nieuwe rondes delen alleen evenementvoorraad. Geen voorraad kan een trekking blokkeren, maar niet een reeds geldige claim ongeldig maken. Automatisch wisselen betekent geen automatische trekking. Cybertruck wisselt uitsluitend handmatig.

## F05 - Winst, prijzen en trekking

E: direct winnen met vaste kans en maximum, of handmatige groepstrekking; geen deelnemer wint tweemaal binnen een evenement. CSPRNG op de server. Alle groepswinnaars worden vóór animatie bepaald en vastgelegd, zonder dubbele selecties. Aantal winnaars moet passen bij kandidaten én reserveerbare prijzen. Onvoldoende deelnemers/voorraad geeft afwijzing zonder deeluitslag.

O: Direct winnen gebruikt kans p=a/b met gehele 0≤a≤b, b>0. Eén uniforme RandomNumberGenerator.GetInt32(b)<a-beslissing per deelname. Maximum M telt **ooit toegekende eerste winmomenten**, ook als een winnaar later vervalt. M=0 geeft geen winsten; null betekent geen beleidsmaximum, voorraad blijft begrenzen. Kanskansbesluit, teller, deelname en prijsreservering worden samen gecommit; retry leest hetzelfde besluit. Geen beschikbare voorraad betekent geregistreerde deelname met resultaat Niet gewonnen (reden Voorraad op in beheer), geen later automatisch winmoment.

E/O: maximum is een bovengrens, geen garantie. Voor een gegarandeerd aantal K is een bekend eindig aantal N geldige deelnames nodig. V: een aparte vooraf vastgelegde slotvariant kan K winposities zonder terugleggen uit N slots kiezen; alleen bij N daadwerkelijke geldige deelnames en voldoende voorraad worden K winsten gegarandeerd. Deze variant is géén vereiste voor 5.3 en mag niet onder het label vaste onafhankelijke kans worden aangeboden. Een maximum-only profiel kan p=1 gebruiken; dit beloont de eerste M geldige deelnames, zonder garantie dat M deelnemers komen.

O: groepstrekking kiest uniform zonder terugleggen uit stabiel gesorteerde geldige snapshot via gedeeltelijke Fisher-Yates en RandomNumberGenerator.GetInt32(i,n). Voor elke geselecteerde winnaar wordt een prijstype gekozen: vast, of uniform uit beschikbare typen. Hoeveelheid telt niet als gewicht. Reserveer en herbereken beschikbare typen per winnaar; onbeperkt is null, nul is uitgeput.

O: geen draw bij open inschrijving, uitgeschakelde prizes of gesloten evenement. Resultaat, algoritmeversie, snapshotreferentie, geselecteerde nummers, prijstype, volgorde en tijdstippen staan na commit vast. Geen client-seed of voorspelbare System.Random. Audit maakt uitvoering controleerbaar, maar levert geen publiek verifieerbare cryptografische loterijgarantie.

Bij handmatig vervallen komt de reservering vrij. Uitgegeven voorraad komt nooit terug. Voorraadverlaging onder gereserveerd+uitgegeven wordt geweigerd. Teruggeven van voorraad verwijdert geen historische winst; een hertrekking kan alleen een andere geldige kandidaat aanwijzen. Hertrekking vervangt expliciet één of meer vervallen awards; geen vrije extra trekking boven het ingestelde aantal winnaars.

## F06 - Bekendmaking en herstel

E: duur 5, 10 of 15 seconden. Tegelijk: alle winnaars op hetzelfde RevealAt. Afzonderlijk: winnaar i op StartedAt+(i+1)×duur; elke winnaar heeft eigen animatie. Alle awards bestaan al vóór de eerste animatie. De reactietermijn begint per winnaar op diens eigen RevealAt.

O: server-UTC is autoriteit; client berekent animatiepositie uit ServerNow en AnimatesFrom/RevealAt. Animatie is decoratief en beïnvloedt uitslag niet. Bij herladen/andere presentator/ander scherm: snapshot ophalen, dezelfde DrawId en tijdlijn volgen. Verstreken momenten tonen meteen uitslag; nooit automatisch opnieuw trekken.

E/O: vóór RevealAt geven ook bezoeker-API, exports, dashboard-DTO en SignalR geen toekomstige winnaar, claimcode of winstatus prijs. Het dashboard mag tonen dat de trekking vastgelegd/gaande is, niet wie nog bekendgemaakt moet worden. Bekendmaken gebeurt ook wanneer geen browser is verbonden; leesqueries berekenen verstreken momenten op servertijd. Na crash blijft de planning bestaan.

## F07 - Claims en uitgifte

E: persoonlijke QR en leesbare code verwijzen naar één claim. Maximaal één uitgifte; uitsluitend bevoegde presentator kan afhandelen. De bezoeker kan na browser sluiten met dezelfde accountpasskey terugkeren. Meerdere accountpasskeys geven toegang tot dezelfde claims. Presentator kan eigenaarschap nooit wijzigen.

O: claim blijft Pending tot ingewisseld, handmatig vervallen of definitieve deadline. Twee minuten is een **reactietermijn**, geen automatische invalidering. Daarna verschijnt de knop Winnaar laten vervallen. Een bevoegde uitgifte mag nog zolang de claim geldig is en geen vervalcommit is gewonnen. Geen automatische hertrekking. Vervallen claimtoken wordt direct ongeldig; oude QR werkt nooit voor een vervanger.

O: hertrekking alleen voor groepstrekking. Bij Direct winnen mag een niet reagerende winnaar vervallen en voorraad vrijgeven, maar komt geen extra kans voor dezelfde deelnemer en geen automatische vervanging. Een volgend geldig account kan volgens resterend maximum winnen.

O: claim-QR bevat een fragmenttoken op een vaste eigen HTTPS-claimroute. JavaScript haalt het fragment weg met history.replaceState en post het na presentatorlogin via CSRF-beveiligde API; geen automatische inwisseling bij GET/scannen. Leesbare code wordt in dezelfde invoer gepost. De pagina toont een bevestiging met prijs en anoniem nummer; uitgifte volgt pas na expliciete bevestiging. Camera-scanner in de webapp is geen vereiste (huidig camera=() beleid blijft); externe scanner/telefooncamera kan de URL openen.

O: behouden accounts krijgen een afzonderlijke herstellogin met discoverable assertion, zonder bestaande bezoekercookie. Alleen bezoekerscredentials worden opgezocht, volledige Fido2-controles blijven verplicht. Bij succes nieuwe beperkte sessie, oude sessie vervangen. Geen login met claimcode of deelnemersnummer. Demonstratie beëindigen beëindigt voor bewaarde accounts de sessie, niet hun account/passkeys; dit afwijkende gedrag wordt vooraf zichtbaar uitgelegd. Voor niet-deelnemende accounts blijft het oude verwijdergedrag gelden.

## F08 - Gegevens en bewaarbeleid

E: vóór winst geen persoonsgegevens. Na RevealAt configureerbare voornaam, achternaam, e-mail, bedrijf, eventueel adres; bij directe fysieke uitreiking allemaal optioneel. Geen automatische marketing. Beheer toont afhandelingsgegevens alleen op de specifieke claim, nooit op presentatiescherm.

O: elk veld krijgt Hidden/Optional/Required, doel en maximumlengte; adres als gestructureerde velden. Nieuwe verplichte velden mogen bestaande claims niet achteraf blokkeren; afhandelingsschema wordt per award gekopieerd. Versleutelde opslag, afzonderlijke rechtencontrole, geen ruwe requestbodies of persoonsgegevens in audit. Toegang door Presenter en Administrator is voor Prize Experience gelijk; exports met noodzakelijke persoonsgegevens vereisen expliciete selectie, doel en audit.

O: evenement Open → Closed zet ClosedAt en PurgeAt=ClosedAt+RetentionDays (1-90, default 30) en ClosedClaimsUntil=min(ClosedAt+configured interval,PurgeAt). Claim geldig tot die grens; reeds vervallen/uitgegeven claims herleven niet. Retention wordt niet met LastSeen verlengd. Open evenementen hebben nog geen afloop; zichtbaar beheer signaleert langdurig open evenementen, er wordt geen niet-gevraagde automatische afsluiting toegevoegd.

O: heropenen vóór PurgeAt houdt het oorspronkelijke PurgeAt als harde bovengrens; een volgende afsluiting verlengt die niet stilzwijgend. Uitgifte-deadline van nog Pending claims kan na heropenen opnieuw het open-evenementbeleid volgen, altijd vóór PurgeAt. Al definitief verlopen claims blijven verlopen. Geen nieuwe ronde/trekking als de geplande bekendmaking of minimum reactietermijn voorbij PurgeAt zou komen. Op PurgeAt stopt toegang direct, ook als cleanup later draait.

O: bewaarbeleid verwijdert evenementdeelnames, snapshots, codes/hashes, claims, PII, prijsmutaties, exports op server, idempotentie, outbox en eventgebonden audit. Afgewezen cleanup wegens fout geeft beheerwaarschuwing en retry; geen actieve claim kan een nog latere einddatum hebben. Terminale claims blijven tot evenementretentie voor uitsluiting en rapportage; reactietermijn overschrijden geeft geen status Expired.

O: alleen accounts die expliciet deelnemen worden tegen idle/delete beschermd, zolang minstens één evenementregistratie moet blijven bestaan. Ook geannuleerde en niet-winnende deelnames behouden accountbinding tot PurgeAt om herinschrijving/meermaals winnen tegen te gaan. Bij meerdere evenementen geldt de laatste noodzakelijke bewaareinddatum; een open evenement houdt de binding vast. Na laatste binding keert account terug naar idlebeleid en volgt opschoning indien verlopen. Credentials worden nooit gekopieerd; bezoekersaccounts die niet deelnemen blijven maximaal 29 minuten inactief.

E/O: wettelijke administratie, indien werkelijk vereist, is een afzonderlijke vastgestelde verwerking met eigen grondslag, minimale velden, toegang, bewaartermijn en beheerprocedure. Geen algemene wettelijke termijn verzinnen. Tijdelijke evenementdata worden niet preventief permanent gearchiveerd. Wettelijke inrichting valt als operationeel besluit vóór gebruik met zulke prijzen; het ontbreken ervan blokkeert de gewone anonieme fysieke demo niet. Bescherm backups en stel een verwijderrotatie vast; restore herhaalt verlopen-retentieopschoning vóór verkeer. Exports op een presentatorapparaat vragen eigen beheer en verdwijnen niet door servercleanup.

## F09 - Dashboard, presentatie en rapportage

E: iedere geauthenticeerde presentator mag een evenement aanmaken en heeft toegang tot eigen en expliciet gedeelde evenementen. De eigenaar en geaccepteerde eventleden mogen bestaande actieve presentatoren voor hun event uitnodigen; alleen een administrator mag een nieuw presenteraccount uitnodigen. Acceptatie met de eigen passkey blijft verplicht en een pending invite geeft geen toegang.

E: gedeelde presentatoren behouden de bestaande eventacties, behalve eigenaarbeleid. Alleen de exacte primaire eventeigenaar mag het Prize-beleid/configuratie wijzigen; administratorrechten geven hiervoor geen bypass wanneer de administrator niet de eventeigenaar is. Een administrator die zelf eigenaar is mag wijzigen. Andere administrator-only account-, audit- en installatieacties blijven ongewijzigd. Deze eis verfijnt de eerdere algemene gedeelde-beheerformulering.

E: Prize-evenement hernoemen blijft toegestaan volgens de bestaande eventbeheerscope. Eventisolatie is geen afzonderlijk per-presentator tenantmodel: het beperkt toegang tot eigen en gedeelde events, maar voorkomt niet dat iedere presenter events aanmaakt.

E: presentatie toont uitsluitend evenementnaam, actieve ronde, deelnemersaantal, animatie en al bekendgemaakte anonieme winnaarsnummers. Geen prijsclaim, PII, interne account-ID of beheeractie. Read-only capability is evenementgebonden, intrekbaar en geeft geen beheer-API-toegang. Meerdere schermen volgen dezelfde serverstatus.

E/O: rapportage per evenement uit consistente snapshot: totaal geregistreerd (inclusief geannuleerd), geldig, geannuleerd, groepen, trekkingen (inclusief hertrekkingen), ooit gewonnen accounts, Pending, Redeemed, Expired, Forfeited, uitgegeven per type en resterende voorraad (null = Onbeperkt). Publicatiegrens geldt ook voor rapporten. CSV en echte .xlsx, anoniem standaard. Persoonsgegevens alleen na expliciete noodzakelijke selectie. Pending over reactietermijn is niet Expired. Elke prijs heeft beschikbare=total-reserved-issued; onbeperkte voorraad toont aantallen reserveringen/uitgiftes maar geen fictief restgetal.

## F10 - Tegenstrijdigheden en vastgelegde oplossingen

| Spanning | Besluit |
| --- | --- |
| Standaard uit versus Cybertruck aan | Module wordt nooit impliciet actief; expliciete evenementstart kopieert enabled-profiel |
| Cybertruck geen code versus openbaar verplicht | Cybertruck besloten; openbaar dwingt code af |
| Tijdelijke accounts versus claim terugvinden | Bewaar deelnameaccounts; voeg herstellogin toe; bestaande cryptografische controles blijven |
| Bestaand één actief evenement versus meerdere | Zelfstandige PrizeEvents naast bestaande statistieken; nooit bestaande index verwijderen |
| Maximaal één winst versus vervallen/hertrekken | Iedere ooit gewonnen deelnemer uitgesloten, ook na verval |
| Twee minuten versus 24 uur na sluiten | Reactietermijn geeft handmatige bevoegdheid; definitieve claimgrens apart |
| Annulering versus vaste deelnemerslijst | Annuleren vóór eerste trekking; daarna onveranderlijke snapshot |
| Voorraad vrijgeven versus maximaal winmomenten | Voorraad terug, historische winst/instantlimiet niet terug |
| Bewaartermijn versus heropening | Oorspronkelijke PurgeAt blijft bovengrens; niets herleeft na definitief verloop |
| Alle winnaars vooraf versus privacy vóór bekendmaking | Opslag vooraf, DTO's en publicatie server-side gefilterd |

## Open technische beslissingen en voorstellen

T1: lokale QR-library en SignalR JavaScript-client exact vastzetten, licenties/offline distributie toetsen in 5.4. Geen package nu installeren.
T2: minimale .xlsx via BCL ZIP/XML versus een goedgekeurde library; ontwerp kiest BCL voor beperkte rapportages, interoperabiliteit met Excel is acceptatiepoort 5.6.
T3: productiebackuprotatie, sleutelbescherming en eventuele wettelijke administratie concreet vaststellen vóór productiegebruik.
T4: voorgestelde limieten (groepsgrootte, rate limits) op echte netwerkopstelling accepteren.
T5: fysieke passkeys, meerdere computers, mobiele layout en 100-bezoekersbelasting valideren; dit document levert geen uitvoeringsbewijs.
V: gegarandeerde slotvariant, externe audit/WORM, multi-host database en automatische tweede-schermplaatsing vallen buiten fase 5.

## F11 - Status na werkpakket 5.1

Opslagfundament gereed; de functionele Prize Experience is nog niet beschikbaar. De definitieve opslag-/idempotentie-/presentatiecontracten staan in architectuur A10. Alleen interne disabled Draft-opslag is testbaar; geen publieke evenementstart of deelname. Vaste migratieversie 6, vier profielseeds en installatiebrede standaardprofielopslag zijn gerealiseerd. De UI-selectie en opslaan van laatst werkelijk gestart profiel volgen in 5.2.

Voor 5.2 geldt expliciet de retentie-acceptatiepoort voor alle verwijderpaden, inclusief SessionStatusService.GetAsync. Geen account- of sessielifecyclewijziging in 5.1. Uitvoeringsbewijs, herstelbaseline en resterende risico's staan in het implementatieplan.

## F12 - Geleverde beheerfunctionaliteit 5.2a

Actuele status op 30 oktober 2026: uitsluitend evenementbeheer en profielen zijn geïmplementeerd. F11 en de oorspronkelijke analyses zijn historische checkpoints. De overige functionele eisen blijven de doelarchitectuur.

- Admin en presenter beheren dezelfde installatiebrede evenementen via het bestaande passkeybeveiligde dashboard. Dit is gedeeld beheer, geen eigenaar- of tenantmodel.
- Overzicht met 25 evenementen per pagina; nieuw evenement met automatisch laatst werkelijk gestart profiel, eerste keer Cybertruck. De profielkiezer laadt een sjabloon vóór het invoeren van de naam; alleen toepassen of een concept opslaan verandert de voorkeur niet.
- Naam, groepsgrootte, winnaars, prijskeuze/-naam/-voorraad en startcodeverplichting staan bij de basis. Overige configuratie staat onder Geavanceerd. Lege voorraad betekent onbeperkt. De trekkingmethode blijft de bestaande vaste waarde Manual; uitvoering ontbreekt nog.
- Basis en geavanceerde configuratie volgen bestaande Nederlandse/Engelse resourcecatalogi. Aanvullende grenzen: reactietermijn maximaal 86400 seconden; claimperiode na sluiten maximaal 90 dagen, in de UI in hele minuten. De latere effectieve claimgrens blijft het minimum van de claimgrenzen en PurgeAt. Er worden geen persoonsgegevens gevraagd of opgeslagen door deze pagina's.
- Start evenement maakt atomair een Open-beheerevenement met configuratiesnapshot en één voorbereid prijstype. Het veld PrizeEnabled blijft **false**, ook bij een enabled sjabloon. Er ontstaan geen bezoekerslink, QR-code, presentatie, groepen, deelnemers, claims of voorraadmutaties.
- Concept opslaan maakt Draft. Starten vanuit Draft is mogelijk. Alleen een eerste succesvolle start werkt LastProfileId installatiebreed bij; gelijke retries, profielopslaan en heropening doen dat niet.
- Draft/Open-instellingen zijn wijzigbaar zolang geen groep, deelnemer, trekking of gereserveerde/uitgegeven voorraad bestaat. Daarna bevriest de configuratie. Closed laat uitsluitend naamswijziging en opslaan als nieuw profiel toe, naast heropenen. Purging of verstreken bewaartermijn is alleen-lezen.
- Sluiten: Open -> Closed, ClosedAt wordt gezet en eerste PurgeAt is ClosedAt + RetentionDays. Heropenen vereist bestaande gegevens en now < PurgeAt. Eerdere gegevens blijven staan; heropenen en opnieuw sluiten verlengen de oorspronkelijke bovengrens niet.
- Expliciet opslaan als nieuw profiel kopieert de configuratie, wijzigt geen bestaand profiel en verandert de evenementinstellingen niet. Deze beheeractie verhoogt wel de evenementrevisie voor conflictcontrole en audit.

Iedere mutatie heeft een servergevalideerde actor, idempotentiesleutel en transactionele registratie. Wijzigingen na creatie vereisen de gelezen revisie; een andere presentator moet na conflict verversen. Route-evenement en verborgen formulierdoel moeten overeenkomen.

**Verplichte poort voor 5.2b:** vóór deelnemersregistratie alle accountverwijderpaden beschermen, inclusief DemoService.OwnedCoreAsync, CleanupCoreAsync (startup/dashboard/Create), EndOwnAsync, EndAsPresenterAsync, CreateAsync(previousToken) en SessionStatusService.GetAsync. Deze aanpassingen zijn bewust niet in 5.2a gedaan. Publieke deelname blijft gesloten totdat de volledige retentie-/herstel-/claimketen is gerealiseerd en geaccepteerd.

# Event Edition — voorgesteld ClickFix-actiecontract voor sprint 3

> Sprint 3: zie [de beperkte implementatie](../event-edition-sprint3-report.md). Hieronder blijft het oorspronkelijke voorstel. De geïmplementeerde slice gebruikt één geautoriseerde keuze per uniek gekoppelde run (volgnummer 1), lokale HTTPS-polling met afzonderlijke operatorsessiegebonden leescode en beschermd manifest buiten SQLite. Geen nieuwe eventengine of MDR-actie.

Status: ontwerp, 1 november 2026. Er is **geen communicatie geïmplementeerd**. Dit kleine contract verandert de bevroren v0.5-architectuur niet en verleent geen toestemming voor een migratie, deployment, externe pairing of verruiming van de bestaande loopbackgrenzen.

## Betekenis en scope

Een actie betekent uitsluitend: de browser meldt dat een bezoeker een keuze in de veilige ClickFix-oefening maakte. Dit bewijst geen shelluitvoering, malware, compromittering, sessiediefstal of MDR-detectie. Een aangepaste browser kan zo'n keuze nabootsen. WPF mag daarop later een informatieve voortgangsmelding tonen, maar geen LIVE-activiteit, test, proces of MDR-respons automatisch starten.

Voorgestelde minimale payload (uitsluitend fictief voorbeeld):

```json
{
  "demoRunId": "11111111-1111-4111-8111-111111111111",
  "actionType": "ClickFixFollowSimulated",
  "sequence": 1,
  "occurredAtUtc": "2026-11-01T12:00:00Z"
}
```

| Veld | Voorgesteld contract |
|---|---|
| `demoRunId` | Bestaande **portal**-DemoRunId, canonieke GUID; de server bepaalt deze uit de geautoriseerde bezoekerssessie, nooit uit een vrij browserveld. Geen account-ID, cookie of credential. |
| `actionType` | Gesloten enum: `ClickFixFollowSimulated` of `ClickFixStopSelected`. Geen vrije tekst, instructie, URL, script, payload of persoonsgegevens. |
| `sequence` | Positief geheel getal binnen een vastgelegde bovengrens; server kent dit atomair toe, strikt oplopend per portalrun. Een UI-reset/herladen zet dit nummer niet terug. |
| `occurredAtUtc` | Server-UTC van acceptatie, RFC 3339 met `Z`; geen vertrouwde browsertijd. Tijd ordent niet de verwerking: daarvoor dient `sequence`. |

De WPF-run heeft nu een eigen DemoRunId. De twee identifiers mogen niet stilzwijgend gelijk worden gemaakt. Voor latere aflevering is een expliciete, geautoriseerde koppeling tussen de geselecteerde portalrun en de WPF-presentatie nodig. De bestaande monitorreferentie is geen bewijs van ownership en geen toestemming voor een nieuw kanaal. Geen nieuwe DemoEvent-/DemoSession-opslag nodig voor het beschrijven van dit contract.

## Dubbele verwerking en opnieuw proberen

- Ontvanger gebruikt `(demoRunId, sequence)` als idempotentiesleutel. Een identiek bericht dat al verwerkt is, bevestigen zonder tweede effect. Dezelfde sleutel met andere inhoud weigeren en geschoond auditen.
- Producer bewaart voor een retry dezelfde sleutel, inhoud en servertijd. Een terugkerende browseraanvraag moet ook aan dezelfde bronactie worden gebonden: een server-uitgegeven eenmalige actie-/pogingreferentie of equivalente idempotentiecontrole is daarvoor nodig. Alleen opnieuw een volgnummer uitdelen voorkomt geen duplicaten aan de bron.
- Een expliciet opnieuw begonnen oefening mag een nieuwe keuze opleveren met een hoger nummer. Onderscheid dit van een netwerkretry; opnieuw laden is geen automatisch nieuw actiebericht.
- Ontvanger verwerkt in volgorde. Bij een gat geen latere actie als voltooid presenteren: vraag begrensd opnieuw op of meld ontbrekende voortgang. Geen onbeperkte queue. Late/afgesloten runs niet automatisch aan een nieuwe presentatie koppelen.
- Betrouwbare deduplicatie over een herstart vereist bewaarde ontvangstpositie en produceradministratie. Zonder die opslag alleen tijdelijke deduplicatie claimen, geen exactly-once-garantie. Opslagkeuze en eventuele databasewijziging vereisen een afzonderlijke beoordeling en voorafgaande goedkeuring.

## Noodzakelijke beveiligingscontroles voor een latere implementatie

1. Hergebruik bestaande server-side bezoekersownership en geslaagde WebAuthn-aanmelding; controleer geldigheid opnieuw bij iedere actie. Alleen registratie is onvoldoende. Verlopen, beëindigde of vervangen bezoekerssessies weigeren. Presenter-/eventrechten controleren vóór koppelen, uitlezen of herkoppelen.
2. Browseraanlevering via een muterende aanvraag met bestaande antiforgery- en exacte Origin-controle; geen mutatie via GET. Sta uitsluitend de twee vaste acties en geldige toestandsovergangen toe. Begrens grootte, aantallen, retries en levensduur; rate limiting en uniforme foutmeldingen.
3. Server maakt de uitgaande payload zelf. Strikte DTO/enum, onbekende velden weigeren; geen cookie, token, private key, accountnaam, IP-adres of willekeurige browsertekst in payload of logs. Een run-ID is een referentie, geen authenticatiemiddel.
4. Afzonderlijke, beperkte autorisatie voor portal–WPF, geldige HTTPS en exacte doel-/originbegrenzing. Geen bezoekerscookie delen, secret in querystring of verruiming van RP ID/certificaatvalidatie. De huidige monitorgrant of monitor-key niet automatisch voor nieuwe bevoegdheden hergebruiken.
5. Koppeling scoped op run en presentatie; voorkom uitlezen van andere bezoekers/runs. Beëindigen, reset naar een nieuwe bezoeker, intrekking en verlopen moeten de oude koppeling ongeldig maken. Geen automatische koppeling op naam, tijd of nabijheid.
6. Weergave encodeert vaste labels, geen uitvoerbare inhoud. Gestructureerde minimale audit van accepteren/weigeren/koppelen; retentie en doelbinding expliciet vastleggen vóór gegevens worden bewaard. Geen migratie naar de uitgebreide auditarchitectuur in deze sprint.
7. WPF blijft zonder verbinding bruikbaar. Status maakt verbindingsverlies en onbekende voortgang zichtbaar; handmatige terugval blijft expliciet presentatorbediening, met onderscheid tussen gemelde keuze en handmatige bevestiging. MDR-tests blijven een afzonderlijke bewuste operatorhandeling.

## Nog te besluiten vóór implementatie

- Aflevermechanisme (bijvoorbeeld beperkt ophalen), exacte endpointcontracten, autorisatie en expliciete runpairing. Er is nog geen nieuwe netwerkroute toegestaan; een algemene remote agent/API valt buiten scope.
- Bron-idempotentie, opslag van ontvangstpositie en herstel bij procesuitval; welke beperkte betrouwbaarheid zonder schemawijziging acceptabel is. Geen databasewijziging zonder voorafgaande goedkeuring.
- Concrete berichtlimieten, retryvenster, bewaartermijn en afhandeling van ontbrekende volgnummers/afgesloten runs.
- UX en bevoegdheden van de handmatige terugval en runselectie. Geen impliciete event-capability-engine of lifecycleintegratie.

## Acceptatie voor sprint 3

Test twee onafhankelijke bezoekers en WPF-runs; registratie zonder login, verlopen/revoked sessies, verkeerde Origin/CSRF, onbekende enum/velden, dubbele aanvraag, dubbele aflevering, inhoudsconflict, volgnummergat, herstart en netwerkverlies. Toon aan dat reset/herladen geen ongewenste dubbele melding veroorzaakt en dat geen actie automatisch een endpoint-/MDR-test start. Behoud de bestaande volledige regressies en zelfstandige WPF-werking.

Sprint 2 voegt uitsluitend de lokale browseroefening en dit ontwerp toe. Er is geen endpoint, queue, DTO-runtime, nieuwe tabel of WPF-polling voor dit contract gebouwd.

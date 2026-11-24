# ClickFix: herladen en replay — 13 november 2026

Werkboom gecontroleerd met `git rev-parse --show-toplevel`: `C:/src/LummaSimulator`. Lokaal werk is behouden. Alleen verificatiecode en dit verslag zijn gewijzigd; geen actieve portal of bestaande demodatabase gebruikt.

## Oorzaak

De oorspronkelijke assertion in `HttpChecks.cs` was exact:

`TestProgram.Check((await visitor.GetStringAsync("/SignedIn")).Contains("value=\"0\""), "ClickFix reload begins with fresh browser-only progress");`

Dit controleerde alleen of de HTML ergens `value="0"` bevatte. Die beginwaarde hoorde bij de bewust verwijderde zichtbare voortgangsbalk. De huidige `_ClickFixExercise.cshtml` bevat geen voortgangsbalk. De assertion voerde ook geen herlaadactie in een browser tijdens een lopende oefening uit. Zijn falen toont daarom geen vastgelopen lokale oefenstap aan.

De huidige Razor-beginstatus is: oefening verborgen, Uitvoeren verborgen, nabespreking verborgen, OK uitgeschakeld en Start beschikbaar. `clickfix.js` initialiseert `active=false` en `runState=null`, gebruikt alleen lokale variabelen/timers en start pas na een klik en succesvolle BeginExercise-respons. `pagehide` annuleert de oude tijdlijn. De browser bewaart geen oefenstap in localStorage/sessionStorage. Een duurzaam opgeslagen CompletedAt is afzonderlijk en autoriseert alleen visuele replay; het hoort niet gewist te worden door herladen.

## Wijzigingen

- `HttpChecks.cs`: afzonderlijke assertions voor de huidige Razor-beginstatus. Geen afhankelijkheid van een oude progresswaarde.
- `clickfix-browser.mjs`: start de echte oefening, wacht tot Uitvoeren zichtbaar is en roept dan `Page.reload` aan. Controleert na herladen alle beginstatussen, behoud van aanmelding, 10,5 seconden zonder automatische hervatting en opnieuw beginnen bij de melding na een expliciete klik. Onderbreken mag geen Complete-/Choice-POST opleveren.
- De browserfixture telt Complete en Choice afzonderlijk en controleert bij replay dat beide aantallen gelijk blijven. Alleen vaste handlernamen worden geteld; querystrings, cookies, tokens of sessie-ID's worden niet gelogd.
- `PrizeRoundsChecks.cs`: afzonderlijke controles dat replay geen extra Prize-deelnemer, ronde-inschrijving of voltooiingsaudit toevoegt en CompletedAt behoudt. Alle gegevens horen bij de bestaande nieuwe synthetische testdatabase.
- `clickfix.test.cjs`: een onafhankelijke JavaScript-/DOM-harnesstest onderbreekt Uitvoeren, controleert timerannulering en maakt een nieuwe documentcontext. De nieuwe context blijft idle en begint alleen expliciet opnieuw. Dit is geen echte browserreload.

Geen aanwijzing in deze bronanalyse dat de productie-UI een oude lokale stap herstelt; daarom is geen productiecode gewijzigd. De echte browseruitvoering blijft nodig om dit dynamisch te bevestigen.

## Nieuwe kopie en werkelijke resultaten

Nieuwe actuele bronkopie: `artifacts/dev-start-5685964518c04b2b83b23c36f1820b9b/source`. Deze bevat ook de bestaande lokale/ongetrackte bronnen en benodigde packaging-bestanden. Testportal-DLL en runtimepaden zijn expliciet op deze eigen kopie ingesteld.

| Controle | Resultaat |
| --- | --- |
| Locked restore | Exit 1: geen leesrecht op `%USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config`; `restore.log` |
| Release-build zonder restore | Exit 1: NETSDK1004, ontbrekende project.assets.json; `build.log` |
| Eerst rechtstreekse webtestrunner | Exit 1 vóór tests: NETSDK1004 voor Web.Tests/obj/project.assets.json; `web-direct.log` |
| Echte portalbrowserfixture | Niet bereikt; geen geslaagde browseracceptatie geclaimd |
| Daarna volledige regressie | Niet gestart omdat de rechtstreekse webtestrunner niet slaagde; `regression-not-run.txt` |
| Volledige expliciete Node-set van de orchestrator, onafhankelijk uitgevoerd | **119 tests: 117 geslaagd, 2 bestaande PE-iconchecks overgeslagen, 0 fouten**; `node.log` |
| Gerichte ClickFix-Node-tests | **9 geslaagd, 0 fouten** |
| Browserfixture-JavaScript-syntax en git diff --check | Geslaagd |

Geen NuGet.Config-, ACL- of identiteitsomweg gebruikt. De nieuwe C#-databasecontroles zijn niet uitgevoerd. Er is dus nog geen gemeten bewijs van de echte browserreload of de volledige HTTP-/databaseketen. De gewone Windows PowerShell-opdrachten bij de oplevering bouwen en testen dezezelfde kopie; de volledige regressie volgt uitsluitend na een geslaagde rechtstreekse webtestrunner.

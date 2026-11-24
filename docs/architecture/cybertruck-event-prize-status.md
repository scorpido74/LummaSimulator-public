# Cybertruck — prijsstatus bij evenement en ronde

Implementatie (12 november 2026): het rondeoverzicht toont de opgeslagen evenementgebonden prijsselectie en gekoppelde prijsnamen, ook als prijzen uitstaan. De bron is PrizeEvents + de bij dat evenement behorende PrizeTypes; een profiel wordt niet opnieuw toegepast. De status staat buiten Instellingen en details. Bekijk prijsinstellingen verwijst naar het bestaande formulier van exact dat evenement; autorisatie en vergrendeling blijven daar gelden.

Beschikbaarheid voor open rondes gebruikt dezelfde AvailableTypes-berekening als CloseAsync (actieve selectie, voorraad, reserveringen, uitreikingen en reeds opgeslagen winnaars). Een ontbrekende koppeling verschilt van uitgeputte voorraad. Afgesloten rondes tonen de opgeslagen uitkomst, niet een nieuwe voorraadbeoordeling. Er zijn geen schema-, lotings-, origin- of RP-ID-wijzigingen. Bij prijzen uit heten de tellers actieve inschrijvingen en de rijstatus Geen loting. Bestaande winnaars blijven zichtbaar. De algemene dashboardwaarschuwing over onbeschikbare Prize Experience is verwijderd.

## Uitgevoerde verificatie

- Nieuwe verificatiekopie: artifacts/dev-start-a46f189511e1401a8ff63421017fa21a/source.
- Start-LummaDev.bat verify: locked restore geprobeerd, geblokkeerd door toegang geweigerd tot %USERPROFILE%/AppData/Roaming/NuGet/NuGet.Config. Geen build of appstart geslaagd. Geen omweg of permissiewijziging toegepast.
- Bestaande expliciete Node-regressieset: 112 tests, 110 geslaagd, 2 overgeslagen, 0 mislukt. Gerichte phase65e-set: 10 geslaagd. Omvat in-memory SQLite-controles met werkelijke prijsnaamselectie voor verschillende evenementen, geen koppeling en lege voorraad, en polling zonder instellingen te vervangen.
- Een eerdere wildcard-Node-run omvatte ook monitor-origin.browser.test.cjs: 109 geslaagd, 2 overgeslagen, 1 mislukt doordat de geïsoleerde headless browser niet normaal startte (exit 2147483651). Dit is geen geslaagde browseracceptatie.
- .NET-regressieorchestrator met expliciete geïsoleerde LUMMA_TEST_WEB_DLL en Web.Tests-runner daadwerkelijk aangeroepen met --no-restore: beide geblokkeerd door ontbrekende project.assets.json na de restorefout.
- C# syntaxparse van service en gewijzigde webtests, JavaScript-syntax, resource-XML/unieke sleutels en git diff --check geslaagd. Dit vervangt geen compilatie/Razor-build.
- .NET-tests uitgebreid voor prijzen uit met bewaarde prijsnaam, prijzen aan, uitgeputte voorraad, ontbrekende vaste-prijskoppeling en terugkeer naar een afgesloten ronde met opgeslagen uitslag. Nog niet uitvoerbaar door de buildblokkade.

Logbestanden: artifacts/prize-status-checks/. Er zijn geen actieve databases of processen gewijzigd.

## Nog open

NuGet-configuratietoegang herstellen via de normale machinebeheerprocedure en verify + .NET-regressies opnieuw uitvoeren. Daarna echte browsertest met twee evenementen, rondewisselingen en herladen (NL/EN), lege voorraad, geen prijs en afgesloten ronde. De interactieve deelnemerslijst, sluitknop en instellingenlink zijn nog niet fysiek in de browser geverifieerd. Geen scenario is als volledig geslaagde runtimeacceptatie aangemerkt.

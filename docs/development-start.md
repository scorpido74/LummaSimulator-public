# Ontwikkeldemo met een dubbelklik

De WPF-app gebruikt bij een nieuwe build het ingebedde [LummaSimulator-icoon en de branding-/taalinstellingen](branding-and-languages.md). Controleer het WPF-venster; de portal-/PowerShell-console heeft een eigen procesicoon. De starter zet geen icooncache of Windows-instellingen om.

Dubbelklik in `C:/src/LummaSimulator` op **Start-LummaDev.bat**, onder het normale Windows-account dat de bestaande ontwikkelinstallatie gebruikt. Geen administratorrechten of PowerShell-opdrachten nodig. Vereist: Git, .NET 10 SDK inclusief Windows Desktop-runtime, bestaande NuGet-toegang en een al geldig en vertrouwd localhost-HTTPS-certificaat. Scripts moeten volgens het bestaande Windows-beleid mogen draaien; er wordt geen execution policy omzeild.

De starter kopieert actuele Git-zichtbare bronbestanden (inclusief gewijzigde en niet-genegeerde nieuwe bronbestanden) naar een nieuwe `artifacts/dev-start-<id>/source`-map. Hij voert locked restore en een Release-build van de solution uit, inclusief tests en transitieve LocalTestServer; hij voert geen tests of endpointactiviteiten uit. Bestaande buildoutput en Event Edition-pakketten blijven intact. Genegeerde bronbestanden worden niet meegenomen.

Daarna start de portal met het bestaande `https`-launchprofile (`Development`, `https://localhost:7243`) in een zichtbare console. Pas na een geslaagde HTTPS-controle start WPF en opent de standaardbrowser de bezoekerspagina **https://localhost:7243**. Het beheerdashboard is **https://localhost:7243/Presenter**. De Session Exposure Monitor blijft optioneel; de starter geeft geen grants uit en schakelt geen LIVE/MDR-tests in.

De bestaande appsettings, development user-secrets en procesomgeving blijven van toepassing. De starter maakt of vertrouwt geen certificaat, wijzigt geen RP-ID/origins en kiest geen nieuwe database. Met de ingecheckte standaardconfiguratie is de portalopslag `%LOCALAPPDATA%\LummaSimulator\PasskeyPortal\portal.db`, met de bestaande keyring ernaast. Absolute opslagoverrides blijven geldig. Relatieve paden in ingecheckte instellingen of bekende runtime-omgevingsvariabelen worden geweigerd omdat ze vanuit de geïsoleerde build naar andere opslag zouden kunnen verwijzen. Gebruik ook in user-secrets uitsluitend al beoordeelde absolute opslagpaden; die configuratie wordt niet door de launcher uitgelezen of getoond. Een dubbelklik erft de Windows-omgeving, niet de tijdelijke variabelen uit een andere terminal.

**Normaal portalgedrag blijft gelden:** startup kan bestaande migraties en idlecleanup uitvoeren. De launcher voegt geen reset, migratiecommando, accountverwijdering of gegevenskopie toe. Gebruik hem alleen wanneer het starten van de actuele code op de geconfigureerde ontwikkelopslag gewenst is. Dit is geen modus die bestaande data read-only opent en geen vervanging van een vereiste upgrade-/backupbeoordeling.

## Stoppen

1. Dubbelklik op **Stop-LummaDev.bat** voor normale afsluiting van de door deze starter geregistreerde processen. WPF wacht op zijn bestaande opruiming; de portal ontvangt een lokaal gebruikersgebonden stopsignaal. Geen geforceerde beëindiging. Bij oude procesregistraties zonder stoproute: gebruik de eigen vensters.
2. Als alternatief: sluit het WPF-venster normaal; de bestaande WPF-lifecycle sluit een eventueel eigen sidecar af.
3. Druk dan **Ctrl+C in de portalconsole**. Wacht op afsluiting en sluit die console (zo nodig Enter).
4. Het startvenster sluit wanneer beide vensters/processen klaar zijn. Sluit de browsertab desgewenst zelf.

Laat het startvenster tijdens gebruik open: een mutex blokkeert een tweede dubbelklik. Daarnaast weigert de starter een bestaande LummaSimulator-app/portal of bezette poort 7243; hij neemt geen onbekende processen over en stopt niets geforceerd. Een mislukte start kan de portalconsole open laten voor diagnose: sluit die zelf voordat je opnieuw start. Gebruik deze starter niet tegelijk onder verschillende Windows-accounts.

## Fouten en controles

Bij restore/buildfouten blijven de foutmelding en bronkopie beschikbaar; er worden geen apps gestart. Een TLS-/startupfout staat in de portalconsole; bij ontbrekend vertrouwen wordt geen browserwaarschuwing omzeild. Laat bestaande certificaat-/NuGet-configuratie beoordelen, zonder accounts of certificaten te resetten. Er worden geen automatische herstel- of opruimacties uitgevoerd. Elke start bewaart een nieuwe buildmap; verwijderen daarvan is geen onderdeel van de starter.

De bestaande `packaging/EventEdition/Demo.ps1` en `.cmd`-starters blijven voor het zelfstandige gepubliceerde pakket met eigen installatie-ID, opslag en `https://localhost` op poort 443. `packaging/Build-EventEdition.ps1` blijft de afzonderlijke pakketbouwer. Deze ontwikkelstarter verandert die contracten niet en bouwt geen release-ZIP.

Verificatie zonder applicatiestart: `verification/Test-LummaDevLauncher.ps1` test preflight met synthetische processen/poorten en parseert het script. De bestaande `verification/Test-EventEditionPackaging.ps1` controleert het ongewijzigde pakketcontract. Werkelijke build-, browser-, TLS- en stopacceptatie blijft een afzonderlijke operationele controle; de analyse heeft geen bestaande portal gestart.

Monitorintegratie: configureer via de administratorpagina en de WPF-instellingen volgens [fase 6.4](phase6-4-monitor-integration.md). Sleutels worden niet in de bronkopie of buildoutput opgeslagen.

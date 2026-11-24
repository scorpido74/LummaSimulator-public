# Prize-bezoekerstoegang en bezoekersorigin — 11 november 2026

## Bevestigd in code, niet bewezen op de actieve database van Test Remco

`PresenterDenied` vertaalt naar 'De aanvraag is geweigerd of verlopen…'. Bij een schone bezoeker controleerde de eerste GET alleen geldige evenementen, status en groepsinformatie. Pas na de keuze-POST maakte Index een demo aan en riep BindVisitorAsync aan. Die vereist een geldige/live evenementkoppeling, exact één PresenterEventScope met een actieve eigenaar, een nog ongebruikte demo en een ingeschakelde route. Vooral de actieve-eigenaarcontrole ontbrak vóór de keuzekaarten. Bij NULL OwnerId, ontbrekende scope of inactieve eigenaar kan beheer voor een administrator beschikbaar zijn, terwijl de anonieme POST wordt geweigerd. De eerste GET geeft dan niet deze generieke fout; de keuze-POST kan dat wel doen.

De precieze huidige eigenaar/status van Test Remco is niet gelezen. Dit is dus een bevestigde inconsistentie in de code en een synthetisch gereproduceerde SQL-conditie, geen claim dat Test Remco aantoonbaar ownerloos is. Er is geen actieve database geopend, gewijzigd of automatisch aan een eigenaar toegewezen.

De beheerwaarschuwing PrizeManagementOnly was op overzicht, nieuw en detail onvoorwaardelijke historische tekst uit de beheerfase. Er bestaat hier geen bijbehorende globale deelname-uit-schakelaar. Deelnemen staat praktisch uit bij alle vier routes uit; daarnaast blijven status, groepsmodus, actieve eigenaar en rondecapaciteit afzonderlijke voorwaarden. PrizeEnabled bepaalt prijzen, niet de beschikbaarheid van passkeydeelname.

## Herstel

- Vanaf beta.4 delen de Prize-detailpagina en Evenementbeheer hetzelfde formulier voor aanmeldopties. De instelling blijft per evenementtype en evenement-ID bewaard en volgt zowel de expliciete link als de ingestelde standaardlink. Experience en Prize vereisen bij opslaan minstens één ingeschakelde methode. Bestaande nul-instellingen behouden hun werking totdat de beheerder een methode kiest. Voor evenementen zonder eigen keuze blijven alle vier methoden ingeschakeld.
- Het opslaan van de aanmeldopties gebruikt de bestaande beheerrechten, antiforgery en exacte Origin-controle. Het wijzigen van de portalbrede standaard blijft onder de bestaande recente passkeycontrole vallen. Aanmeldopties wijzigen geen ronde-inschrijving, voltooiing of prijsstatus.

- CanAcceptVisitorAsync gebruikt voor de openbare voorcontrole exact dezelfde actieve-eigenaar-SQL als BindVisitorAsync. De transactionele bindcontrole blijft ongewijzigd van kracht.
- GET toont bij ontbrekende actieve koppeling een duidelijke, niet-persoonlijke melding vóór keuzekaarten. Directe POST controleert deze toestand en nul routes vóór CreateAsync. Bestaande sessies blijven volgens hun bestaande hervatregels behandeld. Volle/gesloten rondes en routecontrole blijven behouden.
- Beheer toont de actuele deelnamebeschikbaarheid en benoemt de voorwaarden. Overzicht/nieuw geven geen onterechte globale blokkademelding meer.
- VisitorUrl was op de Prize-detailpagina nog een relatief pad, terwijl de QR-service al de geconfigureerde origin gebruikte. De centrale WebAuthnSettings.VisitorLink wordt nu voor QR, klikbare link en fallbacklink gebruikt. De kopieerknop kopieert diezelfde URL. Beheer-Host/localhost bepaalt de bezoekersorigin niet.
- Optioneel WebAuthn:VisitorBaseUrl kiest een al toegestane HTTPS-origin met padprefix. Een afwijkende origin/RP-ID, HTTP, credentials, query of fragment in deze basisinstelling wordt geweigerd. De evenementquery wordt door de bestaande vaste-linkbuilder toegevoegd. Zonder deze nieuwe optionele instelling blijven bestaande oorsprong en request-PathBase behouden. De lokale waarschuwing blijft zichtbaar wanneer de uiteindelijke URL localhost is.

## Dev-start en pakketstart

- Normale dev-start gebruikt het https-launchprofiel met https://localhost:7243 en Development. De portal leest appsettings, bestaande user-secrets en omgeving. WebAuthn:Origins, WebAuthn:RpId en Experience:Hostname komen uit die bestaande configuratie; een bezoekerslink alleen veranderen maakt geen nieuwe netwerklistener of certificaat. Geen dev-startprofiel, origin of RP-ID automatisch gewijzigd.
- De pakketstart zonder PortalOrigin in bestaande event-edition.local.json blijft https://localhost met RP-ID localhost. Bij een expliciet nieuw ingerichte installatie gebruikt Demo.ps1 -Action Setup -PortalOrigin https://<eigen-dns-naam> de vastgelegde origin en hostnaam; een nieuwe origin bij bestaande instellingen wordt geweigerd. De pakketlauncher verwijdert geërfde WebAuthn-omgeving en stelt RP-ID/origin bewust vanuit die installatie-instelling in. Een optionele VisitorBaseUrl moet daarom in de eigen portalconfiguratie staan en bij die bestaande origin passen; verander geen actieve installatie hiervoor zonder een afzonderlijk onderhoudsbesluit.
- Een eventueel publiek padprefix kan in WebAuthn:VisitorBaseUrl staan, bijvoorbeeld https://<bestaande-rp-host>/portal. De reverse proxy moet dit prefix daadwerkelijk routeren. De instelling configureert geen DNS, proxy, TLS of firewall.

Een stabiele beheerde DNS-naam is passend voor nieuwe Cybertruck-installaties. passkey.infinigate.nl kan alleen worden gekozen als het domeinbeheer en een passend certificaat beschikbaar zijn; er is niets voor dit domein geprovisioned. Lokaal DNS kan de naam naar het actuele DHCP-adres laten verwijzen, zonder publieke hosting. Het host-IP wordt niet automatisch als RP-ID gebruikt: deze applicatie vereist een DNS-naam en WebAuthn bindt credentials aan de RP-ID. Bestaande localhost-passkeys worden niet automatisch naar een domein overgezet. Bron: https://www.w3.org/TR/webauthn-3/ (RP ID en originvalidatie).

## Uitgevoerd

- Volledige expliciete Node-set uit de orchestrator: 100 tests, **98 geslaagd, 2 bestaande PE-iconcontroles overgeslagen, 0 fouten**. Dit omvat de echte actieve-eigenaar-SQL op uitsluitend een in-memory synthetisch schema, inclusief NULL/eigenaar actief/eigenaar inactief.
- De werkelijke C# WebAuthnSettings-validatie en linkbuilder plus QR-generator zijn in geheugen gecompileerd. Alleen het externe Fido2Configuration-returntype gebruikt een POCO-teststub; geen eigen WebAuthn-verificatie. Twee evenementcodes zijn onafhankelijk gedecodeerd, met exact HTTPS-host, /portal-prefix en eventquery. Onjuiste configuraties worden geweigerd; bestaande localhost- en request-prefixfallback blijven behouden. Dit is geen portal-HTTP-test.
- 104 pakketchecks, 14 monitorlauncherchecks en 43 synthetische procescompatibiliteitschecks geslaagd. Geen bestaande applicatie gestart/gestopt.
- Resources als XML en git diff --check gecontroleerd.
- Nieuwe bronkopie: artifacts/dev-start-7da49713720543669127ef73a8427c16. Start-LummaDev.bat verify behoudt locked restore. Restore faalt opnieuw door geweigerde leestoegang tot %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. Geen permissie-/configuratieomweg toegepast. Daarna expliciete Release-build en regressieorchestrator met --no-restore geprobeerd: beide geblokkeerd door ontbrekende project.assets.json (NETSDK1004). LUMMA_TEST_WEB_DLL expliciet op de geïsoleerde kopie gezet.
- HTTP-regressie uitgebreid voor schoon bezoekersprofiel: eerste GET zonder generieke fout, routes uit vóór keuze, directe POST zonder nieuwe bezoekerscookie, routes aan en POST naar /Demo zonder presentatorlogin. C#-servicetest controleert ontbreken/herstellen van uitsluitend de synthetische eigenaar. Deze volledige .NET-/HTTP-tests zijn niet uitgevoerd door de restoreblokkade.

Logs: artifacts/visitor-admission-32d1a4e9e309415cb9e6c2ee8bd14939 (node.log, packaging.log, build.log, regression.log); restore.log in de dev-start-map.

## Nog open op de demo-laptop

1. NuGet.Config-leestoegang via de normale beheerroute herstellen en locked restore/build/orchestrator opnieuw uitvoeren.
2. De echte vaste link van Test Remco openen met een schoon browserprofiel zonder presentatorlogin, eerste GET en keuze-POST onderscheiden, en de huidige actieve eigenaar/ronde via geautoriseerd beheer bevestigen. Geen live runtimeclaim zonder deze controle.
3. Deelname aan/uit, passkeyregistratie/-aanmelding en hervatten live uitvoeren; controleer beheermelding, capaciteit en consentregels.
4. De echte geconfigureerde DNS/TLS/listener vanaf een telefoon controleren; klikbare link, gekopieerde link en gedecodeerde QR vergelijken, inclusief eventuele proxy-prefix. Scannen is niet fysiek uitgevoerd. Eerder geweigerde app-/netwerktoegang is niet omzeild.

## Gewijzigde bestanden en regels

| Bestand | Regel |
|---|---|
| LummaSimulator.Web/Services/EventAccessDemos.cs | 6 |
| LummaSimulator.Web/Pages/Index.cshtml.cs | 11 |
| LummaSimulator.Web/Pages/Index.cshtml | 18 |
| LummaSimulator.Web/Configuration/WebAuthnSettings.cs | 10 |
| LummaSimulator.Web/Services/ParticipantQrService.cs | 26 |
| LummaSimulator.Web/Pages/Presenter/Prizes/Event.cshtml.cs | 15 |
| LummaSimulator.Web/Pages/Presenter/Prizes/Event.cshtml | 8 |
| LummaSimulator.Web/Pages/Presenter/Prizes/Index.cshtml | 5 |
| LummaSimulator.Web/Pages/Presenter/Prizes/New.cshtml | 8 |
| LummaSimulator.Web/Pages/Presenter/Events/Index.cshtml.cs | 47 |
| LummaSimulator.Web/Pages/Presenter/Events/Index.cshtml | 28 |
| LummaSimulator.Web/Resources/Localization/UiText.resx | 1266 |
| LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx | 1266 |
| LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx | 1266 |
| tests/LummaSimulator.Web.Tests/SessionDemoHttpChecks.cs | 43 |
| tests/LummaSimulator.Web.Tests/PrizeRoundsChecks.cs | 40 |
| tests/WebAuthn.Ux.Tests/event-invitation-authorization.test.cjs | 58 |
| tests/WebAuthn.Ux.Tests/participant-qr-encoding.test.cjs | 79 |
| tests/WebAuthn.Ux.Tests/Export-VisitorLinks.ps1 | 1 |

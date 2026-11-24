# Eerste-administratorwizard

Implementatie: 2 november 2026. Dit beschrijft de actuele flow, geen uitbreiding van het hardware-authenticatorbeleid.

Bij een nieuwe installatie verwijzen GET /Presenter, /Presenter/Index en /Presenter/Login naar /Presenter/Setup. De service gebruikt dezelfde voorwaarde als bootstrap: geen enkel account met Activated IS NOT NULL. Een wachtend account sluit setup niet; een uitgeschakeld of later van rol veranderd, eerder geactiveerd account opent setup nooit opnieuw.

De wizard gebruikt bestaande vormgeving en NL/EN-resources. Stappen: welkom, lokaal account aanmaken, sleutel registreren, afzonderlijk aanmelden. Accountaanmaak blijft de expliciete onderhoudsactie: een bevoegde OS-beheerder draait de portal-DLL met --bootstrap-administrator vanuit de installatieomgeving. Gebruik exact dezelfde database-, opslag- en omgevingsconfiguratie als de portal. De wizard toont deze instructie en ontvangt vervolgens de tijdelijke code. Er bestaat geen anonieme web-API die bootstrapcodes uitgeeft of accounts aanmaakt.

POST /api/presenter/setup/open accepteert uitsluitend een directe lokale verbinding en een geldige bootstrapcode zolang setup openstaat. Het endpoint hergebruikt OpenEnrollmentAsync met bootstrapOnly; gewone uitnodigingen worden hierdoor niet verbruikt. Origin, antiforgery, rate limiting en bestaande Secure/HttpOnly/Strict cookies blijven behouden. Registratie gebruikt de bestaande enroll/options en enroll/verify met IFido2, user verification, resident credential, challengebinding en eenmalig verbruik.

Na geslaagde registratie sluit Setup onmiddellijk en gaat de browser naar /Presenter/Login. Deze aparte stap authenticeert met de geregistreerde credential en verwijst naar het dashboard. /Presenter/Enroll blijft beschikbaar voor reguliere uitnodigingen. Bootstrap-uitgifte blijft transactioneel: een volgende uitgifte trekt eerdere codes, grants en ceremonies in, zodat concurrerende onderhoudsacties niet twee eerste administrators kunnen activeren.

Een fysieke YubiKey kan worden gebruikt, maar attestation none bewijst of verplicht geen hardwaretype. Fysieke authenticatoracceptatie en productie-TLS moeten op iedere nieuwe laptop worden gecontroleerd. De geconfigureerde RP-ID en HTTPS-origin worden niet automatisch gewijzigd. Een reverse-proxyverbinding is geen directe lokale bootstrapverbinding.

## Verificatie

- Node-regressie: 52 tests geslaagd, inclusief setup-endpointkeuze en scheiding van registratie/authenticatie. Reguliere enrollment blijft getest.
- git diff --check: geslaagd.
- Nieuwe .NET-servicechecks: lege installatie, pending status, activering, uitgeschakelde accounts, gelijktijdige bootstrap-uitgifte, behoud van credentials en reguliere uitnodigingen.
- Nieuwe HTTPS-checks: dashboard/loginredirects, wizard, afwijzing forwarded bootstrap, sluiting na activering en redirects na registratie/authenticatie.
- Solutionbuild voor zeven projecten en regressieorchestrator geprobeerd in een eigen bronkopie zonder runtimegegevens. Beide geblokkeerd bij NuGet-configuratie: toegang geweigerd tot de gebruikersgebonden NuGet.Config. Geen .NET-tests uitgevoerd; compilatie en volledige regressie zijn nog niet bevestigd. De blokkade is niet omzeild.
- De tijdelijke .setup-verification-* bronkopie blijft staan: opruiming werd door het omgevingsbeleid geblokkeerd. Deze kopie is geen actuele bron.
- Geen actieve portal gestart of bestaande database/registratie gewijzigd. Geen schemawijzigingen, migraties, commit of push.

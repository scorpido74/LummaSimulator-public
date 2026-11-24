# Fase 6.5 — beveiligingsreview en resterende risico's

6 november 2026. Broncontrole en testontwerp; geen onafhankelijke securityscan, pentest of bewezen runtimeacceptatie. Nieuwe C#-/HTTPS-controles zijn niet uitgevoerd wegens de NuGet.Config-sandboxblokkade. Niet op een bestaande installatie starten voordat de geïsoleerde regressie is geslaagd.

## Behouden grenzen

- Bestaande FIDO2-library, vereiste user verification, HTTPS/RP-ID/originbinding en cookies blijven de authenticatieautoriteit.
- De centrale eventauthority leest rol, actieve account en authsessie uit SQLite; client-ID's, claimlabels of verborgen navigatie zijn onvoldoende.
- `(eventtype, bron-ID)` voorkomt dat gelijkende ID's van verschillende eventmodellen worden verwisseld. Historisch onbekend eigenaarschap is administrator-only; geen owner afgeleid uit een onbewezen creatorveld.
- Beheer-POSTs houden Razor-antiforgery, exacte Origin en bestaande rate limiting. Letterlijk Origin:null en ontbrekende Origin worden niet aanvaard, ook niet bij Sec-Fetch-Site:same-origin. Protected presenterpagina's gebruiken same-origin referrerbeleid voor native formulieren, zonder externe referrers te delen.
- Prize-New doet zijn eigen exacte Origincontrole en veilige diagnostiek; legacy Monitor doet zijn eigen exacte controle met zichtbare PRG-foutmelding. Dit zijn geen vrijstellingen van validatie.
- Mutaties gebruiken parameterized SQL, expliciete transacties en waar aanwezig bestaande optimistic concurrency/idempotentie. Prize creation retries controleren opnieuw eventtoegang.
- De laatste administrator/laatste passkey blijven beschermd; eigen keyverwijdering trekt eigen sessies in. De eigenaar kan niet worden gedeactiveerd zonder overdracht, ook niet via de oude accountservice.
- Identity Windows Event Log-exporter, bestaande monitorhashopslag/DPAPI, manifestencryptie en normale TLS-validatie zijn niet afgezwakt.
- Geen nieuwe listener, remote shell, endpoint/MDR-test, echte credentialverzameling of automatische LIVE-run.

## Uitnodigingsmodel

32-byte cryptografische Base64URL enrollmenttokens worden alleen gehasht opgeslagen. Nieuwe enrollment uitsluitend voor een nieuwe Presenter-account; geen globale rolkeuze in eventinvite. Bestaande ontvanger authenticeert met eigen passkey en accepteert de voor hem opgeslagen invite. Een token bezit niet de identiteit van een bestaande account. Beheerhandelingen worden met actor en typed eventreferentie geaudit, zonder tokenwaarden.

Nieuwe enrollment maximaal 10 minuten, bestaande-accountinvite/ownershipaanbod 24 uur. Unieke event/target-constraint plus BEGIN IMMEDIATE serialiseren concurrerende aanvragen. Acceptatie controleert doelaccount, vervaltijd en huidige bevoegdheid van de uitgever. Revocation verwijdert nog pending enrollmentautorisatie en ceremonies, zonder geactiveerde credentials te verwijderen. Clipboardkopie alleen na expliciete klik; geen browseropslag of automatische overdracht. Nieuwe links gebruiken fragmenten; browsercode verwijdert het fragment direct uit history voordat WebAuthn begint.

## Monitorisolatie

De gedeelde monitorintegratiesleutel is geen presentatorrecht. GET vereist aanvullend een gehashte capability die gebonden is aan actor, authsessie, typed event en DemoRunId. Lidmaatschap, actuele sessie, eventstatus, globale grant en actieve bezoeker-WebAuthn-sessie worden bij gebruik gecontroleerd. WPF houdt de capability alleen in geheugen en stuurt hem alleen bij het passende portalrun-ID. Sleuteltest via HEAD toont uitsluitend protocol-/keygeldigheid.

A en B kunnen in hetzelfde event eigen capabilities hebben; intrekken van A verwijdert B niet. Account/sessielogout, lidmaatschapsverlies, eventclose of capabilityexpiry blokkeren verder gebruik. Bestaande keys/grantrijen worden tijdens schema-upgrade niet gereset; legacy clients zonder nieuwe capability krijgen voor metadata geen toegang. Dit is een bewuste compatibiliteitswijziging, lokaal te accepteren met bijgewerkte WPF.

Een capability is een bearer secret: een gebruiker die hem opzettelijk deelt, kan de geautoriseerde beperkte metadata delegeren zolang zijn sessie/event/grant geldig zijn. Geen claim van cryptografische binding aan een fysiek WPF-apparaat. Eenmalige uitgifte, geheugenopslag en korte geldigheid beperken dit risico.

## Migratie en bootstrap

De 14 exacte schemaobjecten zijn opgenomen in de bestaande allowlistcontrole. Onbekende/partiële/conflicterende schema's falen gesloten. SQL-marker 1, DDL, contextmapping en classificatie zijn transactioneel. Manifestimport is afzonderlijk, idempotent en blokkeert serverstart bij mislukking; geen distributed-transactionclaim. Upgrade verwijdert geen gebruikers, passkeys, events, keys of auditgegevens.

Nieuwe first-runclassificatie is persistente installatie-informatie met anti-delete/anti-resettriggers, naast bestaande accountactivatie. De GUI-bootstrap vereist direct loopback, HTTPS, exacte Origin en antiforgery, geen geactiveerde identiteit, New-installatiestatus en geen andere live bootstrapautorisatie. Een expired nooit geactiveerde/keyloze poging kan opnieuw worden uitgegeven; een gevestigde identiteit niet. Normale presenterfunctionaliteit blijft geblokkeerd tot voltooiing. Dit beschermt niet tegen een aanvaller met onbeperkte offline database-/bestandsschrijfrechten.

Een ontbrekende DB naast enkele bekende lokale keyring/manifest/monitorpaden stopt startup. Extern overschreven runtimepaden zijn hiermee niet volledig bewezen afgedekt. Recovery blijft een expliciete, consistente database/keyring/DPAPI-procedure; nooit een losse vlag verwijderen of een oude database over een actieve schrijven.

## Configuratie en branding

Alleen administrator plus eenmalige verse WebAuthn-herbevestiging mag globale instellingen/logo's wijzigen. Revisie voorkomt stil overschrijven. Alleen begrensde PNG RGB/RGBA 8-bit non-interlaced, maximaal 256 KiB/1024x1024, met magic, chunkbounds, CRC en begrensde decompression. Ancillary metadata wordt gestript; SVG/actieve content niet geaccepteerd. Publieke logo-endpoint retourneert image/png onder bestaande nosniff/CSP-beveiliging. NL/EN-resources en Razor-encoding blijven gebruikt.

ProposedBaseUrl is uitsluitend een voorstel; geen wijziging van RP-ID, toegestane origins, effectieve hostname of certificaten. Hostingcontrole gebruikt bestaande WebAuthnvalidatie/effectieve HTTPS plus expliciete operatorcontrole van certificaatketen in browser. Geen claim van automatische chain-/expiry-inspectie. Logo-sync naar WPF is niet automatisch toegevoegd.

## Verplicht vervolgonderzoek

1. C#-/Razor-/WPF-compilatie en volledige HTTP-autorisatieregressie uitvoeren. Geen vrijgave zonder dat resultaat.
2. Pagefilter-handlernamen, policyvolgorde, anti-CSRF-identiteit en redirects verifiëren met de geschreven HTTP-tests; met name onboarding vóór normale presenterpagina's.
3. Races uitbreiden voor gelijktijdig invite/accept/revoke/transfer en monitorrevoke; huidige geschreven coverage is niet uitputtend.
4. Afhandeling van I/O-fouten tussen Event Edition-scopecreatie en manifestwrite operatorgericht verifiëren; nooit orphan scopes automatisch opruimen op actieve data.
5. Eigendomsoverdracht verwijdert invites, maar nog open enrollmentgrants kunnen tot hun korte expiry een ongekoppeld Presenter-account activeren. Ze verlenen na inviteverwijdering geen eventrechten. Verdere capability-intrekking is nog te beoordelen.
6. Review live-statuscheck versus daaropvolgende transactie bij grant/admission. GET controleert actuele status opnieuw; geen nieuwe bypass veronderstellen zonder raceproef.
7. Accountrecovery zonder beschikbare eigen/backupauthenticator is bewust geen generieke link-overnameprocedure. Eventownership kan via een bestaande administrator worden hersteld; volledig offline identiteitsherstel vereist afzonderlijke gecontroleerde procedure.
8. Wizard eerste-eventtype, destructieve eventverwijdering, automatische hostingmigratie en WPF-brandingsynchronisatie zijn nog niet opgeleverd; zie implementatiedocument.

Geen actieve data, secrets, ACL's, certificaten of processen zijn voor deze review gewijzigd.

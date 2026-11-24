# Fase 6.5 — operatorhandleiding (ontwikkelstatus)

6 november 2026. De hier beschreven bediening staat in de broncode. C#-/HTTPS-/WPF-acceptatie is nog niet geslaagd aangetoond wegens een geblokkeerde restore. Gebruik eerst uitsluitend een geïsoleerde testinstallatie met synthetische gegevens. Start de huidige bron niet tegen de bestaande database om hem te controleren.

## Bestaande installatie

De upgrade hoort bestaande accounts, passkeys, Prize-data, events, keyring, monitorintegratie en manifest te behouden. Existing-installaties worden niet naar de nieuwe wizard gestuurd. Historische eventowners worden niet gegokt: log als bestaande administrator in, open het event in Dashboard/Evenementbeheer, bevestig je eigen identiteit en kies gecontroleerd een actieve primaire eigenaar. Open demo is een administratorquarantaine en blijft dat.

Voor een operationele upgrade is eerst een expliciet afgesproken consistente backup van database, keyring, manifest en DPAPI-context vereist. Deze opdracht heeft geen backup, upgrade, migratie of deployment op actieve data uitgevoerd. Herstel nooit alleen de database of een losse bootstrapvlag.

## Nieuwe lokale installatie

1. Gebruik een daadwerkelijk nieuwe, geïsoleerde datamap en de bestaande correct geconfigureerde HTTPS/RP-ID/origin. Deze wizard maakt of vervangt geen certificaten.
2. Open /Presenter/Setup rechtstreeks op de lokale HTTPS-origin. Alleen een directe loopbackbrowser kan het eerste account aanmaken. Vul login/weergavenaam in en bevestig. De eenmalige bootstrapcode verschijnt alleen in het beveiligde invoerveld van de oorspronkelijke response.
3. Registreer een eigen passkey via de bestaande browser-WebAuthn-dialoog. Meld daarna afzonderlijk aan; registratie is geen login.
4. De portal toont verplicht /Presenter/Onboarding. Voeg via Mijn account een tweede eigen authenticator toe na herbevestiging als back-up. De portal claimt geen fysiek YubiKey-type dat attestation niet bewijst.
5. Sla organisatie/displaynaam/taal en desgewenst PNG-logo's op via Configuratie, steeds na verse herbevestiging.
6. Controleer de effectieve hostname, RP-ID, origins en certificaatketen in de browser. Vink beide bevestigingen aan en kies **Hostingcontrole bevestigen**. De pagina toont daarna de opgeslagen hostingstatus. Een checkbox voert geen certificaatanalyse uit.
7. Maak het eerste Experience-event aan. Eerste Prize/Event Edition-typekeuze is nog niet beschikbaar in de wizard; maak zulke events na voltooiing via hun bestaande schermen.
8. Kies **Controleren en afronden** zodra de statussen alle vereisten bevestigen. Als recente identiteitsverificatie ontbreekt, opent diezelfde actie de bestaande passkeydialoog. Afronden bewaart de status en opent Evenementbeheer. Na annulering blijft de invoer staan. Een eerder aangemaakt eerste evenement wordt bij hervatten bij naam getoond en hergebruikt.

Na onderbreking kun je in dezelfde browser verdergaan. Eén live bootstrap tegelijk; een nooit geactiveerde verlopen keyloze poging kan lokaal opnieuw worden uitgegeven. Geactiveerde accounts openen bootstrap nooit opnieuw. Bij verlies van alle authenticators geen reset uitvoeren: gebruik afgesproken backup/recovery met bestaande beheerder. De bestaande expliciete CLI-bootstrap is als onderhoudsroute behouden, geen permanente publieke registratie.

## Eventbeheer en uitnodigingen

Login landt op /Presenter/Events. Daar staan alleen toegestane events en eigen invites/ownershipaanbiedingen. Selecteer expliciet een event. Deel de daar getoonde bezoekerslink; deze bevat alleen type en publieke eventreferentie. Een generiek bezoek geeft geen impliciete toegang tot nieuw toegewezen events. Een reeds geregistreerde visitor wordt niet stil naar een ander event omgebonden.

De maker is primaire eigenaar. Als eigenaar selecteer je een bestaande actieve presentator, nodig je uit en wacht je op diens eigen login plus acceptatie. Een gekoppelde presentator kan presenteren/sluiten/starten waar de bestaande lifecycle dit ondersteunt, maar geen leden, owners of globale instellingen wijzigen.

Nieuwe collega: kies nieuwe presentator uitnodigen, voer een unieke login/naam in en kopieer de eenmalig getoonde link via de knop. Deel de link uitsluitend via een afgesproken privé-kanaal, niet in openbare schermopnames/logs. Deze is tien minuten geldig, geeft nooit administratorrechten en kan geen bestaande identiteit vervangen. De ontvanger registreert zijn eigen passkey, logt apart in en accepteert de eventinvite. Bestaande-accountinvites zijn 24 uur geldig. Intrekken kan in hetzelfde eventscherm.

Eigenaarschap: gekoppelde ontvanger eerst laten accepteren; herbevestig, bied overdracht aan. Ontvanger herbevestigt en accepteert het ownershipaanbod binnen 24 uur. Pas daarna verandert de eigenaar. Een administrator kan onbekend/verweesd ownership gecontroleerd herstellen. Deactiveren van een eigenaar wordt geweigerd tot alle scopes zijn overgedragen. Behoud minimaal één actieve administrator en minimaal één eigen passkey.

Historie/afsluiten gebruiken bestaande lifecycle en behoud van gegevens. Geen nieuwe destructieve delete- of pausefunctie toegevoegd. Het exportlinkje geeft uitsluitend de geselecteerde toegestane demo-overzichtsvelden, geen cookie, sessionhash of credentials.

## WPF en monitor

1. Beheer de bestaande integratiesleutel via Configuratie → Session Exposure Monitor. Bestaande secrets worden nooit opnieuw getoond.
2. Configureer WPF via het bestaande beveiligde sleutelveld; DPAPI CurrentUser en normale TLS blijven gelden. Test verbinding: Connected bevestigt key/protocol, geen bezoekertoegang.
3. Maak een visitor met de bedoelde eventlink, registreer en authenticeer een fictieve demopasskey.
4. Selecteer exact die demonstratie in de eventworkspace. Klik Toestaan. Kopieer de eenmalig zichtbare demo-capability naar het afzonderlijke WPF-demotoestemmingsveld, nadat het juiste portal-DemoRunId is ingesteld. Dit is geen WPF-run-ID.
5. WPF stuurt de capability uitsluitend voor die run. De capability blijft in geheugen, vervalt uiterlijk na 29 minuten en geeft zonder integratiesleutel/actieve sessies/eventrechten/grant geen metadata.
6. Intrekken geldt voor jouw eigen presentatorsessie. Een tweede presentator heeft een eigen capability. Logout, membershipverlies, eventclose of expiry blokkeren verder gebruik.
7. Event Edition-pairing blijft apart: gebruik hetzelfde Edition-event, exact portalrun-ID en WPF-run-ID; readcode handmatig in de bedoelde WPF-context. De bezoeker bevestigt de fictieve ClickFix-keuze; presentator start zelf de gesimuleerde aanvallersflow. Geen automatische LIVE-run.

Oude WPF-builds die alleen de integratiesleutel sturen krijgen voor GET-metadata 404. Gebruik portal en WPF uit dezelfde geverifieerde build. Standalone WPF offline blijft zonder portal bruikbaar.

## Configuratie en logo's

NL en EN worden ondersteund. Upload uitsluitend RGB/RGBA 8-bit non-interlaced PNG, maximaal 256 KiB en 1024×1024. Organization/presenter/visitor hebben eigen slots; ontbrekende upload valt terug op bestaande lokale resellerbranding/tekst. Geen externe logo-URL's. WPF-logo's blijven via bestaande lokale brandinginstelling; automatische sync is niet toegevoegd.

Een voorgestelde HTTPS-basis-URL wordt alleen opgeslagen. Verander effectieve hostname/RP-ID/origins/certificaten uitsluitend via een afzonderlijk gevalideerd migratieplan: andere RP-ID kan bestaande passkeys onbruikbaar maken. Geen private keys in browser/logs.

## Handmatige acceptatie na geslaagde lokale regressie

Gebruik twee fysieke presentatoridentiteiten A/B, twee eigen events en één gedeeld event; desgewenst softwarefixtures alleen voor automatisering. Controleer directe vreemde URLs en gewijzigde event/demo-ID's, pending/expired/revoked invites en ownershipacceptatie. Geef A/B elk een eigen WPF-context; trek A in en controleer dat alleen B verder kan. Trek lidmaatschap en integratiesleutel in en controleer onmiddellijke weigering. Sluit beide apps netjes; geen echte endpoint/MDR-test uitvoeren.

Controleer nieuwe lege installatie, bestaande synthetische installatiemigratie en herstart middenin onboarding. Herhaal fysiek WebAuthn met hoofd- en backupauthenticator. Controleer laptop/tablet/telefoon, toetsenbord/focus, 200% browserzoom en WPF-DPI. Deze controles zijn in deze sessie niet uitgevoerd. Publiceer installer/portable alleen na afzonderlijke geïsoleerde publish/sidecaracceptatie; geen actieve installatie overschrijven.

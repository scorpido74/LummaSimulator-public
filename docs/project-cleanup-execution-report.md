# Uitvoeringsrapport gecontroleerde archivering en opschoning

Datum: 31 oktober 2026. Hervatting volgens [project-cleanup-analysis.md](project-cleanup-analysis.md). De eerdere versie van dit rapport staat in de volledige back-up.

## 1. Daadwerkelijk resultaat

De beveiligde projectback-up en historische archivering zijn voltooid. De geïsoleerde build, alle vier regressierunners, publicatie en helperacceptatie zijn geslaagd. Vervolgens zijn uitsluitend exact toegestane A/B/C-paden verwijderd, na bronhash-, back-up-, bestandsvergrendelings- en procescontroles. Na beide verwijderbatches slaagde de volledige regressiesuite opnieuw.

Verwijderd: **1992 bestanden**, **76 exacte bestand-/mapacties**, **860458339 logische bytes** uit de projectmap. Dit is geen netto winst op de hele schijf: de ongecomprimeerde back-up, historische kopieën en testwerkkopie nemen ruimte in. Project: 2319 bestanden bij de snapshot; 327 bestanden bij eindcontrole. Geen Git geïnitialiseerd; .gitignore ongewijzigd.

De actieve portalinstallatie, het oorspronkelijke portalproces PID 39836 en alle beschermde oorspronkelijke gegevens zijn behouden. Er is geen live portal gestart, gestopt of herstart door deze uitvoering. Tests startten uitsluitend eigen geïsoleerde fixtures, browser-, helper- en WPF-processen.

## 2. Archieflocatie en NTFS-rechten

Uitsluitend de goedgekeurde locatie: **C:/Example/Archive/20261031-111434/**. De root bestond nog niet. C:/src bleek nu binnen de schrijfgrens; de bovenliggende map was geen reparsepoint. Geen bestaande archiefset overschreven en geen alternatieve locatie gebruikt.

Verhoogde uitvoering is expliciet aangevraagd en gebruikt. De lege archiefroot is met een vooraf samengestelde beveiligingsdescriptor aangemaakt: overerving uit C:/src uitgeschakeld; uitsluitend de oorspronkelijke Windows-gebruikersidentiteit (SID lokaal vastgelegd), SYSTEM en Administrators hebben FullControl met overerving naar kinderen. De ACL is vóór kopiëren gecontroleerd en na afloop opnieuw gecontroleerd op de root en de gekopieerde beschermde herstelset. Bestaande project-/runtime-ACL's zijn niet aangepast. De normale sandboxidentiteit krijgt geen extra archieftoegang.

De eerste verhoogde poging stopte vóór aanmaken omdat een veronderstelde lokale accountnaam niet naar een SID vertaald kon worden. Daarna is de werkelijke AzureAD-identiteit vastgesteld en gebruikt.

## 3. Consistente back-up zonder portalstop

De twee oorspronkelijke operationele portallogs bleven rechtstreeks onleesbaar wegens een open proceshandle. De normale sessie had geen toegang tot Win32_ShadowCopy. Na toestemming voor verhoogde uitvoering is succesvol een VSS-volumeschaduwkopie gemaakt: **{F5ADE546-4E58-4A1C-A1F8-54CDF4366023}**, Device HarddiskVolumeShadowCopy2. Alle projectbestanden zijn uit diezelfde schaduwkopie gelezen; de portal bleef draaien. Geen geforceerde lezing van de live logs.

De volledige **projectmap op het snapshotmoment** is gekopieerd: **2319 bestanden, 910258168 bytes**. Bron- en doel-SHA-256 en lengtes zijn per bestand vergeleken. CreationTimeUtc en LastWriteTimeUtc zijn vastgelegd en overgenomen; kopiestatus staat in het manifest. Geen reparsepoints aangetroffen. De later gemaakte uitvoeringsscripts en dit bijgewerkte rapport horen bij de uitvoeringsadministratie, niet bij het eerdere snapshotmoment.

Dit is een consistente bestandsmomentopname van de projectmap, inclusief de bestaande statische safetyherstelset en open logs. **Geen nieuwe applicatieconsistente back-up van de live portalgegevens buiten de projectmap.** LOCALAPPDATA/LummaSimulator, externe baselines, live database en live keyring vallen buiten kopie-, schrijf-, test- en verwijderacties. Er is geen SQLite-query, migratie of volledige hersteltest op productiegegevens uitgevoerd. Oorspronkelijke DPAPI-context blijft noodzakelijk voor herstel van beschermde gegevens.

## 4. Archieven en controlebewijs

Alle onderstaande paden liggen onder de genoemde timestampmap.

| Onderdeel | Werkelijk resultaat |
| --- | --- |
| project-backup/ | Volledige projectbestandskopie uit VSS; alle bron/doelhashes gelijk. |
| project-backup-manifest.csv | Bronpad, relatief pad, archiefpad, bytes, SHA-256, creation-/last-write-tijd en status per bestand. |
| historical-builds/, historical-test-results/, historical-documentation/ | Samen **1912 bestanden**, afzonderlijk gekopieerd uit de gevalideerde back-up en opnieuw gehasht. |
| historical-manifest.csv | Oorspronkelijke paden, archiefpaden, hashes, tijden en status van de historische kopieën. Ook dependency-JSON en andere tekstoutput kan onder historical-test-results staan; oorspronkelijke padstructuur blijft leidend. |
| isolated-work/ | Bronkopie zonder bestaande bin/obj; nieuwe build- en synthetische testoutputs. Geen oorspronkelijke databases/keyrings gebruikt. |
| validation/ | Build-, regressie-, publish-, helper- en beide nacontrolelogs; results.json en batch-results.txt. |
| deletion-plan.csv, deletion-actions.json, deleted-actions.jsonl, cleanup-result.json | Exacte verwijderlijst, vooraf gecontroleerde bestandshashes, uitgevoerde acties en totalen. |
| execution-scripts/ | De vier gebruikte uitvoeringsscripts; na afloop uit docs naar het archief verplaatst. |

Database, keyring en bijbehorend safetymanifest zijn uitsluitend als beschermde set in de afgeschermde volledige back-up opgenomen, niet in de historische build-/testarchieven of de testwerkkopie. Historische documenten zijn **gekopieerd, niet uit docs verwijderd**. Bestaande documentlinks blijven daarmee behouden; verwijzingen naar verwijderde historische outputs zijn via historical-manifest.csv op hun oorspronkelijke pad te vinden. Geen gevoelige inhoud of sleutelwaarden in dit rapport gepubliceerd.

## 5. Geïsoleerde builds en regressie

| Controle | Resultaat |
| --- | --- |
| Restore solution | Exit 0; locked mode, bestaande lokale packagecache, lege lokale packagebron, NuGet-audit uit. Geen nieuwe afhankelijkheden gedownload. |
| Build solution | Exit 0; alle zeven projecten inclusief transitieve LocalTestServer; 0 waarschuwingen, 0 fouten. |
| RegressionTests VSTest-target | Exit 0; Web.Tests, drie Node UX-bestanden, SmokeTests en WPF Verification uitgevoerd. |
| Node UX | 43 geslaagd, 0 mislukt, 0 overgeslagen. |
| Web/Prize | Library-, SQLite-migratie-, HTTP-/autorisatie- en Prize-browserformulierchecks geslaagd op eigen tijdelijke data en eigen portal-DLL. |
| WPF / helper | Verificatie geslaagd, inclusief start/stop, volledige simulatie, Navigator en receiver-lifecycle. |
| Publish App plus sidecar | Exit 0; frameworkafhankelijke Debug-publicatie. Geen claim voor alle RID/self-contained-varianten. |
| Test-LocalHttpPublished.ps1 | Exit 0; apphost-identiteit, synthetische POST, weigering ongeldige payload en nette shutdown geslaagd. |
| Na batch A | Volledige regressie opnieuw exit 0. |
| Na batch B/C | Volledige regressie opnieuw exit 0. |

TEMP/TMP voor fixtures wees naar validation/temp binnen het afgeschermde archief. Testdata, logs en exports waren geïsoleerd; de regressieorchestrator zette eigen data-/origininstellingen en een lege monitorgeheimwaarde. LUMMA_TEST_WEB_DLL wees expliciet naar de nieuwe DLL onder isolated-work. Geen fysieke YubiKey-/Windows Hello-/mobiele of productie-TLS/MDR-acceptatie geclaimd.

Een eerste verwijdervoorcontrole stopte vóór enige verwijdering wegens een UTC/lokale-tijdvergelijking. Hash en lengte waren gelijk. De vergelijking is gecorrigeerd naar UTC en de volledige voorcontrole is opnieuw uitgevoerd. Dit was geen bronwijziging of testfout.

## 6. Exact uitgevoerde acties

Batch A: zeven obj-mappen. Batch B/C: uitsluitend de onderstaande bewezen build-/publishoutputs en gearchiveerde historische bewijsbestanden. Voor ieder bestand zijn actuele grootte, tijd en SHA-256 tegen het volledige back-upmanifest gecontroleerd. Bestanden met beschermde extensies/paden, reparsepoints, nieuwe of gewijzigde inhoud of exclusieve leesblokkade zouden de batch stoppen. Procespaden zijn vlak vóór iedere verwijderactie opnieuw gecontroleerd. Geen generieke bin- of verification-verwijdering.

| Exact relatief pad | Categorie | Resultaat |
| --- | --- | --- |
| `LummaSimulator.App/bin` | B | Verwijderd; back-up geverifieerd |
| `LummaSimulator.App/obj` | A | Verwijderd; back-up geverifieerd |
| `LummaSimulator.Web/obj` | A | Verwijderd; back-up geverifieerd |
| `tests/LummaSimulator.RegressionTests/bin` | B | Verwijderd; back-up geverifieerd |
| `tests/LummaSimulator.RegressionTests/obj` | A | Verwijderd; back-up geverifieerd |
| `tests/LummaSimulator.SmokeTests/bin` | B | Verwijderd; back-up geverifieerd |
| `tests/LummaSimulator.SmokeTests/obj` | A | Verwijderd; back-up geverifieerd |
| `tests/LummaSimulator.Web.Tests/bin` | B | Verwijderd; back-up geverifieerd |
| `tests/LummaSimulator.Web.Tests/obj` | A | Verwijderd; back-up geverifieerd |
| `tools/LummaSimulator.LocalTestServer/bin` | B | Verwijderd; back-up geverifieerd |
| `tools/LummaSimulator.LocalTestServer/obj` | A | Verwijderd; back-up geverifieerd |
| `verification/obj` | A | Verwijderd; back-up geverifieerd |
| `verification/bin/Debug` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/discoverable-suite` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-publish` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-publish-artifacts` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-separated-artifacts` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-suite` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase2-tests` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase2-web` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase3-suite` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase3-tests` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase4-suite` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase4-tests` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase4-web` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-regression` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-suite` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-regression` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-suite` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/prize-http400-regression` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/web041-check` | B | Verwijderd; back-up geverifieerd |
| `verification/bin/discoverable-build-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/discoverable-js-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/discoverable-smoke-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/discoverable-web-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/discoverable-wpf-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-build-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-focused-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-focused-separated-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-js-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-publish-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-published-check-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-separated-build-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-smoke-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-web-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-wpf-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/local-http-wpf-separated-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase2-js-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase2-smoke-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase2-web-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase2-wpf-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase3-js-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase3-smoke-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase3-web-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase3-wpf-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase4-js-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase4-smoke-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase4-web-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase4-wpf-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-build-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-final-web-build.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-js-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-smoke-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-source-diff.json` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-web-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-51-wpf-results.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-build.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-final-build.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-js-tests.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-smoke-tests.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-source-diff.json` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-web-tests.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/phase5-52a-wpf-tests.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/prize-http400-after.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/prize-http400-before.log` | C | Verwijderd; back-up geverifieerd |
| `verification/bin/v04-final-test-results.log` | C | Verwijderd; back-up geverifieerd |

## 7. Bewust behouden

Onderstaande B-testdatamappen zijn wel in de beveiligde back-up/historische archivering opgenomen, maar niet verwijderd: de volledige synthetische herkomst is niet opnieuw inhoudelijk bewezen. De voorwaarde uit de analyse blijft open.

- `verification/bin/discoverable-smoke-data`
- `verification/bin/discoverable-wpf-data`
- `verification/bin/local-http-smoke-data`
- `verification/bin/local-http-smoke-separated-data`
- `verification/bin/local-http-wpf-data`
- `verification/bin/phase2-smoke-data`
- `verification/bin/phase3-smoke-data`
- `verification/bin/phase4-smoke-data`
- `verification/bin/phase5-51-smoke-data`
- `verification/bin/phase5-51-wpf-data`
- `verification/bin/phase5-52a-runtime`

Verder behouden: volledige actieve Web/bin, verification-bron/project/publishscript, alle actieve bron/configuratie/resources/migraties/tests/referenties, alle historische documenten op hun oorspronkelijke paden, de twee open portallogs en verification/bin/prize-http400-safety-20261030-232648 met database/keyring/manifest. Geen E-pad verwijderd of verplaatst.

## 8. Eindcontrole, herstel en vervolg

**222 bestaande bestanden** uit de beschermings-/bronbaseline zijn na beide batches vergeleken: SHA-256, lengte en LastWriteTimeUtc gelijk. Dit omvat de bestaande bron/configuratie/docs/testbron (uitgezonderd dit bewust bijgewerkte rapport), alle oorspronkelijke portaluitvoerbestanden en de drie safetyherstelbestanden. PID 39836 en het oorspronkelijke executablepad zijn bevestigd. Geen live-database-integriteitsclaim: het bestaande proces mag eigen runtimegegevens blijven bijwerken.

Herstel bij een ontbrekende historische output: zoek het oorspronkelijke relatieve pad in project-backup-manifest.csv of historical-manifest.csv, verifieer SHA-256 en herstel alleen het benodigde bestand/pad. Geen oude database over een actieve database kopiëren. De back-up is op bestandsniveau gevalideerd; geen volledige applicatie-restoretest geclaimd.

De tijdelijke VSS-kopie wordt na deze gevalideerde archivering op exact haar eigen ID opgeruimd; zie final-verification.json voor de daadwerkelijke uitkomst. De beveiligde archieven blijven staan.

Opschoning van de genoemde B-testdata blijft afhankelijk van inhoudscontrole. .gitignore is niet gewijzigd en Git is niet geïnitialiseerd. Voor een latere eerste staging blijven de afzonderlijke ignorewijziging, stagingcontrole en geheim-/PIIcontrole noodzakelijk. Geen volgende ontwikkelfase of publicatie gestart.

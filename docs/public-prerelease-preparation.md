# Publieke pre-release v0.6.5-beta.1 — voorbereiding

Bijgewerkt: 6 november 2026. Status: broncheckpoint en distributie geblokkeerd; niets openbaar gepubliceerd.

## Bron en testbewijs

- GitHub heeft `scorpido74/LummaSimulator` opnieuw als `private` bevestigd. Defaultbranch: `main`.
- Lokale branch: `feature/phase5-domain-foundation`; bestaande HEAD: `ffe8780dd667be57e6ebca45db608592d03be742`.
- De 304 bronbestanden uit de fase-6.5e-kopie waren bij vergelijking identiek aan de hoofdmap. B1/B2 zijn in beide aanwezig. Daarna zijn uitsluitend packaging, bijbehorende verificatie en release/installatiedocumentatie aangepast.
- De operator bevestigde dat de volledige lokale .NET-verificatie is uitgevoerd en geslaagd. Dit aangeleverde resultaat geldt als testbewijs voor de bestaande applicatiecode; er is in deze vervolgstap geen nieuwe restore uitgevoerd. De eerdere sandboxrestoreblokkade is geen actuele mislukking van de lokaal geteste applicatie.
- Nieuwe packagingwijzigingen en het uiteindelijke pakket vallen niet automatisch onder dat eerdere testbewijs.

## Gewijzigde releasebestanden

- `packaging/Build-EventEdition.ps1`: v0.6.5-beta.1 in ZIP/map en gepubliceerde assemblyversies; README, release notes en VERSION.txt; externe ZIP-SHA-256; aanvullende weigering van certificaten en ontwikkelbestanden.
- Expliciete `-VerifiedSourceDirectory`: vergelijkt gecompileerde invoer met de hoofdmap, gebruikt reeds uitgevoerde lokale verificatie en publiceert met `--no-restore`. De normale modus blijft zijn bestaande restore/build/regressie uitvoeren. Deze modus is geen vrijstelling van publicatie- of installatieacceptatie.
- `packaging/EventEdition/README.md`, `RELEASE-NOTES.md`: openbare conceptdocumentatie, nieuwe-installatiestappen en beperkingen.
- `docs/event-edition-installatie.md`, `packaging/EventEdition/Demo.ps1`: versie en instructie voor de bestaande browserbootstrap en Onboarding. Geen wijziging van certificaat-, bootstrap- of autorisatielogica.
- `verification/Test-EventEditionPackaging.ps1`: aanvullende versie-, no-restore-, bronvergelijkings-, documentatie- en filtercontroles.

## Werkelijk uitgevoerd

| Controle | Resultaat |
|---|---|
| Private repositorycontrole | PASS via GitHub-metadata |
| Nieuwe releasebranch | FAIL: .git/refs/heads/release/v0.6.5-beta.1 niet schrijfbaar |
| Commit / push | Niet uitgevoerd na Git-permissieblokkade; geen API-omweg |
| Eerdere Node-regressie | 79 PASS, 0 FAIL, 2 bestaande PE/publish-skips; eerder zelf uitgevoerd |
| Nieuwe packagingchecks | 87 PASS |
| Monitor-launcherchecks | 14 PASS |
| Procescompatibiliteitschecks | 33 PASS; synthetische argument/stdin-probe, geen echte applicatieinstallatie |
| Kandidaatpaden / beperkte secretpatronen | Geen herkenbare gevoelige bestandspaden; vier patroonhits in synthetische testwaarden |
| Staging | Leeg; codex-start.bat ongemoeid en niet opgenomen |
| Werkelijke self-contained publicatie zonder restore | FAIL NETSDK1047: net10.0/win-x64 ontbreekt in Web/obj/project.assets.json |
| ZIP / SHA-256 / binaire inhoudscontrole | Niet beschikbaar; builder stopt voor archivering |
| Nieuwe installatie / TLS / first-run / start-stop / sidecaracceptatie | Niet uitgevoerd wegens ontbrekend pakket |

Publicatielog: `artifacts/event-edition-package-be0520f285bb4957a499917616820ebe/publish-Portal.log`.
De gewone frameworkassets van App, Web en LocalTestServer bevatten geen Windows-RID-targets. Een geslaagde gewone Release-build levert die niet automatisch op.

## Resterende concrete stappen

1. Voer buiten de sandbox een gecontroleerde checkpointcommit en push uit op `release/v0.6.5-beta.1`. Gebruik de bestaande echte Git-identiteit, expliciete bron-/test-/documentatiepaden en inspecteer staginginhoud en status. Geen `git add .`; codex-start.bat, runtimegegevens, lokale configuratie, bin/obj, artifacts en dist uitsluiten. Een pad/patroonscan is geen volledige geheimengarantie.
2. Bereid lokaal de RID-assets voor de geverifieerde geisoleerde bronkopie voor. Geen databases of certificaten kopieren. Onderstaande restore is uitsluitend een lokale operatorstap; niet in de sandbox uitgevoerd. Bij locked-modefouten stoppen en onderzoeken, niet automatisch afhankelijkheden of lockfiles wijzigen.
3. Bouw het pakket met de bijgewerkte builder. Publiceer geen handmatig samengestelde restbestanden na een mislukte builder.
4. Controleer de werkelijke ZIP en uitgepakte boom op broncode, symbolen, database/sidecars, accounts, passkeys, keyrings, monitorstate, certificaten/private keys, lokale configuratie, logs en testdata. Valideer de checksum.
5. Test op schone Windows 11 x64 zonder SDK: vertrouwd localhost-certificaat vooraf onder demo-gebruiker; Setup; eerste administrator/passkey; aparte login; Onboarding; tweede start zonder duplicaten; WPF/portal/sidecar; nette stop zonder achterblijvende processen. Alleen eigen tijdelijke installatiegegevens gebruiken. Geen trustwijziging of TLS-bypass automatisch uitvoeren.
6. Leg echte resultaten vast. Fysieke authenticator-, mobiele-, DPI- en MDR-tests afzonderlijk benoemen. Synthetische proceschecks bewijzen geen installatieacceptatie.
7. De publieke repository mag worden voorbereid, maar is nog niet aangemaakt of beschreven. Vraag opnieuw concreet akkoord voor een publieke write; publiceer geen release voor succesvolle pakket- en installatieacceptatie. Houd uitsluitend veilige publieke documentatie in die repository; inspecteer ook de inhoud van releasetag en automatisch gegenereerde bronarchieven. Geen private bronboom, Git-historie of PDB's publiceren.

## Lokale packagecommando's

```powershell
Set-Location 'C:/src/LummaSimulator'
$verifiedSource = Join-Path $PWD 'artifacts\phase65e-af640b0200e544b29f01677f30f478c2\source'
dotnet restore "$verifiedSource\LummaSimulator.slnx" --locked-mode -r win-x64 -p:SelfContained=true
if ($LASTEXITCODE -ne 0) { throw 'RID-restore mislukt; stoppen en onderzoeken.' }
& .\packaging\Build-EventEdition.ps1 -VerifiedSourceDirectory $verifiedSource -OutputDirectory (Join-Path $PWD 'dist\v0.6.5-beta.1')
if (-not $?) { throw 'Publicatie mislukt; niets vrijgeven.' }
Get-FileHash .\dist\v0.6.5-beta.1\LummaSimulator-EventEdition-v0.6.5-beta.1-win-x64.zip -Algorithm SHA256
```

Verwachte pakketinhoud: `Portal/` (webportal/runtime), `Simulator/` (WPF/runtime), `Simulator/LocalTestServer/` (sidecar/runtime), acht launcher/scripts, README, release notes, VERSION.txt, documentatie en SHA256SUMS.txt. Dit is de bouwspecificatie, geen aangetroffen ZIP-inhoud.

Geen bestaande portal gestart/gestopt. Geen actieve databases, accounts, credentials, certificaten of configuratie gewijzigd. Geen nieuwe restore, commit, push, publieke repository of release.

# Expliciete NuGet-configuratie — verificatie 12 november 2026

De gebruikersinstructie om met een expliciete leesbare feedconfiguratie te restoren is uitgevoerd. Alle lokale wijzigingen zijn behouden.

## Vaststellingen

- Eerst gelezen: artifacts/dev-start-105b0a830d8b48fa840efa827f6e0f9c/restore.log. De oorspronkelijke fout betreft %USERPROFILE%/AppData/Roaming/NuGet/NuGet.Config (toegang geweigerd).
- Er is geen global.json in de checkout, de opgegeven bronkopie of de bovenliggende C:/src en C:/ directories. Daarom is de aanwezige .NET SDK 10.0.404 gebruikt, zonder SDK-/pakketupgrade of nieuwe SDK-pin.
- De enige directe PackageReferences zijn Fido2.AspNet 4.3.1 en Microsoft.Data.Sqlite 10.0.14. Geen expliciete private RestoreSources/RestoreAdditionalProjectSources of repo-feedconfig gevonden. De bestaande lockfiles zijn behouden; beschikbaarheid van alle locked versies op het netwerk is nog niet door restore bevestigd.

## Wijzigingen

- NuGet.Config:1: expliciete repo-configuratie met alleen https://api.nuget.org/v3/index.json; geen credentials.
- packaging/DevBuild.ps1:32: de bestaande shared restore-aanroep geeft --configfile met de gekopieerde NuGet.Config mee; --locked-mode en RuntimeIdentifiers=win-x64 blijven behouden. De build blijft --no-restore. Zowel BuildOnly/verify als de normale ontwikkelstart gebruiken Invoke-DevBuild.

## Werkelijk uitgevoerd en resultaten

1. Locked restore in precies de eerdere source-map met --configfile C:/src/LummaSimulator/NuGet.Config en SDK 10.0.404: mislukt. De fout verschuift van GetRestoreSettings naar RestoreTask, NuGet.targets:198; die probeert nog steeds de genoemde gebruikersconfig te lezen. Log: artifacts/dev-start-105b0a830d8b48fa840efa827f6e0f9c/restore-explicit-config.log.
2. GenerateRestoreGraphFile met RestoreConfigFile expliciet ingesteld: geslaagd. Voor alle elf projecten verwijst configFilePaths uitsluitend naar de repo-config; de source is nuget.org. Hiermee is aangetoond dat de expliciete configuratie de restoregraph bereikt. Het verklaart niet waarom een latere interne NuGet-codepad alsnog de gebruikersconfig opent. Bewijs: restore-graph.json en restore-graph.log naast de eerdere source-map.
3. Start-LummaDev.bat verify via de aangepaste shared build-route: nieuwe kopie artifacts/dev-start-5c3328592b174d7b87376f7d7e44e396/source. Ook deze restore faalt op NuGet.targets:198 met dezelfde gebruikersconfig-toegangsfout. De drie aanwezige lockfiles zijn byte-identiek aan de checkout. Log: artifacts/explicit-nuget-verification/dev-verify.log en de nieuwe restore.log.
4. Start-LummaDev.bat normaal daadwerkelijk aangeroepen: gestopt vóór appstart met de generieke veilige preflight-foutmelding. Een afzonderlijke read-only Get-CimInstance-probe met ErrorAction Stop bevestigt Access denied voor procesinventarisatie. Er is daarom geen betrouwbare inventarisatie van actieve Lumma-processen. Geen processen gestopt, geen actieve database of runtimepad geopend of gewijzigd. Log: artifacts/explicit-nuget-verification/dev-start.log.
5. De bestaande expliciete Node-regressieset daadwerkelijk herhaald: 116 tests, 114 geslaagd, 2 overgeslagen, 0 mislukt. Inclusief NL/EN-teksten en virtuele DOM-toestandstests. Log: artifacts/explicit-nuget-verification/node.log.
6. PowerShell-parser van DevBuild.ps1, NuGet-config-XML en git diff --check: geslaagd.

Build, .NET-tests en echte portal-browsertests zijn niet opnieuw gestart nadat restore faalde. Er wordt geen geslaagde build geclaimd en een nieuwe NETSDK1004-gevolgfout zou geen aanvullende compileerinformatie geven. Geen rechten, gebruikersprofiel, cache-identiteit of lockfiles aangepast om de blokkade te omzeilen.

## Nog nodig

Een toegestane runner-/Windows-context waarin NuGet zijn gebruikersconfig kan lezen en de ontwikkelstarter zijn normale proces-/poortinventarisatie kan uitvoeren. Daarna dezelfde expliciete locked restore afronden, vervolgens Release-build --no-restore, relevante .NET-regressies met expliciete geïsoleerde LUMMA_TEST_WEB_DLL en normale appstart. Pas dan kunnen eventuele compileerfouten zichtbaar worden en de echte registratie-/aanmeld-/oefening-/herhaal-/uitslagflow in NL/EN op desktop en mobiel worden geverifieerd. Fysieke YubiKey-, telefoon-, DNS-/TLS- en Windows-schermcontroles blijven open.

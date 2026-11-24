# Verifier: padfout na geslaagde regressie

11 november 2026. Onderzocht resultaat: `%TEMP%/LummaHelperVerification_60e292e9ed984539a5c311fb846dbad0`.

`FULL_REGRESSION_EXITCODE=0` en `All configured regression suites passed` waren al aanwezig. De daaropvolgende fout kwam uit de bron-/lockfile-inventarisatie, niet uit een test. De oorspronkelijke `packaging/Verify-HelperDevelopment.ps1:76` voerde uit:

```powershell
Get-ChildItem -LiteralPath $source -Recurse -File -Filter 'packages.lock.json'
```

De controle die `bin`/`obj` oversloeg stond in de loopbody, dus pas ná recursief doorzoeken van deze mappen. Daardoor bezocht de verifier ook `source/tests/LummaSimulator.RegressionTests/bin/HelperVerify_e1d65a080c6e/test-data/DemoRuns/Verification-0704bb16-b34c-4484-952c-94489def3049`.

Die telemetrycontainer bestaat nog, de bovenliggende `DemoRuns` bestaat ook en de container is leeg. `TelemetryChecks` verwijdert bewust de eigen childartefacten via de bestaande cleanup en toetst dat `service.DemoRoot` leeg is; de scopecontainer wordt behouden. Er is dus geen ontbrekende tijdelijke map die opnieuw moet worden aangemaakt. De verifier heeft uitsluitend projectdirectories onder `source` nodig.

Met het concrete lange gebruikersprofielpad telt de mapnaam 241 tekens en het door de filter gemaakte zoekpad inclusief `/packages.lock.json` precies 260 tekens. Een read-only probe met Windows PowerShell 5.1 reproduceert op die bestaande lege map `DirectoryNotFoundException` / `DirIOError`. Dit is geen poging om een bestaand lockfile in de telemetrymap te hashen; het is een fout tijdens de onnodige gefilterde inventarisatie. Het volledige absolute zoekpad met gebruikersprofiel staat uitsluitend buiten Git in `post-regression-path-diagnosis.txt` in de resultaatmap.

De fix gebruikt de bekende `.csproj`-paden uit het bewaarde worktree-snapshot en controleert alleen hun naastgelegen `packages.lock.json`. Er wordt niet meer recursief in build-/runtime-/testdata gezocht. De afsluitende controle is apart uitvoerbaar via `packaging/Complete-HelperVerification.ps1 -ResultDirectory ...`; gewone nieuwe verificaties roepen dezelfde stap na de regressie aan.

Alleen deze metadata-afsluiting is opnieuw uitgevoerd in Windows PowerShell 5.1: procesexitcode 0, `VERIFIER_FINALIZATION_EXITCODE=0`, `VERIFICATION_COMPLETE=True`, `TESTS_GREEN=True` en `COMPILED_SOURCE_MATCH=True`. Er zijn 358 snapshotbestanden gecontroleerd en vier project-lockfiles gevonden. Eén gegenereerd lockfile staat nog voor afzonderlijke review op de lijst. De bestaande tests zijn niet herhaald en oorspronkelijke logs zijn niet overschreven. `result.txt` bevat de eerdere fout als historie en de nieuwe eindstatus als aanvulling.

Alle geteste programma-invoer is ongewijzigd. Alleen de verifier is na de run aangepast; daarom vermeldt de afsluiting eerlijk `WORKTREE_SNAPSHOT_UNCHANGED=False` met de specifieke verifierwijziging. De actuele verifierhashes staan afzonderlijk bij de resultaten. Deze reparatie maakt geen wijzigingen aan actieve gegevens, installaties, Windows-longpathinstellingen of certificaten.

# Blijvend aanmeldsucces op /Demo — 12 november 2026

## Oorzaak en wijziging

passkeys.js maakte na een geslaagde authenticate/verify-respons eerst auth-confirmation zichtbaar en voerde direct daarna window.location.assign('/SignedIn') uit. Het was een directe JavaScript-navigatie, geen succes-timer of serverredirect. De andere timer in deze ceremonie is de bestaande 125-secondenannulering van een hangende WebAuthn-aanvraag; die navigeert niet. De lumma-authenticated-listener vernieuwt alleen de toestemmingsstatus.

De navigatie is verwijderd. Het groene blok op /Demo toont Je bent aangemeld, de autoritatieve tijdelijke alias, de relevante SignedIn-uitleg en Start de demo-oefening. Registratie voltooid blijft een afzonderlijke registratiebevestiging en maakt het groene aanmeldblok niet zichtbaar. De bestaande fictieve oefenpartial en clickfix.js zijn op /Demo opgenomen: een expliciete klik start de oefening op dezelfde pagina. BeginExercise blijft de bestaande antiforgery-/Origin-/IdentitySession-controle op de server gebruiken. Er is geen toegangspoort versoepeld. De bestaande /SignedIn-route blijft compatibel voor directe links.

Bij herladen toont de server het groene blok alleen wanneer SecurityAuditService.OwnerAsync de bezoekersauthenticatie daadwerkelijk bevestigt. Alleen een credential of een historische LastAuthenticatedAt is niet voldoende. Het blok behoudt zijn groene huisstijl; de server vult de bekende verificatietijd opnieuw in.

## Bestanden

| Bestand | Regel | Wijziging |
|---|---|---|
| LummaSimulator.Web/wwwroot/js/passkeys.js | 147 | Automatische navigatie verwijderd |
| LummaSimulator.Web/Pages/Demo.cshtml | 22 | Groen blok, uitleg, expliciete oefenknop en gedeelde oefening |
| LummaSimulator.Web/Pages/Demo.cshtml.cs | 30 | Authenticated rendering via bestaande OwnerAsync |
| tests/WebAuthn.Ux.Tests/passkeys.test.cjs | 411 | Registratie apart; aanmeldblok blijft 10,1 echte seconden zonder navigatie |
| tests/LummaSimulator.Web.Tests/clickfix-browser.mjs | 71 | Schone synthetische browser met virtuele authenticator: registratie, login, tien seconden op /Demo, klik en oefening |

## Werkelijk uitgevoerd

- 44 relevante Node-tests geslaagd, 0 fouten. Inclusief een echte wachtduur van 10,1 seconden in de JavaScript-/DOM-harness; alias en succesblok blijven behouden en geen navigatie wordt aangeroepen. Registratie-only en geweigerde verificatie tonen geen aanmeldsucces. De bestaande oefeningstests controleren expliciete start, dubbele klik en voltooiing.
- JavaScript-syntax en git diff --check slagen.
- Start-LummaDev.bat verify met locked restore: nieuwe artifacts/dev-start-b493e8ed73eb44808d30eea993db0727. Restore faalt op geweigerde leestoegang tot %USERPROFILE%\AppData\Roaming\NuGet\NuGet.Config. Geen omweg of toegangsrechtenwijziging uitgevoerd. Web.Tests-runner in de geïsoleerde kopie met --no-restore en expliciet LUMMA_TEST_WEB_DLL geprobeerd: NETSDK1004, ontbrekende project.assets.json.
- De uitgebreide echte browserregressie kan daardoor niet op de bijgewerkte geïsoleerde portal worden uitgevoerd. De tiensecondencontrole hierboven is geen browseracceptatie of fysieke passkeytest. Geen actieve portal/database gestart of bestaande processen herstart.

Logs: artifacts/demo-stays-signed-in-5b18aa63396447e9b7af35b7f5566ac0/node.log en web-tests.log; restore.log in de dev-start-map.

Nog open: na herstel van normale NuGet-leestoegang build en browserfixture uitvoeren; in de browser bevestigen dat registratie geen aanmeldsucces toont, /Demo na login minstens tien seconden dezelfde URL houdt en de knop daarna de server-gecontroleerde oefening opent. NL/EN en de fysieke passkeydialoog zijn hiermee niet live bevestigd.

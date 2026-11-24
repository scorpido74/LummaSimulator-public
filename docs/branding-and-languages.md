# Branding, Windows-icoon en talen

Datum: 4 november 2026. Wijzigingen aan presentatie, lokale assets en buildmetadata; geen wijziging van demo-engine, authenticatie, pairing, identity-export of database. De nog niet gecommitteerde fase-6.3- en launcherwijzigingen blijven aanwezig.

## Aangetroffen situatie

- WPF had geen `ApplicationIcon`, `Window.Icon`, eigen ICO of logo-afbeelding in de huidige XAML. Het ontbrekende Infinigate-logo was dus geen aantoonbaar schaalprobleem. Uit deze inventarisatie volgt niet wanneer een eventueel historisch logo is verdwenen.
- De portal gebruikte lokale `infinigate-official.svg` en `yubico-official.svg`. Alleen `Experience.OrganizationName` was configureerbaar; de afbeelding was vast Infinigate.
- Portal: Nederlands en Engels via bestaande UiText-resources, `local-text`, `localization.js` en de beveiligde taalvoorkeurroute. Wisselen kan tijdens gebruik. Afzonderlijke bezoekers-/presentatorcookies bewaren de voorkeur maximaal 180 dagen; de bestaande bezoekerswissel kan de bezoekersvoorkeur resetten. Nederlands is de standaard. Deze mechanismen zijn behouden.
- WPF: alleen LocalHttpText-resources voor enkele HTTP-statussen, afhankelijk van de bestaande UI-cultuur. Veel Engelse en Nederlandse teksten stonden rechtstreeks in XAML, viewmodels en scenario-/simulatiegegevens. Een algemene taalkeuze ontbrak.
- De fase-6.3-ClickFix-, herstel-, uitleg- en afrondingsteksten waren al via de twee portaalcatalogi beschikbaar. De vaste oefencode, eventnamen en identifiers zijn technische gegevens, geen vertaalbare instructiepayloads.

## Applicatie-icoon

`LummaSimulator.App/Assets/LummaSimulator.ico` is één neutraal ICO met PNG-beelden van **16, 32, 48 en 256 pixels**: een donker schild met een L/circuitmarkering. Geen Infinigate-/Yubico-logo. De reproduceerbare vectorgebaseerde generator is `tools/New-LummaIcon.ps1`.

Hetzelfde bestand staat in `ApplicationIcon` van het App-project en als embedded WPF-resource in `MainWindow.Icon`. Daarmee zijn executable-icoon en venstericoon ingesteld voor titelbalk, taakbalk en Alt+Tab. De bestaande build/publicatie neemt de iconen via de normale exe-/assemblyresources mee; geen los runtime-ICO of aparte resellerbuild nodig. De ontwikkellauncher start de actuele gebouwde apphost en heeft geen iconoverride. De Event Edition-bouwer publiceert hetzelfde project.

**Nog niet runtime bevestigd:** restore is geblokkeerd, dus geen nieuwe exe/publicatie gebouwd of gestart. Een correct ingestelde bron is geen bewijs dat Windows het nieuwe taakbalkicoon al heeft getoond. Het bronicoon is wel visueel bekeken en alle vier beelden zijn door Windows gedecodeerd.

Na een geslaagde geïsoleerde build: controleer zowel de nieuwe ontwikkel-exe via `Start-LummaDev.bat` als een afzonderlijke schone Event Edition-publicatie. Bekijk Verkenner, titelbalk, taakbalk en Alt+Tab. Sluit de oude WPF-app normaal voordat de nieuwe wordt gestart. Controleer bij een oud icoon eerst het echte executablepad en een eventueel vastgemaakt oud snelkoppelingsdoel. Windows kan iconen cachen; een oude vastgemaakte verwijzing kan na controle handmatig opnieuw worden vastgemaakt. Er wordt geen iconcache verwijderd, Explorer gestopt of Windows-register aangepast.

## Resellerbranding configureren

Applicatie: **LummaSimulator**. Reseller/distributeur: standaard **Infinigate**. Yubico blijft herkenbaar in de passkeyportal; de WPF-aanvalspresentatie krijgt geen vendorlogo als applicatie-icoon. De bestaande WebAuthn-displaynaam, RP-ID, origins en TLS-configuratie zijn niet gewijzigd.

WPF leest de bestaande `LummaSimulator.App.settings.json` naast de executable. Voeg brandingvelden aan die bestaande instellingen toe; vervang niet de opslag-/portalinstellingen:

```json
{
  "OrganizationName": "Voorbeeldreseller",
  "LogoFile": "reseller.png",
  "DefaultLanguage": "nl-NL"
}
```

Plaats het logo in **Branding/reseller.png** naast de WPF-executable. Standaard Infinigate gebruikt een ingebedde statische WPF-DrawingImage, afgeleid van het reeds aanwezige lokale SVG. Custom PNG's in de projectmap `LummaSimulator.App/Branding` worden door normale build/publicatie meegenomen. Na distributie kunnen instellingen en logo ook zonder nieuwe build worden toegevoegd. WPF leest branding en standaardtaal bij het starten; de runtime-taalkeuze schrijft geen voorkeurbestand.

Portal hergebruikt de bestaande Experience-configuratie:

```json
{
  "Experience": {
    "OrganizationName": "Voorbeeldreseller",
    "LogoFile": "reseller.png"
  }
}
```

Plaats het logo in **wwwroot/branding/reseller.png** van die portalinstallatie. De bestaande configuratieproviders gelden ook voor deze velden. Voeg alleen de gewenste brandingvelden toe; RP-ID/origins, certificaten en opslagpaden horen niet bij resellerbranding. Het ingebouwde Infinigate-logo blijft de standaard wanneer geen custom logo is ingesteld en de organisatienaam Infinigate is.

Beide interfaces tonen de organisatienaam als fallback. Een reseller zonder eigen afbeelding krijgt niet stilzwijgend het Infinigate-logo. Het web encodeert de naam; WPF toont gewone tekst. De afbeeldingen worden proportioneel weergegeven met `object-fit: contain` respectievelijk `Stretch=Uniform` en begrensde maten.

Custom logo's: uitsluitend een eenvoudige PNG-bestandsnaam, maximaal 100 tekens, bestand maximaal 2 MiB, headerafmetingen maximaal 4096×4096. URL's, UNC-paden, traversal en reparsepunten worden geweigerd; uitsluitend een lokale vaste schijf en de vaste brandingmap worden gebruikt. WPF decodeert de afbeelding zonder blijvende filelock; decodeerfouten vallen terug op tekst. De portal selecteert alleen de lokale asset-URL na pad-/headercontrole; bij een ontbrekend bestand volgt tekstfallback. Dit is geen volledige PNG-contentanalyse: beheer levert vertrouwde artwork aan in de beheerde installatiemap. Er zijn geen externe logo-aanvragen of upload-/bestandsbeheerendpoints toegevoegd.

## Taalgedrag

WPF heeft altijd bereikbare **NL/EN**-knoppen boven het werkvlak, ook tijdens een run en op het introductiescherm. Nederlands is standaard; `DefaultLanguage=en-GB` kiest Engels bij een volgende start. De keuze blijft in geheugen behouden bij tabwisseling, reset en nieuwe runs. Na herstart geldt opnieuw de geconfigureerde standaard. De portal en WPF houden hun taalvoorkeuren onafhankelijk; er is geen nieuw communicatiecontract.

`DesktopText.resx` en `DesktopText.en-GB.resx` gebruiken hetzelfde bestaande .NET ResourceManager-patroon als LocalHttpText. XAML-bindingen vernieuwen zichtbare tekst bij taalwisseling. Intro/rollen, zes fasekaarten, status-/beoordelingslabels, bekende fouten, simulatorberichten, monitor, Event Edition, afronding en bekende LIVE-telemetriemeldingen hebben catalogusteksten. De engine, enumwaarden, commands, run-ID's, logregels en exports blijven canoniek; alleen hun **weergave** wordt vertaald. Taalwisseling start of reset niets. Technische namen/productnamen en onbekende OS-/library-exceptiedetails blijven origineel, zodat diagnostiek niet wordt verzonnen of bewijs wordt veranderd. Geen claim dat iedere mogelijke OS-fout vertaald is.

## Verificatie en beperkingen

- Node: vijf UX-bestanden, **61 geslaagd, 0 mislukt, 2 overgeslagen**. De overgeslagen gevallen controleren de exacte icoonbeelden in een daadwerkelijk gebouwde en gepubliceerde exe; zulke actuele binaries ontbreken hier.
- Windows-assetdecoder: vier ICO-resoluties en het lokale Infinigate-vectorlogo succesvol gedecodeerd met `verification/Test-BrandingArtwork.ps1`. Geen appvenster of taakbalktest.
- Bestaande verpakkingstests: **72 checks geslaagd**. Windows PowerShell-procescompatibiliteit: **27 checks geslaagd**, uitsluitend synthetische probe.
- XAML-/project-XML en resource-uniciteit gecontroleerd; `git diff --check` geslaagd. Node controleert onder meer resourceverwijzingen, placeholderpariteit, zes fasebenamingen, ClickFix-dekking en logo-containment in de bron. Dit zijn geen uitgevoerde responsive browser-/DPI-tests.
- Nieuwe .NET-tests voor logo-path/fallback/headerbegrenzing en WPF-taalwisseling zijn toegevoegd, maar **niet uitgevoerd**.
- Nieuwe isolated bronkopie met fase 6.3: `artifacts/branding-31358c89b2cd419987e868da9d5125ee/source`. Locked restore van alle zeven projecten faalt opnieuw op toegang tot `C:\Users\TestUser\AppData\Roaming\NuGet\NuGet.Config`. Geen omweg via andere configuratie/identiteit gebruikt. Daardoor zijn compilatie van fase 6.3 en deze wijzigingen, volledige regressie, Razor-rendering en publicatie **niet bevestigd**. Definitieve logs: `node-acceptance.log`, `artwork.log`, `packaging-acceptance.log`; restoreblokkade in `restore.log`, alle in de bovenliggende artefactmap. De laatste bronaanpassingen zijn in de bronkopie bijgewerkt; restore bleef een voorafgaande configuratieblokkade, geen compilatie-uitkomst.

Na een toegestane isolated build kunnen de exe-checks draaien met `LUMMA_TEST_APP_EXE` en `LUMMA_TEST_PUBLISHED_APP_EXE`, gevolgd door `node --test tests/WebAuthn.Ux.Tests/branding.test.cjs`. Gebruik uitsluitend die eigen build-/publishpaden. Bij publish-/sidecaracceptatie blijft `verification/Test-LocalHttpPublished.ps1` met expliciete geïsoleerde PublishDirectory vereist. Bestaande installaties zijn niet gepubliceerd of gestart.

Handmatig open: werkelijk Windows-icoon/cache en Alt+Tab; NL/EN wisselen tijdens actieve presentatie zonder statusverandering; lange vertalingen en resellernaam op kleine schermen; custom/ontbrekend/corrupt logo; logo's op 320/768/1024/1920 CSS-pixels, echte 200% browserzoom en Windows-DPI. Bestaande fysieke passkey-/TLS-/mobiele acceptatie blijft nodig.

## Bestanden in deze wijziging

| Gebied | Bestanden |
|---|---|
| Icoon/standaardlogo | nieuw `LummaSimulator.App/Assets/LummaSimulator.ico`, `Assets/Branding.xaml`, `tools/New-LummaIcon.ps1`; App.csproj en MainWindow.xaml |
| WPF-branding/talen | DesktopSettings.cs, App.settings.json, MainWindow.xaml.cs; nieuw DesktopLanguage.cs, DesktopText.resx, DesktopText.en-GB.resx |
| Gedeelde logo-controles | nieuw `shared/BrandingAssets.cs`; linked source in App.csproj en Web.csproj |
| Portalbranding | ExperienceSettings.cs, Pages/Shared/_Layout.cshtml, wwwroot/css/site.css; nieuw Services/ResellerBranding.cs |
| Packaging | packaging/Build-EventEdition.ps1: branding-/taaldefaults en deze handleiding in het pakket |
| Tests | nieuw BrandingChecks.cs, branding.test.cjs, Test-BrandingArtwork.ps1; Web.Tests/Program.cs, RegressionTests.csproj, verification/Program.cs |
| Documentatie | dit document, README.md, Event Edition-installatiehandleiding en ontwikkelstartinstructies |

Geen commit, push, actieve databasehandeling, certificaatwijziging, nieuwe dependency of Windows-instelling. `codex-start.bat`, Event Edition-launcherbeveiliging en de identity-auditexporter blijven ongemoeid.

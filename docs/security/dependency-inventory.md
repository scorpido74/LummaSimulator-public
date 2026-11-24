# Dependency inventory - LummaSimulator

Datum: 30 oktober 2026. Offline metadata-inventaris; geen vulnerability-/license-/signaturevrijgave.
[Beleid](security-requirements.md) | [assurancerapport](security-assurance-report.md) | [toekomstig scanproces](security-test-plan.md).

## Bronnen en grenzen

Gelezen: alle .csproj, LummaSimulator.slnx, shared/LocalTestServer.Build.targets, de drie aanwezige packages.lock.json-bestanden en lokale bron. Geen restore, NuGet/feedcontact, package-installatie of scannerupdate. Version/license/advisorydata zijn niet online opgezocht.

Productieweb-lock: 17 packages, waarvan twee direct en 15 transitief. Webtest-lock: 31 packages plus projectreferentie lummasimulator.web. Unie: 31 verschillende package-ID/versiecombinaties. De testgraph bevat 14 extra packages; dit bewijst niet dat die in een webpublish zitten. Framework-referenties kunnen dependencyselectie beïnvloeden; daadwerkelijke publish/.deps/RID-SBOM is leidend voor uitlevering.

## Eigen componenten en platform

| Component | Target / dependency | Distributiegrens |
| --- | --- | --- |
| LummaSimulator.App | net10.0-windows, WPF, assemblyversie 0.4.0; geen directe NuGet-reference | WindowsDesktop/.NET-runtime plus eigen helper; runtimepatch/RID niet vastgelegd in project |
| LummaSimulator.Web | net10.0, ASP.NET Core, assemblyversie 0.4.1 | Microsoft.NETCore.App/Microsoft.AspNetCore.App plus onderstaande webgraph |
| LocalTestServer | net10.0, geen directe NuGet-reference | Eigen helperproces; shared protocolbron wordt meegecompileerd |
| shared | RuntimePaths.cs, LocalTestServerProtocol.cs, MSBuild-targets | Eigen gedeelde bron; targets kopiëren/publishen helper |
| Web.Tests | Eigen executable assertions en softwareauthenticator | Test-only; nooit meepubliceren |
| SmokeTests / Verification | Gekoppelde productiebron / WPF-testproject | Test-only; kunnen echte lokale processen starten |
| JavaScript-tests | Node ingebouwde node:test/assert/vm/fs/path/crypto | Geen package.json/npm-runtimepakket gevonden; node:vm is uitsluitend testuitvoering |
| Webfrontend | Eigen JS/CSS, resx-catalogi en twee lokale SVG-merken | Geen CDN/frontendframework/analyticsclient aangetroffen. Assetrechten/herkomst moeten releasebeheer apart documenteren |
| ATT&CK-referenties | Lokale Enterprise-20.0-subset en Navigator-4.5-formaatreferentie | Testdata/documentatie; geen malwaredependency of automatische externe fetch |
| Windows/OS | whoami.exe, WPF, filesystem/socket/DNS/DPAPI/Windowsprincipal | OS-onderdelen buiten NuGet-SBOM; exacte hostpatches en indirekte API-calls nog vast te leggen |
| Buildtoolchain | .NET SDK/MSBuild/Roslyn en Node | Geen global.json of vastgelegde Node-toolchain aangetroffen. Release moet SDK/runtime/toolhash vastzetten |

Geen directe malwaredependency gevonden. Namen en packagehashes zijn geen bewijs dat alle packagecode veilig is; managed/native binaries en updatergedrag zijn niet integraal onderzocht. Authenticator, browser/passkeymanager, OS en certificateprovider vallen als externe trustdependencies buiten appbron.

## Complete package-unie uit lockfiles

| Package | Resolved versie | Scope | Directe edges in het gekozen lockrecord |
| --- | --- | --- | --- |
| Fido2 | 4.3.1 | Transitief web | Fido2.Models, Microsoft.IdentityModel.JsonWebTokens, NSec.Cryptography |
| Fido2.AspNet | 4.3.1 | Direct web | Fido2, Fido2.Models |
| Fido2.Models | 4.3.1 | Transitief web | Microsoft.Bcl.Memory |
| Microsoft.Bcl.Memory | 9.0.14 | Transitief web | Geen in dit lockrecord |
| Microsoft.Bcl.TimeProvider | 8.0.1 | Transitief web | Geen in dit lockrecord |
| Microsoft.Data.Sqlite | 10.0.14 | Direct web | Microsoft.Data.Sqlite.Core, SQLitePCLRaw.bundle_e_sqlite3, SQLitePCLRaw.core |
| Microsoft.Data.Sqlite.Core | 10.0.14 | Transitief web | SQLitePCLRaw.core |
| Microsoft.Extensions.Configuration | 9.0.0 | Alleen testgraph | Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Primitives |
| Microsoft.Extensions.Configuration.Abstractions | 9.0.0 | Alleen testgraph | Microsoft.Extensions.Primitives |
| Microsoft.Extensions.Configuration.Binder | 9.0.0 | Alleen testgraph | Microsoft.Extensions.Configuration.Abstractions |
| Microsoft.Extensions.DependencyInjection | 9.0.0 | Alleen testgraph | Microsoft.Extensions.DependencyInjection.Abstractions |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.0 | Alleen testgraph | Geen in dit lockrecord |
| Microsoft.Extensions.Diagnostics | 9.0.0 | Alleen testgraph | Microsoft.Extensions.Configuration, Microsoft.Extensions.Diagnostics.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions |
| Microsoft.Extensions.Diagnostics.Abstractions | 9.0.0 | Alleen testgraph | Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Options |
| Microsoft.Extensions.Http | 9.0.0 | Alleen testgraph | Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Diagnostics, Microsoft.Extensions.Logging, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options |
| Microsoft.Extensions.Logging | 9.0.0 | Alleen testgraph | Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options |
| Microsoft.Extensions.Logging.Abstractions | 9.0.0 | Alleen testgraph | Microsoft.Extensions.DependencyInjection.Abstractions |
| Microsoft.Extensions.Options | 9.0.0 | Alleen testgraph | Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Primitives |
| Microsoft.Extensions.Options.ConfigurationExtensions | 9.0.0 | Alleen testgraph | Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Configuration.Binder, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Primitives |
| Microsoft.Extensions.Primitives | 9.0.0 | Alleen testgraph | Geen in dit lockrecord |
| Microsoft.IdentityModel.Abstractions | 8.2.0 | Transitief web | Geen in dit lockrecord |
| Microsoft.IdentityModel.JsonWebTokens | 8.2.0 | Transitief web | Microsoft.Bcl.TimeProvider, Microsoft.IdentityModel.Tokens |
| Microsoft.IdentityModel.Logging | 8.2.0 | Transitief web | Microsoft.IdentityModel.Abstractions |
| Microsoft.IdentityModel.Tokens | 8.2.0 | Transitief web | Microsoft.Bcl.TimeProvider, Microsoft.IdentityModel.Logging |
| NSec.Cryptography | 25.4.0 | Transitief web | libsodium |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.13 | Transitief web | SQLitePCLRaw.lib.e_sqlite3, SQLitePCLRaw.provider.e_sqlite3 |
| SQLitePCLRaw.core | 2.1.13 | Transitief web | Geen in dit lockrecord |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.13 | Transitief web | Geen in dit lockrecord |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.13 | Transitief web | SQLitePCLRaw.core |
| System.Formats.Cbor | 9.0.0 | Alleen testgraph | Geen in dit lockrecord |
| libsodium | 1.0.20.1 | Transitief web | Geen in dit lockrecord |

Voor Fido2 4.3.1 noemt de testgraph aanvullend Microsoft.Extensions.Http 9.0.0 en System.Formats.Cbor 9.0.0; het web-lockrecord noemt Fido2.Models, Microsoft.IdentityModel.JsonWebTokens en NSec.Cryptography. Beide graphs zijn expliciet geïnventariseerd; geen graph is zonder publishbewijs gelijkgesteld aan shipped runtime.

Directe references staan in LummaSimulator.Web/LummaSimulator.Web.csproj:12-15:
- Fido2.AspNet 4.3.1: WebAuthn-servervalidatie; app implementeert de handtekeningcryptografie niet zelf.
- Microsoft.Data.Sqlite 10.0.14: SQLite-opslag; geen EF Core.

NSec.Cryptography/libsodium en SQLitePCLRaw.lib.e_sqlite3 omvatten native trust/deploymentoppervlak. Vergelijk per publish-RID werkelijk meegeleverde DLL/.so/.dylib met SBOM en packagecontent; Windows-only output sluit andere RID-assets in het package niet uit.

## Integriteitsmetadata

De hieronder overgenomen contentHash is NuGet-lockmetadata (SHA-512/base64), **niet** de SHA-256 van een uitgeleverd bestand en **geen** eigen signatuurvalidatie. Alle aanwezige lockrecords hebben een contentHash behalve de interne projectreferentie.

| Package | contentHash uit lokaal lockbestand |
| --- | --- |
| Fido2@4.3.1 | `maWrkMwX1YUfKF9lsNRT3fVW6QaTjsTLYi7vN01wYGj3hE8jQsbeC0iQAbKMc8etVLAQg7Ui3S8wiaOe/mWJLA==` |
| Fido2.AspNet@4.3.1 | `tPafvfUQDdTXm5fyqLEMaBjeAwYYzne9+seMrbv95tKDlZI+HkMF6tNExQG/VqiGvZGecG3jXKC3OLt4wOn2Qg==` |
| Fido2.Models@4.3.1 | `dshL6l+3yTE3H0XLhzBfUNSC8h2y92ud4ezdgwYg/GJWjKF7m1gosZVuCCz4IYxOlARg2O+w0I+ZKSYrgmSl5w==` |
| Microsoft.Bcl.Memory@9.0.14 | `laQCcjTM2FnbyznkAu9hwBuQh3z2/OtJKwjGkJOUevylFpFXZIUVq/+MU2jM0HQm1n5wsjjwtLIDOiWJ6Nai3A==` |
| Microsoft.Bcl.TimeProvider@8.0.1 | `C7kWHJnMRY7EvJev2S8+yJHZ1y7A4ZlLbA4NE+O23BDIAN5mHeqND1m+SKv1ChRS5YlCDW7yAMUe7lttRsJaAA==` |
| Microsoft.Data.Sqlite@10.0.14 | `muyroan6rKrf0omZL8UF7Kw5uOGp/yBg1L/XWvkFN3WhKaNtK2IWqRZw93nIwVqjhTVDO0l4CEGhHIh+mb+sKg==` |
| Microsoft.Data.Sqlite.Core@10.0.14 | `1p9rO3Q2X/L/9YrYog3vaDzi8u+WZfJCOkDYoHqmofqYP0DLPLPyZcS2Rnh/79JXAodgbs1AgsaNwLpIvC3Xew==` |
| Microsoft.Extensions.Configuration@9.0.0 | `YIMO9T3JL8MeEXgVozKt2v79hquo/EFtnY0vgxmLnUvk1Rei/halI7kOWZL2RBeV9FMGzgM9LZA8CVaNwFMaNA==` |
| Microsoft.Extensions.Configuration.Abstractions@9.0.0 | `lqvd7W3FGKUO1+ZoUEMaZ5XDJeWvjpy2/M/ptCGz3tXLD4HWVaSzjufsAsjemasBEg+2SxXVtYVvGt5r2nKDlg==` |
| Microsoft.Extensions.Configuration.Binder@9.0.0 | `RiScL99DcyngY9zJA2ROrri7Br8tn5N4hP4YNvGdTN/bvg1A3dwvDOxHnNZ3Im7x2SJ5i4LkX1uPiR/MfSFBLQ==` |
| Microsoft.Extensions.DependencyInjection@9.0.0 | `MCPrg7v3QgNMr0vX4vzRXvkNGgLg8vKWX0nKCWUxu2uPyMsaRgiRc1tHBnbTcfJMhMKj2slE/j2M9oGkd25DNw==` |
| Microsoft.Extensions.DependencyInjection.Abstractions@9.0.0 | `+6f2qv2a3dLwd5w6JanPIPs47CxRbnk+ZocMJUhv9NxP88VlOcJYZs9jY+MYSjxvady08bUZn6qgiNh7DadGgg==` |
| Microsoft.Extensions.Diagnostics@9.0.0 | `0CF9ZrNw5RAlRfbZuVIvzzhP8QeWqHiUmMBU/2H7Nmit8/vwP3/SbHeEctth7D4Gz2fBnEbokPc1NU8/j/1ZLw==` |
| Microsoft.Extensions.Diagnostics.Abstractions@9.0.0 | `1K8P7XzuzX8W8pmXcZjcrqS6x5eSSdvhQohmcpgiQNY/HlDAlnrhR9dvlURfFz428A+RTCJpUyB+aKTA6AgVcQ==` |
| Microsoft.Extensions.Http@9.0.0 | `DqI4q54U4hH7bIAq9M5a/hl5Odr/KBAoaZ0dcT4OgutD8dook34CbkvAfAIzkMVjYXiL+E5ul9etwwqiX4PHGw==` |
| Microsoft.Extensions.Logging@9.0.0 | `crjWyORoug0kK7RSNJBTeSE6VX8IQgLf3nUpTB9m62bPXp/tzbnOsnbe8TXEG0AASNaKZddnpHKw7fET8E++Pg==` |
| Microsoft.Extensions.Logging.Abstractions@9.0.0 | `g0UfujELzlLbHoVG8kPKVBaW470Ewi+jnptGS9KUi6jcb+k2StujtK3m26DFSGGwQ/+bVgZfsWqNzlP6YOejvw==` |
| Microsoft.Extensions.Options@9.0.0 | `y2146b3jrPI3Q0lokKXdKLpmXqakYbDIPDV6r3M8SqvSf45WwOTzkyfDpxnZXJsJQEpAsAqjUq5Pu8RCJMjubg==` |
| Microsoft.Extensions.Options.ConfigurationExtensions@9.0.0 | `Ob3FXsXkcSMQmGZi7qP07EQ39kZpSBlTcAZLbJLdI4FIf0Jug8biv2HTavWmnTirchctPlq9bl/26CXtQRguzA==` |
| Microsoft.Extensions.Primitives@9.0.0 | `N3qEBzmLMYiASUlKxxFIISP4AiwuPTHF5uCh+2CWSwwzAJiIYx0kBJsS30cp1nvhSySFAVi30jecD307jV+8Kg==` |
| Microsoft.IdentityModel.Abstractions@8.2.0 | `27ClfnelIu92kLGOaz0vjdXR1Jv7hAdLffxxNgR8T0+IMWmxeVyO3cU8oohmuTrWUFOfd2tsSGaRNewnuClIZw==` |
| Microsoft.IdentityModel.JsonWebTokens@8.2.0 | `/DAx+9HeqfkH/PccwHx7cUtQe9fYM6AxEmTla8WXUT+w+mapKLnigWmdKtF55hNvxiSnmGhSSCcG7XrvkpGKFA==` |
| Microsoft.IdentityModel.Logging@8.2.0 | `mZsjOZlbmCZfM71y8Fyo+D5UJ1RZFvmKXkxTfE2llQ0/CrfEeWmbpoew51w++EWs+G8B/peZqR1DQtbX3bB6Fg==` |
| Microsoft.IdentityModel.Tokens@8.2.0 | `/I+6D3SwW8hQh5wznGzQCrS4L5y5Re/0AEKKfXXAduWzz4WKqJzY8RmjwZ6W66bIFUhPrqOy6zsLKPik4Ppnbw==` |
| NSec.Cryptography@25.4.0 | `Z3wPpG0xefMLmhn9T+Ka/EzAmMQPT0THL/5E4lf9cXI/N9rbGH0G23ZFpRcPnD0ast2k7ieG6QSVNIjmfPCXBQ==` |
| SQLitePCLRaw.bundle_e_sqlite3@2.1.13 | `NlDixlC7uHmo+ojXRgkNvN0tQtVGpLDZ3x2OpgNj+G07VHh5cXSszFDrAiC4BpUiPwkcaeuQkMV9WQ123xVtmQ==` |
| SQLitePCLRaw.core@2.1.13 | `2joofoinuHBFs0nMX3nKuXgxpXryRkCOdM2Ua6HBqQLLy6IW+5f28ZI+HAvMyF8vG7XfNz/lwfBfE6V4juru8Q==` |
| SQLitePCLRaw.lib.e_sqlite3@2.1.13 | `D2UAuiGxFYBqeRXjjjGgvJ5UQifzn6wefmSqeKU7XcNGKLbJRP6X7kcPZ6opOBJGdCIlgbSxTvDpBbLvsN594A==` |
| SQLitePCLRaw.provider.e_sqlite3@2.1.13 | `ZU60dSqoSNRe49KcQDUmOVgdRtjITOssVXFtiISs+zJXDI4D2Sndwpn2NOtzDQNPu0ZAvpoLPQ+GLKv6iEZn8Q==` |
| System.Formats.Cbor@9.0.0 | `v8LmGrvD0thxFBn5/SaMc5Y0AecfOeoKlAcek1X0fIta8Eb0+fZA3kGelUNE9HhOAe1J1h/tDKjKxh88DWBMjA==` |
| libsodium@1.0.20.1 | `pkiceEqJDQFHjbga3k/oPfTVX9g+GKJZ1VrkuiLhaymt9YNw1Dr7cX9hNuZuNaWcr9WD0moW55k4pMwzQ7JaZQ==` |

Bronhashes:
- Web/packages.lock.json: `eb760f61aae357eb98a84e1fe9c2d4ba1dc3cd4b9ba59414afe3b1f8d1b3325d`
- Web.Tests/packages.lock.json: `8b363249ae43feb9dadcb7fbbf873c901787e6bd554e1010b023d35110c4d2d6`
- RegressionTests/packages.lock.json: `531d4cf71e5420c1c03da4c06778ba2a212c7058a3882e7a3a873ae2f811511e`

## Open supply-chaincontroles

| Controle | Huidige status | Benodigd |
| --- | --- | --- |
| Actuele CVE/advisoryscan | **Onbekend; niet uitgevoerd** | Gedateerde betrouwbare advisorysnapshot, product/RIDbereik, false-positivebeoordeling, update-eigenaar |
| Licenties/SPDX/packagecopyright | **Niet geverifieerd voor alle packages/assets** | Beoordeling uit goedgekeurde oorspronkelijke pakket-/assetmetadata; geen licenties raden |
| NuGet-provenance/signature/source mapping | **Niet aangetoond** | Approved sources/cache, package signature-/hashcontrole; feedtrust niet uit lock alleen afleiden |
| Locked restore afgedwongen | Lockfiles en RestorePackagesWithLockFile bestaan; releasegate niet aangetoond | Herhaalbare --locked-mode/RestoreLockedMode-equivalentgate en review op lockdiff; netwerkbeleid expliciet |
| Volledige publish-SBOM | **Ontbreekt** | Werkelijke RID/framework-dependent of self-contained publish, native libs, runtimes en helper meenemen |
| Toolchain/SBOM-integriteit | **Ontbreekt** | Vastgezette SDK/Node/scannerversies, source/build-ID en SHA-256; ondertekende attestatie |
| Geen testcode/secrets in release | **Geen automatische release-inhoudgate aangetroffen** | Publishallowlist; testauthenticator, PFX, DB, keyring, test-/verificationtrees uitsluiten |
| Vulnerability-/licentie-uitzonderingen | **Geen formele records aangetroffen** | Tijdelijke scopegebonden acceptatie met eigenaar/vervaldatum, nooit stilzwijgend als veilig behandelen |

Dit document is een controleerbare broninventaris en input voor SBOM-generatie, geen reeds gegenereerde SPDX/CycloneDX-release-SBOM. Er zijn in deze opdracht geen nieuwe dependencies toegevoegd.

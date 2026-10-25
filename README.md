# Thunderbird Account Backup & Restore — 0.2.0

En portabel Windows-app med mörkt WPF-gränssnitt. Säkerhetskopierar IMAP-/POP3-konton, identiteter, SMTP-inställningar och sparade inloggningar till en lösenordskrypterad `.tbaccounts`-fil. E-postmeddelanden, bilagor och lokala mappar kopieras inte.

**Första versionen. Kompilerad för Windows x64; Automatiska gränssnittstester renderar fönster och dialoger på Windows med syntetiska konton. En verklig Thunderbird-installation på Windows måste fortfarande acceptanstestas. Provåterställ innan du installerar om Windows.**

## Användning

1. Packa upp Windows-paketet till en mapp du har skrivrättigheter till. Kör `Thunderbird Account Backup and Restore.exe`. Ingen installation, separat .NET-runtime eller administratör krävs. Windows kan varna eftersom programmet saknar kodsignatur.
2. Stäng Thunderbird helt. Välj profil i listan. Om installation eller profil inte hittas automatiskt, använd **Välj Thunderbird…** respektive **Välj profilmapp…**. En profilmapp innehåller `prefs.js`.
3. Klicka **Backup Accounts**, välj ett nytt filnamn och ange ett unikt lösenord på minst 12 tecken. Spara filen på ett annat lagringsmedium och lösenordet separat. Glömt backuplösenord kan inte återställas.
4. Efter ominstallation: installera samma eller en nyare Thunderbird-version och stäng den. Starta detta program och välj **Restore Accounts**. Välj fil, ange backuplösenord och granska sammanfattningen.
5. Klicka **Öppna återställd profil**. Kontona finns i en separat profil. Kontrollera konton och servrar och välj sedan Hämta meddelanden i Thunderbird. IMAP-historik hämtas från servern.
6. För normal start med den nya profilen: öppna `about:profiles` i Thunderbirds felsökningsinformation/profilhantering och välj den återställda profilen som standard. Alternativt använd `thunderbird.exe -P`. Programmet ändrar inte befintliga standardprofiler för andra Thunderbird-installationer.

Varje `.tbaccounts`-fil innehåller en vald profil med samtliga dess stödda konton. Gör en backup per profil om du använder flera. Samma backup återställs inte två gånger till samma målrot. Separata backupögonblick kan ge separata profiler; konton slås aldrig ihop.

## Begränsningar som påverkar återställningen

- **Befintliga profiler bevaras.** Den här versionen stöder inte sammanslagning med befintliga konton. Detta undviker att skriva över deras NSS-nycklar och skapa oläsbara lösenord. En eventuell tom Thunderbird-profil lämnas kvar.
- **Sparade inloggningar:** `logins.json` kopieras med den tillhörande `key4.db`. Programmet dekrypterar inte dina mail-lösenord. Om filparet redan var inkonsekvent i ursprungsprofilen kan den här appen inte reparera det eller verifiera att lösenorden är rätt. SQLite-integritet och JSON-struktur kontrolleras.
- **Primary Password:** det ursprungliga Primary Password gäller även i den återställda profilen. Backuplösenordet är ett separat lösenord. Appen kringgår eller tar inte bort Primary Password.
- **OAuth:** sparade token bevaras där de finns i `logins.json`, men leverantören kan kräva ny inloggning. Cookies, webbsessioner, enhetsbundna hemligheter och Windows-inloggningsuppgifter kopieras inte.
- **POP3:** lokala meddelanden finns inte i backupen. Meddelanden som redan har raderats från servern kan inte återhämtas. Automatisk hämtning stängs av och lämna på server aktiveras. `popstate.dat` följer inte med; manuell hämtning kan därför hämta servermeddelanden igen. Säkerhetskopiera dina lokala mail separat före ominstallation.
- **Uppstart:** automatisk inloggning vid start stängs av för alla exporterade servrar. För IMAP behålls övriga normala synkroniseringsinställningar. Hämta mail när du granskat inställningarna; slå vid behov på automatisk hämtning i Thunderbird.
- **OpenPGP/S/MIME:** konfigurerade nyckel-/certifikatidentiteter blockerar backup. Exportera nycklar med Thunderbirds egna verktyg; stöd för denna separata arbetsgång är inte implementerat här. Denna app exporterar inte OpenPGP-nyckelringar. `cert9.db` tas med om den finns, som del av NSS-miljön.
- Externa signaturfiler kopieras inte; filsignaturer avaktiveras. Signaturtext som finns direkt i kontoinställningarna bevaras. Gamla lokala specialmapps-URI:er utelämnas så att Thunderbird kan skapa standardmappar i den nya profilen.
- Adressböcker, kalender, tillägg, meddelandefilter, externa PKCS#11-moduler, smartkort och generella Thunderbird-inställningar omfattas inte. Tilläggsberoende kontotyper som Exchange/NNTP/RSS blockeras. Version 0.2 stöder IMAP, POP3 och lokala kontoinställningar.
- Profiler med symboliska länkar/junctions och databaser med kvarvarande journaler avvisas. Nätverksprofiler och godtyckliga framtida format stöds inte uttryckligen. En strukturellt giltig profil är inte ett löfte om framtida kompatibilitet.
- Programmet gör inga nätverksanrop. Byggverktygen behöver nätverk för .NET-/NuGet-paket. Själva Thunderbird kontaktar naturligtvis mailservrar när du använder den.

## Skydd och återgång

Backupen använder AES-256-GCM med Argon2id (64 MiB, 3 iterationer, 4 parallella banor), slumpmässigt salt och nonce. Kontonamn och annan metadata krypteras också. Befintliga backupfiler skrivs aldrig över. Läs [formatbeskrivningen](docs/FORMAT.md).

Vid restore kontrolleras lösenord och integritet före filändringar. Filer skrivs till en ny stagingmapp, databaserna kontrolleras och profilen registreras sist. Alla registrerade profiler låses medan profilregistret uppdateras. En redan körande Thunderbird eller otillgänglig profil stoppar arbetet. Starta inte Thunderbird parallellt med backup/restore.

Innan `profiles.ini` ersätts skapar operativsystemets atomiska filersättning en kopia i samma mapp: `profiles.ini.before-tbaccounts-<id>`. Befintliga profilfiler ändras inte. Standardprofil och `installs.ini` bevaras. Vid fel innan registrering tas den nya profilmappen bort.

För manuell återgång: stäng Thunderbird och appen, jämför det sparade profilregistret och ersätt vid behov `profiles.ini` med säkerhetskopian. Gör inte detta om du har skapat andra profiler efter återställningen utan att först bevara deras registrering. Den nya profilen kan annars tas bort med Thunderbirds profilhanterare efter att du valt din gamla profil. **Ta aldrig bort en profil som innehåller mail du behöver.**

Den återställda profilen innehåller NSS-nycklar och inloggningar på disk precis som en normal Thunderbird-profil. Krypteringen skyddar backupfilen, inte ett redan upplåst Windows-konto. .NET kan inte garantera radering av alla lösenordssträngar i arbetsminnet. Förlust av ström under ett filsystemsarbete kan kräva manuell kontroll; lösningen är ingen generell transaktionsmotor för filsystem.

## Bygg från källkod

Kräver .NET 10 SDK. WPF körs bara på Windows; cross-compilering stöds med `EnableWindowsTargeting`.

```powershell
./build.ps1
```

Skriptet kör de syntetiska testerna och publicerar en fristående single-file Windows x64-exe till `artifacts/win-x64`. Den inkluderar native SQLite- och .NET-bibliotek. Vissa native-bibliotek extraheras av .NET vid körning till användarens temporära katalog; administratör krävs inte.

Separata kommandon:

```powershell
dotnet run --project tests/ThunderbirdAccounts.Tests -c Release
dotnet publish src/ThunderbirdAccounts.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/win-x64
```

Tester körs med en liten exekverbar testharness, inte `dotnet test`. Den returnerar en felkod om något test fallerar. Testerna använder endast syntetiska profiler och tillfälliga mappar. Ett valfritt Linux-test använder `libnss3.so` för en riktig NSS-rundtur med syntetiska lösenord:

```sh
python3 tests/nss_roundtrip.py /path/to/dotnet tests/ThunderbirdAccounts.Tests/bin/Release/net10.0/ThunderbirdAccounts.Tests.dll
```

Se [arkitektur och källor](docs/ARCHITECTURE.md) och [testprotokollet](docs/TESTING.md). GitHub Actions kör Windows-bygge, regressionstester och WPF-gränssnittstester. Förhandsbilder från gränssnittstesterna finns i artefakten `ThunderbirdAccounts-ui-preview`.


## Nytt i 0.2.0

Enhetligt mörkt grafittema med lila accent, mörk profilväljare, egna tabell-/scroll-/checkbox-stilar, mörka appdialoger och mörk titelrad där Windows stöder det. De vanliga filväljarna tillhör Windows och följer dess inställningar.

Kortare backuplösenord är ett aktivt val vid skapandet. Återställningsdialogen accepterar det lösenord som valdes, utan extra kryssruta. Krypteringsalgoritmer och filformat är oförändrade. Äldre backuper fungerar i 0.2.0; **0.1.0 kan inte öppna nya backuper med lösenord under 12 tecken**. Använd 0.2.0 eller senare för dem.

# Arkitektur och profilundersökning

## Projekt

- `src/ThunderbirdAccounts.Core`: filbaserad profilidentifiering, säker parser, inställningsgraph, filkontroller, SQLite-integritet, krypterat format och separat profilrestore. Kan testas på Linux.
- `src/ThunderbirdAccounts.App`: .NET 10 WPF, mörkt tema, profilval, kontotabell, lösenordsdialog, filval, status och explicit start av återställd profil. Fil- och Argon2-arbete körs utanför UI-tråden. UI tillåter en operation åt gången.
- `tests/ThunderbirdAccounts.Tests`: exekverbar regressionstestharness med syntetiska SQLite-databaser och profiler. Inga riktiga konton läses.
- `tests/nss_roundtrip.py`: separat interoperabilitetstest mot Mozillas NSS-bibliotek.

## Underlag, granskat 2026-10-25

Primärkällor:

- [Mozilla nsMsgAccountManager.cpp](https://github.com/mozilla/releases-comm-central/blob/master/mailnews/base/src/nsMsgAccountManager.cpp): `mail.accountmanager.accounts` leder till konton, server-ID:n och identiteter. Dessa är en sammanhängande graph och får inte ersättas av enbart en lista med adresser.
- [Mozilla nsMsgIncomingServer.cpp](https://github.com/mozilla/releases-comm-central/blob/master/mailnews/base/src/nsMsgIncomingServer.cpp): `GetFileValue` läser `directory-rel` före den absoluta `directory`-inställningen; relativa pref-filer använder profilmappen som bas. Därför genererar programmet `[ProfD]ImapMail/serverN` och `[ProfD]Mail/serverN`, utelämnar gamla absoluta sökvägar och skapar tomma lokala lager.
- [Mozilla nsToolkitProfileService.cpp](https://github.com/mozilla/gecko-dev/blob/master/toolkit/profile/nsToolkitProfileService.cpp): `profiles.ini` använder Profile-sektioner, `Path` och `IsRelative`, samt per-installationens Install-sektioner. `installs.ini` förekommer också. Per-installationens standardprofil är inte samma sak som bara Profile-sektionens `Default=1`.
- [Mozilla bug 1779022](https://bugzilla.mozilla.org/show_bug.cgi?id=1779022): NSS-profiler har egna nycklar; naiv kopiering över befintliga nyckeldatabaser är inte en säker sammanslagning.
- [Mozilla bug 1790610](https://bugzilla.mozilla.org/show_bug.cgi?id=1790610): beroenden mellan `key4.db`, sparade inloggningar, OpenPGP och S/MIME motiverar en separat ny målprofil och explicit avgränsning av signeringsnycklar.
- [Mozilla bug 1835786](https://bugzilla.mozilla.org/show_bug.cgi?id=1835786): OpenPGP-relaterade filer har inbördes beroenden; att bara flytta vissa av dem kan bryta nyckelåtkomst.

Källfiler för konto-, server- och profilhantering hämtades och relevanta delar lästes. Mozilla-buggarna beskriver kända formatberoenden, inte en certifiering av denna app eller en garanti för varje framtida Thunderbird-release.

## Backupbeslut

Parsern läser enbart `user_pref` med sträng, int32 eller bool och stöder kommentarer, JSON-liknande escaping samt `\xNN`. Ingen JavaScript exekveras. `user.js` läses som överlagring om filen finns; godtyckliga skript avvisas. Konton, deras identiteter och SMTP-servrar exporteras med fält-allowlists. Avsiktliga förändringar för säker nystart: filsignaturer av, lokala folder-URI:er bort och POP3-hämtning/deletion av.

NSS-filerna fångas under profillås, efter kontroll att Thunderbird är stängd. Ej tomma journaler blockerar backup. `key4.db` och `cert9.db` öppnas read-only för SQLite `quick_check`. `logins.json` strukturellvalideras. Inga mailmappar söks igenom och ingen rekursiv profilmappskopiering används.

Backupens version kommer från profilens `compatibility.ini`/`LastVersion` när den finns, annars den valda Thunderbird-installationens produktversion. GUI blockerar återställning från ett känt högre huvudversionsnummer. Detta är ett konservativt skydd, inte en fullständig formatförhandlare.

## Restorebeslut

En separat profil används även om destinationens Thunderbird redan har en tom profil. Ingen NSS-nyckelmerging implementeras. ID:n inom den exporterade profilen bevaras och kolliderar inte med andra profiler.

SHA-256 över payloaden identifierar en återställning. Deterministiskt mappnamn och markör gör upprepad restore till samma root idempotent. Konton i andra profiler ändras inte. En uppdaterad backup med annan payload kan skapa en separat profil.

Efter autentisering valideras inställningar/filer. En ny stagingmapp får `prefs.js`, tillåtna NSS-filer och tomma lager. SQLite kontrolleras igen innan registrering. Befintliga registrerade profiler låses. `profiles.ini` läses om före commit för att upptäcka samtidig förändring. Operativsystemets `File.Replace` skapar en tidigare-version-kopia och byter registret. Befintliga defaultvärden och `installs.ini` lämnas kvar, för att inte gissa vilken av flera installationer användaren vill ändra.

Programmet hittar en eller flera kandidater till standardprofil via Install-sektionerna; vid flera installationer kan ingen enda aktiv profil fastställas när Thunderbird är stängd. Användaren väljer i listan. Manuell profilmapp och installationsfil stöds. Programmet startar återställd profil med separata processargument `-no-remote -profile <path>`, utan shell-stränginterpolering.

## Kända gränser

Filkontroller och profillås minskar risken för samtidighet men är inte ett skydd mot en annan lokal process som medvetet manipulerar filer under operationen. Håll Thunderbird stängd hela tiden. OS-rättigheter skyddar profilen efter restore. Själva appen har inga nätverksklienter, telemetri, uppdateringskontroller eller lösenordsloggar.

OpenPGP/S/MIME-nyckelexport, NSS-dekryptering vid körning, kontomerge och automatisk ändring av per-installationens standardprofil är framtida funktioner. De är inte gömda bakom ofärdiga knappar.

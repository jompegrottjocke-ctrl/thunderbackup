# Testuppdatering för 0.2.0

Regressionstester har utökats med opt-in för korta lösenord, rundtur med 1–11 tecken, standardspärr, tomt lösenord och maxlängd. Windows-testprojektet `ThunderbirdAccounts.UiTests` renderar huvudfönster (normal och minsta storlek), profilväljare och appdialoger. Det interagerar även med lösenordsdialogerna och verifierar att korta lösenord kräver aktivt val vid backup men kan anges direkt vid restore. Endast syntetiska konton används och ingen Thunderbird-profil läses.

`build.ps1` kör detta WPF-test på Windows och sparar PNG-filer till `artifacts/ui-preview`. GitHub Actions laddar upp dem som `ThunderbirdAccounts-ui-preview`. Windows-CI för 0.1.0 har redan passerat; protokollet nedan beskriver det ursprungliga Linux-underlaget. Inget automatiskt test ersätter acceptanstest mot en verklig Thunderbird-installation.

---

# Testprotokoll — 2026-10-25

## Genomfört

- .NET SDK 10.0.402, Debian Linux x64 byggmiljö.
- WPF-byggnad med Windows-targeting: **godkänd, 0 varningar, 0 fel**.
- Self-contained single-file publicering: **godkänd**, Windows x64 PE32+ GUI-exe, .NET runtime 10.0.13 inkluderad. Inkluderar native SQLite för Windows.
- **29/29 syntetiska regressionstester godkända.**
- **2/2 NSS-interoperabilitetstester godkända** med systemets Linux `libnss3.so`: riktiga NSS-nycklar och krypterade, syntetiska mail-lösenord skapades. Efter appens krypterade backup, dekryptering och restore kunde NSS läsa samma användarnamn och lösenord. Ett fall utan Primary Password och ett med Primary Password; fel Primary Password avvisades i det senare fallet.

Regressionstesterna omfattar bland annat tio e-postkonton plus lokala kontoinställningar, flera identiteter, ny profil, befintliga profiler, upprepad restore, bevarade NSS-nycklar, fel lösenord, manipulerad GCM-tagg/salt/nonce/ciphertext, fel längd, saknade filer, ogiltig JavaScript-syntax, skadad JSON, SQLite-skador både i och bortom headern, journaler, absoluta/relativa/Unicode-profilsökvägar, profillås, staging-rollback, POP3-skydd, user.js, osäkra arkivfilnamn, junction/symlink-kontroll och att mail/bilagor/index inte ingår.

Huvuddelen av regressionstesterna använder syntetiska SQLite-tabeller som inte utgör fullständiga NSS-vaultar. Den separata NSS-rundturen använder verkliga NSS-vaultar och kompletterar dessa struktur-/säkerhetstester. Inga personliga mailkonton eller riktiga lösenord användes.

## Inte genomfört

- Exekvering av WPF-programmet på Windows, interaktiv tillgänglighets-/layoutkontroll och Windows profillås i kombination med Thunderbird.
- Start av en verklig Thunderbird-release på Windows med återställd profil och synkronisering mot mailservrar.
- OAuth-leverantörernas återautentisering, Windows Store/MSIX-installationer, specialbyggda Thunderbird-varianter eller ARM64.
- Extern säkerhetsrevision, kodsignering och Authenticode-verifiering.

Dessa punkter är inte godkända bara för att cross-kompileringen eller Linux-testerna passerar. Windows Actions-workflow medföljer men har inte startats här.

## Rekommenderat Windows-acceptanstest före ominstallation

1. Använd ett separat Windows-testkonto eller VM och en aktuell Thunderbird-installation. Konfigurera först ett särskilt IMAP-testkonto.
2. Stäng Thunderbird, skapa backup i appen och granska att backupstatus lyckas.
3. Återställ backupen och öppna den nya profilen via appen. Kontrollera konton, alla avsändaridentiteter, SMTP, portar och autentiseringsmetod.
4. Verifiera ett testmeddelandes mottagning och sändning samt IMAP-mappar. Granska lokala skickat-/utkastmappar efter sökvägsnormaliseringen.
5. Upprepa med Primary Password och OAuth. Förvänta dig möjlig interaktiv OAuth-inloggning.
6. Skapa ett separat befintligt konto i en annan profil, återställ igen och kontrollera att den profilen och dess inloggningar är oförändrade.
7. Prova fel backuplösenord och restore medan Thunderbird körs. Båda ska stoppas utan att befintliga inställningar ändras.
8. Kontrollera POP3 separat utan viktiga mail: automatisk hämtning av, lämna på server på, och inga löften om lokala meddelanden.
9. Behåll även en vanlig fullständig Thunderbird-profilbackup på annat medium tills du har verifierat att de data du behöver finns kvar.

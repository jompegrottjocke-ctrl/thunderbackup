# Filformat .tbaccounts, version 1

Alla heltal little-endian. Filen är ett binärt, autentiserat envelope, inte en ZIP-fil.

| Offset | Längd | Innehåll |
|---|---:|---|
| 0 | 8 | ASCII `TBACCT01` |
| 8 | 16 | Slumpmässigt Argon2id-salt |
| 24 | 12 | Slumpmässigt AES-GCM-nonce |
| 36 | 4 | Klartextlängd, positiv int32 |
| 40 | 16 | GCM-autentiseringstagg |
| 56 | varierar | Krypterad JSON-payload |

Nyckel: 32 byte från Argon2id med UTF-8-lösenord, 65 536 KiB minne, 3 iterationer och 4 parallella banor. Parametrarna är fasta för format version 1; okända format avvisas. Bytes 0–39 autentiseras som AES-GCM AAD. Salt och nonce genereras om för varje kryptering med operativsystemets kryptografiska slumpgenerator. Lösenord normaliseras inte; exakt Unicode-inmatning behövs.

Klartexten innehåller schema (1), UTC-tid, profilnamn, Thunderbird-version, en dictionary med scalar-inställningar och en dictionary med base64-kodade filer. Namn/nycklar följer System.Text.Json:s record-egenskaper: `Schema`, `CreatedUtc`, `ProfileName`, `ThunderbirdVersion`, `Preferences`, `Files`.

Tillåtna filer är exakt `logins.json`, `key4.db`, `cert9.db`. `logins.json` utan `key4.db` avvisas. Maximal JSON-klartext är 48 MiB, varje profilfil högst 16 MiB, prefs.js högst 8 MiB och antalet inställningar högst 20 000. Ingen dekomprimering eller arkivextrahering sker. Inställningarna återvalideras mot allowlists och normaliserade relativa sökvägar. Inga arkivstyrda godtyckliga filnamn accepteras.

Detta är ett eget versionshanterat containerformat med etablerade kryptografiska algoritmer via .NET AesGcm och Konscious Argon2id. Det har inte genomgått extern säkerhetsrevision. AES autentiserar att data hör till rätt lösenord, inte att ursprungsprofilen var korrekt. Originalets NSS-databas dekrypteras inte av appen och ett logiskt felmatchat NSS-filpar kan inte upptäckas genom enbart JSON-/SQLite-kontroller.


Från appversion 0.2.0 kan användaren aktivt tillåta lösenord på 1–11 tecken vid skapandet. Standardgränsen är fortfarande 12, maximal längd 1024 och tomma lösenord avvisas. Läsning accepterar 1–1024 tecken utan att ändra nyckelhärledningen eller formatet. Äldre appversion 0.1.0 avvisar lösenord under 12 tecken och behöver uppdateras för sådana filer.

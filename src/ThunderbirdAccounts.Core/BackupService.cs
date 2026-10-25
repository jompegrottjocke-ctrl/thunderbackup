using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ThunderbirdAccounts.Core;

public sealed record RestoreResult(string ProfilePath, bool AlreadyRestored, string? RegistryBackup);

public sealed class BackupService
{
    private static readonly HashSet<string> AllowedFiles = new(["logins.json", "key4.db", "cert9.db"], StringComparer.Ordinal);
    public AccountArchive Capture(Profile profile, string version)
    {
        using var guard = SafeFiles.LockProfile(profile.Path);
        // user.js can override prefs.js at the next launch; include effective preferences as data.
        var prefs = Preferences.Parse(Encoding.UTF8.GetString(SafeFiles.Read(Path.Combine(profile.Path, "prefs.js"), 8 * 1024 * 1024)));
        string user = Path.Combine(profile.Path, "user.js");
        if (File.Exists(user)) foreach (var pair in Preferences.Parse(Encoding.UTF8.GetString(SafeFiles.Read(user)))) prefs[pair.Key] = pair.Value;
        string compatibility = Path.Combine(profile.Path, "compatibility.ini");
        if (File.Exists(compatibility))
        {
            var info = Ini.Parse(Encoding.UTF8.GetString(SafeFiles.Read(compatibility, 1024 * 1024)));
            if (info.TryGetValue("Compatibility", out var section) && section.TryGetValue("LastVersion", out var lastVersion)) version = lastVersion;
        }
        if (!File.Exists(Path.Combine(profile.Path, "key4.db")) && (File.Exists(Path.Combine(profile.Path, "key3.db")) || File.Exists(Path.Combine(profile.Path, "signons.sqlite"))))
            throw new InvalidDataException("Äldre lösenordsformat upptäckt. Uppgradera profilen i Thunderbird och avsluta Thunderbird innan backup.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in AllowedFiles)
        {
            string file = Path.Combine(profile.Path, name);
            if (!File.Exists(file)) continue;
            foreach (string suffix in new[] { "-wal", "-journal" })
            {
                string sidecar = file + suffix;
                SafeFiles.NoLinks(sidecar);
                if (File.Exists(sidecar) && new FileInfo(sidecar).Length > 0) throw new IOException("Databasjournal finns kvar. Starta och stäng Thunderbird normalt innan backup: " + name);
            }
            if (name.EndsWith(".db", StringComparison.Ordinal)) CheckDatabase(file);
            files[name] = SafeFiles.Read(file);
        }
        var archive = new AccountArchive(1, DateTimeOffset.UtcNow, profile.Name, version, AccountSettings.Export(prefs), files);
        Validate(archive); SafeFiles.EnsureStopped(); return archive;
    }
    private static void CheckDatabase(string path)
    {
        // Read-only NSS SQLite database integrity check, never decrypt or log stored secrets.
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA quick_check"; command.CommandTimeout = 10;
            using var result = command.ExecuteReader();
            if (!result.Read() || result.GetString(0) != "ok" || result.Read()) throw new InvalidDataException("Databasens integritetskontroll misslyckades: " + Path.GetFileName(path));
        }
        catch (SqliteException) { throw new InvalidDataException("Skadad eller oläsbar SQLite-databas: " + Path.GetFileName(path)); }
    }
    public void Backup(Profile profile, string version, string destination, string password, bool allowShortPassword = false)
    {
        var archive = Capture(profile, version);
        try { Archive.AtomicCreate(destination, Archive.Encrypt(archive, password, allowShortPassword)); }
        finally { ClearFiles(archive); }
    }
    public static void ClearFiles(AccountArchive archive)
    { foreach (var bytes in archive.Files.Values) CryptographicOperations.ZeroMemory(bytes); }

    public static void Validate(AccountArchive archive)
    {
        if (archive.Schema != 1 || archive.Preferences is null || archive.Files is null || archive.ProfileName is null || archive.ThunderbirdVersion is null)
            throw new InvalidDataException("Backupens schema stöds inte.");
        if (archive.Preferences.Count > 20000 || archive.Files.Count > AllowedFiles.Count) throw new InvalidDataException("För många poster i backupen.");
        var safe = AccountSettings.Export(archive.Preferences);
        if (safe.Count != archive.Preferences.Count || safe.Any(p => !archive.Preferences.TryGetValue(p.Key, out var v) || v.GetRawText() != p.Value.GetRawText()))
            throw new InvalidDataException("Backupen innehåller otillåtna inställningar eller osäkra profilsökvägar.");
        foreach (var file in archive.Files)
        {
            if (!AllowedFiles.Contains(file.Key) || file.Value is null || file.Value.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("Backupen innehåller en otillåten fil.");
            if (file.Key.EndsWith(".db", StringComparison.Ordinal))
            {
                if (file.Value.Length < 100 || !file.Value.AsSpan(0, 16).SequenceEqual("SQLite format 3\0"u8))
                    throw new InvalidDataException("Skadad eller äldre nyckel-/certifikatdatabas: " + file.Key);
            }
        }
        if (archive.Files.TryGetValue("logins.json", out var logins))
        {
            if (!archive.Files.ContainsKey("key4.db")) throw new InvalidDataException("logins.json finns men key4.db saknas. Lösenorden kan inte återställas säkert.");
            try
            {
                using var doc = JsonDocument.Parse(logins);
                var root = doc.RootElement;
                if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int n) || n is < 1 or > 3 ||
                    !root.TryGetProperty("logins", out var entries) || entries.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Okänt logins.json-format.");
                foreach (var entry in entries.EnumerateArray())
                    foreach (string field in new[] { "hostname", "encryptedUsername", "encryptedPassword" })
                        if (!entry.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String) throw new InvalidDataException("Skadad inloggningspost.");
            }
            catch (JsonException) { throw new InvalidDataException("logins.json är skadad."); }
        }
    }

    // Deliberately never merge profile-local NSS keys or overwrite an existing profile.
    public RestoreResult Restore(AccountArchive archive, string root)
    {
        Validate(archive); SafeFiles.EnsureStopped();
        root = Path.GetFullPath(root); SafeFiles.NoLinks(root); Directory.CreateDirectory(root);
        string gate = Path.Combine(root, ".tbaccounts-operation.lock"); SafeFiles.NoLinks(gate);
        using var operation = new FileStream(gate, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var heldLocks = new List<IDisposable>();
        try
        {
            foreach (var profile in ProfileDiscovery.Find(root)) heldLocks.Add(SafeFiles.LockProfile(profile.Path));
            SafeFiles.EnsureStopped();
            byte[] identityData = JsonSerializer.SerializeToUtf8Bytes(archive);
            string digest;
            try { digest = Convert.ToHexString(SHA256.HashData(identityData)).ToLowerInvariant(); }
            finally { CryptographicOperations.ZeroMemory(identityData); }
            string relative = "Profiles/tbaccounts-" + digest[..24];
            string target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            SafeFiles.NoLinks(target);
            if (Directory.Exists(target))
            {
                string marker = Path.Combine(target, ".tbaccounts-id");
                if (File.Exists(marker) && Encoding.UTF8.GetString(SafeFiles.Read(marker, 128)) == digest && ProfileDiscovery.Find(root).Any(p => Path.GetFullPath(p.Path) == target))
                    return new(target, true, null);
                throw new IOException("Återställningsmappen finns redan och skrivs inte över.");
            }
            string profiles = Path.Combine(root, "Profiles"); SafeFiles.NoLinks(profiles); Directory.CreateDirectory(profiles);
            string staging = Path.Combine(profiles, ".tbaccounts-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            bool moved = false; bool committed = false; bool targetLocked = false;
            try
            {
                File.WriteAllText(Path.Combine(staging, "prefs.js"), Preferences.Write(archive.Preferences), new UTF8Encoding(false));
                foreach (var file in archive.Files)
                {
                    string path = Path.Combine(staging, file.Key); File.WriteAllBytes(path, file.Value);
                    if (file.Key.EndsWith(".db", StringComparison.Ordinal)) CheckDatabase(path);
                }
                foreach (var p in archive.Preferences.Where(p => p.Key.EndsWith(".directory-rel", StringComparison.Ordinal)))
                {
                    string relativeStore = p.Value.GetString()!["[ProfD]".Length..];
                    Directory.CreateDirectory(Path.Combine(staging, relativeStore.Replace('/', Path.DirectorySeparatorChar)));
                }
                File.WriteAllText(Path.Combine(staging, ".tbaccounts-id"), digest, new UTF8Encoding(false));
                Directory.Move(staging, target); moved = true;
                heldLocks.Add(SafeFiles.LockProfile(target)); targetLocked = true;
                string iniPath = Path.Combine(root, "profiles.ini"); SafeFiles.NoLinks(iniPath);
                byte[]? before = File.Exists(iniPath) ? SafeFiles.Read(iniPath, 1024 * 1024) : null;
                var ini = Ini.Parse(before is null ? "" : Encoding.UTF8.GetString(before));
                int index = 0; while (ini.ContainsKey("Profile" + index)) index++;
                ini["Profile" + index] = new(StringComparer.Ordinal) { ["Name"] = "Restored accounts " + digest[..8], ["IsRelative"] = "1", ["Path"] = relative };
                // Keep per-install defaults unchanged. Explicit launch avoids selecting the wrong install hash.
                if (!ini.ContainsKey("General")) ini["General"] = new(StringComparer.Ordinal) { ["StartWithLastProfile"] = "0", ["Version"] = "2" };
                string temp = Path.Combine(root, ".profiles-" + Guid.NewGuid().ToString("N") + ".tmp");
                string? backup = before is null ? null : iniPath + ".before-tbaccounts-" + Guid.NewGuid().ToString("N");
                try
                {
                    Archive.AtomicCreate(temp, Encoding.UTF8.GetBytes(Ini.Write(ini)));
                    SafeFiles.EnsureStopped();
                    // Detect changes by another profile manager before commit.
                    if ((before is null && File.Exists(iniPath)) || (before is not null && (!File.Exists(iniPath) || !SafeFiles.Read(iniPath).SequenceEqual(before))))
                        throw new IOException("profiles.ini ändrades under arbetet. Försök igen.");
                    if (before is not null) File.Replace(temp, iniPath, backup); else File.Move(temp, iniPath, false);
                    committed = true; return new(target, false, backup);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            finally
            {
                if (!moved && Directory.Exists(staging)) Directory.Delete(staging, true);
                if (moved && !committed)
                {
                    // Release new profile handle before rolling back its newly created directory.
                    if (targetLocked) { heldLocks[^1].Dispose(); heldLocks.RemoveAt(heldLocks.Count - 1); }
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                }
            }
        }
        finally { foreach (var handle in heldLocks) handle.Dispose(); }
    }
}

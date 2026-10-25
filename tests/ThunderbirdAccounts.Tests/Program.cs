using System.Text;
using System.Text.Json;
using ThunderbirdAccounts.Core;
using Microsoft.Data.Sqlite;

if (args.Length == 3 && args[0] == "--nss-roundtrip")
{
    var engine = new BackupService();
    string backup = Path.Combine(Path.GetDirectoryName(args[2])!, "nss-fixture.tbaccounts");
    engine.Backup(new Profile("NSS synthetic account", args[1], false), "145.0", backup, "synthetic-backup-password");
    var data = Archive.Read(backup, "synthetic-backup-password");
    try { Console.WriteLine(engine.Restore(data, args[2]).ProfilePath); }
    finally { BackupService.ClearFiles(data); }
    return 0;
}

var tests = new (string, Action)[]
{
    ("Multiple IMAP/POP3 accounts and identities", MultiAccount),
    ("Fresh restore remaps paths and registers profile", FreshRestore),
    ("Existing accounts and NSS keys remain byte-identical", ExistingRestore),
    ("Repeated restore is idempotent", RepeatedRestore),
    ("Missing prefs fails safely", MissingPrefs),
    ("Malformed prefs and script injection rejected", MalformedPrefs),
    ("Missing key database fails safely", MissingKey),
    ("Malformed credential JSON rejected", CorruptLogins),
    ("Corrupt SQLite header rejected", CorruptKey),
    ("SQLite corruption beyond the header rejected", DeepCorruption),
    ("Profile compatibility version is captured", SourceVersion),
    ("Ten mail accounts survive encrypted roundtrip", TenAccounts),
    ("Locked existing target blocks restore without changes", LockedRestore),
    ("Staged database failure rolls back before registration", StagingRollback),
    ("Wrong backup password rejected", WrongPassword),
    ("Ciphertext and header tampering rejected", Tampering),
    ("Truncated and oversized envelopes rejected", BadEnvelope),
    ("Unicode and custom profile paths supported", ProfilePaths),
    ("Mail, attachments, indexes and signatures excluded", NoMessages),
    ("Unsupported account types and signing keys blocked", Unsupported),
    ("Uncheckpointed SQLite journal blocks capture", DirtyDatabase),
    ("Profile lock blocks capture", LockedProfile),
    ("Existing output backup is never overwritten", ExistingBackup),
    ("Arbitrary archive file paths rejected", MaliciousArchive),
    ("Corrupt profiles.ini leaves no restored profile", RegistryRollback),
    ("POP3 automatic fetch and server deletion disabled", PopSafety),
    ("Effective user.js settings exported without executing code", UserOverrides),
    ("Unknown / duplicate INI entries fail closed", BadIni),
    ("Symbolic profile links rejected", LinkedProfile)
};
int failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static string Temp()
{
    string path = Path.Combine(Path.GetTempPath(), "tbaccounts-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path); return path;
}
static Profile Fixture(string root)
{
    string path = Path.Combine(root, "Profiles", "üser accounts"); Directory.CreateDirectory(path);
    var p = new Dictionary<string, JsonElement>();
    void Set(string key, object value) => p[key] = JsonSerializer.SerializeToElement(value);
    Set("mail.accountmanager.accounts", "account1,account2,account3");
    Set("mail.accountmanager.defaultaccount", "account1"); Set("mail.accountmanager.localfoldersserver", "server3");
    Set("mail.smtpservers", "smtp1,smtp2"); Set("mail.smtp.defaultserver", "smtp1");
    for (int i = 1; i <= 3; i++)
    {
        Set($"mail.account.account{i}.server", $"server{i}");
        Set($"mail.server.server{i}.type", i == 1 ? "imap" : i == 2 ? "pop3" : "none");
        Set($"mail.server.server{i}.name", "Konto " + i);
        Set($"mail.server.server{i}.hostname", i == 3 ? "Local Folders" : $"mail{i}.example.org");
        Set($"mail.server.server{i}.directory", @"C:\OldUser\Mail\Inbox");
        Set($"mail.server.server{i}.directory-rel", "[ProfD]../../old-mail");
        if (i == 3) continue;
        Set($"mail.account.account{i}.identities", i == 1 ? "id1,id3" : "id2");
        Set($"mail.identity.id{i}.useremail", $"user{i}@example.org");
        Set($"mail.identity.id{i}.smtpServer", "smtp" + i);
        Set($"mail.identity.id{i}.signature", @"C:\Secret\signature.html");
        Set($"mail.identity.id{i}.attach_signature", true);
        Set($"mail.identity.id{i}.fcc_folder", "mailbox:///C:/OldUser/Secret/Sent");
        Set($"mail.smtpserver.smtp{i}.hostname", $"smtp{i}.example.org");
        Set($"mail.smtpserver.smtp{i}.port", 587);
    }
    Set("mail.identity.id3.useremail", "alias@example.org"); Set("mail.identity.id3.smtpServer", "smtp1");
    Set("browser.cache.disk.parent_directory", @"C:\OldUser\cache");
    File.WriteAllText(Path.Combine(path, "prefs.js"), Preferences.Write(p));
    File.WriteAllText(Path.Combine(path, "logins.json"), """{"version":3,"logins":[{"hostname":"imap://mail1.example.org","encryptedUsername":"fake-user","encryptedPassword":"fake-password"}]}""");
    using (var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(path, "key4.db"), Pooling = false }.ToString()))
    {
        database.Open(); using var command = database.CreateCommand();
        command.CommandText = "CREATE TABLE metadata (id TEXT PRIMARY KEY, item1 BLOB, item2 BLOB); CREATE TABLE nssPrivate (id PRIMARY KEY, a11 BLOB);";
        command.ExecuteNonQuery();
    }
    Directory.CreateDirectory(Path.Combine(path, "Mail", "Local Folders"));
    File.WriteAllText(Path.Combine(path, "Mail", "Local Folders", "Inbox"), "MAIL BODY MUST NOT BE COPIED");
    File.WriteAllText(Path.Combine(path, "attachment.pdf"), "ATTACHMENT MUST NOT BE COPIED");
    File.WriteAllText(Path.Combine(path, "global-messages-db.sqlite"), "SEARCH INDEX");
    return new("Syntetisk profil", path, true);
}
static AccountArchive Capture(Profile p) => new BackupService().Capture(p, "145.0");
static void MultiAccount()
{
    string root = Temp(); try { var a = Capture(Fixture(root)); Check(AccountSettings.Read(a.Preferences).Count == 3); Check(a.Preferences.Text("mail.identity.id3.useremail") == "alias@example.org"); BackupService.Validate(a); } finally { Directory.Delete(root, true); }
}
static void FreshRestore()
{
    string root = Temp(); try
    {
        var a = Capture(Fixture(root)); string destination = Path.Combine(root, "new-windows");
        var result = new BackupService().Restore(a, destination);
        Check(File.Exists(Path.Combine(result.ProfilePath, "prefs.js")));
        Check(Directory.Exists(Path.Combine(result.ProfilePath, "ImapMail", "server1")));
        Check(ProfileDiscovery.Find(destination).Single().Path == result.ProfilePath);
        var prefs = File.ReadAllText(Path.Combine(result.ProfilePath, "prefs.js")); Check(!prefs.Contains("OldUser")); Check(!prefs.Contains("../../"));
        Check(File.ReadAllBytes(Path.Combine(result.ProfilePath, "key4.db")).SequenceEqual(a.Files["key4.db"]));
    } finally { Directory.Delete(root, true); }
}
static void ExistingRestore()
{
    string root = Temp(); try
    {
        var existing = Fixture(root); var a = Capture(existing);
        File.WriteAllText(Path.Combine(root, "profiles.ini"), "[InstallABC]\nDefault=Profiles/üser accounts\nLocked=1\n[Profile0]\nName=Old\nIsRelative=1\nPath=Profiles/üser accounts\nDefault=1\n");
        byte[] prefs = File.ReadAllBytes(Path.Combine(existing.Path, "prefs.js")), keys = File.ReadAllBytes(Path.Combine(existing.Path, "key4.db"));
        string before = File.ReadAllText(Path.Combine(root, "profiles.ini"));
        var result = new BackupService().Restore(a, root);
        Check(ProfileDiscovery.Find(root).Count == 2); Check(File.ReadAllBytes(Path.Combine(existing.Path, "prefs.js")).SequenceEqual(prefs));
        Check(File.ReadAllBytes(Path.Combine(existing.Path, "key4.db")).SequenceEqual(keys)); Check(File.ReadAllText(result.RegistryBackup!) == before);
        Check(ProfileDiscovery.Find(root).Single(p => p.IsDefault).Path == existing.Path);
    } finally { Directory.Delete(root, true); }
}
static void RepeatedRestore()
{
    string root = Temp(); try { var a = Capture(Fixture(root)); var service = new BackupService(); string dest = Path.Combine(root, "target"); var first = service.Restore(a, dest); var second = service.Restore(a, dest); Check(second.AlreadyRestored && second.ProfilePath == first.ProfilePath); Check(ProfileDiscovery.Find(dest).Count == 1); } finally { Directory.Delete(root, true); }
}
static void MissingPrefs()
{ string root = Temp(); try { var p = Fixture(root); File.Delete(Path.Combine(p.Path, "prefs.js")); Throws<FileNotFoundException>(() => Capture(p)); } finally { Directory.Delete(root, true); } }
static void MalformedPrefs()
{
    Throws<InvalidDataException>(() => Preferences.Parse("user_pref(\"x\", run_code());"));
    Throws<InvalidDataException>(() => Preferences.Parse("user_pref(\"x\", true); malicious();"));
    Throws<InvalidDataException>(() => Preferences.Parse("/* missing end"));
    Check(Preferences.Parse("// ok\nuser_pref(\"x\", \"a\\x20b\\u00e5\");").Text("x") == "a bå");
}
static void MissingKey()
{ string root = Temp(); try { var p = Fixture(root); File.Delete(Path.Combine(p.Path, "key4.db")); Throws<InvalidDataException>(() => Capture(p)); } finally { Directory.Delete(root, true); } }
static void CorruptLogins()
{ string root = Temp(); try { var p = Fixture(root); File.WriteAllText(Path.Combine(p.Path, "logins.json"), "{invalid"); Throws<InvalidDataException>(() => Capture(p)); } finally { Directory.Delete(root, true); } }
static void CorruptKey()
{ string root = Temp(); try { var p = Fixture(root); File.WriteAllText(Path.Combine(p.Path, "key4.db"), "broken"); Throws<InvalidDataException>(() => Capture(p)); } finally { Directory.Delete(root, true); } }
static void WrongPassword()
{ string root = Temp(); try { byte[] bytes = Archive.Encrypt(Capture(Fixture(root)), "correct-password"); Throws<InvalidDataException>(() => Archive.Decrypt(bytes, "incorrect-password")); } finally { Directory.Delete(root, true); } }
static void Tampering()
{
    string root = Temp(); try
    {
        byte[] original = Archive.Encrypt(Capture(Fixture(root)), "correct-password");
        foreach (int offset in new[] { 8, 24, 40, original.Length - 1 }) { byte[] bad = (byte[])original.Clone(); bad[offset] ^= 1; Throws<InvalidDataException>(() => Archive.Decrypt(bad, "correct-password")); }
        var a = Archive.Decrypt(original, "correct-password"); Check(a.Files.ContainsKey("key4.db"));
    } finally { Directory.Delete(root, true); }
}
static void BadEnvelope()
{
    Throws<InvalidDataException>(() => Archive.Decrypt([], "correct-password"));
    var bytes = new byte[56]; "TBACCT01"u8.CopyTo(bytes); Array.Fill<byte>(bytes, 255, 36, 4); Throws<InvalidDataException>(() => Archive.Decrypt(bytes, "correct-password"));
}
static void ProfilePaths()
{
    string root = Temp(); try
    {
        var p = Fixture(root);
        File.WriteAllText(Path.Combine(root, "profiles.ini"), $"[Profile0]\nName=Åsa\nIsRelative=0\nPath={p.Path}\n[InstallABC]\nDefault={p.Path}\n");
        var found = ProfileDiscovery.Find(root).Single(); Check(found.Path == p.Path && found.IsDefault);
        File.WriteAllText(Path.Combine(root, "profiles.ini"), "[Profile0]\nName=Åsa\nIsRelative=1\nPath=Profiles/üser accounts\nDefault=1\n");
        Check(ProfileDiscovery.Find(root).Single().Path == p.Path);
    } finally { Directory.Delete(root, true); }
}
static void NoMessages()
{
    string root = Temp(); try
    {
        var a = Capture(Fixture(root)); Check(a.Files.Keys.Order().SequenceEqual(new[] { "key4.db", "logins.json" }));
        string json = JsonSerializer.Serialize(a); Check(!json.Contains("MAIL BODY") && !json.Contains("ATTACHMENT") && !json.Contains("OldUser") && !json.Contains("Secret"));
        byte[] bytes = Archive.Encrypt(a, "correct-password"); Check(!Encoding.UTF8.GetString(bytes).Contains("example.org"));
    } finally { Directory.Delete(root, true); }
}
static void Unsupported()
{
    string root = Temp(); try
    {
        var p = Fixture(root); var prefs = Preferences.Parse(File.ReadAllText(Path.Combine(p.Path, "prefs.js")));
        prefs["mail.server.server1.type"] = Preferences.Json("nntp"); Throws<InvalidDataException>(() => AccountSettings.Export(prefs));
        prefs["mail.server.server1.type"] = Preferences.Json("imap"); prefs["mail.identity.id1.openpgp_key_id"] = Preferences.Json("1234"); Throws<InvalidDataException>(() => AccountSettings.Export(prefs));
    } finally { Directory.Delete(root, true); }
}
static void DirtyDatabase()
{ string root = Temp(); try { var p = Fixture(root); File.WriteAllText(Path.Combine(p.Path, "key4.db-wal"), "pending"); Throws<IOException>(() => Capture(p)); } finally { Directory.Delete(root, true); } }
static void LockedProfile()
{ string root = Temp(); try { var p = Fixture(root); using var held = SafeFiles.LockProfile(p.Path); Throws<IOException>(() => Capture(p)); } finally { Directory.Delete(root, true); } }
static void ExistingBackup()
{
    string root = Temp(); try
    {
        var p = Fixture(root); string file = Path.Combine(root, "backup.tbaccounts"); File.WriteAllText(file, "OLD BACKUP");
        Throws<IOException>(() => new BackupService().Backup(p, "145.0", file, "correct-password")); Check(File.ReadAllText(file) == "OLD BACKUP"); Check(!Directory.GetFiles(root, "*.tmp").Any());
    } finally { Directory.Delete(root, true); }
}
static void MaliciousArchive()
{
    string root = Temp(); try
    {
        var a = Capture(Fixture(root)); a.Files["../Inbox"] = [1]; Throws<InvalidDataException>(() => new BackupService().Restore(a, Path.Combine(root, "target")));
        a.Files.Remove("../Inbox"); a.Preferences["mail.server.server1.directory-rel"] = Preferences.Json("[ProfD]../../escape"); Throws<InvalidDataException>(() => BackupService.Validate(a));
    } finally { Directory.Delete(root, true); }
}
static void RegistryRollback()
{
    string root = Temp(); try
    {
        var a = Capture(Fixture(root)); string dest = Path.Combine(root, "target"); Directory.CreateDirectory(dest); File.WriteAllText(Path.Combine(dest, "profiles.ini"), "garbage");
        Throws<InvalidDataException>(() => new BackupService().Restore(a, dest)); Check(File.ReadAllText(Path.Combine(dest, "profiles.ini")) == "garbage"); Check(!Directory.Exists(Path.Combine(dest, "Profiles")) || Directory.GetDirectories(Path.Combine(dest, "Profiles")).Length == 0);
    } finally { Directory.Delete(root, true); }
}
static void PopSafety()
{
    string root = Temp(); try
    {
        var a = Capture(Fixture(root)); var p = a.Preferences;
        Check(!p["mail.server.server2.check_new_mail"].GetBoolean()); Check(p["mail.server.server2.leave_on_server"].GetBoolean()); Check(!p["mail.server.server2.delete_mail_left_on_server"].GetBoolean());
    } finally { Directory.Delete(root, true); }
}
static void UserOverrides()
{
    string root = Temp(); try
    {
        var p = Fixture(root); File.WriteAllText(Path.Combine(p.Path, "user.js"), "user_pref(\"mail.server.server1.name\", \"Overridden\");");
        Check(Capture(p).Preferences.Text("mail.server.server1.name") == "Overridden");
        File.WriteAllText(Path.Combine(p.Path, "user.js"), "run_code();"); Throws<InvalidDataException>(() => Capture(p));
    } finally { Directory.Delete(root, true); }
}
static void BadIni()
{
    string root = Temp(); try
    {
        File.WriteAllText(Path.Combine(root, "profiles.ini"), "[Profile0]\nPath=a\nPath=b\n"); Throws<InvalidDataException>(() => ProfileDiscovery.Find(root));
        File.WriteAllText(Path.Combine(root, "profiles.ini"), "[Profile0]\nPath=a\n[Profile0]\nPath=b\n"); Throws<InvalidDataException>(() => ProfileDiscovery.Find(root));
    } finally { Directory.Delete(root, true); }
}
static void LinkedProfile()
{
    string root = Temp(); try
    {
        var p = Fixture(root); string link = Path.Combine(root, "linked");
        try { Directory.CreateSymbolicLink(link, p.Path); } catch (UnauthorizedAccessException) { if (OperatingSystem.IsWindows()) return; throw; }
        Throws<IOException>(() => Capture(new("Linked", link, false)));
    } finally { Directory.Delete(root, true); }
}

static void DeepCorruption()
{
    string root = Temp(); try
    {
        var p = Fixture(root); string file = Path.Combine(p.Path, "key4.db"); byte[] bytes = File.ReadAllBytes(file);
        Array.Fill<byte>(bytes, 255, 100, bytes.Length - 100); File.WriteAllBytes(file, bytes);
        Throws<InvalidDataException>(() => Capture(p));
    } finally { Directory.Delete(root, true); }
}
static void SourceVersion()
{
    string root = Temp(); try
    {
        var p = Fixture(root); File.WriteAllText(Path.Combine(p.Path, "compatibility.ini"), "[Compatibility]\nLastVersion=150.2.1_20261025/20261025\nLastOSABI=WINNT_x86_64-msvc\n");
        Check(Capture(p).ThunderbirdVersion.StartsWith("150.2.1", StringComparison.Ordinal));
    } finally { Directory.Delete(root, true); }
}

static void TenAccounts()
{
    string root = Temp(); try
    {
        var p = Fixture(root); var prefs = Preferences.Parse(File.ReadAllText(Path.Combine(p.Path, "prefs.js")));
        var ids = new List<string> { "account1", "account2", "account3" };
        for (int i = 4; i <= 11; i++)
        {
            ids.Add("account" + i);
            prefs[$"mail.account.account{i}.server"] = Preferences.Json("server" + i);
            prefs[$"mail.account.account{i}.identities"] = Preferences.Json("id" + i);
            prefs[$"mail.server.server{i}.type"] = Preferences.Json("imap");
            prefs[$"mail.server.server{i}.hostname"] = Preferences.Json($"imap{i}.example.org");
            prefs[$"mail.identity.id{i}.useremail"] = Preferences.Json($"user{i}@example.org");
            prefs[$"mail.identity.id{i}.smtpServer"] = Preferences.Json("smtp1");
        }
        prefs["mail.accountmanager.accounts"] = Preferences.Json(string.Join(",", ids));
        File.WriteAllText(Path.Combine(p.Path, "prefs.js"), Preferences.Write(prefs));
        string backup = Path.Combine(root, "ten.tbaccounts"); new BackupService().Backup(p, "145.0", backup, "synthetic-password");
        var data = Archive.Read(backup, "synthetic-password");
        var result = new BackupService().Restore(data, Path.Combine(root, "new"));
        var restored = Preferences.Parse(File.ReadAllText(Path.Combine(result.ProfilePath, "prefs.js")));
        Check(AccountSettings.Read(restored).Count(a => a.Type != "none") == 10);
    } finally { Directory.Delete(root, true); }
}
static void LockedRestore()
{
    string root = Temp(); try
    {
        var p = Fixture(root); var a = Capture(p);
        File.WriteAllText(Path.Combine(root, "profiles.ini"), "[Profile0]\nPath=Profiles/üser accounts\nIsRelative=1\nName=Existing\n");
        byte[] before = File.ReadAllBytes(Path.Combine(root, "profiles.ini"));
        using (SafeFiles.LockProfile(p.Path)) Throws<IOException>(() => new BackupService().Restore(a, root));
        Check(File.ReadAllBytes(Path.Combine(root, "profiles.ini")).SequenceEqual(before));
        Check(Directory.GetDirectories(Path.Combine(root, "Profiles")).Length == 1);
    } finally { Directory.Delete(root, true); }
}
static void StagingRollback()
{
    string root = Temp(); try
    {
        var a = Capture(Fixture(root)); var db = a.Files["key4.db"]; Array.Fill<byte>(db, 255, 100, db.Length - 100);
        string dest = Path.Combine(root, "target");
        Throws<InvalidDataException>(() => new BackupService().Restore(a, dest));
        Check(!File.Exists(Path.Combine(dest, "profiles.ini")));
        Check(Directory.GetDirectories(Path.Combine(dest, "Profiles")).Length == 0);
    } finally { Directory.Delete(root, true); }
}

using System.Diagnostics;
using System.Text;

namespace ThunderbirdAccounts.Core;

public sealed record Profile(string Name, string Path, bool IsDefault)
{
    public override string ToString() => Name + (IsDefault ? TextCatalog.Get("DefaultProfile") : "") + " — " + Path;
}

public static class ProfileDiscovery
{
    public static string ThunderbirdRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thunderbird");
    public static IReadOnlyList<Profile> Find(string root)
    {
        string ini = System.IO.Path.Combine(root, "profiles.ini");
        if (!File.Exists(ini)) return [];
        SafeFiles.NoLinks(ini);
        var sections = Ini.Parse(File.ReadAllText(ini));
        var defaults = sections.Where(s => s.Key.StartsWith("Install", StringComparison.Ordinal)).Select(s => s.Value.GetValueOrDefault("Default", "")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sections.Where(s => s.Key.StartsWith("Profile", StringComparison.Ordinal) && s.Value.ContainsKey("Path")).Select(s =>
        {
            string path = s.Value["Path"];
            string absolute = System.IO.Path.GetFullPath(s.Value.GetValueOrDefault("IsRelative") == "1" ? System.IO.Path.Combine(root, path.Replace('/', System.IO.Path.DirectorySeparatorChar)) : path);
            return new Profile(s.Value.GetValueOrDefault("Name", s.Key), absolute, defaults.Contains(path) || (defaults.Count == 0 && s.Value.GetValueOrDefault("Default") == "1"));
        }).DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static string? FindExecutable()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
                {
                    using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive == Microsoft.Win32.Registry.CurrentUser ? Microsoft.Win32.RegistryHive.CurrentUser : Microsoft.Win32.RegistryHive.LocalMachine, view);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\thunderbird.exe");
                    if (key?.GetValue("") is string path) candidates.Add(path.Trim('"'));
                }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
            candidates.Add(System.IO.Path.Combine(Environment.GetFolderPath(folder), "Mozilla Thunderbird", "thunderbird.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
    public static string Version(string? executable) => executable is null ? TextCatalog.Get("Unknown") : FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? TextCatalog.Get("Unknown");
}

internal static class Ini
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new(StringComparer.Ordinal); if (!result.TryAdd(line[1..^1], current)) throw new InvalidDataException(TextCatalog.Get("DuplicateSectionsInProfilesIni"));
            }
            else
            {
                int equal = line.IndexOf('=');
                if (current is null || equal <= 0) throw new InvalidDataException(TextCatalog.Get("InvalidProfilesIni"));
                if (!current.TryAdd(line[..equal].Trim(), line[(equal + 1)..].Trim())) throw new InvalidDataException(TextCatalog.Get("DuplicateValuesInProfilesIni"));
            }
        }
        return result;
    }
    public static string Write(Dictionary<string, Dictionary<string, string>> sections) => string.Join("\r\n\r\n", sections.Select(s => "[" + s.Key + "]\r\n" + string.Join("\r\n", s.Value.Select(v => v.Key + "=" + v.Value)))) + "\r\n";
}

public static class SafeFiles
{
    public static void NoLinks(string path)
    {
        string? current = System.IO.Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(TextCatalog.Get("SymbolicLinksJunctionsAreNotSupported") + current);
            current = System.IO.Path.GetDirectoryName(current);
        }
    }
    public static void EnsureStopped()
    {
        foreach (var process in Process.GetProcessesByName("thunderbird"))
        { using (process) { throw new IOException(TextCatalog.Get("CloseThunderbirdCompletelyBeforeContinuing")); } }
    }
    public static IDisposable LockProfile(string profile)
    {
        EnsureStopped(); NoLinks(profile);
        if (!Directory.Exists(profile)) throw new DirectoryNotFoundException(TextCatalog.Get("TheProfileFolderIsMissing"));
        string file = System.IO.Path.Combine(profile, "parent.lock"); NoLinks(file);
        try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException(TextCatalog.Get("TheProfileIsLockedCloseThunderbirdAndTryAgain")); }
    }
    public static byte[] Read(string path, int limit = 16 * 1024 * 1024)
    {
        NoLinks(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > limit) throw new InvalidDataException(TextCatalog.Get("ProfileFileIsTooLarge") + System.IO.Path.GetFileName(path));
        byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }
}

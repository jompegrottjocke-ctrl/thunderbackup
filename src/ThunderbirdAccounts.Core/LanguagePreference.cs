namespace ThunderbirdAccounts.Core;

/// <summary>Stores only the UI language, outside Thunderbird profiles and backup files.</summary>
public static class LanguagePreference
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThunderbirdAccounts", "language.txt");
    public static bool IsValid(string choice) => choice is "auto" or "sv" or "en";
    public static string Load(string path)
    {
        try { string choice = File.ReadAllText(path).Trim(); return IsValid(choice) ? choice : "auto"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "auto"; }
    }
    public static void Save(string path, string choice)
    {
        if (!IsValid(choice)) throw new ArgumentException("Unsupported language.", nameof(choice));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, choice);
    }
}

using System.Globalization;
using System.Resources;

namespace ThunderbirdAccounts.Core;

/// <summary>Application-owned text. Swedish is the source language; other systems use English.</summary>
public static class TextCatalog
{
    private static readonly ResourceManager Resources = new("ThunderbirdAccounts.Core.Strings", typeof(TextCatalog).Assembly);
    public static CultureInfo Culture { get; private set; } = ResolveCulture("auto", CultureInfo.CurrentUICulture);
    public static CultureInfo ResolveCulture(string choice, CultureInfo systemCulture) =>
        CultureInfo.GetCultureInfo(choice == "sv" || (choice == "auto" && systemCulture.TwoLetterISOLanguageName == "sv") ? "sv" : "en");
    public static void SetLanguage(string choice, CultureInfo systemCulture) => Culture = ResolveCulture(choice, systemCulture);
    public static string Get(string key, params object[] args)
    {
        string text = Resources.GetString(key, Culture) ?? throw new MissingManifestResourceException(key);
        return args.Length == 0 ? text : string.Format(Culture, text, args);
    }
    public static IReadOnlyDictionary<string, string> All()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Resources.GetResourceSet(Culture, true, true)!)
            result.Add((string)entry.Key, (string)entry.Value!);
        return result;
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ThunderbirdAccounts.Core;

// prefs.js is data, never executed. Unknown syntax fails closed.
public static class Preferences
{
    public static Dictionary<string, JsonElement> Parse(string text)
    {
        if (text.Length > 8 * 1024 * 1024) throw new InvalidDataException(TextCatalog.Get("PrefsJsIsTooLarge"));
        var reader = new Reader(text);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        while (reader.More)
        {
            reader.Expect("user_pref"); reader.Expect("(");
            string key = reader.String(); reader.Expect(",");
            JsonElement value = reader.Value(); reader.Expect(")"); reader.Expect(";");
            result[key] = value;
        }
        return result;
    }
    public static string Write(IReadOnlyDictionary<string, JsonElement> prefs) =>
        "// Thunderbird account settings, generated without mail data.\n" + string.Join("\n",
        prefs.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"user_pref({JsonSerializer.Serialize(x.Key)}, {x.Value.GetRawText()});")) + "\n";
    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
    public static string Text(this IReadOnlyDictionary<string, JsonElement> prefs, string key) =>
        prefs.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    public static string[] List(string text) => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private sealed class Reader(string text)
    {
        private int pos;
        private void Skip()
        {
            for (;;)
            {
                while (pos < text.Length && (char.IsWhiteSpace(text[pos]) || text[pos] == '\uFEFF')) pos++;
                if (text.AsSpan(pos).StartsWith("//")) { while (pos < text.Length && text[pos] != '\n') pos++; }
                else if (text.AsSpan(pos).StartsWith("/*"))
                {
                    int end = text.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                    if (end < 0) throw Bad(); pos = end + 2;
                }
                else return;
            }
        }
        public bool More { get { Skip(); return pos < text.Length; } }
        private InvalidDataException Bad() => new(TextCatalog.Get("InvalidPrefsJsNearCharacterNoScriptsAreExecuted", pos));
        public void Expect(string token)
        {
            Skip(); if (!text.AsSpan(pos).StartsWith(token, StringComparison.Ordinal)) throw Bad(); pos += token.Length;
        }
        public string String()
        {
            Skip(); if (pos >= text.Length || text[pos++] != '"') throw Bad();
            var output = new StringBuilder();
            while (pos < text.Length)
            {
                char c = text[pos++]; if (c == '"') return output.ToString();
                if (c < 32) throw Bad();
                if (c != '\\') { output.Append(c); continue; }
                if (pos >= text.Length) throw Bad();
                c = text[pos++];
                switch (c)
                {
                    case '"': case '\\': case '/': output.Append(c); break;
                    case 'n': output.Append('\n'); break;
                    case 'r': output.Append('\r'); break;
                    case 't': output.Append('\t'); break;
                    case 'b': output.Append('\b'); break;
                    case 'f': output.Append('\f'); break;
                    case 'u': case 'x':
                        int count = c == 'u' ? 4 : 2;
                        if (pos + count > text.Length || !ushort.TryParse(text.AsSpan(pos, count), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) throw Bad();
                        output.Append((char)code); pos += count; break;
                    default: throw Bad();
                }
            }
            throw Bad();
        }
        public JsonElement Value()
        {
            Skip(); if (pos < text.Length && text[pos] == '"') return Json(String());
            if (text.AsSpan(pos).StartsWith("true")) { pos += 4; return Json(true); }
            if (text.AsSpan(pos).StartsWith("false")) { pos += 5; return Json(false); }
            int start = pos;
            if (pos < text.Length && text[pos] == '-') pos++;
            while (pos < text.Length && char.IsAsciiDigit(text[pos])) pos++;
            if (!int.TryParse(text.AsSpan(start, pos - start), CultureInfo.InvariantCulture, out int value)) throw Bad();
            return Json(value);
        }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Windows;
using CaptionForge.Modularity;
namespace CaptionForge.Modules.MediaTools;
internal static class Ui
{
    private static readonly Dictionary<string,string> English = Load();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(string Key,object?[] Args)> Rendered = new();
    static Ui() { InterfaceLanguage.Changed += (_, _) => Publish(); Publish(); }
    private static Dictionary<string,string> Load()
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("CaptionForge.Modules.MediaTools.Languages.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
    internal static void Publish()
    {
        if (Application.Current is null) return;
        foreach (var pair in English)
            Application.Current.Resources["MediaTools.t" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pair.Key))).ToLowerInvariant()[..16]] = T(pair.Key);
    }
    internal static string T(string text) => InterfaceLanguage.Code == "en" ? English.GetValueOrDefault(text, text) : text;
    internal static string F(string text, params object?[] args)
    {
        var value = string.Format(CultureInfo.CurrentCulture, T(text), args);
        if (Rendered.Count > 2048) Rendered.Clear();
        Rendered[value] = (text,args); return value;
    }
    internal static string Render(string text)
    {
        if (Rendered.TryGetValue(text, out var source)) return F(source.Key,source.Args);
        if (English.ContainsKey(text)) return T(text);
        foreach (var pair in English)
        {
            if (pair.Value == text) return T(pair.Key);
            if (pair.Key.EndsWith(": ") || pair.Key.EndsWith(" · "))
            {
                if (text.StartsWith(pair.Key, StringComparison.Ordinal)) return T(pair.Key)+text[pair.Key.Length..];
                if (text.StartsWith(pair.Value, StringComparison.Ordinal)) return T(pair.Key)+text[pair.Value.Length..];
            }
        }
        return text;
    }
}

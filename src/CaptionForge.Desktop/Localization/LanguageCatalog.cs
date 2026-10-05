using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
namespace CaptionForge.Desktop.Localization;

public sealed record LanguageDocument(string Code, string Name, IReadOnlyDictionary<string,string> Translations);
public sealed record LanguageScan(IReadOnlyList<LanguageDocument> Languages, IReadOnlyList<string> RejectedFiles);
/// <summary>Catálogo independiente de WPF. Sólo acepta textos, nunca código ni rutas dentro del documento.</summary>
public static class LanguageCatalog
{
    private static readonly Regex CodePattern = new("^[a-z]{2}$", RegexOptions.CultureInvariant);
    public static LanguageDocument Parse(Stream stream, string fileCode)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("JSON: root");
        var fields = root.EnumerateObject().ToArray();
        if (fields.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=fields.Length) throw new InvalidDataException("JSON: duplicate metadata");
        string code=root.GetProperty("code").GetString() ?? "", name=root.GetProperty("name").GetString() ?? "";
        if (!CodePattern.IsMatch(code) || code!=fileCode || string.IsNullOrWhiteSpace(name) || name.Length>80 || name.Any(char.IsControl))
            throw new InvalidDataException("JSON: code/name");
        var texts=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var p in root.GetProperty("translations").EnumerateObject())
        {
            if(string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>160 || p.Value.ValueKind!=JsonValueKind.String || !texts.TryAdd(p.Name,p.Value.GetString()!))
                throw new InvalidDataException("JSON: translation/duplicate key");
        }
        return new(code,name,texts);
    }
    public static LanguageScan Scan(string directory, LanguageDocument fallback)
    {
        var languages=new Dictionary<string,LanguageDocument>(StringComparer.Ordinal) { ["es"]=fallback };
        var rejected=new List<string>();
        if(!Directory.Exists(directory)) return new(languages.Values.ToArray(), rejected);
        IEnumerable<string> files;
        try { files=Directory.GetFiles(directory,"*",SearchOption.TopDirectoryOnly).Where(f=>Path.GetExtension(f).Equals(".json",StringComparison.OrdinalIgnoreCase)).OrderBy(f=>f,StringComparer.Ordinal).ToArray(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { return new(languages.Values.ToArray(),new[]{directory}); }
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in files.GroupBy(f=>Path.GetFileNameWithoutExtension(f).ToLowerInvariant()))
        {
            if(group.Count()>1) { rejected.AddRange(group.Select(Path.GetFileName)!); continue; }
            var file=group.Single();
            try
            {
                if(new FileInfo(file).Length>2*1024*1024) throw new InvalidDataException("JSON: too large");
                using var stream=File.OpenRead(file);
                var d=Parse(stream,Path.GetFileNameWithoutExtension(file));
                if(!seen.Add(d.Code)) throw new InvalidDataException("JSON: duplicate code");
                var merged=new Dictionary<string,string>(fallback.Translations,StringComparer.Ordinal);
                foreach(var pair in d.Translations)
                {
                    // Reject malformed placeholders and changes to the required argument indexes.
                    var candidate=CompositeFormat.Parse(pair.Value);
                    if(fallback.Translations.TryGetValue(pair.Key,out var original))
                    {
                        var expected=CompositeFormat.Parse(original);
                        if(candidate.MinimumArgumentCount!=expected.MinimumArgumentCount || !FormatIndexes(pair.Value).SetEquals(FormatIndexes(original)))
                            throw new InvalidDataException("JSON: placeholders");
                    }
                    merged[pair.Key]=pair.Value;
                }
                languages[d.Code]=d with { Translations=merged };
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException or FormatException)
            { rejected.Add(Path.GetFileName(file)); }
        }
        return new(languages.Values.OrderBy(d=>d.Name,StringComparer.OrdinalIgnoreCase).ToArray(),rejected);
    }
    private static HashSet<int> FormatIndexes(string value)
    {
        // Ignore escaped braces; CompositeFormat.Parse above validates the grammar.
        var result=new HashSet<int>();
        for(int i=0;i<value.Length;i++)
        {
            if(value[i]!='{')continue;
            if(i+1<value.Length && value[i+1]=='{'){i++;continue;}
            int j=i+1;while(j<value.Length && char.IsWhiteSpace(value[j]))j++;
            int start=j;while(j<value.Length && char.IsDigit(value[j]))j++;
            if(j>start)result.Add(int.Parse(value[start..j],CultureInfo.InvariantCulture));
        }
        return result;
    }
}

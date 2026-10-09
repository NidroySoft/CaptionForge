using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;
namespace CaptionForge.Application.Services;

public static class SubtitleFileReader
{
    public static IReadOnlyList<SubtitleCue> Parse(string text, string extension)
    {
        bool vtt = extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase);
        if (!vtt && !extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Selecciona un archivo SRT o WebVTT.");
        text = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        if (vtt && !text.StartsWith("WEBVTT", StringComparison.Ordinal)) throw new InvalidDataException("Falta la cabecera WEBVTT.");
        if (text.Contains("X-TIMESTAMP-MAP", StringComparison.Ordinal)) throw new InvalidDataException("Este WebVTT usa un mapa de tiempo externo. Expórtalo como SRT para importarlo.");
        var cues = new List<SubtitleCue>();
        foreach (string block in Regex.Split(text.Trim(), @"\n[ \t]*\n+"))
        {
            var lines = block.Split('\n');
            if (vtt && (lines[0].StartsWith("WEBVTT", StringComparison.Ordinal) || Regex.IsMatch(lines[0], @"^(NOTE(?:\s|$)|STYLE$|REGION$)"))) continue;
            int timing = Array.FindIndex(lines, line => line.Contains("-->"));
            if (timing is < 0 or > 1) throw new InvalidDataException($"Bloque {cues.Count + 1}: falta un intervalo válido.");
            var match = Regex.Match(lines[timing], @"^\s*(\S+)\s+-->\s+(\S+)(?:\s+.*)?$");
            if (!match.Success) throw new InvalidDataException($"Bloque {cues.Count + 1}: intervalo inválido.");
            long start = Time(match.Groups[1].Value, vtt), end = Time(match.Groups[2].Value, vtt);
            if (end <= start) throw new InvalidDataException($"Bloque {cues.Count + 1}: el fin debe ser mayor que el inicio.");
            string content = string.Join("\n", lines.Skip(timing + 1));
            content = Regex.Replace(content, @"</?(?:b|i|u|font|c|v|lang|ruby|rt)(?:[\s.][^>]*)?>|<\d{2}:\d{2}(?::\d{2})?\.\d{3}>", "", RegexOptions.IgnoreCase);
            content = WebUtility.HtmlDecode(Regex.Replace(content, @"\{\\an[1-9]\}", ""));
            cues.Add(ExistingSubtitleCueBuilder.Build("file-cue-" + (cues.Count + 1), content, new TimeRangeUs(start, end - start)));
            if (cues.Count > 100000) throw new InvalidDataException("El archivo contiene demasiados bloques.");
        }
        if (cues.Count == 0) throw new InvalidDataException("El archivo no contiene subtítulos.");
        return cues.OrderBy(c => c.TimelineRange.StartUs).ToArray();
    }
    private static long Time(string value, bool vtt)
    {
        string pattern = vtt ? @"^(?:(\d{2,}):)?(\d{2}):(\d{2})\.(\d{3})$" : @"^(\d{2,}):(\d{2}):(\d{2})[,.](\d{3})$";
        var m = Regex.Match(value, pattern);
        if (!m.Success) throw new InvalidDataException("Tiempo de subtítulo inválido: " + value);
        long h = m.Groups[1].Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        int min = int.Parse(m.Groups[2].Value), sec = int.Parse(m.Groups[3].Value), ms = int.Parse(m.Groups[4].Value);
        if (min >= 60 || sec >= 60) throw new InvalidDataException("Tiempo fuera de rango: " + value);
        return checked((h * 3600000 + min * 60000L + sec * 1000L + ms) * 1000);
    }
}

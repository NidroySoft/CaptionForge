using System.Globalization;
using System.Text;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Application.Services;

/// <summary>Exportación complementaria sin acceso al disco ni cambio de los subtítulos animados.</summary>
public static class SrtFormatter
{
    public static string Format(IEnumerable<SubtitleCue> captions)
    {
        ArgumentNullException.ThrowIfNull(captions);
        var ordered = captions.ToArray();
        if (ordered.Any(c => c is null)) throw new ArgumentException("Caption nulo.", nameof(captions));
        Array.Sort(ordered, (a, b) => a.TimelineRange.StartUs.CompareTo(b.TimelineRange.StartUs));
        var text = new StringBuilder();
        for (int i = 0; i < ordered.Length; i++)
        {
            var cue = ordered[i];
            text.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("\r\n")
                .Append(FormatTime(cue.TimelineRange.StartUs)).Append(" --> ")
                .Append(FormatTime(cue.TimelineRange.EndUs)).Append("\r\n")
                .Append(cue.Text).Append("\r\n\r\n");
        }
        return text.ToString();
    }

    private static string FormatTime(long us)
    {
        long ms = TimeRangeUs.RoundToMilliseconds(us);
        return string.Create(CultureInfo.InvariantCulture,
            $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00},{ms % 1000:000}");
    }
}

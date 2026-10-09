using System.Text.RegularExpressions;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;
namespace CaptionForge.Application.Services;

public static class ExistingSubtitleCueBuilder
{
    public static SubtitleCue Build(string id, string text, TimeRangeUs range)
    {
        var tokens = Regex.Split(text.Trim(), @"\s+").Where(t => t.Length > 0).ToArray();
        long duration = TimeRangeUs.RoundToMilliseconds(range.DurationUs);
        if (tokens.Length == 0 || duration < tokens.Length) throw new InvalidDataException("El bloque está vacío o es demasiado corto para sus palabras.");
        var words = new List<TimedWord>(); long cursor = 0, totalWeight = tokens.Sum(t => (long)t.Length), usedWeight = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            usedWeight += tokens[i].Length;
            long end = i == tokens.Length - 1 ? duration : Math.Clamp(duration * usedWeight / totalWeight, cursor + 1, duration - (tokens.Length - i - 1));
            words.Add(new(tokens[i], cursor, end)); if (i + 1 < tokens.Length) words.Add(new(" ", end, end)); cursor = end;
        }
        return new(id, range, words);
    }
}

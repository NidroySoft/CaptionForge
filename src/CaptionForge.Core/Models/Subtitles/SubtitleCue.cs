using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Core.Models.Subtitles;

/// <summary>Caption listo para adaptar a CapCut: rango global y palabras relativas.</summary>
public sealed record SubtitleCue
{
    public string SourceSegmentId { get; }
    public TimeRangeUs TimelineRange { get; }
    public IReadOnlyList<TimedWord> Words { get; }
    public string Text { get; }

    public SubtitleCue(string sourceSegmentId, TimeRangeUs timelineRange,
        IEnumerable<TimedWord> words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSegmentId);
        ArgumentNullException.ThrowIfNull(words);
        if (timelineRange.IsEmpty)
            throw new ArgumentException("Un caption debe tener duración positiva.", nameof(timelineRange));
        var copy = words.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("Un caption debe contener palabras.", nameof(words));
        long limitMs = TimeRangeUs.RoundToMilliseconds(timelineRange.DurationUs);
        long previousEnd = 0;
        for (int i = 0; i < copy.Length; i++)
        {
            var word = copy[i];
            ArgumentNullException.ThrowIfNull(word);
            if (word.StartTimeMs < previousEnd || word.EndTimeMs > limitMs)
                throw new ArgumentException("Las palabras se solapan o exceden el caption.", nameof(words));
            if (word.IsSpace != (i % 2 == 1))
                throw new ArgumentException("El molde requiere alternar palabra y espacio explícito.", nameof(words));
            if (word.IsSpace && word.StartTimeMs != previousEnd)
                throw new ArgumentException("El espacio debe coincidir con el final de la palabra previa.", nameof(words));
            previousEnd = word.EndTimeMs;
        }
        if (copy[^1].IsSpace || copy[0].StartTimeMs != 0 || copy[^1].EndTimeMs != limitMs)
            throw new ArgumentException("Los extremos de las palabras deben cubrir el caption según el molde v3.", nameof(words));
        SourceSegmentId = sourceSegmentId;
        TimelineRange = timelineRange;
        Words = Array.AsReadOnly(copy);
        Text = string.Concat(copy.Select(word => word.Text));
    }
}

using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Core.Models.Transcription;

/// <summary>Frase reconocida; tanto su rango como los tokens se refieren al mismo WAV.</summary>
public sealed record TranscriptionSegment
{
    public string Text { get; }
    public TimeRangeUs Range { get; }
    public IReadOnlyList<TranscriptionToken> Tokens { get; }

    public TranscriptionSegment(string text, TimeRangeUs range,
        IEnumerable<TranscriptionToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokens);
        var copy = tokens.ToArray();
        foreach (var token in copy)
            ArgumentNullException.ThrowIfNull(token);
        Text = text;
        Range = range;
        Tokens = Array.AsReadOnly(copy);
    }
}

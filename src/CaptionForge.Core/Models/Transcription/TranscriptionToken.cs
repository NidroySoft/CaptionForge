using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Core.Models.Transcription;

/// <summary>Token con rango absoluto dentro del WAV preparado, normalizado a microsegundos.</summary>
public sealed record TranscriptionToken
{
    /// <summary>Conserva espacios iniciales: distinguen una palabra de su continuación.</summary>
    public string Text { get; }
    public TimeRangeUs Range { get; }
    public bool IsControl { get; }

    public TranscriptionToken(string text, TimeRangeUs range, bool isControl = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Range = range;
        IsControl = isControl;
    }
}

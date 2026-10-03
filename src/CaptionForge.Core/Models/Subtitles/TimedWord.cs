namespace CaptionForge.Core.Models.Subtitles;

/// <summary>Palabra o espacio explícito, con tiempos en milisegundos relativos al caption.</summary>
public sealed record TimedWord
{
    public string Text { get; }
    public long StartTimeMs { get; }
    public long EndTimeMs { get; }
    public bool IsSpace => Text == " ";

    public TimedWord(string text, long startTimeMs, long endTimeMs)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfNegative(startTimeMs);
        ArgumentOutOfRangeException.ThrowIfNegative(endTimeMs);
        if (endTimeMs < startTimeMs)
            throw new ArgumentException("El final no puede preceder al inicio.", nameof(endTimeMs));
        if (text == " ")
        {
            if (startTimeMs != endTimeMs)
                throw new ArgumentException("El espacio del molde v3 debe tener duración cero.", nameof(endTimeMs));
        }
        else
        {
            if (text.Any(char.IsWhiteSpace))
                throw new ArgumentException("Una palabra no puede contener espacios internos.", nameof(text));
            if (startTimeMs == endTimeMs)
                throw new ArgumentException("Una palabra debe tener duración positiva.", nameof(endTimeMs));
        }
        Text = text;
        StartTimeMs = startTimeMs;
        EndTimeMs = endTimeMs;
    }
}

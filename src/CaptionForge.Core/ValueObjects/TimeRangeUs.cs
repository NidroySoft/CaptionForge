namespace CaptionForge.Core.ValueObjects;

/// <summary>Intervalo con inicio y duración en microsegundos. El final es exclusivo.</summary>
public readonly record struct TimeRangeUs
{
    public long StartUs { get; }
    public long DurationUs { get; }
    public long EndUs => checked(StartUs + DurationUs);
    public bool IsEmpty => DurationUs == 0;

    public TimeRangeUs(long startUs, long durationUs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startUs);
        ArgumentOutOfRangeException.ThrowIfNegative(durationUs);
        _ = checked(startUs + durationUs);
        StartUs = startUs;
        DurationUs = durationUs;
    }

    public static TimeRangeUs FromMilliseconds(long startMs, long durationMs) =>
        new(checked(startMs * 1_000), checked(durationMs * 1_000));

    /// <summary>Redondeo al milisegundo más cercano; los puntos medios positivos suben.</summary>
    public static long RoundToMilliseconds(long microseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(microseconds);
        // La separación evita desbordar al sumar 500 a long.MaxValue.
        return microseconds / 1_000 + (microseconds % 1_000 >= 500 ? 1 : 0);
    }
}

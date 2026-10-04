namespace CaptionForge.Core.ValueObjects;

/// <summary>FPS como razón exacta: 30/1, 25/1 o 30000/1001, por ejemplo.</summary>
public readonly record struct FrameRate
{
    public int Numerator { get; }
    public int Denominator { get; }
    public bool IsValid => Numerator > 0 && Denominator > 0;
    public decimal FramesPerSecond
    {
        get
        {
            EnsureValid();
            return (decimal)Numerator / Denominator;
        }
    }

    public FrameRate(int numerator, int denominator = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        var divisor = GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    /// <summary>floor(tiempo × FPS). Sirve también para contar frames de una duración.</summary>
    public long ToFrameIndex(long microseconds)
    {
        EnsureValid();
        ArgumentOutOfRangeException.ThrowIfNegative(microseconds);
        return checked((long)((Int128)microseconds * Numerator / ((Int128)1_000_000 * Denominator)));
    }

    /// <summary>Inicio del frame en microsegundos, truncado como en el fixture v3.</summary>
    public long GetFrameStartUs(long frameIndex)
    {
        EnsureValid();
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        return checked((long)((Int128)frameIndex * 1_000_000 * Denominator / Numerator));
    }

    private void EnsureValid()
    {
        if (!IsValid)
            throw new InvalidOperationException("FrameRate debe construirse con FPS positivos; default no es válido.");
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}

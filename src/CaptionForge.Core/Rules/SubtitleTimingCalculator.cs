using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Core.Rules;

/// <summary>Reglas temporales sin acceso a archivos ni dependencia del motor de transcripción.</summary>
public static class SubtitleTimingCalculator
{
    /// <summary>
    /// Cuantiza una frase local del WAV: floor(inicio × FPS), floor(duración × FPS).
    /// No cuantiza el final independientemente, ni debe aplicarse dos veces al mismo rango.
    /// </summary>
    public static TimeRangeUs QuantizeToFrames(TimeRangeUs rawRange, FrameRate frameRate)
    {
        long startFrame = frameRate.ToFrameIndex(rawRange.StartUs);
        long durationFrames = frameRate.ToFrameIndex(rawRange.DurationUs);
        long startUs = frameRate.GetFrameStartUs(startFrame);
        long endUs = frameRate.GetFrameStartUs(checked(startFrame + durationFrames));
        return new TimeRangeUs(startUs, checked(endUs - startUs));
    }

    /// <summary>Devuelve el rango local acotado al WAV, o null si no queda duración.</summary>
    public static TimeRangeUs? ConstrainToFragment(TimeRangeUs localRange, long fragmentDurationUs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fragmentDurationUs);
        if (localRange.StartUs >= fragmentDurationUs || localRange.IsEmpty)
            return null;
        long endUs = Math.Min(localRange.EndUs, fragmentDurationUs);
        return new TimeRangeUs(localRange.StartUs, endUs - localRange.StartUs);
    }

    /// <summary>
    /// Traslada un rango local a la timeline. El WAV ya debe reflejar la velocidad del montaje.
    /// No usa el inicio de origen ni elimina los huecos entre fragmentos.
    /// </summary>
    public static TimeRangeUs PlaceOnTimeline(TimeRangeUs localRange, TimeRangeUs targetRange)
    {
        if (localRange.IsEmpty)
            throw new ArgumentException("El rango local debe tener duración positiva.", nameof(localRange));
        if (localRange.EndUs > targetRange.DurationUs)
            throw new ArgumentException("El rango local excede la duración de destino; acótalo antes.", nameof(localRange));
        return new TimeRangeUs(checked(targetRange.StartUs + localRange.StartUs), localRange.DurationUs);
    }
}

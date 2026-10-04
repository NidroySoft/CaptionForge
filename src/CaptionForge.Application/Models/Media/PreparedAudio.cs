using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Media;

/// <summary>WAV PCM preparado a 16 kHz mono/16 bits y duración lógica de destino; el adaptador verifica sus bytes.</summary>
public sealed record PreparedAudio
{
    public string SourceSegmentId { get; }
    public string Path { get; }
    public long DurationUs { get; }
    /// <summary>Duración medida del WAV; puede diferir un máximo de una muestra de 16 kHz (63 µs).</summary>
    public long MeasuredDurationUs { get; }

    public PreparedAudio(string sourceSegmentId, string path, long durationUs, long measuredDurationUs)
    {

        Guard.Text(sourceSegmentId, nameof(sourceSegmentId));
        Guard.Text(path, nameof(path));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationUs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredDurationUs);
        if (Math.Abs(measuredDurationUs - durationUs) > 63)
            throw new ArgumentException("La duración medida no corresponde al destino con precisión de una muestra.", nameof(measuredDurationUs));
        SourceSegmentId = sourceSegmentId;
        Path = path;
        DurationUs = durationUs;
        MeasuredDurationUs = measuredDurationUs;
    }

}

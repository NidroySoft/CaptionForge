namespace CaptionForge.Core.Models.Transcription;

/// <summary>Transcripción de un único fragmento, independiente de Whisper.net.</summary>
public sealed record TranscriptionResult
{
    public string SourceSegmentId { get; }
    public string ModelName { get; }
    public string Language { get; }
    public long PreparedAudioDurationUs { get; }
    public IReadOnlyList<TranscriptionSegment> Segments { get; }

    public TranscriptionResult(string sourceSegmentId, string modelName, string language,
        long preparedAudioDurationUs, IEnumerable<TranscriptionSegment> segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSegmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(preparedAudioDurationUs);
        ArgumentNullException.ThrowIfNull(segments);
        var copy = segments.ToArray();
        foreach (var segment in copy)
            ArgumentNullException.ThrowIfNull(segment);
        SourceSegmentId = sourceSegmentId;
        ModelName = modelName;
        Language = language;
        PreparedAudioDurationUs = preparedAudioDurationUs;
        Segments = Array.AsReadOnly(copy);
    }
}

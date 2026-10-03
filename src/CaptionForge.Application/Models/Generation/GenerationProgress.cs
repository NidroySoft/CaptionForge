using CaptionForge.Application.Enums;
using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Generation;

/// <summary>Etapa y cantidad de fragmentos finalizados. No promete porcentajes artificiales del motor.</summary>
public sealed record GenerationProgress
{
    public string RunId { get; }
    public RunStatus Stage { get; }
    public int CompletedSegments { get; }
    public int TotalSegments { get; }
    public string? SegmentId { get; }

    public GenerationProgress(string runId, RunStatus stage, int completedSegments, int totalSegments, string? segmentId = null)
    {

        Guard.Text(runId, nameof(runId));
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalSegments);
        if (completedSegments < 0 || completedSegments > totalSegments)
            throw new ArgumentOutOfRangeException(nameof(completedSegments));
        RunId = runId;
        Stage = stage;
        CompletedSegments = completedSegments;
        TotalSegments = totalSegments;
        SegmentId = segmentId;
    }

}

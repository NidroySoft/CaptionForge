using CaptionForge.Application.Internal;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
namespace CaptionForge.Application.Models.Generation;

/// <summary>Resultado listo para revisión y aplicación explícita. Cada transcripción corresponde a un fragmento.</summary>
public sealed record GenerationResult
{
    public RunContext Run { get; }
    public TimelineSnapshot Snapshot { get; }
    public TranscriptionOptions Options { get; }
    public IReadOnlyList<SubtitleCue> Captions { get; }
    public IReadOnlyList<TranscriptionResult> Transcriptions { get; }
    public PreparedSubtitlePlan Plan { get; }

    public GenerationResult(RunContext run, TimelineSnapshot snapshot, TranscriptionOptions options,
        IEnumerable<SubtitleCue> captions, IEnumerable<TranscriptionResult> transcriptions, PreparedSubtitlePlan plan)
    {

        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plan);
        var captionCopy = Guard.Copy(captions, nameof(captions));
        var transcriptionCopy = Guard.Copy(transcriptions, nameof(transcriptions));
        Guard.Unique(transcriptionCopy.Select(t => t.SourceSegmentId), nameof(transcriptions));
        if (captionCopy.Count == 0 || plan.CaptionCount != captionCopy.Count || plan.Run != run ||
            snapshot.Project.Id != run.ProjectId || snapshot.Timeline.Id != run.TimelineId)
            throw new ArgumentException("Resultado, captura y plan no corresponden.", nameof(plan));
        if (captionCopy.Any(c => !transcriptionCopy.Any(t => t.SourceSegmentId == c.SourceSegmentId)))
            throw new ArgumentException("Hay captions sin su transcripción.", nameof(captions));
        Run = run;
        Snapshot = snapshot;
        Options = options;
        Captions = captionCopy;
        Transcriptions = transcriptionCopy;
        Plan = plan;
    }

}

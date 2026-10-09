using CaptionForge.Application.Internal;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Core.Models.Subtitles;
namespace CaptionForge.Application.Models.Writing;

/// <summary>Preparar una copia usando el molde v3; preservar textos ajenos y actualizar solamente el conjunto propio.</summary>
public sealed record SubtitleWriteRequest
{
    public RunContext Run { get; }
    public TimelineSnapshot Snapshot { get; }
    public IReadOnlyList<SubtitleCue> Captions { get; }
    public ManagedSubtitleSet? PreviouslyManaged { get; }
    public string? RemoveSourceTrackId { get; }
    public string? SourceSubtitleTrackId { get; }

    public SubtitleWriteRequest(RunContext run, TimelineSnapshot snapshot, IEnumerable<SubtitleCue> captions, ManagedSubtitleSet? previouslyManaged = null, string? removeSourceTrackId = null, string? sourceSubtitleTrackId = null)
    {

        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (run.ProjectId != snapshot.Project.Id || run.TimelineId != snapshot.Timeline.Id)
            throw new ArgumentException("La ejecución y la captura no corresponden.", nameof(run));
        if (previouslyManaged is not null && (previouslyManaged.ProjectId != run.ProjectId || previouslyManaged.TimelineId != run.TimelineId))
            throw new ArgumentException("Las identidades administradas pertenecen a otra timeline.", nameof(previouslyManaged));
        var copy = Guard.Copy(captions, nameof(captions));
        if (copy.Count == 0) throw new ArgumentException("No hay subtítulos para preparar.", nameof(captions));
        Run = run;
        Snapshot = snapshot;
        Captions = copy;
        PreviouslyManaged = previouslyManaged;
        RemoveSourceTrackId = removeSourceTrackId;
        SourceSubtitleTrackId = sourceSubtitleTrackId;
    }

    public string TemplateResourceId => "7535399757947161873";
    public string FontResourceId => "7517426090072149264";
    public bool PreserveUnmanagedSubtitles => true;

}

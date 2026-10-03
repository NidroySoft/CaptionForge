using CaptionForge.Application.Internal;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
namespace CaptionForge.Application.Models.CapCut;

/// <summary>Lectura coherente de la timeline seleccionada, sus medios y huellas; conserva los tres IDs distintos.</summary>
public sealed record TimelineSnapshot
{
    public CapCutProject Project { get; }
    public CapCutTimeline Timeline { get; }
    public IReadOnlyList<MediaTrack> Tracks { get; }
    public IReadOnlyList<SourceFileStamp> SourceFiles { get; }

    public TimelineSnapshot(CapCutProject project, CapCutTimeline timeline, IEnumerable<MediaTrack> tracks, IEnumerable<SourceFileStamp> sourceFiles)
    {

        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(timeline);
        if (timeline.ProjectId != project.Id)
            throw new ArgumentException("La timeline no pertenece al proyecto.", nameof(timeline));
        var trackCopy = Guard.Copy(tracks, nameof(tracks));
        var fileCopy = Guard.Copy(sourceFiles, nameof(sourceFiles));
        Guard.Unique(trackCopy.Select(t => t.Id), nameof(tracks));
        Guard.Unique(trackCopy.SelectMany(t => t.Segments).Select(s => s.Id), nameof(tracks));
        // Rutas de un contrato Windows: la comparación de ruta no distingue mayúsculas.
        if (fileCopy.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fileCopy.Count)
            throw new ArgumentException("Hay rutas duplicadas en la captura.", nameof(sourceFiles));
        if (!fileCopy.Any(f => f.Exists && string.Equals(f.Path, timeline.DraftContentPath, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Falta la huella del draft_content seleccionado.", nameof(sourceFiles));
        if (!fileCopy.Any(f => string.Equals(f.Path, timeline.DraftContentPath + ".bak", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Falta el estado del .bak nativo seleccionado, incluso si aún no existe.", nameof(sourceFiles));
        Project = project;
        Timeline = timeline;
        Tracks = trackCopy;
        SourceFiles = fileCopy;
    }

}

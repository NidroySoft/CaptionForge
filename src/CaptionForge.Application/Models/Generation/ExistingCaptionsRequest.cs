using CaptionForge.Application.Models.CapCut;
using CaptionForge.Core.Models.Subtitles;
namespace CaptionForge.Application.Models.Generation;

public sealed record CaptionImportOrigin(string Kind, string Name, bool ApproximateWordTimings, string? TrackId = null);
public sealed record ExistingCaptionsRequest
{
    public TimelineSnapshot Snapshot { get; }
    public IReadOnlyList<SubtitleCue> Captions { get; }
    public string WorkspaceRoot { get; }
    public CaptionImportOrigin Origin { get; }
    public string? RemoveSourceTrackId { get; }
    public ExistingCaptionsRequest(TimelineSnapshot snapshot, IEnumerable<SubtitleCue> captions, string workspaceRoot, CaptionImportOrigin origin, string? removeSourceTrackId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(origin); ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (origin.Kind is not "CapCutTrack" and not "SubtitleFile") throw new ArgumentException("Origen de subtítulos no válido.");
        var copy = captions.OrderBy(c => c.TimelineRange.StartUs).ToArray();
        if (copy.Length == 0) throw new ArgumentException("No hay subtítulos para preparar.");
        if (removeSourceTrackId is not null && origin.Kind != "CapCutTrack") throw new ArgumentException("Solo se puede eliminar una pista usada como origen.");
        if (removeSourceTrackId is not null && removeSourceTrackId != origin.TrackId) throw new ArgumentException("La pista a eliminar debe ser la pista original seleccionada.");
        Snapshot = snapshot; Captions = Array.AsReadOnly(copy); WorkspaceRoot = workspaceRoot; Origin = origin; RemoveSourceTrackId = removeSourceTrackId;
    }
}

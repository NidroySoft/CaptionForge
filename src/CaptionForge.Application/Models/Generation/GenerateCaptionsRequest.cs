using CaptionForge.Application.Internal;
using CaptionForge.Application.Models.CapCut;
namespace CaptionForge.Application.Models.Generation;

/// <summary>Selección inmutable. Los fragmentos excluidos siguen intactos en CapCut.</summary>
public sealed record GenerateCaptionsRequest
{
    public TimelineSnapshot Snapshot { get; }
    public IReadOnlyList<string> SelectedSegmentIds { get; }
    public TranscriptionOptions Options { get; }
    public string WorkspaceRoot { get; }
    public IReadOnlyList<SourcePathOverride> SourceOverrides { get; }

    public GenerateCaptionsRequest(TimelineSnapshot snapshot, IEnumerable<string> selectedSegmentIds,
        TranscriptionOptions options, string workspaceRoot,
        IEnumerable<SourcePathOverride>? sourceOverrides = null)
    {

        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        Guard.Text(workspaceRoot, nameof(workspaceRoot));
        var selected = Guard.Copy(selectedSegmentIds, nameof(selectedSegmentIds));
        Guard.Unique(selected, nameof(selectedSegmentIds));
        if (selected.Count == 0) throw new ArgumentException("Selecciona al menos un fragmento.", nameof(selectedSegmentIds));
        var overrides = Guard.Copy(sourceOverrides ?? Array.Empty<SourcePathOverride>(), nameof(sourceOverrides));
        Guard.Unique(overrides.Select(o => o.SegmentId), nameof(sourceOverrides));
        if (overrides.Any(o => !selected.Contains(o.SegmentId, StringComparer.Ordinal)))
            throw new ArgumentException("Una ruta alternativa pertenece a un fragmento no seleccionado.", nameof(sourceOverrides));
        Snapshot = snapshot;
        SelectedSegmentIds = selected;
        Options = options;
        WorkspaceRoot = workspaceRoot;
        SourceOverrides = overrides;
    }

}

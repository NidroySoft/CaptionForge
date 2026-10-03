using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Generation;

/// <summary>Localización manual para esta ejecución; no modifica la ruta almacenada por CapCut.</summary>
public sealed record SourcePathOverride
{
    public string SegmentId { get; }
    public string SourcePath { get; }

    public SourcePathOverride(string segmentId, string sourcePath)
    {

        Guard.Text(segmentId, nameof(segmentId));
        Guard.Text(sourcePath, nameof(sourcePath));
        SegmentId = segmentId;
        SourcePath = sourcePath;
    }

}

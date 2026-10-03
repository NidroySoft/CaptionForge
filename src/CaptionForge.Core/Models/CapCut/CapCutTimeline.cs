using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Core.Models.CapCut;

/// <summary>Identidad y datos temporales de una timeline válida. IsMain no significa pestaña activa.</summary>
public sealed record CapCutTimeline
{
    public string Id { get; }
    public string ProjectId { get; }
    public string Name { get; }
    public string DirectoryPath { get; }
    public string DraftContentPath { get; }
    public string? CoverPath { get; }
    public bool IsMain { get; }
    public FrameRate FrameRate { get; }
    public long DurationUs { get; }

    public CapCutTimeline(string id, string projectId, string name, string directoryPath,
        string draftContentPath, FrameRate frameRate, long durationUs,
        bool isMain = false, string? coverPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(draftContentPath);
        ArgumentOutOfRangeException.ThrowIfNegative(durationUs);
        if (!frameRate.IsValid)
            throw new ArgumentException("La timeline necesita FPS válidos.", nameof(frameRate));
        Id = id;
        ProjectId = projectId;
        Name = name;
        DirectoryPath = directoryPath;
        DraftContentPath = draftContentPath;
        FrameRate = frameRate;
        DurationUs = durationUs;
        IsMain = isMain;
        CoverPath = coverPath;
    }
}

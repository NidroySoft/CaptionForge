namespace CaptionForge.Core.Models.CapCut;

/// <summary>Proyecto identificado por draft_meta_info.draft_id, nunca por la timeline.</summary>
public sealed record CapCutProject
{
    public string Id { get; }
    public string Name { get; }
    public string DirectoryPath { get; }
    public string? CoverPath { get; }
    public DateTimeOffset? LastModifiedAt { get; }
    public string? TimelineRegistryId { get; }

    public CapCutProject(string id, string name, string directoryPath,
        string? coverPath = null, DateTimeOffset? lastModifiedAt = null,
        string? timelineRegistryId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        Id = id;
        Name = name;
        DirectoryPath = directoryPath;
        CoverPath = coverPath;
        LastModifiedAt = lastModifiedAt;
        TimelineRegistryId = timelineRegistryId;
    }
}

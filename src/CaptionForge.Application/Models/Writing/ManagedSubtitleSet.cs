using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Writing;

/// <summary>Única prueba de autoría para actualizar subtítulos propios; no se deduce por color, tipo o plantilla.</summary>
public sealed record ManagedSubtitleSet
{
    public string ProjectId { get; }
    public string TimelineId { get; }
    public IReadOnlyList<ManagedSubtitleObject> Objects { get; }

    public ManagedSubtitleSet(string projectId, string timelineId, IEnumerable<ManagedSubtitleObject> objects)
    {

        Guard.Text(projectId, nameof(projectId));
        Guard.Text(timelineId, nameof(timelineId));
        var copy = Guard.Copy(objects, nameof(objects));
        Guard.Unique(copy.Select(o => o.Id), nameof(objects));
        if (copy.Select(o => (o.Kind, o.LogicalKey)).Distinct().Count() != copy.Count)
            throw new ArgumentException("Claves lógicas repetidas para un mismo tipo de objeto.", nameof(objects));
        ProjectId = projectId;
        TimelineId = timelineId;
        Objects = copy;
    }

}

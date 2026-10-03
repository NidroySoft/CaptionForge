using CaptionForge.Application.Enums;
using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Writing;

/// <summary>Identidad propia persistida. LogicalKey relaciona un objeto con su función para conservar IDs al actualizar.</summary>
public sealed record ManagedSubtitleObject
{
    public SubtitleObjectKind Kind { get; }
    public string Id { get; }
    public string LogicalKey { get; }

    public ManagedSubtitleObject(SubtitleObjectKind kind, string id, string logicalKey)
    {

        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Guard.Text(id, nameof(id));
        Guard.Text(logicalKey, nameof(logicalKey));
        Kind = kind;
        Id = id;
        LogicalKey = logicalKey;
    }

}

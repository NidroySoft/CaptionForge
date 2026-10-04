using CaptionForge.Core.Enums;

namespace CaptionForge.Core.Models.Media;

/// <summary>Pista de la timeline; su nombre puede estar vacío en el JSON original.</summary>
public sealed record MediaTrack
{
    public string Id { get; }
    public string Name { get; }
    public MediaTrackType Type { get; }
    public IReadOnlyList<MediaSegment> Segments { get; }

    public MediaTrack(string id, string name, MediaTrackType type,
        IEnumerable<MediaSegment>? segments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        var copy = segments?.ToArray() ?? Array.Empty<MediaSegment>();
        foreach (var segment in copy)
        {
            ArgumentNullException.ThrowIfNull(segment);
            if (!string.Equals(segment.TrackId, id, StringComparison.Ordinal))
                throw new ArgumentException("Un fragmento pertenece a una pista diferente.", nameof(segments));
        }
        if (type == MediaTrackType.Text && copy.Length != 0)
            throw new ArgumentException("Los segmentos de texto se representan como SubtitleCue, no MediaSegment.", nameof(segments));
        Id = id;
        Name = name;
        Type = type;
        Segments = Array.AsReadOnly(copy);
    }
}

namespace CaptionForge.Application.Internal;

internal static class Guard
{
    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(items, name);
        var copy = items.ToArray();
        if (copy.Any(item => item is null))
            throw new ArgumentException("La colección contiene elementos nulos.", name);
        return Array.AsReadOnly(copy);
    }

    internal static void Text(string value, string name) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);

    internal static void Unique(IEnumerable<string> ids, string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            Text(id, name);
            if (!seen.Add(id))
                throw new ArgumentException("La colección contiene identidades duplicadas.", name);
        }
    }
}

namespace CaptionForge.Infrastructure.Internal;

internal static class PathSafety
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static string Full(string path) { ArgumentException.ThrowIfNullOrWhiteSpace(path); return Path.GetFullPath(path); }
    internal static string Part(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(id, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("El ID no puede utilizarse como nombre de carpeta.");
        return id;
    }
    internal static bool Inside(string path, string root)
    {
        string a=Full(path), b=Path.TrimEndingDirectorySeparator(Full(root));
        return a.Equals(b,Comparison) || a.StartsWith(b+Path.DirectorySeparatorChar,Comparison);
    }
    internal static void RequireInside(string path, string root)
    { if (!Inside(path,root)) throw new InvalidDataException("Una ruta sale de su carpeta autorizada."); }
    internal static void RequireSeparate(string a, string b)
    { if (Inside(a,b) || Inside(b,a)) throw new InvalidDataException("El espacio propio y los proyectos de CapCut deben estar separados."); }
    internal static void RejectLinks(string path)
    {
        string? current=Full(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("No se admite escritura mediante enlaces o junctions: usa la ruta física.");
            current=Path.GetDirectoryName(current);
        }
    }
}

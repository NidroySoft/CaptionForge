using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.CapCut;

/// <summary>Estado de un archivo que el escritor revalidará inmediatamente antes del backup.</summary>
public sealed record SourceFileStamp
{
    public string Path { get; }
    public bool Exists { get; }
    public string? Sha256 { get; }

    public SourceFileStamp(string path, bool exists, string? sha256 = null)
    {

        Guard.Text(path, nameof(path));
        if (exists && (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)))
            throw new ArgumentException("Un archivo existente necesita su SHA-256 hexadecimal.", nameof(sha256));
        if (!exists && sha256 is not null)
            throw new ArgumentException("Un archivo inexistente no tiene hash.", nameof(sha256));
        Path = path;
        Exists = exists;
        Sha256 = sha256;
    }

}

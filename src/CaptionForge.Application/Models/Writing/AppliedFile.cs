using CaptionForge.Application.Internal;
using CaptionForge.Application.Models.CapCut;
namespace CaptionForge.Application.Models.Writing;

/// <summary>Recibo de un archivo cambiado. El backup propio contiene sus bytes anteriores si existía.</summary>
public sealed record AppliedFile
{
    public SourceFileStamp Before { get; }
    public string Sha256After { get; }
    public string? BackupPath { get; }

    public AppliedFile(SourceFileStamp before, string sha256After, string? backupPath = null)
    {

        ArgumentNullException.ThrowIfNull(before);
        if (sha256After is null || sha256After.Length != 64 || !sha256After.All(Uri.IsHexDigit))
            throw new ArgumentException("Hash final inválido.", nameof(sha256After));
        if (before.Exists) Guard.Text(backupPath!, nameof(backupPath));
        if (!before.Exists && backupPath is not null)
            throw new ArgumentException("Un archivo nuevo no tiene bytes anteriores.", nameof(backupPath));
        Before = before;
        Sha256After = sha256After;
        BackupPath = backupPath;
    }

}

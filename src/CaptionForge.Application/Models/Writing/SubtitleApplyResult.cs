using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Writing;

/// <summary>Recibo durable de commit y backups. La aplicación comprueba que coincide con el plan.</summary>
public sealed record SubtitleApplyResult
{
    public string RunId { get; }
    public string JournalPath { get; }
    public IReadOnlyList<AppliedFile> Files { get; }

    public SubtitleApplyResult(string runId, string journalPath, IEnumerable<AppliedFile> files)
    {

        Guard.Text(runId, nameof(runId));
        Guard.Text(journalPath, nameof(journalPath));
        var copy = Guard.Copy(files, nameof(files));
        if (copy.Count == 0 || copy.Select(f => f.Before.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Count)
            throw new ArgumentException("El recibo necesita archivos únicos.", nameof(files));
        RunId = runId;
        JournalPath = journalPath;
        Files = copy;
    }

}

using CaptionForge.Application.Internal;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Workspace;
namespace CaptionForge.Application.Models.Writing;

/// <summary>Plan durable y validado, todavía sin tocar CapCut; contiene hashes de todos los archivos que modificará.</summary>
public sealed record PreparedSubtitlePlan
{
    public RunContext Run { get; }
    public string PlanPath { get; }
    public string PlanSha256 { get; }
    public int CaptionCount { get; }
    public IReadOnlyList<SourceFileStamp> ExpectedFiles { get; }
    public ManagedSubtitleSet ManagedSubtitles { get; }
    public IReadOnlyList<string> Warnings { get; }
    public SubtitleOverwriteInfo? OverwriteInfo { get; }

    public PreparedSubtitlePlan(RunContext run, string planPath, string planSha256, int captionCount, IEnumerable<SourceFileStamp> expectedFiles,
        ManagedSubtitleSet managedSubtitles, IEnumerable<string>? warnings = null, SubtitleOverwriteInfo? overwriteInfo = null)
    {

        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(managedSubtitles);
        Guard.Text(planPath, nameof(planPath));
        if (planSha256 is null || planSha256.Length != 64 || !planSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("El plan durable necesita su SHA-256.", nameof(planSha256));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captionCount);
        var files = Guard.Copy(expectedFiles, nameof(expectedFiles));
        if (files.Count == 0 || files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new ArgumentException("El plan necesita rutas únicas.", nameof(expectedFiles));
        if (managedSubtitles.ProjectId != run.ProjectId || managedSubtitles.TimelineId != run.TimelineId || managedSubtitles.Objects.Count == 0)
            throw new ArgumentException("El plan necesita las identidades propias de la timeline.", nameof(managedSubtitles));
        var warningCopy = Guard.Copy(warnings ?? Array.Empty<string>(), nameof(warnings));
        Run = run;
        PlanPath = planPath;
        PlanSha256 = planSha256;
        CaptionCount = captionCount;
        ExpectedFiles = files;
        ManagedSubtitles = managedSubtitles;
        Warnings = warningCopy;
        OverwriteInfo = overwriteInfo;
    }

}

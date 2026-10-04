using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Models.Writing;

namespace CaptionForge.Infrastructure.CapCut;

/// <summary>Molde del draft real con el mismo commit, backup, pin y restauración que el escritor golden.</summary>
public sealed class DraftTemplateSubtitleWriter : ICapCutSubtitleWriter
{
    private readonly GoldenV3SubtitleWriter _transaction;
    public DraftTemplateSubtitleWriter(string templateSegmentId, ICapCutProcessGuard? processGuard = null,
        IFileCommitter? fileCommitter = null, bool validateTemplateAssets = true)
        => _transaction = new(templateSegmentId, validateTemplateAssets, processGuard, fileCommitter);
    public Task<PreparedSubtitlePlan> PrepareAsync(SubtitleWriteRequest request, CancellationToken cancellationToken = default)
        => _transaction.PrepareAsync(request, cancellationToken);
    public Task<SubtitleApplyResult> ApplyAsync(PreparedSubtitlePlan plan, CancellationToken cancellationToken = default)
        => _transaction.ApplyAsync(plan, cancellationToken);
    public Task RestoreAsync(string journalPath, CancellationToken cancellationToken = default)
        => _transaction.RestoreAsync(journalPath, cancellationToken);
}

using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;

namespace CaptionForge.Application.Abstractions;

public interface IWorkspaceStore
{
    /// <summary>
    /// Crea carpeta bajo workspaceRoot / project.draft_id / runs / ejecución única y manifiesto versionado Created.
    /// Persiste selección, captura, rangos, overrides y opciones. Valida que el espacio propio queda fuera de CapCut.
    /// Esta operación es íntegra: si falla, no deja una ejecución válida sin identidad recuperable.
    /// </summary>
    Task<RunContext> CreateRunAsync(GenerateCaptionsRequest request, CancellationToken cancellationToken = default);
    Task<ManagedSubtitleSet?> ReadManagedSubtitlesAsync(string projectId, string timelineId, CancellationToken cancellationToken = default);
    /// <summary>Guarda resultado revisable y su plan dentro del run; no cambia el registro de IDs ya aplicados.</summary>
    Task SaveGenerationAsync(GenerationResult result, CancellationToken cancellationToken = default);
    /// <summary>
    /// Comprobación y cambio de estado deben ser indivisibles por run; conflicto -> RunStateConflict, sin sobrescribir.
    /// Cancelación se acepta antes de persistir: una transición confirmada retorna normalmente, incluso si llega cancelación después.
    /// </summary>
    Task TransitionAsync(RunContext run, RunUpdate update, CancellationToken cancellationToken = default);
    /// <summary>
    /// Desde Applying: guarda recibo, actualiza registro de IDs propios de la timeline y estado Completed de forma recuperable.
    /// Si falla, el journal del escritor permite reconciliar el estado. No utiliza el .bak de CapCut como backup propio.
    /// </summary>
    Task CompleteAsync(GenerationResult result, SubtitleApplyResult applied, DateTimeOffset at, CancellationToken cancellationToken = default);
}

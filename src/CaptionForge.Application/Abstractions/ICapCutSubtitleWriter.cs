using CaptionForge.Application.Models.Writing;

namespace CaptionForge.Application.Abstractions;

public interface ICapCutSubtitleWriter
{
    /// <summary>
    /// Prepara/valida JSON derivado del documento real usando el molde golden v3, sin escribir en CapCut.
    /// Conserva propiedades desconocidas, IDs existentes, textos ajenos y canvas/FPS. Actualiza solo objetos propios.
    /// Incluye todos los archivos a modificar y sus huellas de lectura: timeline + .bak y espejo raíz solo si corresponde.
    /// Verifica assets, grafo, palabras, unidades y equivalencia con los captions antes de devolver el plan durable.
    /// Revalida la captura para preparar desde los mismos bytes leídos, y advierte si hay subtítulos ajenos con posible duplicación visual.
    /// </summary>
    Task<PreparedSubtitlePlan> PrepareAsync(SubtitleWriteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Antes de escribir, exige CapCut cerrado, exclusión entre escritores de la misma timeline y hashes vigentes
    /// de fuentes y plan. Crea backups propios byte a byte de TODOS los archivos existentes a tocar, también .bak.
    /// Persiste journal, plan e identidades administradas antes de commit; sincroniza .bak nativos al JSON nuevo.
    /// Cancelación solo antes de commit: durante commit finaliza o revierte coherentemente y conserva journal recuperable.
    /// Nunca declara atomicidad entre varios archivos. El recibo e identidades deben ser recuperables aunque falle el manifiesto.
    /// CapCutOpen, SourceChanged y ResourceUnavailable significan rechazo anterior a cualquier escritura.
    /// OperationCanceledException solo puede salir antes de escribir; un commit incierto se informa con otra excepción y journal.
    /// </summary>
    Task<SubtitleApplyResult> ApplyAsync(PreparedSubtitlePlan plan, CancellationToken cancellationToken = default);
}

using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;

namespace CaptionForge.Application.Services;

/// <summary>Orquestación secuencial MVP: generar un resultado revisable y aplicarlo por una llamada separada.</summary>
public sealed class CaptionGenerationService
{
    private readonly IAudioPreparationService _audio;
    private readonly ITranscriptionService _transcription;
    private readonly ICapCutSubtitleWriter _writer;
    private readonly IWorkspaceStore _workspace;
    private readonly SubtitleCuePlanner _planner;
    private readonly TimeProvider _clock;

    public CaptionGenerationService(IAudioPreparationService audio, ITranscriptionService transcription,
        ICapCutSubtitleWriter writer, IWorkspaceStore workspace,
        SubtitleCuePlanner? planner = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(transcription);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(workspace);
        _audio = audio;
        _transcription = transcription;
        _writer = writer;
        _workspace = workspace;
        _planner = planner ?? new SubtitleCuePlanner();
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Guarda artefactos en el espacio propio y prepara un plan. No escribe en el proyecto de CapCut.</summary>
    public async Task<GenerationResult> GenerateAsync(GenerateCaptionsRequest request,
        IProgress<GenerationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = ValidateSelection(request);
        var run = await _workspace.CreateRunAsync(request, cancellationToken).ConfigureAwait(false);
        RunStatus status = RunStatus.Created;
        try
        {
            if (run.ProjectId != request.Snapshot.Project.Id || run.TimelineId != request.Snapshot.Timeline.Id)
                throw InvalidResult("El almacén devolvió una ejecución de otro proyecto o timeline.", run.RunId);
            var previouslyManaged = await _workspace.ReadManagedSubtitlesAsync(run.ProjectId, run.TimelineId, cancellationToken).ConfigureAwait(false);
            var captions = new List<SubtitleCue>();
            var transcriptions = new List<TranscriptionResult>();
            int completed = 0;
            foreach (var segment in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await MoveAsync(RunStatus.PreparingAudio).ConfigureAwait(false);
                Report(progress, run, status, completed, selected.Count, segment.Id);
                string sourcePath = request.SourceOverrides.FirstOrDefault(o => o.SegmentId == segment.Id)?.SourcePath ?? segment.SourcePath;
                var audio = await _audio.PrepareAsync(new AudioPreparationRequest(run, segment, sourcePath), cancellationToken).ConfigureAwait(false);
                if (audio.SourceSegmentId != segment.Id || audio.DurationUs != segment.TargetRange.DurationUs)
                    throw InvalidResult("El WAV preparado no conserva la identidad o duración de destino.", run.RunId);

                await MoveAsync(RunStatus.Transcribing).ConfigureAwait(false);
                Report(progress, run, status, completed, selected.Count, segment.Id);
                var transcription = await _transcription.TranscribeAsync(run, audio, request.Options, cancellationToken).ConfigureAwait(false);
                if (transcription.SourceSegmentId != segment.Id || transcription.PreparedAudioDurationUs != audio.DurationUs ||
                    transcription.ModelName != request.Options.ModelName ||
                    (request.Options.Language != "auto" && !string.Equals(transcription.Language, request.Options.Language, StringComparison.OrdinalIgnoreCase)))
                    throw InvalidResult("Whisper devolvió una identidad, duración, modelo o idioma distinto al solicitado.", run.RunId);
                captions.AddRange(_planner.Build(segment, transcription, request.Snapshot.Timeline.FrameRate));
                transcriptions.Add(transcription);
                completed++;
                Report(progress, run, status, completed, selected.Count, segment.Id);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (captions.Count == 0)
                throw new CaptionForgeOperationException(OperationErrorCode.NoCaptions,
                    "No se reconocieron palabras con tiempos válidos en los fragmentos seleccionados.", run.RunId);
            await MoveAsync(RunStatus.PreparingSubtitles).ConfigureAwait(false);
            Report(progress, run, status, selected.Count, selected.Count);
            var plan = await _writer.PrepareAsync(new SubtitleWriteRequest(run, request.Snapshot, captions, previouslyManaged), cancellationToken).ConfigureAwait(false);
            ValidatePlan(plan, run, request.Snapshot, captions.Count);
            var result = new GenerationResult(run, request.Snapshot, request.Options, captions, transcriptions, plan);
            await _workspace.SaveGenerationAsync(result, cancellationToken).ConfigureAwait(false);
            await MoveAsync(RunStatus.ReadyToApply).ConfigureAwait(false);
            Report(progress, run, status, selected.Count, selected.Count);
            return result;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            await RecordTerminalAsync(run, status, RunStatus.Cancelled, ex).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await RecordTerminalAsync(run, status, RunStatus.Failed, ex).ConfigureAwait(false);
            if (ex is CaptionForgeOperationException operationError && operationError.RunId is null)
                throw new CaptionForgeOperationException(operationError.Code, operationError.Message, run.RunId, operationError);
            throw;
        }

        async Task MoveAsync(RunStatus next)
        {
            await _workspace.TransitionAsync(run, new RunUpdate(status, next, _clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            status = next;
        }
    }

    public async Task<GenerationResult> PrepareExistingAsync(ExistingCaptionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var run = await _workspace.CreateRunAsync(request, cancellationToken).ConfigureAwait(false);
        RunStatus status = RunStatus.Created;
        try
        {
            var previous = await _workspace.ReadManagedSubtitlesAsync(run.ProjectId, run.TimelineId, cancellationToken).ConfigureAwait(false);
            await _workspace.TransitionAsync(run, new(status, RunStatus.PreparingSubtitles, _clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            status = RunStatus.PreparingSubtitles;
            var plan = await _writer.PrepareAsync(new SubtitleWriteRequest(run, request.Snapshot, request.Captions, previous, request.RemoveSourceTrackId, request.Origin.TrackId), cancellationToken).ConfigureAwait(false);
            ValidatePlan(plan, run, request.Snapshot, request.Captions.Count);
            var result = new GenerationResult(run, request.Snapshot, null, request.Captions, [], plan, request.Origin);
            await _workspace.SaveGenerationAsync(result, cancellationToken).ConfigureAwait(false);
            await _workspace.TransitionAsync(run, new(status, RunStatus.ReadyToApply, _clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested) { await RecordTerminalAsync(run, status, RunStatus.Cancelled, ex).ConfigureAwait(false); throw; }
        catch (Exception ex) { await RecordTerminalAsync(run, status, RunStatus.Failed, ex).ConfigureAwait(false); throw; }
    }

    /// <summary>Aplica el resultado revisado. Un fallo desde Applying exige reconciliar el journal antes de reintentar.</summary>
    public async Task<SubtitleApplyResult> ApplyAsync(GenerationResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePlan(result.Plan, result.Run, result.Snapshot, result.Captions.Count);
        // Fuera del try: un conflicto aquí no puede sobrescribir el estado de la ejecución que ya se está aplicando.
        await _workspace.TransitionAsync(result.Run,
            new RunUpdate(RunStatus.ReadyToApply, RunStatus.Applying, _clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        try
        {
            var applied = await _writer.ApplyAsync(result.Plan, cancellationToken).ConfigureAwait(false);
            ValidateReceipt(result.Plan, applied);
            // Un commit exitoso no se convierte en Cancelled si la cancelación llega después.
            await _workspace.CompleteAsync(result, applied, _clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
            return applied;
        }
        catch (CaptionForgeOperationException ex) when (ex.Code is OperationErrorCode.CapCutOpen or OperationErrorCode.SourceChanged or OperationErrorCode.ResourceUnavailable)
        {
            // Estos diagnósticos son garantía contractual de rechazo ANTES de cualquier escritura.
            var next = ex.Code == OperationErrorCode.SourceChanged ? RunStatus.Failed : RunStatus.ReadyToApply;
            await RecordTerminalAsync(result.Run, RunStatus.Applying, next, ex).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // El escritor solo admite cancelación antes de commit; durante commit termina o revierte.
            await RecordTerminalAsync(result.Run, RunStatus.Applying, RunStatus.ReadyToApply, ex).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await RecordTerminalAsync(result.Run, RunStatus.Applying, RunStatus.RecoveryRequired, ex).ConfigureAwait(false);
            throw new CaptionForgeOperationException(OperationErrorCode.RecoveryRequired,
                "No se pudo confirmar toda la aplicación. Conserva los backups y revisa el journal antes de reintentar.", result.Run.RunId, ex);
        }
    }

    private static IReadOnlyList<MediaSegment> ValidateSelection(GenerateCaptionsRequest request)
    {
        var available = request.Snapshot.Tracks.SelectMany(t => t.Segments).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var selected = new List<MediaSegment>();
        foreach (var id in request.SelectedSegmentIds)
        {
            if (!available.TryGetValue(id, out var segment))
                throw new CaptionForgeOperationException(OperationErrorCode.InvalidSelection, $"El fragmento {id} no pertenece a la timeline seleccionada.");
            if (segment.Speed != 1.0 || segment.HasVariableSpeed || segment.IsReversed || segment.IsMuted)
                throw new CaptionForgeOperationException(OperationErrorCode.UnsupportedMedia,
                    $"El fragmento {id} está silenciado, invertido o usa una velocidad aún no admitida por el MVP.");
            if (segment.TargetRange.EndUs > request.Snapshot.Timeline.DurationUs)
                throw new CaptionForgeOperationException(OperationErrorCode.InvalidSelection, $"El fragmento {id} excede la duración de la timeline.");
            selected.Add(segment);
        }
        selected.Sort((a, b) => a.TargetRange.StartUs != b.TargetRange.StartUs
            ? a.TargetRange.StartUs.CompareTo(b.TargetRange.StartUs) : string.CompareOrdinal(a.Id, b.Id));
        for (int i = 1; i < selected.Count; i++)
            if (selected[i].TargetRange.StartUs < selected[i - 1].TargetRange.EndUs)
                throw new CaptionForgeOperationException(OperationErrorCode.OverlappingSegments,
                    "Los fragmentos seleccionados se solapan. El MVP necesita una selección sin solapamientos; todavía no mezcla pistas.");
        return selected.AsReadOnly();
    }

    private static void ValidatePlan(PreparedSubtitlePlan plan, RunContext run, TimelineSnapshot snapshot, int count)
    {
        if (plan.Run != run || plan.CaptionCount != count)
            throw InvalidResult("El plan no corresponde a la ejecución o a los captions generados.", run.RunId);
        foreach (var file in plan.ExpectedFiles)
        {
            var original = snapshot.SourceFiles.FirstOrDefault(f => string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            if (original is null || !SameStamp(original, file))
                throw InvalidResult("El plan incluye un archivo no capturado o una huella diferente.", run.RunId);
        }
        if (!plan.ExpectedFiles.Any(f => string.Equals(f.Path, snapshot.Timeline.DraftContentPath, StringComparison.OrdinalIgnoreCase)))
            throw InvalidResult("El plan no incluye el draft_content de la timeline seleccionada.", run.RunId);
        foreach (var content in plan.ExpectedFiles.Where(f => f.Path.EndsWith("draft_content.json", StringComparison.OrdinalIgnoreCase)))
            if (!plan.ExpectedFiles.Any(f => string.Equals(f.Path, content.Path + ".bak", StringComparison.OrdinalIgnoreCase)))
                throw InvalidResult("El plan debe sincronizar el .bak nativo de cada draft_content que cambie.", run.RunId);
    }

    private static void ValidateReceipt(PreparedSubtitlePlan plan, SubtitleApplyResult receipt)
    {
        if (receipt.RunId != plan.Run.RunId || receipt.Files.Count != plan.ExpectedFiles.Count)
            throw InvalidResult("El recibo no corresponde al plan aplicado.", plan.Run.RunId);
        foreach (var expected in plan.ExpectedFiles)
        {
            var applied = receipt.Files.FirstOrDefault(f => string.Equals(f.Before.Path, expected.Path, StringComparison.OrdinalIgnoreCase));
            if (applied is null || !SameStamp(expected, applied.Before))
                throw InvalidResult("El recibo no conserva las huellas anteriores del plan.", plan.Run.RunId);
        }
        foreach (var content in receipt.Files.Where(f => f.Before.Path.EndsWith("draft_content.json", StringComparison.OrdinalIgnoreCase)))
        {
            var nativeBackup = receipt.Files.First(f => string.Equals(f.Before.Path, content.Before.Path + ".bak", StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(content.Sha256After, nativeBackup.Sha256After, StringComparison.OrdinalIgnoreCase))
                throw InvalidResult("El .bak nativo no coincide con el draft_content nuevo.", plan.Run.RunId);
        }
    }

    private static bool SameStamp(SourceFileStamp a, SourceFileStamp b) =>
        a.Exists == b.Exists && string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase);

    private static CaptionForgeOperationException InvalidResult(string message, string? runId = null) =>
        new(OperationErrorCode.InvalidAdapterResult, message, runId);

    private async Task RecordTerminalAsync(RunContext run, RunStatus current, RunStatus terminal, Exception error)
    {
        try
        {
            await _workspace.TransitionAsync(run,
                new RunUpdate(current, terminal, _clock.GetUtcNow(), error.Message), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception persistenceError)
        {
            throw new CaptionForgeOperationException(OperationErrorCode.PersistenceFailure,
                "La operación falló y tampoco pudo persistirse su estado. Conserva los artefactos para recuperación.", run.RunId,
                new AggregateException(error, persistenceError));
        }
    }

    private static void Report(IProgress<GenerationProgress>? progress, RunContext run, RunStatus status,
        int completed, int total, string? segmentId = null)
    {
        // Un observador visual no puede romper el procesamiento ni transformar un resultado guardado en fallido.
        try { progress?.Report(new GenerationProgress(run.RunId, status, completed, total, segmentId)); }
        catch (Exception) { /* La notificación es auxiliar; no forma parte del commit. */ }
    }
}

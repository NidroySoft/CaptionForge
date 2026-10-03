using System.Text.Json;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Application.Services;
using CaptionForge.Core.Enums;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.ValueObjects;

using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;

namespace CaptionForge.Tests.Application;

public sealed class ApplicationRegressionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GoldenDtwProduceLosQuinceCaptionsExactos()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        string fixtures = FixtureFiles.Directory;
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "whisper_dtw.json")));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "expected_captions_v3.json")));
        var phrases = new List<TranscriptionSegment>();
        int index = 0;
        foreach (var entry in input.RootElement.GetProperty("transcription").EnumerateArray())
        {
            var offsets = entry.GetProperty("offsets");
            long from = offsets.GetProperty("from").GetInt64(), to = offsets.GetProperty("to").GetInt64();
            var tokens = new List<TranscriptionToken>();
            foreach (var token in entry.GetProperty("tokens").EnumerateArray())
            {
                string text = token.GetProperty("text").GetString()!;
                // Ajustes editoriales conocidos del fixture, exclusivos de esta verificación.
                if (index == 9 && text == " words") text = "words";
                if (index == 14 && text == ",") text = ".";
                if (index == 14 && text == " math") text = " Math";
                var o = token.GetProperty("offsets");
                long start = o.GetProperty("from").GetInt64(), end = o.GetProperty("to").GetInt64();
                tokens.Add(new TranscriptionToken(text, TimeRangeUs.FromMilliseconds(start, end - start), text.StartsWith("[_", StringComparison.Ordinal)));
            }
            phrases.Add(new TranscriptionSegment(entry.GetProperty("text").GetString()!, TimeRangeUs.FromMilliseconds(from, to - from), tokens));
            index++;
        }
        var referenceSegment = new MediaSegment("reference", "audio", "media", "C:/source.mp3", new(0, 36_533_333), new(0, 36_533_333));
        var referenceResult = new TranscriptionResult(referenceSegment.Id, "medium.en", "en", 36_533_333, phrases);
        var referenceCues = planner.Build(referenceSegment, referenceResult, new FrameRate(30));
        tests.Check(referenceCues.Count == 15, "DTW: 15 captions mediante Application");
        for (int i = 0; i < referenceCues.Count; i++)
        {
            var cue = referenceCues[i]; var e = expected.RootElement[i]; var r = e.GetProperty("range");
            tests.Check(cue.TimelineRange == new TimeRangeUs(r.GetProperty("start").GetInt64(), r.GetProperty("duration").GetInt64()), $"Golden {i + 1}: rango µs exacto");
            tests.Check(cue.Text == e.GetProperty("text").GetString(), $"Golden {i + 1}: texto");
            var w = e.GetProperty("words");
            tests.Check(cue.Words.Select(x => x.Text).SequenceEqual(w.GetProperty("text").EnumerateArray().Select(x => x.GetString())), $"Golden {i + 1}: palabras/espacios");
            tests.Check(cue.Words.Select(x => x.StartTimeMs).SequenceEqual(w.GetProperty("start_time").EnumerateArray().Select(x => x.GetInt64())), $"Golden {i + 1}: inicios ms");
            tests.Check(cue.Words.Select(x => x.EndTimeMs).SequenceEqual(w.GetProperty("end_time").EnumerateArray().Select(x => x.GetInt64())), $"Golden {i + 1}: finales ms");
        }
        
    }

    [Fact]
    public async Task FlujoCompletoDeEstadosYAplicacionExplicita()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var fixture = new Fixture();
        var request = fixture.Request();
        var observed = new List<GenerationProgress>();
        var generated = await fixture.Service.GenerateAsync(request, new InlineProgress(observed.Add));
        tests.Check(fixture.Store.Status == RunStatus.ReadyToApply && fixture.Writer.ApplyCalls == 0, "Generar no aplica CapCut");
        tests.Check(generated.Captions.Count == 2 && generated.Transcriptions.Count == 2, "Una transcripción/caption por fragmento seleccionado");
        tests.Check(fixture.Events.SequenceEqual(new[] { "create", "read-managed", "PreparingAudio", "audio:a", "Transcribing", "transcribe:a", "PreparingAudio", "audio:b", "Transcribing", "transcribe:b", "PreparingSubtitles", "prepare-plan", "save-generation", "ReadyToApply" }), "Orden secuencial completo de puertos/estados");
        tests.Check(fixture.Audio.Requests[0].Segment.SourceRange.StartUs == 10_000_000 && fixture.Audio.Requests[0].Segment.TargetRange.StartUs == 20_000_000, "Recorte origen 10s independiente de destino 20s");
        tests.Check(generated.Captions[0].TimelineRange.StartUs == 20_000_000 && generated.Captions[1].TimelineRange.StartUs == 30_000_000, "Hueco 20s/30s preservado");
        tests.Check(observed.First().CompletedSegments == 0 && observed.Last().CompletedSegments == 2 && observed.Last().Stage == RunStatus.ReadyToApply, "Progreso por etapa y fragmentos");
        tests.Check(fixture.Writer.LastRequest!.TemplateResourceId == "7535399757947161873" && fixture.Writer.LastRequest.FontResourceId == "7517426090072149264", "Golden v3 definitivo, no plantilla manual reciente");
        tests.Check(fixture.Writer.LastRequest.PreserveUnmanagedSubtitles, "Política MVP conserva textos ajenos");
        tests.Check(request.Options.RequireDtwAlignment, "Contrato exige DTW");
        var applied = await fixture.Service.ApplyAsync(generated);
        tests.Check(fixture.Store.Status == RunStatus.Completed && fixture.Writer.ApplyCalls == 1 && applied.Files.Count == 2, "Aplicación explícita y recibo de content + bak");
        tests.Check(fixture.Store.Managed == generated.Plan.ManagedSubtitles, "Registro propio solo cambia tras aplicar");
        await tests.ErrorAsync(() => fixture.Service.ApplyAsync(generated), OperationErrorCode.RunStateConflict, "No aplica dos veces la misma ejecución");
        tests.Check(fixture.Writer.ApplyCalls == 1 && fixture.Store.Status == RunStatus.Completed, "Conflicto no cambia estado ni repite escritura");
        
    }

    [Fact]
    public async Task ExclusionRelocalizacionYRegistroPrevio()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var excluded = new Fixture();
        var onlyB = await excluded.Service.GenerateAsync(excluded.Request(new[] { "b" }));
        tests.Check(excluded.Audio.Requests.Count == 1 && onlyB.Captions[0].TimelineRange.StartUs == 30_000_000, "Excluir a no desplaza b");
        var relocated = new Fixture();
        await relocated.Service.GenerateAsync(relocated.Request(overrides: new[] { new SourcePathOverride("a", "D:/relocated.mp3") }));
        tests.Check(relocated.Audio.Requests[0].ResolvedSourcePath == "D:/relocated.mp3" && relocated.Snapshot.Tracks[0].Segments[0].SourcePath == "C:/source-a.mp3", "Localizar medio no cambia ruta CapCut");
        var update = new Fixture();
        var prior = new ManagedSubtitleSet("project", "timeline", new[] { new ManagedSubtitleObject(SubtitleObjectKind.Track, "prior-track", "captions") });
        update.Store.Managed = prior;
        await update.Service.GenerateAsync(update.Request());
        tests.Check(update.Writer.LastRequest!.PreviouslyManaged == prior, "IDs previos entregados al escritor, sin deducir autoría");
        tests.Check(update.Store.Managed == prior, "Preparar no cambia registro previo");
        
    }

    [Fact]
    public async Task ModosNoSoportadosSeRechazanAntesDelTrabajo()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        foreach (var (segment, error, name) in new[] {
            (Fixture.Segment("a", speed: 2), OperationErrorCode.UnsupportedMedia, "Velocidad constante pendiente"),
            (Fixture.Segment("a", variable: true), OperationErrorCode.UnsupportedMedia, "Curva de velocidad pendiente"),
            (Fixture.Segment("a", reversed: true), OperationErrorCode.UnsupportedMedia, "Reversa pendiente"),
            (Fixture.Segment("a", muted: true), OperationErrorCode.UnsupportedMedia, "Fragmento silenciado"),
            (Fixture.Segment("b", targetStart: 20_500_000), OperationErrorCode.OverlappingSegments, "Solapamiento sin mezcla"),
            (Fixture.Segment("a", targetStart: 40_000_000), OperationErrorCode.InvalidSelection, "Fragmento fuera de timeline") })
        {
            var invalid = new Fixture(segment);
            await tests.ErrorAsync(() => invalid.Service.GenerateAsync(invalid.Request()), error, name);
            tests.Check(invalid.Store.CreateCalls == 0 && invalid.Audio.Requests.Count == 0, name + ": rechazo antes de trabajo");
        }
    }

    [Fact]
    public async Task SeleccionYCancelacionInicialDuranteAudioYWhisper()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var unknown = new Fixture();
        await tests.ErrorAsync(() => unknown.Service.GenerateAsync(unknown.Request(new[] { "unknown" })), OperationErrorCode.InvalidSelection, "ID seleccionado ajeno");
        tests.Check(unknown.Store.CreateCalls == 0, "Selección ajena no crea ejecución");
        var preCancelled = new Fixture(); using var ct0 = new CancellationTokenSource(); ct0.Cancel();
        await tests.ThrowsAsync<OperationCanceledException>(() => preCancelled.Service.GenerateAsync(preCancelled.Request(), cancellationToken: ct0.Token), "Cancelación antes de empezar");
        tests.Check(preCancelled.Store.CreateCalls == 0, "Cancelación inicial no crea run");
        var duringAudio = new Fixture(); using var ct1 = new CancellationTokenSource();
        duringAudio.Audio.Before = () => { ct1.Cancel(); throw new OperationCanceledException(ct1.Token); };
        await tests.ThrowsAsync<OperationCanceledException>(() => duringAudio.Service.GenerateAsync(duringAudio.Request(), cancellationToken: ct1.Token), "Cancelación preparando audio");
        tests.Check(duringAudio.Store.Status == RunStatus.Cancelled && duringAudio.Writer.ApplyCalls == 0, "Cancelación se persiste sin escribir CapCut");
        var duringTranscription = new Fixture(); using var ct2 = new CancellationTokenSource();
        duringTranscription.Transcriber.Before = () => { ct2.Cancel(); throw new OperationCanceledException(ct2.Token); };
        await tests.ThrowsAsync<OperationCanceledException>(() => duringTranscription.Service.GenerateAsync(duringTranscription.Request(), cancellationToken: ct2.Token), "Cancelación transcribiendo");
        tests.Check(duringTranscription.Store.Status == RunStatus.Cancelled && duringTranscription.Audio.Requests.Count == 1, "Cancelación no prepara segundo fragmento");
    }

    [Fact]
    public async Task AdaptadoresIncoherentesNoProducenPlan()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var invalidAudio = new Fixture(); invalidAudio.Audio.WrongIdentity = true;
        await tests.ErrorAsync(() => invalidAudio.Service.GenerateAsync(invalidAudio.Request()), OperationErrorCode.InvalidAdapterResult, "WAV de otro fragmento");
        tests.Check(invalidAudio.Store.Status == RunStatus.Failed && invalidAudio.Transcriber.Calls == 0, "WAV incorrecto no se transcribe");
        var invalidDuration = new Fixture(); invalidDuration.Audio.WrongDuration = true;
        await tests.ErrorAsync(() => invalidDuration.Service.GenerateAsync(invalidDuration.Request()), OperationErrorCode.InvalidAdapterResult, "WAV con duración lógica incorrecta");
        foreach (string mismatch in new[] { "id", "duration", "model", "language" })
        {
            var bad = new Fixture(); bad.Transcriber.Mismatch = mismatch;
            await tests.ErrorAsync(() => bad.Service.GenerateAsync(bad.Request()), OperationErrorCode.InvalidAdapterResult, "Transcripción incoherente: " + mismatch);
            tests.Check(bad.Store.Status == RunStatus.Failed && bad.Writer.LastRequest is null, "No prepara JSON ante " + mismatch);
        }
    }

    [Fact]
    public async Task AutodeteccionSilencioYPalabrasInvalidas()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var autodetect = new Fixture();
        await autodetect.Service.GenerateAsync(autodetect.Request(options: new TranscriptionOptions("test", "C:/model.bin", "auto", 6, 8)));
        tests.Check(autodetect.Store.Saved!.Transcriptions.All(t => t.Language == "en"), "Idioma automático conserva idioma efectivo");
        var noWords = new Fixture(); noWords.Transcriber.ControlOnly = true;
        await tests.ErrorAsync(() => noWords.Service.GenerateAsync(noWords.Request()), OperationErrorCode.NoCaptions, "Sin palabras reconocidas no aplica plan vacío");
        tests.Check(noWords.Store.Status == RunStatus.Failed && noWords.Writer.LastRequest is null, "Silencio/control no toca CapCut");
        var invalidWords = new Fixture(); invalidWords.Transcriber.InvalidWords = true;
        var wordError = await tests.ErrorAsync(() => invalidWords.Service.GenerateAsync(invalidWords.Request()), OperationErrorCode.InvalidWordTiming, "No fabrica tiempos para palabras solapadas");
        tests.Check(wordError.RunId == "run", "Diagnóstico del planner conserva runId para encontrar artefactos");
        
    }

    [Fact]
    public async Task PlanesInvalidosYFallosDePersistencia()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        foreach (string flaw in new[] { "count", "hash", "path", "no-draft", "no-bak" })
        {
            var bad = new Fixture(); bad.Writer.PlanFlaw = flaw;
            await tests.ErrorAsync(() => bad.Service.GenerateAsync(bad.Request()), OperationErrorCode.InvalidAdapterResult, "Plan rechazado: " + flaw);
            tests.Check(bad.Store.Status == RunStatus.Failed && bad.Writer.ApplyCalls == 0, "Plan " + flaw + " no aplica");
        }
        var failedSave = new Fixture(); failedSave.Store.FailSave = true;
        await tests.ThrowsAsync<IOException>(() => failedSave.Service.GenerateAsync(failedSave.Request()), "Fallo al guardar resultado");
        tests.Check(failedSave.Store.Status == RunStatus.Failed, "Resultado no guardado no llega a Ready");
        var failedRecovery = new Fixture(); failedRecovery.Audio.Before = () => throw new IOException("audio failure"); failedRecovery.Store.FailTerminal = true;
        var doubleFailure = await tests.ErrorAsync(() => failedRecovery.Service.GenerateAsync(failedRecovery.Request()), OperationErrorCode.PersistenceFailure, "Error operativo y error de persistencia");
        tests.Check(doubleFailure.InnerException is AggregateException a && a.InnerExceptions.Count == 2, "Conserva ambas causas");
        var brokenObserver = new Fixture();
        var observerResult = await brokenObserver.Service.GenerateAsync(brokenObserver.Request(), new InlineProgress(_ => throw new InvalidOperationException("UI error")));
        tests.Check(observerResult.Captions.Count == 2 && brokenObserver.Store.Status == RunStatus.ReadyToApply, "Observador visual no rompe generación");
        
    }

    [Fact]
    public async Task RechazosPreviosAlCommitMantienenEstadoCorrecto()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        foreach (var diagnostic in new[] { OperationErrorCode.CapCutOpen, OperationErrorCode.SourceChanged, OperationErrorCode.ResourceUnavailable })
        {
            var reject = new Fixture(); var ready = await reject.Service.GenerateAsync(reject.Request());
            reject.Writer.ApplyFailure = new CaptionForgeOperationException(diagnostic, "precommit rejection");
            await tests.ErrorAsync(() => reject.Service.ApplyAsync(ready), diagnostic, "Diagnóstico precommit: " + diagnostic);
            tests.Check(reject.Store.Status == (diagnostic == OperationErrorCode.SourceChanged ? RunStatus.Failed : RunStatus.ReadyToApply), "Estado tras precommit " + diagnostic);
        }
    }

    [Fact]
    public async Task CommitInciertoNoSeReintenta()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var uncertain = new Fixture(); var uncertainReady = await uncertain.Service.GenerateAsync(uncertain.Request());
        uncertain.Writer.ApplyFailure = new IOException("uncertain commit");
        await tests.ErrorAsync(() => uncertain.Service.ApplyAsync(uncertainReady), OperationErrorCode.RecoveryRequired, "Commit incierto");
        tests.Check(uncertain.Store.Status == RunStatus.RecoveryRequired, "Commit incierto queda recuperable");
        await tests.ErrorAsync(() => uncertain.Service.ApplyAsync(uncertainReady), OperationErrorCode.RunStateConflict, "No reintenta commit incierto ciegamente");
        
    }

    [Fact]
    public async Task RecibosInvalidosYFalloDeFinalizacionExigenRecuperacion()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        foreach (string flaw in new[] { "run", "hash", "count", "stale-bak" })
        {
            var invalidReceipt = new Fixture(); var r = await invalidReceipt.Service.GenerateAsync(invalidReceipt.Request()); invalidReceipt.Writer.ReceiptFlaw = flaw;
            await tests.ErrorAsync(() => invalidReceipt.Service.ApplyAsync(r), OperationErrorCode.RecoveryRequired, "Recibo incoherente: " + flaw);
            tests.Check(invalidReceipt.Store.Status == RunStatus.RecoveryRequired, "Recibo " + flaw + " exige recuperación");
        }
        var failedComplete = new Fixture(); var completeReady = await failedComplete.Service.GenerateAsync(failedComplete.Request()); failedComplete.Store.FailComplete = true;
        await tests.ErrorAsync(() => failedComplete.Service.ApplyAsync(completeReady), OperationErrorCode.RecoveryRequired, "Commit exitoso, manifiesto final falla");
        tests.Check(failedComplete.Store.Status == RunStatus.RecoveryRequired && failedComplete.Writer.ApplyCalls == 1, "No repite escritura tras fallo de manifiesto");
    }

    [Fact]
    public async Task CancelacionAntesYDespuesDelCommit()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var applyCancel = new Fixture(); var cancelReady = await applyCancel.Service.GenerateAsync(applyCancel.Request()); using var ct3 = new CancellationTokenSource();
        applyCancel.Writer.BeforeApply = () => { ct3.Cancel(); throw new OperationCanceledException(ct3.Token); };
        await tests.ThrowsAsync<OperationCanceledException>(() => applyCancel.Service.ApplyAsync(cancelReady, ct3.Token), "Cancelación precommit en escritor");
        tests.Check(applyCancel.Store.Status == RunStatus.ReadyToApply, "Cancelación precommit permite aplicar resultado luego");
        var lateCancel = new Fixture(); var lateReady = await lateCancel.Service.GenerateAsync(lateCancel.Request()); using var ct4 = new CancellationTokenSource(); lateCancel.Writer.AfterCommit = ct4.Cancel;
        await lateCancel.Service.ApplyAsync(lateReady, ct4.Token);
        tests.Check(lateCancel.Store.Status == RunStatus.Completed && lateCancel.Store.CompletionToken == CancellationToken.None, "Cancelación tras commit no pierde estado Completed");
        
    }

    [Fact]
    public async Task DosAplicacionesConcurrentesSoloEscribenUnaVez()
    {
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        // Dos llamadas realmente concurrentes: la segunda no entra en el escritor.
        var concurrent = new Fixture(); var concurrentReady = await concurrent.Service.GenerateAsync(concurrent.Request());
        concurrent.Writer.Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        concurrent.Writer.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = concurrent.Service.ApplyAsync(concurrentReady);
        await concurrent.Writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await tests.ErrorAsync(() => concurrent.Service.ApplyAsync(concurrentReady), OperationErrorCode.RunStateConflict, "Aplicaciones concurrentes: solo una gana");
        tests.Check(concurrent.Store.Status == RunStatus.Applying && concurrent.Writer.ApplyCalls == 1, "Conflicto concurrente no pisa Applying");
        concurrent.Writer.Release.SetResult(); await first;
        tests.Check(concurrent.Store.Status == RunStatus.Completed, "Ganador concurrente completa normalmente");
        
    }

    [Fact]
    public async Task PlannerLimitaFrasesYRespetaFpsRacional()
    {
        var fixture = new Fixture();
        var generated = await fixture.Service.GenerateAsync(fixture.Request());
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        var subframe = new TranscriptionResult("a", "test", "en", 1_000_000, new[] { new TranscriptionSegment(" tiny", new(0, 1000), new[] { Fixture.Token(" tiny", 0, 1000) }) });
        tests.Check(planner.Build(Fixture.Segment("a"), subframe, new FrameRate(30)).Count == 0, "Frase menor que un frame se omite");
        var clip = new TranscriptionResult("a", "test", "en", 1_000_000, new[] { new TranscriptionSegment(" tail", new(900_000, 500_000), new[] { Fixture.Token(" tail", 900_000, 1_400_000) }) });
        var clipped = planner.Build(Fixture.Segment("a"), clip, new FrameRate(30));
        tests.Check(clipped[0].TimelineRange.EndUs == 21_000_000 && clipped[0].Words[^1].EndTimeMs == 100, "Frase final limitada al fragmento y última palabra reajustada");
        tests.Throws<CaptionForgeOperationException>(() => planner.Build(Fixture.Segment("b"), clip, new FrameRate(30)), "Planner rechaza otra identidad");
        var overlappingPhrases = new TranscriptionResult("a", "test", "en", 1_000_000, new[] { Fixture.Phrase(0, 700_000), Fixture.Phrase(500_000, 900_000) });
        tests.Throws<CaptionForgeOperationException>(() => planner.Build(Fixture.Segment("a"), overlappingPhrases, new FrameRate(30)), "Planner rechaza frases solapadas");
        var rational = planner.Build(Fixture.Segment("a"), Fixture.Transcription(Fixture.Segment("a")), new FrameRate(30000, 1001));
        tests.Check(rational[0].TimelineRange.DurationUs == 967_633, "Application respeta FPS 30000/1001");
        var overflowPhrase = new TranscriptionResult("a", "test", "en", 1_000_000, new[] { new TranscriptionSegment(" bad", new(long.MaxValue - 1, 1), new[] { Fixture.Token(" bad", 0, 1) }) });
        tests.Throws<CaptionForgeOperationException>(() => planner.Build(Fixture.Segment("a"), overflowPhrase, new FrameRate(int.MaxValue)), "Overflow temporal del adaptador produce diagnóstico");
    }

    [Fact]
    public async Task SrtE_HilosDeCpuRespetanLimites()
    {
        var fixture = new Fixture();
        var generated = await fixture.Service.GenerateAsync(fixture.Request());
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        tests.Check(SrtFormatter.Format(Array.Empty<SubtitleCue>()) == "", "SRT vacío válido");
        var srt = SrtFormatter.Format(generated.Captions.Reverse());
        tests.Check(srt.StartsWith("1\r\n00:00:20,000 --> 00:00:21,000\r\nHello\r\n\r\n2\r\n00:00:30,000", StringComparison.Ordinal), "SRT ordenado, CRLF y tiempo global");
        tests.Check(SrtFormatter.Format(new[] { new SubtitleCue("a", new(360_000_000_000, 1_000_000), new[] { new TimedWord("Hello", 0, 1000) }) }).Contains("100:00:00,000"), "SRT más de 99 horas sin envolver");
        tests.Check(CpuThreadRecommendation.Suggest(1) == 1 && CpuThreadRecommendation.Suggest(2) == 1 && CpuThreadRecommendation.Suggest(8) == 6 && CpuThreadRecommendation.Suggest(16) == 12, "Sugerencia 75% de hilos");
        tests.Check(CpuThreadRecommendation.Suggest(int.MaxValue) == 1_610_612_735, "Cálculo de hilos sin overflow");
        tests.Check(CpuThreadRecommendation.ResolveSaved(64, 8) == 8 && CpuThreadRecommendation.ResolveSaved(4, 8) == 4 && CpuThreadRecommendation.ResolveSaved(null, 8) == 6, "Preferencia de hilos revalidada por equipo");
        tests.Throws<ArgumentOutOfRangeException>(() => CpuThreadRecommendation.Suggest(0), "CPU sin hilos inválida");
    }

    [Fact]
    public async Task ContratosYResultadosInmutables()
    {
        var fixture = new Fixture();
        var generated = await fixture.Service.GenerateAsync(fixture.Request());
        var tests = new Checks(output);
        var planner = new SubtitleCuePlanner();
        tests.Throws<ArgumentOutOfRangeException>(() => new TranscriptionOptions("m", "p", "en", 9, 8), "Hilos por encima del hardware");
        tests.Throws<ArgumentException>(() => new SourceFileStamp("p", true, "bad"), "Hash original obligatorio y válido");
        tests.Throws<ArgumentException>(() => new SourceFileStamp("p", false, new string('a', 64)), "Archivo ausente no lleva hash");
        tests.Throws<ArgumentException>(() => new PreparedAudio("a", "p", 1_000_000, 1_000_064), "Duración medida fuera de una muestra");
        tests.Check(new PreparedAudio("a", "p", 1_000_000, 1_000_063).MeasuredDurationUs == 1_000_063, "Tolerancia máxima de una muestra");
        tests.Throws<ArgumentException>(() => new AppliedFile(new SourceFileStamp("p", true, new string('a',64)), new string('b',64)), "Archivo anterior necesita backup propio");
        tests.Throws<ArgumentException>(() => new AppliedFile(new SourceFileStamp("p", false), new string('b',64), "backup"), "Archivo nuevo no inventa backup");
        tests.Throws<ArgumentException>(() => fixture.Request(new[] { "a", "a" }), "IDs seleccionados duplicados");
        tests.Throws<ArgumentException>(() => fixture.Request(new[] { "a" }, new[] { new SourcePathOverride("b", "p") }), "Override ajeno a selección");
        tests.Throws<ArgumentException>(() => new ManagedSubtitleSet("p", "t", new[] { new ManagedSubtitleObject(SubtitleObjectKind.Text, "id", "one"), new ManagedSubtitleObject(SubtitleObjectKind.Template, "id", "two") }), "Identidades administradas duplicadas");
        tests.Throws<ArgumentException>(() => new ManagedSubtitleSet("p", "t", new[] { new ManagedSubtitleObject(SubtitleObjectKind.Text, "id1", "same"), new ManagedSubtitleObject(SubtitleObjectKind.Text, "id2", "same") }), "Claves propias duplicadas");
        tests.Throws<ArgumentException>(() => new TimelineSnapshot(new CapCutProject("foreign", "Foreign", "C:/f"), fixture.Snapshot.Timeline, fixture.Snapshot.Tracks, fixture.Snapshot.SourceFiles), "Captura de proyecto ajeno");
        tests.Throws<ArgumentException>(() => new TimelineSnapshot(fixture.Snapshot.Project, fixture.Snapshot.Timeline, fixture.Snapshot.Tracks, fixture.Snapshot.SourceFiles.Take(1)), "Captura requiere bak aunque sea ausente");
        tests.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(cpuThreads: 0), "Ajustes no admiten cero hilos");
        var selectionArray = new[] { "a" }; var immutableRequest = fixture.Request(selectionArray); selectionArray[0] = "b";
        tests.Check(immutableRequest.SelectedSegmentIds[0] == "a", "Selección copia defensivamente array");
        tests.Throws<NotSupportedException>(() => ((IList<string>)immutableRequest.SelectedSegmentIds)[0] = "b", "Selección de solo lectura");
        tests.Throws<NotSupportedException>(() => ((IList<SubtitleCue>)generated.Captions).Clear(), "Resultado de solo lectura");
        
    }

}

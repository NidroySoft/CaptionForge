using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Services;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Tests.Support;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

[Collection("External engines")]
public sealed class WhisperNativeTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [WhisperFact]
    public async Task VozRealConDtwGeneraJsonV3YAutoEnReutilizaLaFabrica()
    {
        var audioService = new FfmpegAudioPreparationService();
        var nativeSession = await SessionAsync(); var draft = Read(nativeSession.Draft); var seg = draft["tracks"]![1]!["segments"]![0]!;
        seg["source_timerange"] = new JsonObject { ["start"] = 0, ["duration"] = 11_000_000 }; seg["target_timerange"] = new JsonObject { ["start"] = 20_000_000, ["duration"] = 11_000_000 };
        Write(nativeSession.Draft, draft); Write(nativeSession.RootDraft, draft); nativeSession.Snapshot = await nativeSession.Catalog.ReadTimelineAsync(nativeSession.Project, nativeSession.Snapshot.Timeline);
        await using var whisper = new WhisperNetTranscriptionService(); var service = new CaptionGenerationService(audioService, whisper, nativeSession.Writer, nativeSession.Store);
        string sourceId = nativeSession.Snapshot.Tracks[1].Segments[0].Id; var options = new TranscriptionOptions("native-check", Path.GetFullPath(EngineConfiguration.ModelPath!), "en", Math.Min(2, Environment.ProcessorCount), Environment.ProcessorCount);
        var req = new GenerateCaptionsRequest(nativeSession.Snapshot, new[] { sourceId }, options, nativeSession.Store.RootDirectory, new[] { new SourcePathOverride(sourceId, Path.GetFullPath(EngineConfiguration.VoicePath!)) });
        var watch = Stopwatch.StartNew();
        GenerationResult nativeResult;

        try
        {
            nativeResult = await service.GenerateAsync(req);
        }
        catch (Exception ex)
        {
            var diagnostics = Path.Combine(
                Path.GetTempPath(), "CaptionForge_Whisper_Diagnostico",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(diagnostics);

            foreach (var directory in Directory.GetDirectories(
                nativeSession.Store.RootDirectory,
                "transcription",
                SearchOption.AllDirectories))
            {
                foreach (var file in Directory.GetFiles(directory, "*.json"))
                {
                    File.Copy(
                        file,
                        Path.Combine(diagnostics, Path.GetFileName(file)));
                }
            }

            throw new InvalidOperationException(
                $"Transcripción fallida. JSON guardados en: {diagnostics}",
                ex);
        }
        finally
        {
            watch.Stop();
        }
        tests.Check(nativeResult.Captions.Count > 0 && nativeResult.Transcriptions.Single().Segments.Count > 0, "Whisper.net CPU+DTW real reconoce voz sin CLI");
        tests.Check(nativeResult.Captions.All(c => c.TimelineRange.StartUs >= 20_000_000 && c.TimelineRange.EndUs <= 31_000_000), "Pipeline nativo traslada captions al destino 20s y los limita");
        var nativeReceipt = await service.ApplyAsync(nativeResult); tests.Check(nativeReceipt.Files.Count == 4, "Pipeline completo nativo aplica JSON v3 a copias");
        string nativeWave = Directory.GetFiles(Path.Combine(nativeResult.Run.RunDirectory, "audio"), "*.wav").Single();
        var automatic = await whisper.TranscribeAsync(nativeResult.Run, new PreparedAudio(sourceId, nativeWave, 11_000_000, PcmWaveInfo.Read(nativeWave).DurationUs), new TranscriptionOptions("native-check", Path.GetFullPath(EngineConfiguration.ModelPath!), "auto", CpuThreadRecommendation.Suggest(Environment.ProcessorCount), Environment.ProcessorCount));
        tests.Check(automatic.Language == "en" && automatic.Segments.Count > 0, "Whisper.net real usa idioma inglés del modelo .en con auto y reutiliza la fábrica");

    }

}

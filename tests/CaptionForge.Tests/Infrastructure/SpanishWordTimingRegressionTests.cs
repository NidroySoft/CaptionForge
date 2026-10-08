using System.Text.Json;
using System.Text.Json.Nodes;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Infrastructure.CapCut;
using CaptionForge.Tests.Support;
using Whisper.net;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class SpanishWordTimingRegressionTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Fact]
    public void DiagnosticoEspanolProduceTodasLasFrasesSinPerderPalabras()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureFiles.Directory,
            "whisper_spanish_collapsed_native.json")));
        var phrases = doc.RootElement.GetProperty("segments").EnumerateArray()
            .Select(ReadNative).Select(s => WhisperTokenNormalizer.Normalize(s, false)).ToArray();
        var source = new MediaSegment("spanish", "audio", "voice", "voice.wav",
            new(0, 28_966_666), new(20_000_000, 28_966_666));
        var result = new TranscriptionResult(source.Id, "medium", "es", source.TargetRange.DurationUs, phrases);
        var captions = new SubtitleCuePlanner().Build(source, result, new FrameRate(30));
        Assert.Equal(12, captions.Count);
        Assert.Equal(phrases.Select(p => p.Text.Trim()), captions.Select(c => c.Text));
        Assert.All(captions, c =>
        {
            Assert.InRange(c.TimelineRange.StartUs, source.TargetRange.StartUs, source.TargetRange.EndUs);
            Assert.InRange(c.TimelineRange.EndUs, source.TargetRange.StartUs, source.TargetRange.EndUs);
            Assert.All(c.Words.Where(w => !w.IsSpace), w => Assert.True(w.EndTimeMs > w.StartTimeMs));
        });
    }

    [Fact]
    public void SoloRecuperaTresPalabrasCompletasYConservaLosDemasOffsetsNativos()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixturePath,
            "whisper_spanish_collapsed_native.json")));
        var recoveries = new List<WhisperWordTimingRecovery>();
        foreach (var s in doc.RootElement.GetProperty("segments").EnumerateArray())
        {
            var native = ReadNative(s);
            var normalized = WhisperTokenNormalizer.Normalize(native, false, out var restored);
            recoveries.AddRange(restored);
            for (int i = 0; i < native.Tokens.Length; i++)
            {
                var original = native.Tokens[i];
                var token = normalized.Tokens[i];
                Assert.Equal(original.Text, token.Text);
                if (token.IsControl) continue;
                if (restored.Any(r => r.OriginalRange.StartUs == original.Start * 10_000 &&
                    token.Range == r.RecoveredRange)) continue;
                Assert.Equal(original.Start * 10_000, token.Range.StartUs);
                Assert.Equal(original.End * 10_000, token.Range.EndUs);
            }
        }
        Assert.Equal(new[] { "de", "de", "línea" }, recoveries.Select(r => r.Text));
        Assert.Equal(new[] { new TimeRangeUs(9_010_000, 110_000),
            new TimeRangeUs(14_570_000, 30_000), new TimeRangeUs(24_020_000, 50_000) },
            recoveries.Select(r => r.RecoveredRange));
        Assert.All(recoveries, r => Assert.True(r.OriginalRange.IsEmpty));
    }

    [Fact]
    public void ContinuacionSinDuracionNoModificaUnaPalabraQueYaTieneIntervalo()
    {
        var native = Phrase(Token(" eleg", 10, 30, 20), Token("iste", 50, 50, 45),
            Token(" en", 60, 90, 80));
        var normalized = WhisperTokenNormalizer.Normalize(native, false, out var repairs);
        Assert.Empty(repairs);
        Assert.Equal(new TimeRangeUs(500_000, 0), normalized.Tokens[1].Range);
        var words = CaptionForge.Core.Rules.WordTimingBuilder.Build(normalized.Tokens, normalized.Range);
        Assert.Equal("elegiste", words[0].Text);
        Assert.Equal(500, words[0].EndTimeMs);
    }

    [Theory]
    [InlineData(40, 30, 40)] // DTW antes del punto nativo.
    [InlineData(40, 55, 55)] // DTW después del punto nativo.
    [InlineData(40, 70, 60)] // El siguiente inicio limita la recuperación.
    public void RecuperacionAcotadaNoInvadePalabrasVecinas(long point, long anchor, long expectedEnd)
    {
        var native = Phrase(Token(" uno", 0, 20, 10), Token(" dos", point, point, anchor),
            Token(" tres", 60, 90, 80));
        var normalized = WhisperTokenNormalizer.Normalize(native, false, out var repairs);
        var repair = Assert.Single(repairs);
        Assert.Equal(expectedEnd * 10_000, repair.RecoveredRange.EndUs);
        Assert.InRange(repair.RecoveredRange.StartUs, 200_000, 600_000);
        Assert.InRange(repair.RecoveredRange.EndUs, 200_000, 600_000);
        Assert.Equal(native.Tokens[0].End * 10_000, normalized.Tokens[0].Range.EndUs);
        Assert.Equal(native.Tokens[2].Start * 10_000, normalized.Tokens[2].Range.StartUs);
        Assert.Equal("uno dos tres", string.Concat(CaptionForge.Core.Rules.WordTimingBuilder
            .Build(normalized.Tokens, normalized.Range).Select(w => w.Text)));
    }

    [Theory]
    [InlineData(-1)] // DTW ausente.
    [InlineData(40)] // El punto DTW también está colapsado.
    [InlineData(101)] // DTW fuera de la frase.
    public void AlineacionInsuficienteSigueFallandoConPalabraYPosicion(long anchor)
    {
        var error = Assert.Throws<InvalidDataException>(() => WhisperTokenNormalizer.Normalize(
            Phrase(Token(" uno", 0, 20, 10), Token(" dos", 40, 40, anchor),
                Token(" tres", 60, 90, 80)), false));
        Assert.Contains("«dos»", error.Message);
        Assert.Contains("400 ms", error.Message);
    }

    [Fact]
    public void NoCorrigeUnSolapamientoRealParaOcultarElProblema()
    {
        var native = Phrase(Token(" uno", 0, 60, 30), Token(" dos", 50, 80, 70));
        var normalized = WhisperTokenNormalizer.Normalize(native, false, out var repairs);
        Assert.Empty(repairs);
        Assert.Throws<InvalidOperationException>(() => CaptionForge.Core.Rules.WordTimingBuilder
            .Build(normalized.Tokens, normalized.Range));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PalabraColapsadaEnLimiteExactoUsaDtwDentroDeLaFrase(bool first)
    {
        var native = first ? Phrase(Token(" uno", 0, 0, 10), Token(" dos", 20, 80, 50))
            : Phrase(Token(" uno", 0, 80, 50), Token(" dos", 100, 100, 90));
        var normalized = WhisperTokenNormalizer.Normalize(native, false, out var repairs);
        Assert.Single(repairs);
        var words = CaptionForge.Core.Rules.WordTimingBuilder.Build(normalized.Tokens, normalized.Range);
        Assert.Equal("uno dos", string.Concat(words.Select(w => w.Text)));
    }

    [Fact]
    public void DiagnosticoInglesAnteriorConservaTodosLosOffsetsYNoNecesitaRecuperaciones()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixturePath,
            "3219BA9AFC48C4252E875CA8-native.json")));
        foreach (var s in doc.RootElement.GetProperty("segments").EnumerateArray())
        {
            var native = ReadNative(s);
            var normalized = WhisperTokenNormalizer.Normalize(native, true, out var repairs);
            Assert.Empty(repairs);
            for (int i = 0; i < native.Tokens.Length; i++)
            {
                if (normalized.Tokens[i].IsControl) continue;
                Assert.Equal(native.Tokens[i].Start * 10_000, normalized.Tokens[i].Range.StartUs);
                Assert.Equal(native.Tokens[i].End * 10_000, normalized.Tokens[i].Range.EndUs);
            }
        }
    }

    [Fact]
    public async Task EspanolSeAplicaPorBloquesEnCopiasConBakYRestauraSinCambiarOtrasPistas()
    {
        var s = await SessionAsync("snapshot_multiple_templates.json");
        var before = Read(s.Draft);
        byte[] original = File.ReadAllBytes(s.Draft);
        var candidate = (await DraftTemplateCatalog.ReadAsync(s.Snapshot))[1];
        var writer = new DraftTemplateSubtitleWriter(candidate.SegmentId, new ClosedGuard(),
            validateTemplateAssets: false);
        var service = new CaptionGenerationService(new FixtureAudio(), new ReplaySpanish(fixturePath), writer, s.Store);
        var request = new GenerateCaptionsRequest(s.Snapshot, s.Request().SelectedSegmentIds,
            new("medium", "fixture.bin", "es", 2, 8), s.Store.RootDirectory);
        var result = await service.GenerateAsync(request);
        Assert.Equal(12, result.Captions.Count);
        Assert.Equal(original, File.ReadAllBytes(s.Draft)); // Generar no modifica CapCut.
        var receipt = await service.ApplyAsync(result);
        Assert.Equal(4, receipt.Files.Count);
        var after = Read(s.Draft);
        var selected = after["tracks"]!.AsArray().Single(t => t!["id"]!.GetValue<string>() == candidate.TrackId)!;
        Assert.Equal(12, selected["segments"]!.AsArray().Count);
        foreach (var track in before["tracks"]!.AsArray().Where(t => t!["id"]!.GetValue<string>() != candidate.TrackId))
            Assert.True(JsonNode.DeepEquals(track, after["tracks"]!.AsArray()
                .Single(t => t!["id"]!.GetValue<string>() == track!["id"]!.GetValue<string>())));
        foreach (var path in s.Files)
            Assert.Equal(File.ReadAllBytes(s.Draft), File.ReadAllBytes(path));
        await writer.RestoreAsync(Path.Combine(result.Run.RunDirectory, "journal.json"));
        Assert.Equal(original, File.ReadAllBytes(s.Draft));
        Assert.Equal(original, File.ReadAllBytes(s.RootDraft));
        Assert.False(File.Exists(s.Draft + ".bak"));
        Assert.False(File.Exists(s.RootDraft + ".bak"));
    }

    private static WhisperToken Token(string text, long start, long end, long anchor)
        => new() { Id = 1, Text = text, Start = start, End = end, DtwTimestamp = anchor };

    private static SegmentData Phrase(params WhisperToken[] tokens)
        => new(string.Concat(tokens.Select(t => t.Text)), TimeSpan.Zero, TimeSpan.FromSeconds(1),
            0, 0, 0, 0, "es", tokens);

    private sealed class ReplaySpanish(string fixtures) : ITranscriptionService
    {
        public Task<TranscriptionResult> TranscribeAsync(RunContext run, PreparedAudio audio,
            TranscriptionOptions options, CancellationToken cancellationToken = default)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures,
                "whisper_spanish_collapsed_native.json")));
            var phrases = doc.RootElement.GetProperty("segments").EnumerateArray()
                .Select(ReadNative).Select(s => WhisperTokenNormalizer.Normalize(s, false)).ToArray();
            return Task.FromResult(new TranscriptionResult(audio.SourceSegmentId, options.ModelName,
                "es", audio.DurationUs, phrases));
        }
    }

    private static SegmentData ReadNative(JsonElement s) => new(s.GetProperty("text").GetString()!,
        TimeSpan.FromTicks(s.GetProperty("startTicks").GetInt64()),
        TimeSpan.FromTicks(s.GetProperty("endTicks").GetInt64()), 0, 0, 0, 0, "es",
        s.GetProperty("tokens").EnumerateArray().Select(t => new WhisperToken
        {
            Id = t.GetProperty("id").GetInt32(), Text = t.GetProperty("text").GetString()!,
            Start = t.GetProperty("start10Ms").GetInt64(), End = t.GetProperty("end10Ms").GetInt64(),
            DtwTimestamp = t.GetProperty("dtwTimestamp").GetInt64()
        }).ToArray());
}

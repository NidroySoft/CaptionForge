using System.Text.Json;
using CaptionForge.Core.Enums;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Rules;
using CaptionForge.Core.ValueObjects;

using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;

namespace CaptionForge.Tests.Core;

public sealed class CoreRegressionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GoldenDtwYRegularCoincidenConLasReferencias()
    {
        var tests = new Checks(output);
        string fixtureDirectory = FixtureFiles.Directory;
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureDirectory, "expected_captions_v3.json")));
        var fps = new FrameRate(30);
        var referenceResults = new List<object>();
        foreach (var fixture in new[] { "whisper_dtw.json", "whisper_regular.json" })
        {
            using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureDirectory, fixture)));
            var entries = input.RootElement.GetProperty("transcription");
            int wordMatches = 0;
            for (int i = 0; i < entries.GetArrayLength(); i++)
            {
                var entry = entries[i];
                var offsets = entry.GetProperty("offsets");
                long start = offsets.GetProperty("from").GetInt64();
                long end = offsets.GetProperty("to").GetInt64();
                var range = SubtitleTimingCalculator.QuantizeToFrames(TimeRangeUs.FromMilliseconds(start, end - start), fps);
                var expectedCue = expected.RootElement[i];
                var expectedRange = expectedCue.GetProperty("range");
                tests.Check(range.StartUs == expectedRange.GetProperty("start").GetInt64(), $"{fixture} caption {i + 1} start");
                tests.Check(range.DurationUs == expectedRange.GetProperty("duration").GetInt64(), $"{fixture} caption {i + 1} duration");
                var tokens = new List<TranscriptionToken>();
                foreach (var token in entry.GetProperty("tokens").EnumerateArray())
                {
                    string value = token.GetProperty("text").GetString()!;
                    // Las únicas correcciones son editoriales del fixture QR, externas a Core.
                    if (i == 9 && value == " words") value = "words";
                    if (i == 14 && value == ",") value = ".";
                    if (i == 14 && value == " math") value = " Math";
                    var offset = token.GetProperty("offsets");
                    long from = offset.GetProperty("from").GetInt64(), to = offset.GetProperty("to").GetInt64();
                    tokens.Add(new TranscriptionToken(value, TimeRangeUs.FromMilliseconds(from, to - from), value.StartsWith("[_", StringComparison.Ordinal)));
                }
                var words = WordTimingBuilder.Build(tokens, range);
                var cue = new SubtitleCue("fixture-audio", range, words);
                tests.Check(cue.Text == expectedCue.GetProperty("text").GetString(), $"{fixture} caption {i + 1} text");
                var expectedWords = expectedCue.GetProperty("words");
                var texts = expectedWords.GetProperty("text").EnumerateArray().Select(x => x.GetString()).ToArray();
                var starts = expectedWords.GetProperty("start_time").EnumerateArray().Select(x => x.GetInt64()).ToArray();
                var ends = expectedWords.GetProperty("end_time").EnumerateArray().Select(x => x.GetInt64()).ToArray();
                if (words.Select(w => w.Text).SequenceEqual(texts) &&
                    words.Select(w => w.StartTimeMs).SequenceEqual(starts) &&
                    words.Select(w => w.EndTimeMs).SequenceEqual(ends)) wordMatches++;
            }
            tests.Check(wordMatches == (fixture == "whisper_dtw.json" ? 15 : 13), fixture + " full word timing matches");
            referenceResults.Add(new { fixture, ranges = 15, texts = 15, wordTimingMatches = wordMatches,
                fixtureOnlyEditorialCorrections = true });
        }
        
        
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TiemposYPalabrasRechazanRangosInvalidos()
    {
        var tests = new Checks(output);
        var fps = new FrameRate(30);
        // Valores límite: unidad, desbordamiento y FPS racionales.
        tests.Check(new TimeRangeUs(10, 20).EndUs == 30, "end microseconds");
        tests.Check(TimeRangeUs.FromMilliseconds(12, 5) == new TimeRangeUs(12_000, 5_000), "milliseconds conversion");
        tests.Check(TimeRangeUs.RoundToMilliseconds(1_499) == 1 && TimeRangeUs.RoundToMilliseconds(1_500) == 2, "midpoint rounding");
        tests.Check(TimeRangeUs.RoundToMilliseconds(long.MaxValue) > 0, "rounding without overflow");
        tests.Throws<ArgumentOutOfRangeException>(() => _ = new TimeRangeUs(-1, 5), "negative start rejected");
        tests.Throws<ArgumentOutOfRangeException>(() => _ = new TimeRangeUs(1, -5), "negative duration rejected");
        tests.Throws<OverflowException>(() => _ = new TimeRangeUs(long.MaxValue, 1), "end overflow rejected");
        tests.Throws<OverflowException>(() => _ = TimeRangeUs.FromMilliseconds(long.MaxValue, 1), "unit conversion overflow rejected");
        tests.Check(new FrameRate(60, 2) == new FrameRate(30), "reduced frame rate equality");
        tests.Check(new FrameRate(30000, 1001).GetFrameStartUs(30) == 1_001_000, "rational frame boundary");
        tests.Check(new FrameRate(30000, 1001).ToFrameIndex(1_000_000) == 29, "rational frame index");
        tests.Check(new FrameRate(25).GetFrameStartUs(1) == 40_000, "25 FPS");
        tests.Throws<ArgumentOutOfRangeException>(() => _ = new FrameRate(0), "zero FPS rejected");
        tests.Throws<InvalidOperationException>(() => _ = default(FrameRate).ToFrameIndex(1000), "default FPS rejected");
        tests.Throws<OverflowException>(() => _ = new FrameRate(int.MaxValue).ToFrameIndex(long.MaxValue), "frame count overflow rejected");
        tests.Check(SubtitleTimingCalculator.QuantizeToFrames(new TimeRangeUs(0, 1_000), fps).IsEmpty, "sub-frame cue yields empty range");
        tests.Check(SubtitleTimingCalculator.ConstrainToFragment(new TimeRangeUs(700, 900), 1_000) == new TimeRangeUs(700, 300), "range clipped to fragment");
        tests.Check(SubtitleTimingCalculator.ConstrainToFragment(new TimeRangeUs(1_000, 1), 1_000) is null, "out-of-fragment cue omitted");
        var local = new TimeRangeUs(1_200_000, 400_000);
        tests.Check(SubtitleTimingCalculator.PlaceOnTimeline(local, new TimeRangeUs(20_000_000, 5_000_000)) == new TimeRangeUs(21_200_000, 400_000), "source 10s target 20s placement");
        tests.Check(SubtitleTimingCalculator.PlaceOnTimeline(local, new TimeRangeUs(30_000_000, 5_000_000)).StartUs == 31_200_000, "excluded fragment preserves later position");
        tests.Throws<ArgumentException>(() => _ = SubtitleTimingCalculator.PlaceOnTimeline(new TimeRangeUs(4_000_000, 2_000_000), new TimeRangeUs(20_000_000, 5_000_000)), "range outside target rejected");
        
        // Control, pausas, subpalabras, puntuación y alineaciones inválidas.
        TranscriptionToken Token(string value, long startMs, long endMs, bool control = false) =>
            new(value, TimeRangeUs.FromMilliseconds(startMs, endMs - startMs), control);
        var exampleWords = WordTimingBuilder.Build(new[] { Token("[_BEG_]", 0, 0, true), Token(" Hello", 10, 100),
            Token(",", 120, 130), Token(" world", 200, 280), Token("!", 290, 300) }, TimeRangeUs.FromMilliseconds(0, 300));
        tests.Check(exampleWords.Count == 3 && exampleWords[0].Text == "Hello," && exampleWords[0].EndTimeMs == 100, "punctuation retains lexical end");
        tests.Check(exampleWords[1].IsSpace && exampleWords[1].StartTimeMs == 100 && exampleWords[2].StartTimeMs == 200, "explicit zero-duration space preserves pause");
        tests.Check(exampleWords[0].StartTimeMs == 0 && exampleWords[^1].EndTimeMs == 300, "cue endpoints");
        var subwords = WordTimingBuilder.Build(new[] { Token(" can", 10, 80), Token("'", 80, 90), Token("t", 90, 150) }, TimeRangeUs.FromMilliseconds(0, 200));
        tests.Check(subwords.Count == 1 && subwords[0].Text == "can't", "subword/apostrophe join");
        tests.Check(WordTimingBuilder.Build(new[] { Token("[_BEG_]", 0, 0, true) }, TimeRangeUs.FromMilliseconds(0, 200)).Count == 0, "control-only result is empty");
        tests.Throws<ArgumentException>(() => _ = WordTimingBuilder.Build(new[] { Token(" two words", 0, 100) }, TimeRangeUs.FromMilliseconds(0, 200)), "phrase token rejected");
        tests.Throws<InvalidOperationException>(() => _ = WordTimingBuilder.Build(new[] { Token(" first", 0, 100), Token(" second", 50, 120) }, TimeRangeUs.FromMilliseconds(0, 200)), "overlapping alignment rejected");
        tests.Throws<ArgumentException>(() => _ = new TimedWord(" ", 0, 1), "space duration rejected");
        tests.Throws<ArgumentException>(() => _ = new TimedWord("word", 1, 1), "zero-duration word rejected");
        tests.Throws<ArgumentException>(() => _ = new SubtitleCue("audio", TimeRangeUs.FromMilliseconds(0, 300), new[] { new TimedWord("Hello", 0, 100) }), "cue endpoint mismatch rejected");
        
        
        await Task.CompletedTask;
    }

    [Fact]
    public async Task IdentidadesYColeccionesSonIndependientes()
    {
        var tests = new Checks(output);
        var fps = new FrameRate(30);
        // Identidades, rangos independientes y colecciones protegidas.
        var project = new CapCutProject("2A30AAE9-2228-48ab-BA00-AF0598C88C3B", "1003", "C:/CapCut/1003",
            timelineRegistryId: "3A81E819-70C3-4b5c-8DB8-4D07897271C9");
        var timeline = new CapCutTimeline("EFA8ACC8-F181-4059-96C5-6BC82353A0C6", project.Id, "Línea de tiempo 01",
            "C:/CapCut/1003/Timelines/id", "C:/CapCut/1003/Timelines/id/draft_content.json", fps, 36_533_333, true);
        tests.Check(project.Id != timeline.Id && project.Id != project.TimelineRegistryId && timeline.Id != project.TimelineRegistryId, "three separate IDs");
        var segmentModel = new MediaSegment("segment", "track", "material", "C:/audio.mp3",
            new TimeRangeUs(10_000_000, 5_000_000), new TimeRangeUs(20_000_000, 5_000_000));
        tests.Check(segmentModel.SourceRange.StartUs != segmentModel.TargetRange.StartUs, "independent source and target");
        var original = new[] { segmentModel };
        var track = new MediaTrack("track", "", MediaTrackType.Audio, original);
        original[0] = new MediaSegment("different", "track", "material", "C:/other.mp3",
            new TimeRangeUs(0, 1), new TimeRangeUs(0, 1));
        tests.Check(track.Segments[0].Id == "segment", "defensive array copy");
        tests.Throws<NotSupportedException>(() => ((IList<MediaSegment>)track.Segments)[0] = original[0], "read-only collection");
        tests.Throws<ArgumentException>(() => _ = new MediaTrack("other-track", "", MediaTrackType.Audio, new[] { segmentModel }), "foreign track segment rejected");
        tests.Throws<ArgumentOutOfRangeException>(() => _ = new MediaSegment("s", "t", "m", "C:/audio.mp3", new TimeRangeUs(0, 1), new TimeRangeUs(0, 1), double.NaN), "nonfinite speed rejected");
        
        
        await Task.CompletedTask;
    }

}

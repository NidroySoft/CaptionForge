using System.Text.Json.Nodes;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Core.Enums;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Tests.Support;
using Xunit;

namespace CaptionForge.Tests.Application;

public sealed class SourceTrackGenerationTests
{
    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 5)]
    [InlineData(2, 1)]
    [InlineData(3, 9)]
    public async Task TranscribeSoloLaPistaElegidaAunqueLasOtrasSeSolapen(int trackIndex, int expected)
    {
        var fixture = new Fixture();
        var snapshot = FourTracks(fixture.Snapshot);
        var selected = snapshot.Tracks[trackIndex];
        var request = new GenerateCaptionsRequest(snapshot, selected.Segments.Reverse().Select(s => s.Id),
            fixture.Request().Options, "C:/CaptionForge");

        var result = await fixture.Service.GenerateAsync(request);

        Assert.Equal(expected, fixture.Audio.Requests.Count);
        Assert.Equal(expected, fixture.Transcriber.Calls);
        Assert.Equal(selected.Segments.Select(s => s.Id), fixture.Audio.Requests.Select(r => r.Segment.Id));
        Assert.All(fixture.Audio.Requests, r => Assert.Equal(selected.Id, r.Segment.TrackId));
        Assert.Equal(selected.Segments.Select(s => s.Id), result.Transcriptions.Select(t => t.SourceSegmentId));
        Assert.Equal(selected.Segments.Select(s => s.TargetRange.StartUs), result.Captions.Select(c => c.TimelineRange.StartUs));
        Assert.Equal(0, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public async Task ExcluirUnClipDeVozNoLoTranscribeNiDesplazaLosSiguientes()
    {
        var fixture = new Fixture();
        var snapshot = FourTracks(fixture.Snapshot);
        var voice = snapshot.Tracks[1];
        var included = voice.Segments.Where((_, i) => i != 1).ToArray();
        var request = new GenerateCaptionsRequest(snapshot, included.Select(s => s.Id), fixture.Request().Options, "C:/CaptionForge");

        var result = await fixture.Service.GenerateAsync(request);

        Assert.Equal(4, result.Transcriptions.Count);
        Assert.DoesNotContain(fixture.Audio.Requests, r => r.Segment.Id == voice.Segments[1].Id);
        Assert.Equal(included.Select(s => s.SourceRange), fixture.Audio.Requests.Select(r => r.Segment.SourceRange));
        Assert.Equal(included.Select(s => s.TargetRange.StartUs), result.Captions.Select(c => c.TimelineRange.StartUs));
        Assert.Equal(58_300_000, result.Captions[1].TimelineRange.StartUs);
    }

    private static TimelineSnapshot FourTracks(TimelineSnapshot original)
    {
        var draft = JsonNode.Parse(File.ReadAllBytes(Path.Combine(FixtureFiles.Directory, "source_four_tracks.json")))!;
        var tracks = draft["tracks"]!.AsArray().Select(t =>
        {
            string id = t!["id"]!.GetValue<string>();
            string type = t["type"]!.GetValue<string>();
            var materials = draft["materials"]![type == "audio" ? "audios" : "videos"]!.AsArray();
            var clips = t["segments"]!.AsArray().Select(s =>
            {
                string materialId = s!["material_id"]!.GetValue<string>();
                var material = materials.Single(m => m!["id"]!.GetValue<string>() == materialId)!;
                TimeRangeUs Range(string key) => new(s[key]!["start"]!.GetValue<long>(), s[key]!["duration"]!.GetValue<long>());
                return new MediaSegment(s["id"]!.GetValue<string>(), id, materialId, material["path"]!.GetValue<string>(),
                    Range("source_timerange"), Range("target_timerange"));
            });
            return new MediaTrack(id, "", type == "audio" ? MediaTrackType.Audio : MediaTrackType.Video, clips);
        });
        var timeline = new CapCutTimeline(original.Timeline.Id, original.Project.Id, "Four tracks",
            original.Timeline.DirectoryPath, original.Timeline.DraftContentPath, new FrameRate(30), 121_133_333);
        return new TimelineSnapshot(original.Project, timeline, tracks, original.SourceFiles);
    }
}

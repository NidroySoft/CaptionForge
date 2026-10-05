using System.Text.Json.Nodes;
using CaptionForge.Core.Enums;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class SourceTrackCatalogTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Fact]
    public async Task CuatroPistasRealesConservanIdsClipsRecortesYHuecosSinEscribir()
    {
        var session = await SessionAsync();
        var draft = Read(Path.Combine(fixturePath, "source_four_tracks.json"));
        draft["id"] = session.Snapshot.Timeline.Id;
        Write(session.Draft, draft);
        byte[] before = File.ReadAllBytes(session.Draft);

        var snapshot = await session.Catalog.ReadTimelineAsync(session.Project, session.Snapshot.Timeline);

        Assert.Equal(new[] { MediaTrackType.Video, MediaTrackType.Audio, MediaTrackType.Audio, MediaTrackType.Audio },
            snapshot.Tracks.Select(t => t.Type));
        Assert.Equal(new[] { 8, 5, 1, 9 }, snapshot.Tracks.Select(t => t.Segments.Count));
        Assert.Equal(draft["tracks"]!.AsArray().Select(t => t!["id"]!.GetValue<string>()), snapshot.Tracks.Select(t => t.Id));
        Assert.All(snapshot.Tracks, track => Assert.All(track.Segments, clip => Assert.Equal(track.Id, clip.TrackId)));
        var voice = snapshot.Tracks[1].Segments;
        Assert.Equal(28_966_666, voice[1].SourceRange.StartUs);
        Assert.Equal(31_266_666, voice[1].TargetRange.StartUs);
        Assert.Equal(2_300_000, voice[1].TargetRange.StartUs - voice[0].TargetRange.EndUs);
        Assert.Equal(102_933_333, snapshot.Tracks[2].Segments[0].TargetRange.StartUs);
        Assert.Equal(before, File.ReadAllBytes(session.Draft));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 8)]
    [InlineData(null, 8)]
    public async Task VideosSinAudioSeExcluyenDelCatalogoDeClips(bool? hasAudio, int expected)
    {
        var session = await SessionAsync();
        var draft = Read(Path.Combine(fixturePath, "source_four_tracks.json"));
        draft["id"] = session.Snapshot.Timeline.Id;
        foreach (var material in draft["materials"]!["videos"]!.AsArray())
        {
            if (hasAudio.HasValue) material!["has_audio"] = hasAudio.Value;
            else material!.AsObject().Remove("has_audio");
        }
        Write(session.Draft, draft);

        var snapshot = await session.Catalog.ReadTimelineAsync(session.Project, session.Snapshot.Timeline);

        Assert.Equal(expected, snapshot.Tracks[0].Segments.Count);
        Assert.Equal(new[] { 5, 1, 9 }, snapshot.Tracks.Skip(1).Select(t => t.Segments.Count));
    }
}

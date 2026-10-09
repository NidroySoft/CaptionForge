using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Services;
using CaptionForge.Infrastructure.CapCut;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;
namespace CaptionForge.Tests.Infrastructure;

public sealed class ExistingCaptionImportTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConvertsPlainTrackKeepsOrDeletesOriginAndRestoresEverything(bool remove)
    {
        var s = await SessionAsync("snapshot_multiple_templates.json");
        var source = (await ExistingSubtitleCatalog.ReadAsync(s.Snapshot)).First(t => t.IsSupported && t.Captions.Count > 1);
        var template = (await DraftTemplateCatalog.ReadAsync(s.Snapshot)).First(t => t.IsSupported && t.TrackId != source.Id);
        var writer = new DraftTemplateSubtitleWriter(template.SegmentId, new ClosedGuard(), validateTemplateAssets: false);
        var service = new CaptionGenerationService(new FixtureAudio(), s.Transcriber, writer, s.Store);
        var before = s.Files.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null); var original = Read(s.Draft);
        var result = await service.PrepareExistingAsync(new(s.Snapshot, source.Captions, s.Store.RootDirectory, new("CapCutTrack", source.DisplayName, source.ApproximateWordTimings, source.Id), remove ? source.Id : null));
        Assert.Null(result.Options); Assert.Empty(result.Transcriptions); Assert.Equal(source.Captions.Count, result.Captions.Count);
        foreach (string path in s.Files) Assert.True(EqualBytes(path, before[path]));
        await service.ApplyAsync(result); var after = Read(s.Draft);
        var track = after["tracks"]!.AsArray().Single(t => t?["id"]?.GetValue<string>() == template.TrackId)!;
        Assert.Equal(source.Captions.Count, track["segments"]!.AsArray().Count);
        Assert.Equal(source.Captions.Select(c => c.TimelineRange.StartUs), track["segments"]!.AsArray().Select(n => n!["target_timerange"]!["start"]!.GetValue<long>()));
        Assert.Equal(!remove, after["tracks"]!.AsArray().Any(t => t?["id"]?.GetValue<string>() == source.Id));
        if (!remove) Assert.True(JsonNode.DeepEquals(original["tracks"]!.AsArray().Single(t => t?["id"]?.GetValue<string>() == source.Id), after["tracks"]!.AsArray().Single(t => t?["id"]?.GetValue<string>() == source.Id)));
        foreach (var other in original["tracks"]!.AsArray().Where(t => t?["id"]?.GetValue<string>() != template.TrackId && t?["id"]?.GetValue<string>() != source.Id))
            Assert.True(JsonNode.DeepEquals(other, after["tracks"]!.AsArray().Single(t => t?["id"]?.GetValue<string>() == other?["id"]?.GetValue<string>())));
        Assert.Equal(File.ReadAllBytes(s.Draft), File.ReadAllBytes(s.RootDraft));
        await writer.RestoreAsync(Path.Combine(result.Run.RunDirectory, "journal.json"));
        foreach (string path in s.Files) Assert.True(EqualBytes(path, before[path]));
    }
    [Fact]
    public async Task ImportsFileWithoutAudioOrWhisperAndCanRegenerate()
    {
        var s = await SessionAsync("snapshot_multiple_templates.json"); var template = (await DraftTemplateCatalog.ReadAsync(s.Snapshot)).First(t => t.IsSupported);
        var writer = new DraftTemplateSubtitleWriter(template.SegmentId, new ClosedGuard(), validateTemplateAssets: false);
        var service = new CaptionGenerationService(new FixtureAudio(), s.Transcriber, writer, s.Store);
        var cues = SubtitleFileReader.Parse("1\n00:00:01,000 --> 00:00:02,500\nImported example.\n\n2\n00:00:04,000 --> 00:00:06,000\nSecond example.", ".srt");
        var result = await service.PrepareExistingAsync(new(s.Snapshot, cues, s.Store.RootDirectory, new("SubtitleFile", "sample.srt", true)));
        Assert.Empty(result.Transcriptions); await service.ApplyAsync(result);
        s.Snapshot = await s.Catalog.ReadTimelineAsync(s.Project, s.Snapshot.Timeline);
        var second = await service.PrepareExistingAsync(new(s.Snapshot, cues, s.Store.RootDirectory, new("SubtitleFile", "sample.srt", true)));
        await service.ApplyAsync(second);
        Assert.Equal(2, Read(s.Draft)["tracks"]!.AsArray().Single(t => t?["id"]?.GetValue<string>() == template.TrackId)!["segments"]!.AsArray().Count);
    }
    [Fact]
    public async Task RejectsUsingOriginalTrackAsTemplateEvenWhenKeepingIt()
    {
        var s = await SessionAsync("snapshot_multiple_templates.json"); var template = (await DraftTemplateCatalog.ReadAsync(s.Snapshot)).First(t => t.IsSupported);
        var source = (await ExistingSubtitleCatalog.ReadAsync(s.Snapshot)).Single(t => t.Id == template.TrackId);
        var service = new CaptionGenerationService(new FixtureAudio(), s.Transcriber, new DraftTemplateSubtitleWriter(template.SegmentId, new ClosedGuard(), validateTemplateAssets: false), s.Store);
        var before = File.ReadAllBytes(s.Draft);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareExistingAsync(new(s.Snapshot, source.Captions, s.Store.RootDirectory, new("CapCutTrack", source.DisplayName, source.ApproximateWordTimings, source.Id))));
        Assert.Equal(before, File.ReadAllBytes(s.Draft));
    }
    [CapCutImportFact]
    public async Task SuppliedRealJsonConvertsFourPlainCaptionsOnDisposableCopy()
    {
        var s = await SessionAsync("snapshot_multiple_templates.json"); string real = Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_IMPORT_DRAFT")!;
        byte[] untouched = File.ReadAllBytes(real); var actual = Read(real); var doc = Read(s.Draft);
        var sourceTrack = actual["tracks"]!.AsArray().Single(t => t?["type"]?.GetValue<string>() == "text");
        doc["tracks"]!.AsArray().Add(sourceTrack!.DeepClone()); foreach (var text in actual["materials"]!["texts"]!.AsArray()) doc["materials"]!["texts"]!.AsArray().Add(text!.DeepClone());
        Write(s.Draft, doc); Write(s.RootDraft, doc); s.Snapshot = await s.Catalog.ReadTimelineAsync(s.Project, s.Snapshot.Timeline);
        var source = (await ExistingSubtitleCatalog.ReadAsync(s.Snapshot)).Single(t => t.Id == sourceTrack!["id"]!.GetValue<string>());
        Assert.True(source.IsSupported, source.UnsupportedReason); Assert.Equal(4, source.Captions.Count); Assert.True(source.ApproximateWordTimings);
        var template = (await DraftTemplateCatalog.ReadAsync(s.Snapshot)).First(t => t.IsSupported && t.TrackId != source.Id);
        var writer = new DraftTemplateSubtitleWriter(template.SegmentId, new ClosedGuard(), validateTemplateAssets: false);
        var service = new CaptionGenerationService(new FixtureAudio(), s.Transcriber, writer, s.Store);
        var result = await service.PrepareExistingAsync(new(s.Snapshot, source.Captions, s.Store.RootDirectory, new("CapCutTrack", "Real sample", true, source.Id), source.Id));
        await service.ApplyAsync(result); Assert.Equal(4, Read(s.Draft)["tracks"]!.AsArray().Single(t => t?["id"]?.GetValue<string>() == template.TrackId)!["segments"]!.AsArray().Count);
        Assert.Equal(untouched, File.ReadAllBytes(real)); await writer.RestoreAsync(Path.Combine(result.Run.RunDirectory, "journal.json")); Assert.True(JsonNode.DeepEquals(doc, Read(s.Draft)));
    }
}
public sealed class CapCutImportFactAttribute : FactAttribute
{
    public CapCutImportFactAttribute() { if (!File.Exists(Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_IMPORT_DRAFT"))) Skip = "JSON real opcional: configura CAPTIONFORGE_TEST_IMPORT_DRAFT."; }
}

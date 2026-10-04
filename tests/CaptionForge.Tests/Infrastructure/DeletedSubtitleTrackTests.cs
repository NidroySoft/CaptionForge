using System.Text.Json.Nodes;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Services;
using CaptionForge.Infrastructure.CapCut;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;
public sealed class DeletedSubtitleTrackTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData(false,false)] [InlineData(false,true)]
    public async Task PistaEliminadaPermiteCrearOtraSinModificarObjetosExistentes(bool dynamicTemplate,bool retainMaterials)
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");
        ICapCutSubtitleWriter writer=s.Writer;
        if(dynamicTemplate)
        {
            var seed=(await DraftTemplateCatalog.ReadAsync(s.Snapshot))[0];
            var seedDoc=Read(s.Draft);var seedTrack=seedDoc["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==seed.TrackId)!;
            seedTrack["native_custom_track_setting"]="preserved";Write(s.Draft,seedDoc);Write(s.RootDraft,seedDoc);
            s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
            writer=new DraftTemplateSubtitleWriter(seed.SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        }
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        var first=await service.GenerateAsync(s.Request());await service.ApplyAsync(first);
        var oldIds=first.Plan.ManagedSubtitles.Objects.Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
        var doc=Read(s.Draft);var tracks=doc["tracks"]!.AsArray();
        var oldTrack=tracks.Single(t=>t?["id"]?.GetValue<string>()==first.Plan.ManagedSubtitles.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id)!;
        if(retainMaterials)
        {
            // A moved segment still uses the old generated materials. It is now
            // preserved as external content; resetting the registry must not delete it.
            var moved=oldTrack["segments"]![0]!.DeepClone();moved["id"]="MOVED-FOREIGN-SEGMENT";
            tracks.Add(new JsonObject{["id"]="FOREIGN-TEXT-TRACK",["type"]="text",["segments"]=new JsonArray(moved)});
        }
        tracks.Remove(oldTrack);
        if(!retainMaterials)foreach(var category in doc["materials"]!.AsObject())if(category.Value is JsonArray array)
            foreach(var node in array.Where(n=>oldIds.Contains(n?["id"]?.GetValue<string>()??"")).ToArray())array.Remove(node);
        Write(s.Draft,doc);Write(s.RootDraft,doc);Write(s.Draft+".bak",doc);Write(s.RootDraft+".bak",doc);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        string registry=Path.Combine(s.Store.RootDirectory,s.Project.Id,"managed",s.Snapshot.Timeline.Id+".json");
        var registryBefore=File.ReadAllBytes(registry);var before=s.Files.ToDictionary(p=>p,File.ReadAllBytes);
        var second=await service.GenerateAsync(s.Request());
        Assert.Contains(second.Plan.Warnings,w=>w.Contains("pista nueva"));
        Assert.All(second.Plan.ManagedSubtitles.Objects,o=>Assert.DoesNotContain(o.Id,oldIds));
        foreach(var p in s.Files)Assert.Equal(before[p],File.ReadAllBytes(p));
        Assert.Equal(registryBefore,File.ReadAllBytes(registry));
        await service.ApplyAsync(second);var after=Read(s.Draft);
        Assert.Equal(tracks.Count+1,after["tracks"]!.AsArray().Count);
        foreach(var track in tracks)Assert.True(JsonNode.DeepEquals(track,after["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==track?["id"]?.GetValue<string>())));
        foreach(var category in doc["materials"]!.AsObject())if(category.Value is JsonArray array)
            foreach(var material in array)Assert.True(JsonNode.DeepEquals(material,after["materials"]![category.Key]!.AsArray().Single(m=>m?["id"]?.GetValue<string>()==material?["id"]?.GetValue<string>())));
        var ownTrack=after["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==second.Plan.ManagedSubtitles.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id)!;
        Assert.Equal(second.Captions.Count,ownTrack["segments"]!.AsArray().Count);
        if(dynamicTemplate)Assert.Equal("preserved",ownTrack["native_custom_track_setting"]!.GetValue<string>());
        var managed=await s.Store.ReadManagedSubtitlesAsync(s.Project.Id,s.Snapshot.Timeline.Id);
        Assert.Equal(second.Plan.ManagedSubtitles.Objects,managed!.Objects);
        Assert.Equal(File.ReadAllBytes(s.Draft),File.ReadAllBytes(s.RootDraft));
        Assert.Equal(File.ReadAllBytes(s.Draft),File.ReadAllBytes(s.Draft+".bak"));
        string journal=Path.Combine(second.Run.RunDirectory,"journal.json");
        if(writer is DraftTemplateSubtitleWriter dynamicWriter)await dynamicWriter.RestoreAsync(journal);
        else await s.Writer.RestoreAsync(journal);
        foreach(var p in s.Files)Assert.Equal(before[p],File.ReadAllBytes(p));
        Assert.Equal(registryBefore,File.ReadAllBytes(registry));
    }
}

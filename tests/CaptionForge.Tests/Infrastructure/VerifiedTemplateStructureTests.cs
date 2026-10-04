using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.CapCut;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class VerifiedTemplateStructureTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Fact]
    public async Task EscritorReproduceElJsonPorBloquesVerificadoEnCapCut()
    {
        var s=await SessionAsync();
        var seed=Read(Path.Combine(fixturePath,"user_verified_seed.json"));
        var expected=Read(Path.Combine(fixturePath,"user_verified_blocks.json"));
        seed["id"]=s.Snapshot.Timeline.Id;expected["id"]=s.Snapshot.Timeline.Id;
        Write(s.Draft,seed);Write(s.RootDraft,seed);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        string sourceId=s.Request().SelectedSegmentIds[0];
        var expectedSegments=expected["tracks"]![1]!["segments"]!.AsArray();
        var cues=expectedSegments.Select(seg=>
        {
            var template=Find(expected,"text_templates",seg!["material_id"]!.GetValue<string>());
            var text=Find(expected,"texts",template["text_info_resources"]![0]!["text_material_id"]!.GetValue<string>());
            var words=text["words"]!;
            var timed=words["text"]!.AsArray().Select((w,i)=>new TimedWord(w!.GetValue<string>(),words["start_time"]![i]!.GetValue<long>(),words["end_time"]![i]!.GetValue<long>()));
            return new SubtitleCue(sourceId,new TimeRangeUs(seg["target_timerange"]!["start"]!.GetValue<long>(),seg["target_timerange"]!["duration"]!.GetValue<long>()),timed);
        }).ToArray();
        var candidate=(await DraftTemplateCatalog.ReadAsync(s.Snapshot)).Single();
        var writer=new DraftTemplateSubtitleWriter(candidate.SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var before=File.ReadAllBytes(s.Draft);
        var run=await s.Store.CreateRunAsync(s.Request());
        var plan=await writer.PrepareAsync(new SubtitleWriteRequest(run,s.Snapshot,cues));
        Assert.Equal(before,File.ReadAllBytes(s.Draft));
        var actual=Read(Read(plan.PlanPath)["files"]![0]!["stagedPath"]!.GetValue<string>());
        Assert.Equal(15,actual["tracks"]![1]!["segments"]!.AsArray().Count);
        var map=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<15;i++)
        {
            var a=actual["tracks"]![1]!["segments"]![i]!;var e=expectedSegments[i]!;
            Pair(a,e);
            var at=Find(actual,"text_templates",a["material_id"]!.GetValue<string>());
            var et=Find(expected,"text_templates",e["material_id"]!.GetValue<string>());Pair(at,et);
            var aa=at["text_info_resources"]![0]!;var ea=et["text_info_resources"]![0]!;Pair(aa,ea);
            Pair(Find(actual,"texts",aa["text_material_id"]!.GetValue<string>()),Find(expected,"texts",ea["text_material_id"]!.GetValue<string>()));
            foreach(string category in new[]{"material_animations","effects"})
            {
                var ad=actual["materials"]![category]!.AsArray().Single(m=>a["extra_material_refs"]!.AsArray().Any(r=>r!.GetValue<string>()==m!["id"]!.GetValue<string>()))!;
                var ed=expected["materials"]![category]!.AsArray().Single(m=>e["extra_material_refs"]!.AsArray().Any(r=>r!.GetValue<string>()==m!["id"]!.GetValue<string>()))!;Pair(ad,ed);
            }
        }
        Remap(actual,map);
        NormalizeContent(actual);NormalizeContent(expected);
        Assert.True(JsonNode.DeepEquals(expected,actual),"El documento debe coincidir con el JSON validado por el usuario, salvo los GUID de las copias.");
        void Pair(JsonNode a,JsonNode e)=>map[a["id"]!.GetValue<string>()]=e["id"]!.GetValue<string>();
    }

    [Fact]
    public async Task RegenerarReutilizaElPrimerBloqueYNoDuplicaLaPista()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");
        var candidate=(await DraftTemplateCatalog.ReadAsync(s.Snapshot))[0];
        var writer=new DraftTemplateSubtitleWriter(candidate.SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        var first=await service.GenerateAsync(s.Request());await service.ApplyAsync(first);
        var before=Read(s.Draft);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot,first.Plan.ManagedSubtitles.Objects.Where(o=>o.Kind==SubtitleObjectKind.Segment).Skip(1).Select(o=>o.Id));
        Assert.Single(candidates.Where(c=>c.SegmentId==candidate.SegmentId));
        var second=await service.GenerateAsync(s.Request());await service.ApplyAsync(second);
        Assert.Equal(first.Plan.ManagedSubtitles.Objects,second.Plan.ManagedSubtitles.Objects);
        Assert.True(JsonNode.DeepEquals(before,Read(s.Draft)));
    }

    [Fact]
    public async Task SustitucionAfectaTodaLaPistaSeleccionadaYNoCreaDuplicados()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var doc=Read(s.Draft);var tracks=doc["tracks"]!.AsArray();
        var selected=tracks.Single(t=>t?["id"]?.GetValue<string>()==candidates[0].TrackId)!;
        var other=tracks.Single(t=>t?["id"]?.GetValue<string>()==candidates[1].TrackId)!;
        var foreign=other["segments"]![0]!.DeepClone();other["segments"]!.AsArray().RemoveAt(0);
        selected["segments"]!.AsArray().Add(foreign);
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var writer=new DraftTemplateSubtitleWriter(candidates[0].SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        for(int run=0;run<2;run++)
        {
            var result=await service.GenerateAsync(s.Request());await service.ApplyAsync(result);
            var after=Read(s.Draft);var track=after["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==candidates[0].TrackId)!;
            Assert.Equal(result.Captions.Count,track["segments"]!.AsArray().Count);
            Assert.DoesNotContain(track["segments"]!.AsArray(),x=>x?["id"]?.GetValue<string>()==foreign["id"]!.GetValue<string>());
            Assert.True(result.Plan.OverwriteInfo!.RequiresConfirmation);
            s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        }
    }

    [Fact]
    public async Task PistaEliminadaPermiteUsarOtraPlantillaExistente()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var writer=new DraftTemplateSubtitleWriter(candidates[0].SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        var first=await service.GenerateAsync(s.Request());await service.ApplyAsync(first);
        var doc=Read(s.Draft);var tracks=doc["tracks"]!.AsArray();tracks.Remove(tracks.Single(t=>t?["id"]?.GetValue<string>()==candidates[0].TrackId));
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var writer2=new DraftTemplateSubtitleWriter(candidates[1].SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service2=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer2,s.Store);
        var second=await service2.GenerateAsync(s.Request());await service2.ApplyAsync(second);
        Assert.Equal(candidates[1].TrackId,second.Plan.ManagedSubtitles.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id);
        Assert.Equal(tracks.Count,Read(s.Draft)["tracks"]!.AsArray().Count);
        Assert.Contains(second.Plan.Warnings,w=>w.Contains("eliminada"));
    }

    private static JsonNode Find(JsonObject doc,string category,string id)=>doc["materials"]![category]!.AsArray().Single(n=>n?["id"]?.GetValue<string>()==id)!;
    private static void Remap(JsonNode node,Dictionary<string,string> map)
    {
        if(node is JsonObject o) foreach(var p in o.ToArray())
        {
            if(p.Value is JsonValue v && v.TryGetValue<string>(out var value) && map.TryGetValue(value,out var replacement)) o[p.Key]=replacement;
            else if(p.Value is not null) Remap(p.Value,map);
        }
        else if(node is JsonArray a) for(int i=0;i<a.Count;i++)
        {
            if(a[i] is JsonValue v && v.TryGetValue<string>(out var value) && map.TryGetValue(value,out var replacement)) a[i]=replacement;
            else if(a[i] is not null) Remap(a[i]!,map);
        }
    }
    private static void NormalizeContent(JsonObject doc)
    {
        foreach(var text in doc["materials"]!["texts"]!.AsArray()) text!["content"]=JsonNode.Parse(text["content"]!.GetValue<string>())!.ToJsonString();
    }
}

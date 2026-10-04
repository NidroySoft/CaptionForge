using System.Text.Json.Nodes;
using CaptionForge.Application.Services;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Infrastructure.CapCut;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;
public sealed class DraftTemplateTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData(0,1)] [InlineData(1,1)] [InlineData(2,1)] [InlineData(3,2)]
    public async Task MoldeRealConservaTodasSusCapasYRestauracionExacta(int index,int layerCount)
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");
        File.WriteAllText(s.Draft+".bak","prior timeline backup");File.WriteAllText(s.RootDraft+".bak","prior root backup");
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var before=s.Files.ToDictionary(p=>p,File.ReadAllBytes);var original=Read(s.Draft);
        var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        Assert.Equal(4,candidates.Count);Assert.All(candidates,c=>Assert.True(c.IsSupported,c.UnsupportedReason));
        var writer=new DraftTemplateSubtitleWriter(candidates[index].SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        var generated=await service.GenerateAsync(s.Request());
        Assert.Equal(4,generated.Plan.ExpectedFiles.Count);
        foreach(var p in s.Files)Assert.Equal(before[p],File.ReadAllBytes(p));
        await service.ApplyAsync(generated);var after=Read(s.Draft);
        string selectedTrackId=candidates[index].TrackId;
        var originalSegment=original["tracks"]!.AsArray().SelectMany(t=>t!["segments"]!.AsArray()).Single(s=>s?["id"]?.GetValue<string>()==candidates[index].SegmentId)!;
        var seedTemplate=original["materials"]!["text_templates"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==originalSegment["material_id"]!.GetValue<string>())!;
        var replacedIds=new HashSet<string>{seedTemplate["id"]!.GetValue<string>()};
        foreach(var a in seedTemplate["text_info_resources"]!.AsArray()) replacedIds.Add(a!["text_material_id"]!.GetValue<string>());
        foreach(var id in originalSegment["extra_material_refs"]!.AsArray()) replacedIds.Add(id!.GetValue<string>());
        foreach(var track in original["tracks"]!.AsArray().Where(t=>t?["id"]?.GetValue<string>()!=selectedTrackId))
            Assert.True(JsonNode.DeepEquals(track,after["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==track?["id"]?.GetValue<string>())));
        foreach(var category in original["materials"]!.AsObject()) if(category.Value is JsonArray a)
            foreach(var material in a.Where(m=>!replacedIds.Contains(m?["id"]?.GetValue<string>() ?? "")))
                Assert.True(JsonNode.DeepEquals(material,after["materials"]![category.Key]!.AsArray().Single(m=>m?["id"]?.GetValue<string>()==material?["id"]?.GetValue<string>())));
        var own=after["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==generated.Plan.ManagedSubtitles.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id)!;
        Assert.Equal(selectedTrackId,own["id"]!.GetValue<string>());
        Assert.Equal(original["tracks"]!.AsArray().Count,after["tracks"]!.AsArray().Count);
        Assert.Equal(candidates[index].SegmentId,own["segments"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(seedTemplate["id"]!.GetValue<string>(),own["segments"]![0]!["material_id"]!.GetValue<string>());
        for(int i=0;i<generated.Captions.Count;i++)
        {
            var seg=own["segments"]![i]!;
            var template=after["materials"]!["text_templates"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==seg["material_id"]!.GetValue<string>())!;
            Assert.Equal(candidates[index].ResourceId,template["resource_id"]!.GetValue<string>());
            Assert.Equal(layerCount,template["text_info_resources"]!.AsArray().Count);
            foreach(var a in template["text_info_resources"]!.AsArray())
            {
                var text=after["materials"]!["texts"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==a!["text_material_id"]!.GetValue<string>())!;
                var content=JsonNode.Parse(text["content"]!.GetValue<string>())!;
                Assert.Equal(generated.Captions[i].Text,content["text"]!.GetValue<string>());
                Assert.Equal(generated.Captions[i].Text.Length,content["styles"]![0]!["range"]![1]!.GetValue<int>());
                Assert.Equal(generated.Captions[i].Words.Count,text["words"]!["text"]!.AsArray().Count);
                if(index==3)Assert.Equal(generated.Captions[i].Words.Count,a!["word_index"]![1]!.GetValue<int>());
            }
            if(index==3)
            {
                Assert.Equal(generated.Captions[i].Text,template["origin_word_info"]!["text"]!.GetValue<string>());
                Assert.Equal(generated.Captions[i].Text,template["current_word_info"]!["text"]!.GetValue<string>());
                Assert.Equal("can_not_render",template["text_info_resources"]![1]!["clip_type"]!.GetValue<string>());
            }
        }
        Assert.Equal(File.ReadAllBytes(s.Draft),File.ReadAllBytes(s.RootDraft));
        Assert.Equal(File.ReadAllBytes(s.Draft),File.ReadAllBytes(s.Draft+".bak"));
        await writer.RestoreAsync(Path.Combine(generated.Run.RunDirectory,"journal.json"));
        foreach(var p in s.Files)Assert.Equal(before[p],File.ReadAllBytes(p));
    }
    [Fact]
    public async Task CambiarMoldeConservaLaPistaAnteriorYUsaLaSeleccionada()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var templates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var writer=new DraftTemplateSubtitleWriter(templates[0].SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        var first=await service.GenerateAsync(s.Request());await service.ApplyAsync(first);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var writer2=new DraftTemplateSubtitleWriter(templates[3].SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service2=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer2,s.Store);
        var second=await service2.GenerateAsync(s.Request());await service2.ApplyAsync(second);
        Assert.Equal(templates[3].TrackId,second.Plan.ManagedSubtitles.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id);
        var doc=Read(s.Draft);
        Assert.Equal(7,doc["tracks"]!.AsArray().Count);
        Assert.Equal(2+first.Captions.Count+second.Captions.Count,doc["materials"]!["text_templates"]!.AsArray().Count);
        Assert.Equal(17+first.Captions.Count+second.Captions.Count*2,doc["materials"]!["texts"]!.AsArray().Count);
        await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>DraftTemplateCatalog.ReadAsync(s.Snapshot));
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot,second.Plan.ManagedSubtitles.Objects.Where(o=>o.Kind==SubtitleObjectKind.Segment).Skip(1).Select(o=>o.Id));Assert.Equal(4,candidates.Count);
    }
    [Fact]
    public async Task RecursosAusentesRechazanAntesDeEscribir()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var doc=Read(s.Draft);
        doc["materials"]!["text_templates"]![0]!["path"]=Path.Combine(work,"missing-resource-"+Guid.NewGuid().ToString("N"));
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var original=File.ReadAllBytes(s.Draft);
        var templates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var writer=new DraftTemplateSubtitleWriter(templates[0].SegmentId,new ClosedGuard());
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        var error=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>service.GenerateAsync(s.Request()));
        Assert.Equal(OperationErrorCode.ResourceUnavailable,error.Code);Assert.Equal(original,File.ReadAllBytes(s.Draft));
    }
    [Fact]
    public async Task RangosParcialesSeDiagnosticanSinInventarReparto()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var doc=Read(s.Draft);
        var tpl=doc["materials"]!["text_templates"]![0]!;string textId=tpl["text_info_resources"]![0]!["text_material_id"]!.GetValue<string>();
        var text=doc["materials"]!["texts"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==textId)!;
        var content=JsonNode.Parse(text["content"]!.GetValue<string>())!;content["styles"]![0]!["range"]![1]=5;text["content"]=content.ToJsonString();Write(s.Draft,doc);Write(s.RootDraft,doc);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);Assert.False(candidates[0].IsSupported);Assert.Contains("rangos parciales",candidates[0].UnsupportedReason);
        Assert.All(candidates.Skip(1),c=>Assert.True(c.IsSupported));
    }
    [Fact]
    public async Task MoldeSeleccionadoEnSecundariaConservaRaizYBackup()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");string selected=s.Snapshot.Timeline.Id;
        var registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");var registry=Read(registryPath);
        var otherEntry=registry["timelines"]![0]!.DeepClone();otherEntry["id"]="OTHER-TIMELINE";otherEntry["name"]="Otra timeline";
        registry["timelines"]!.AsArray().Add(otherEntry);registry["main_timeline_id"]="OTHER-TIMELINE";Write(registryPath,registry);
        var other=Read(s.RootDraft);other["id"]="OTHER-TIMELINE";
        string otherFolder=Path.Combine(s.Project.DirectoryPath,"Timelines","OTHER-TIMELINE");Directory.CreateDirectory(otherFolder);Write(Path.Combine(otherFolder,"draft_content.json"),other);
        Write(s.RootDraft,other);Write(s.RootDraft+".bak",other);byte[] root=File.ReadAllBytes(s.RootDraft),bak=File.ReadAllBytes(s.RootDraft+".bak");
        var timeline=(await s.Catalog.GetTimelinesAsync(s.Project)).Single(t=>t.Id==selected);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,timeline);
        var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var writer=new DraftTemplateSubtitleWriter(candidates[3].SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);
        var generated=await service.GenerateAsync(s.Request());Assert.Equal(2,generated.Plan.ExpectedFiles.Count);await service.ApplyAsync(generated);
        Assert.Equal(root,File.ReadAllBytes(s.RootDraft));Assert.Equal(bak,File.ReadAllBytes(s.RootDraft+".bak"));
        Assert.Equal("OTHER-TIMELINE",Read(registryPath)["main_timeline_id"]!.GetValue<string>());
    }
    [Fact]
    public async Task RecursoEliminadoDespuesDePrepararImpideCommit()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var doc=Read(s.Draft);
        var map=new Dictionary<string,string>();int sequence=0;
        string Map(string source)
        {
            if(map.TryGetValue(source,out var mapped))return mapped;
            string path=Path.Combine(work,"resource-"+sequence++);if(source.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase) || source.EndsWith(".otf",StringComparison.OrdinalIgnoreCase))File.WriteAllText(path,"font fixture");else Directory.CreateDirectory(path);
            map.Add(source,path);return path;
        }
        void Walk(JsonNode? node)
        {
            if(node is JsonObject o)foreach(var item in o.ToArray())
            {
                if(item.Key=="path" && item.Value is JsonValue v && v.TryGetValue<string>(out var path) && !string.IsNullOrEmpty(path))o[item.Key]=Map(path);
                else if(item.Key=="content" && item.Value is JsonValue c && c.TryGetValue<string>(out var content) && content.StartsWith('{')){var parsed=JsonNode.Parse(content)!;Walk(parsed);o[item.Key]=parsed.ToJsonString();}
                else Walk(item.Value);
            }
            else if(node is JsonArray a)foreach(var n in a)Walk(n);
        }
        foreach(string category in new[]{"texts","text_templates","effects","material_animations"})Walk(doc["materials"]![category]);
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var candidate=(await DraftTemplateCatalog.ReadAsync(s.Snapshot))[0];await DraftTemplateCatalog.ValidateResourcesAsync(s.Snapshot,candidate.SegmentId);
        var writer=new DraftTemplateSubtitleWriter(candidate.SegmentId,new ClosedGuard());
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,writer,s.Store);var generated=await service.GenerateAsync(s.Request());
        byte[] original=File.ReadAllBytes(s.Draft);string templatePath=doc["materials"]!["text_templates"]![0]!["path"]!.GetValue<string>();Directory.Delete(templatePath);
        var error=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>service.ApplyAsync(generated));Assert.Equal(OperationErrorCode.ResourceUnavailable,error.Code);Assert.Equal(original,File.ReadAllBytes(s.Draft));
        Assert.False(File.Exists(Path.Combine(generated.Run.RunDirectory,"journal.json")));
    }

}

using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.CapCut;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class TrackAndLayerRegressionTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    private const string NewTrack="FDB84D84-24E0-468a-B2BB-B349ACE3FF4F";
    private const string ExistingTrack="670EBFB2-771E-41d9-BDF3-B8344DCB3F3A";
    private const string NewSeed="1A539B29-E57F-4389-BE34-DD45A924F893";

    [Fact]
    public async Task EjemploRealConTresCapasEsSeleccionableSinConfirmacionYTextoCompleto()
    {
        var s=await RealAsync();var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        Assert.Equal(2,candidates.Count);
        var seed=candidates.Single(c=>c.TrackId==NewTrack);
        Assert.True(seed.IsSupported,seed.UnsupportedReason);
        Assert.Equal("The quick brown fox jumps over the lazy dog",seed.SampleText);
        Assert.Equal(3,seed.TextLayerCount);Assert.False(seed.RequiresOverwriteConfirmation);
        Assert.True(candidates.Single(c=>c.TrackId==ExistingTrack).RequiresOverwriteConfirmation);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(5)] [InlineData(9)] [InlineData(12)]
    public async Task RepartoRealMantieneTiemposGlobalesAlBloqueYReferenciaOculta(int count)
    {
        var s=await RealAsync();var before=Read(s.Draft);
        var cue=Cue(s.Request().SelectedSegmentIds[0],count,2_000_000);
        var writer=new DraftTemplateSubtitleWriter(NewSeed,new ClosedGuard(),validateTemplateAssets:false);
        var run=await s.Store.CreateRunAsync(s.Request());
        var plan=await writer.PrepareAsync(new SubtitleWriteRequest(run,s.Snapshot,[cue]));
        Assert.False(plan.OverwriteInfo!.RequiresConfirmation);
        var doc=Staged(plan);var template=Template(doc,NewSeed);
        var layers=template["text_info_resources"]!.AsArray();
        var hidden=layers.Single(l=>l?["clip_type"]?.GetValue<string>()=="can_not_render")!;
        Assert.Equal(cue.Text,Content(Text(doc,hidden)));
        var visible=layers.Where(l=>l?["clip_type"]?.GetValue<string>()!="can_not_render").ToArray();
        Assert.Equal(cue.Text,string.Concat(visible.Select(l=>Content(Text(doc,l!)))));
        Assert.Equal(count==1?1:2,visible.Length);
        foreach(var layer in layers)
        {
            int start=layer!["word_index"]![0]!.GetValue<int>(),end=layer["word_index"]![1]!.GetValue<int>();
            var text=Text(doc,layer);
            Assert.Equal(string.Concat(cue.Words.Skip(start).Take(end-start).Select(w=>w.Text)),Content(text));
            Assert.Equal(cue.Words.Skip(start).Take(end-start).Select(w=>w.StartTimeMs),text["words"]!["start_time"]!.AsArray().Select(w=>w!.GetValue<long>()));
            Assert.Equal(cue.Words.Skip(start).Take(end-start).Select(w=>w.EndTimeMs),text["words"]!["end_time"]!.AsArray().Select(w=>w!.GetValue<long>()));
            long attachStart=layer["attach_info"]!["start_time"]!.GetValue<long>(),duration=layer["attach_info"]!["duration"]!.GetValue<long>();
            Assert.InRange(attachStart,0,cue.TimelineRange.DurationUs-1);Assert.InRange(duration,1,cue.TimelineRange.DurationUs-attachStart);
            if(layer!=hidden)
            {
                Assert.Equal(cue.Words[start].StartTimeMs*1000,attachStart);
                long expectedEnd=end==cue.Words.Count?cue.TimelineRange.DurationUs:cue.Words[end-1].EndTimeMs*1000;
                Assert.Equal(expectedEnd-attachStart,duration);
            }
            foreach(var reference in layer["extra_material_refs"]!.AsArray())
            {
                var animation=Find(doc,"material_animations",reference!.GetValue<string>());
                Assert.Equal(duration,animation["animations"]![0]!["duration"]!.GetValue<long>());
            }
        }
        Assert.Equal(2000,template["current_word_info"]!["start_time"]!.GetValue<long>());
        Assert.Equal(TimeRangeUs.RoundToMilliseconds(cue.TimelineRange.EndUs),template["current_word_info"]!["end_time"]!.GetValue<long>());
        AssertGraphUnchanged(before,doc,ExistingTrack);
        Assert.True(JsonNode.DeepEquals(before["tracks"]![3],doc["tracks"]![3]));
    }

    [Fact]
    public async Task DosPistasSeRegistranRegeneranIndependientementeYRestauranRegistroCompleto()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var candidates=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var a=candidates[0];var b=candidates[3];
        var serviceA=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,new DraftTemplateSubtitleWriter(a.SegmentId,new ClosedGuard(),validateTemplateAssets:false),s.Store);
        var first=await serviceA.GenerateAsync(s.Request());await serviceA.ApplyAsync(first);
        var afterA=Read(s.Draft);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var serviceB=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,new DraftTemplateSubtitleWriter(b.SegmentId,new ClosedGuard(),validateTemplateAssets:false),s.Store);
        var second=await serviceB.GenerateAsync(s.Request());await serviceB.ApplyAsync(second);
        AssertGraphUnchanged(afterA,Read(s.Draft),a.TrackId);
        string registry=Path.Combine(first.Run.ProjectDirectory,"managed",first.Run.TimelineId+".json");
        Assert.Equal(2,Read(registry)["tracks"]!.AsArray().Count);
        byte[] registryBefore=File.ReadAllBytes(registry),draftBefore=File.ReadAllBytes(s.Draft);
        var bBefore=Read(s.Draft);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var third=await serviceA.GenerateAsync(s.Request());Assert.True(third.Plan.OverwriteInfo!.IsManagedTrack);Assert.True(third.Plan.OverwriteInfo.RequiresConfirmation);
        await serviceA.ApplyAsync(third);
        Assert.Equal(first.Plan.ManagedSubtitles.Objects,third.Plan.ManagedSubtitles.Objects);
        AssertGraphUnchanged(bBefore,Read(s.Draft),b.TrackId);
        Assert.Equal(2,Read(registry)["tracks"]!.AsArray().Count);
        await new DraftTemplateSubtitleWriter(a.SegmentId,new ClosedGuard(),validateTemplateAssets:false).RestoreAsync(Path.Combine(third.Run.RunDirectory,"journal.json"));
        Assert.Equal(draftBefore,File.ReadAllBytes(s.Draft));Assert.Equal(registryBefore,File.ReadAllBytes(registry));
    }

    [Fact]
    public async Task MoldeOriginalSobreviveBloqueDeUnaPalabraYRegeneracionLarga()
    {
        var s=await RealAsync();string source=s.Request().SelectedSegmentIds[0];
        var writer=new DraftTemplateSubtitleWriter(NewSeed,new ClosedGuard(),validateTemplateAssets:false);
        var run=await s.Store.CreateRunAsync(s.Request());var plan=await writer.PrepareAsync(new SubtitleWriteRequest(run,s.Snapshot,[Cue(source,1,0)]));
        MarkApplying(run.RunDirectory);var receipt=await writer.ApplyAsync(plan);
        Assert.Equal(2,Template(Read(s.Draft),NewSeed)["text_info_resources"]!.AsArray().Count);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var previous=await s.Store.ReadManagedSubtitlesAsync(s.Project.Id,s.Snapshot.Timeline.Id);
        var nextRun=await s.Store.CreateRunAsync(s.Request());var next=await writer.PrepareAsync(new SubtitleWriteRequest(nextRun,s.Snapshot,[Cue(source,12,0)],previous));
        Assert.Equal(3,Template(Staged(next),NewSeed)["text_info_resources"]!.AsArray().Count);
        Assert.True(next.OverwriteInfo!.RequiresConfirmation);
        await writer.RestoreAsync(receipt.JournalPath);
    }

    [Fact]
    public async Task CambiarSoloElTextoNoHaceLaPlantillaIncompatibleYExigeConfirmacion()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var candidate=(await DraftTemplateCatalog.ReadAsync(s.Snapshot))[0];var doc=Read(s.Draft);
        var template=Template(doc,candidate.SegmentId);var layer=template["text_info_resources"]![0]!;var text=Text(doc,layer);
        var content=JsonNode.Parse(text["content"]!.GetValue<string>())!;
        string replacement="My custom caption";content["text"]=replacement;content["styles"]![0]!["range"]=new JsonArray(0,replacement.Length);
        text["content"]=content.ToJsonString();text["words"]=new JsonObject { ["text"]=new JsonArray("My"," ","custom"," ","caption"),["start_time"]=new JsonArray(0,200,200,600,600),["end_time"]=new JsonArray(200,200,600,600,900) };
        if(layer["word_index"] is JsonArray indices && indices.Count>0) layer["word_index"]=new JsonArray(0,5);
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var found=(await DraftTemplateCatalog.ReadAsync(s.Snapshot)).Single(c=>c.SegmentId==candidate.SegmentId);
        Assert.True(found.IsSupported,found.UnsupportedReason);Assert.True(found.RequiresOverwriteConfirmation);
    }

    [Fact]
    public async Task RegistroAntiguoSinListaDePistasSeMigraSinEliminarSubtitulos()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var c=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,new DraftTemplateSubtitleWriter(c[0].SegmentId,new ClosedGuard(),validateTemplateAssets:false),s.Store);
        var first=await service.GenerateAsync(s.Request());await service.ApplyAsync(first);
        string path=Path.Combine(first.Run.ProjectDirectory,"managed",first.Run.TimelineId+".json");var registry=Read(path);registry.Remove("tracks");Write(path,registry);
        var before=Read(s.Draft);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var service2=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,new DraftTemplateSubtitleWriter(c[1].SegmentId,new ClosedGuard(),validateTemplateAssets:false),s.Store);
        var next=await service2.GenerateAsync(s.Request());await service2.ApplyAsync(next);
        Assert.Equal(2,Read(path)["tracks"]!.AsArray().Count);AssertGraphUnchanged(before,Read(s.Draft),c[0].TrackId);
    }

    [Fact]
    public async Task ElegirPistaConQuinceSubtitulosSoloSobrescribeEsaPista()
    {
        var s=await RealAsync();var before=Read(s.Draft);
        var seed=(await DraftTemplateCatalog.ReadAsync(s.Snapshot)).Single(c=>c.TrackId==ExistingTrack);
        var run=await s.Store.CreateRunAsync(s.Request());var writer=new DraftTemplateSubtitleWriter(seed.SegmentId,new ClosedGuard(),validateTemplateAssets:false);
        string source=s.Request().SelectedSegmentIds[0];var plan=await writer.PrepareAsync(new SubtitleWriteRequest(run,s.Snapshot,[Cue(source,3,0),Cue(source,4,2_000_000)]));
        var after=Staged(plan);Assert.Equal(15,plan.OverwriteInfo!.ExistingBlockCount);Assert.True(plan.OverwriteInfo.RequiresConfirmation);
        Assert.Equal(2,after["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==ExistingTrack)!["segments"]!.AsArray().Count);
        AssertGraphUnchanged(before,after,NewTrack);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReferenciasCompletasFuncionanSinPalabrasAntiguasONuevoTextoEnMetadatosObsoletos(bool removeWords)
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var c=(await DraftTemplateCatalog.ReadAsync(s.Snapshot))[0];var doc=Read(s.Draft);
        var template=Template(doc,c.SegmentId);var layer=template["text_info_resources"]![0]!;var text=Text(doc,layer);
        template["current_word_info"]=new JsonObject { ["text"]="Obsolete text that is longer than the actual caption",["words"]=new JsonArray() };
        if(removeWords)
        {
            text["words"]=new JsonObject { ["text"]=new JsonArray(),["start_time"]=new JsonArray(),["end_time"]=new JsonArray() };layer["word_index"]=new JsonArray();
        }
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var writer=new DraftTemplateSubtitleWriter(c.SegmentId,new ClosedGuard(),validateTemplateAssets:false);var run=await s.Store.CreateRunAsync(s.Request());
        var cue=Cue(s.Request().SelectedSegmentIds[0],5,0);var plan=await writer.PrepareAsync(new SubtitleWriteRequest(run,s.Snapshot,[cue]));
        var after=Staged(plan);Assert.Equal(cue.Text,Content(Text(after,Template(after,c.SegmentId)["text_info_resources"]![0]!)));
    }

    [Fact]
    public async Task RepartoSinCapaAuxiliarSeReconstruyeDesdeLosRangos()
    {
        var s=await RealAsync();var doc=Read(s.Draft);var template=Template(doc,NewSeed);template["text_info_resources"]!.AsArray().RemoveAt(2);
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var candidate=(await DraftTemplateCatalog.ReadAsync(s.Snapshot)).Single(c=>c.TrackId==NewTrack);
        Assert.True(candidate.IsSupported,candidate.UnsupportedReason);Assert.Equal("The quick brown fox jumps over the lazy dog",candidate.SampleText);
        var run=await s.Store.CreateRunAsync(s.Request());var cue=Cue(s.Request().SelectedSegmentIds[0],5,0);
        var plan=await new DraftTemplateSubtitleWriter(NewSeed,new ClosedGuard(),validateTemplateAssets:false).PrepareAsync(new SubtitleWriteRequest(run,s.Snapshot,[cue]));
        var after=Staged(plan);Assert.Equal(cue.Text,string.Concat(Template(after,NewSeed)["text_info_resources"]!.AsArray().Select(l=>Content(Text(after,l!)))));
    }

    [Fact]
    public async Task CommitFallidoConOtraPistaRegistradaRestauraArchivosYRegistroExactos()
    {
        var s=await SessionAsync("snapshot_multiple_templates.json");var c=await DraftTemplateCatalog.ReadAsync(s.Snapshot);
        var service=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,new DraftTemplateSubtitleWriter(c[0].SegmentId,new ClosedGuard(),validateTemplateAssets:false),s.Store);
        var first=await service.GenerateAsync(s.Request());await service.ApplyAsync(first);
        string path=Path.Combine(first.Run.ProjectDirectory,"managed",first.Run.TimelineId+".json");var bytes=File.ReadAllBytes(path);var files=s.Files.ToDictionary(p=>p,File.ReadAllBytes);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var service2=new CaptionGenerationService(new FixtureAudio(),s.Transcriber,new DraftTemplateSubtitleWriter(c[1].SegmentId,new ClosedGuard(),new FailCommitter(2),validateTemplateAssets:false),s.Store);
        var next=await service2.GenerateAsync(s.Request());var error=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>service2.ApplyAsync(next));
        Assert.Equal(OperationErrorCode.RecoveryRequired,error.Code);
        Assert.Equal(bytes,File.ReadAllBytes(path));foreach(string file in s.Files) Assert.Equal(files[file],File.ReadAllBytes(file));
    }

    private async Task<Session> RealAsync()
    {
        var s=await SessionAsync();var doc=Read(Path.Combine(fixturePath,"user_multilayer_and_existing_captions.json"));doc["id"]=s.Snapshot.Timeline.Id;
        Write(s.Draft,doc);Write(s.RootDraft,doc);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);return s;
    }
    private static SubtitleCue Cue(string source,int count,long start)
    {
        var words=new List<TimedWord>();long end=0;
        for(int i=0;i<count;i++)
        {
            if(i>0) words.Add(new(" ",end,end));
            long wordStart=i==0?0:end+37;end=wordStart+101+i*29;
            words.Add(new("word"+i,wordStart,end));
        }
        return new(source,new TimeRangeUs(start,end*1000),words);
    }
    private static void MarkApplying(string directory)
    {string path=Path.Combine(directory,"run.json");var doc=Read(path);doc["status"]="Applying";Write(path,doc);}
    private static JsonObject Staged(PreparedSubtitlePlan plan)=>Read(Read(plan.PlanPath)["files"]![0]!["stagedPath"]!.GetValue<string>());
    private static JsonNode Find(JsonObject doc,string category,string id)=>doc["materials"]![category]!.AsArray().Single(n=>n?["id"]?.GetValue<string>()==id)!;
    private static JsonNode Template(JsonObject doc,string id)
    {var segment=doc["tracks"]!.AsArray().SelectMany(t=>t!["segments"]!.AsArray()).Single(s=>s?["id"]?.GetValue<string>()==id)!;return Find(doc,"text_templates",segment["material_id"]!.GetValue<string>());}
    private static JsonNode Text(JsonObject doc,JsonNode layer)=>Find(doc,"texts",layer["text_material_id"]!.GetValue<string>());
    private static string Content(JsonNode text)=>JsonNode.Parse(text["content"]!.GetValue<string>())!["text"]!.GetValue<string>();
    private static void AssertGraphUnchanged(JsonObject before,JsonObject after,string trackId)
    {
        var track=before["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==trackId)!;
        Assert.True(JsonNode.DeepEquals(track,after["tracks"]!.AsArray().Single(t=>t?["id"]?.GetValue<string>()==trackId)));
        var ids=new HashSet<string>();
        foreach(var segment in track["segments"]!.AsArray())
        {
            var template=Find(before,"text_templates",segment!["material_id"]!.GetValue<string>());ids.Add(template["id"]!.GetValue<string>());
            foreach(var r in segment["extra_material_refs"]!.AsArray()) ids.Add(r!.GetValue<string>());
            foreach(var l in template["text_info_resources"]!.AsArray())
            { ids.Add(l!["text_material_id"]!.GetValue<string>());foreach(var r in l["extra_material_refs"]!.AsArray()) ids.Add(r!.GetValue<string>()); }
        }
        foreach(var category in before["materials"]!.AsObject()) if(category.Value is JsonArray array)
            foreach(var node in array.Where(n=>ids.Contains(n?["id"]?.GetValue<string>() ?? "")))
                Assert.True(JsonNode.DeepEquals(node,Find(after,category.Key,node!["id"]!.GetValue<string>())));
    }
}

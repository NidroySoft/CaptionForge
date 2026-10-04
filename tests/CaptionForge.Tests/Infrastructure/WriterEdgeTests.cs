using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Infrastructure.CapCut;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class WriterEdgeTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task FalloEnCadaReemplazoRecuperaTodosLosBytes(int failAt)
    {
        var s=await SessionAsync();var before=s.Files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        s.SetWriter(new(s.Assets,new ClosedGuard(),new FailCommitter(failAt)));var result=await s.GenerateAsync();
        var ex=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>s.Service.ApplyAsync(result));Assert.Equal(OperationErrorCode.RecoveryRequired,ex.Code);
        Assert.All(s.Files,p=>Assert.True(EqualBytes(p,before[p]),p));
        Assert.Equal("RolledBack",Read(Path.Combine(result.Run.RunDirectory,"journal.json"))["phase"]!.GetValue<string>());
        Assert.Null(await s.Store.ReadManagedSubtitlesAsync(s.Project.Id,s.Snapshot.Timeline.Id));
    }
    [Theory]
    [InlineData("metadata")] [InlineData("registry")] [InlineData("bak-created")]
    [InlineData("draft-deleted")] [InlineData("root")]
    public async Task CambioEnCualquierArchivoLeidoRechazaAntesDeEscribir(string changed)
    {
        var s=await SessionAsync();var result=await s.GenerateAsync();
        var path=changed switch {"metadata"=>Path.Combine(s.Project.DirectoryPath,"draft_meta_info.json"),"registry"=>Path.Combine(s.Project.DirectoryPath,"Timelines","project.json"),"bak-created"=>s.Draft+".bak","root"=>s.RootDraft,_=>s.Draft};
        if(changed=="draft-deleted")File.Delete(path);else File.AppendAllText(path," ");
        var bytes=s.Files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        var ex=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>s.Service.ApplyAsync(result));Assert.Equal(OperationErrorCode.SourceChanged,ex.Code);
        Assert.All(s.Files,p=>Assert.True(EqualBytes(p,bytes[p])));Assert.False(File.Exists(Path.Combine(result.Run.RunDirectory,"journal.json")));
    }
    [Theory]
    [InlineData("own-text-deleted")] [InlineData("foreign-in-own-track")]
    [InlineData("foreign-track-ref")] [InlineData("foreign-material-ref")]
    public async Task ActualizacionNoBorraObjetosConAutoriaIncierta(string scenario)
    {
        var s=await SessionAsync();var result=await s.GenerateAsync();await s.Service.ApplyAsync(result);
        var draft=Read(s.Draft);var objects=result.Plan.ManagedSubtitles.Objects;
        var trackId=objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id;var textId=objects.First(o=>o.Kind==SubtitleObjectKind.Text).Id;
        var track=draft["tracks"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==trackId)!;
        switch(scenario)
        {
            case "track-deleted":draft["tracks"]!.AsArray().Remove(track);break;
            case "own-text-deleted":var texts=draft["materials"]!["texts"]!.AsArray();texts.Remove(texts.Single(t=>t!["id"]!.GetValue<string>()==textId));break;
            case "foreign-in-own-track":var segment=track["segments"]![0]!.DeepClone();segment["id"]="foreign";track["segments"]!.AsArray().Add(segment);break;
            case "foreign-track-ref":draft["tracks"]!.AsArray().Add(new JsonObject{["id"]="foreign-track",["type"]="text",["segments"]=new JsonArray(new JsonObject{["id"]="foreign-segment",["material_id"]=textId})});break;
            case "foreign-material-ref":draft["materials"]!["texts"]!.AsArray().Add(new JsonObject{["id"]="foreign-material",["shared"]=textId});break;
        }
        Write(s.Draft,draft);Write(s.RootDraft,draft);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var before=File.ReadAllBytes(s.Draft);await Assert.ThrowsAsync<InvalidDataException>(()=>s.GenerateAsync());Assert.Equal(before,File.ReadAllBytes(s.Draft));
    }
    [Theory]
    [InlineData("backup")] [InlineData("plan")] [InlineData("journal-files")] [InlineData("journal-version")]
    public async Task RestauracionRechazaSoportesAlterados(string scenario)
    {
        var s=await SessionAsync();var result=await s.GenerateAsync();var receipt=await s.Service.ApplyAsync(result);var before=s.Files.ToDictionary(p=>p,p=>File.ReadAllBytes(p));
        switch(scenario)
        {
            case "backup":File.AppendAllText(receipt.Files.First(f=>f.BackupPath is not null).BackupPath!,"damaged");break;
            case "plan":File.AppendAllText(result.Plan.PlanPath," ");break;
            case "journal-files":var j=Read(receipt.JournalPath);j["files"]![0]!["afterSha256"]="altered";Write(receipt.JournalPath,j);break;
            case "journal-version":var jv=Read(receipt.JournalPath);jv["schemaVersion"]=2;Write(receipt.JournalPath,jv);break;
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>s.Writer.RestoreAsync(receipt.JournalPath));Assert.All(s.Files,p=>Assert.Equal(before[p],File.ReadAllBytes(p)));
    }
    [Fact]
    public async Task RestauracionEsIdempotenteSobreElMismoEstado()
    {
        var s=await SessionAsync();var before=s.Files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        var r=await s.GenerateAsync();var receipt=await s.Service.ApplyAsync(r);await s.Writer.RestoreAsync(receipt.JournalPath);await s.Writer.RestoreAsync(receipt.JournalPath);
        Assert.All(s.Files,p=>Assert.True(EqualBytes(p,before[p])));
    }
    [Theory]
    [InlineData("stagedPath")] [InlineData("backupPath")] [InlineData("registryPath")]
    public async Task PlanConRutaFueraDeSuEjecucionNoEscribeAunqueSuHashCoincida(string field)
    {
        var s=await SessionAsync();var r=await s.GenerateAsync();var doc=Read(r.Plan.PlanPath);var outside=Path.Combine(work,"outside.json");File.WriteAllText(outside,"preserve");
        if(field=="registryPath")doc[field]=outside;else doc["files"]![0]![field]=outside;Write(r.Plan.PlanPath,doc);
        var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(r.Plan.PlanPath))).ToLowerInvariant();
        var forged=new PreparedSubtitlePlan(r.Run,r.Plan.PlanPath,hash,r.Plan.CaptionCount,r.Plan.ExpectedFiles,r.Plan.ManagedSubtitles);
        await Assert.ThrowsAsync<InvalidDataException>(()=>s.Writer.ApplyAsync(forged));Assert.Equal("preserve",File.ReadAllText(outside));
    }
    [Theory]
    [InlineData("acción")] [InlineData("中文")] [InlineData("👩🏽‍💻")]
    [InlineData("e\u0301")] [InlineData("<tag>&\"quoted\"")]
    public async Task JsonContentEscapaTextoYPreservaRangosUtf16(string text)
    {
        var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());
        var cue=new SubtitleCue(s.Snapshot.Tracks[1].Segments[0].Id,new(0,1_000_000),new[]{new TimedWord(text,0,1000)});
        var plan=await s.Writer.PrepareAsync(new(run,s.Snapshot,new[]{cue}));var doc=Read(plan.PlanPath);var staged=Read(doc["files"]![0]!["stagedPath"]!.GetValue<string>());
        var id=plan.ManagedSubtitles.Objects.Single(o=>o.Kind==SubtitleObjectKind.Text).Id;
        var material=staged["materials"]!["texts"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==id)!;
        var content=JsonNode.Parse(material["content"]!.GetValue<string>())!;
        Assert.Equal(text,content["text"]!.GetValue<string>());Assert.Equal(text.Length,content["styles"]![0]!["range"]![1]!.GetValue<int>());
        Assert.Equal(text,material["words"]!["text"]![0]!.GetValue<string>());
    }
    [Fact]
    public async Task CanvasHorizontalSeConservaYAdvierteAjusteVisualPendiente()
    {
        var s=await SessionAsync();var draft=Read(s.Draft);draft["canvas_config"]!["width"]=1920;draft["canvas_config"]!["height"]=1080;
        Write(s.Draft,draft);Write(s.RootDraft,draft);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var r=await s.GenerateAsync();await s.Service.ApplyAsync(r);
        Assert.Equal(1920,Read(s.Draft)["canvas_config"]!["width"]!.GetValue<int>());
        Assert.Contains(r.Plan.Warnings,w=>w.Contains("horizontal",StringComparison.Ordinal));
    }
    [Fact]
    public async Task CancelacionDuranteCommitNoDejaAplicacionParcial()
    {
        var s=await SessionAsync();using var cts=new CancellationTokenSource();s.SetWriter(new(s.Assets,new ClosedGuard(),new CancellingCommitter(cts)));
        var r=await s.GenerateAsync();await s.Service.ApplyAsync(r,cts.Token);
        Assert.True(cts.IsCancellationRequested);Assert.Equal("Completed",s.Status());Assert.Equal(File.ReadAllBytes(s.Draft),File.ReadAllBytes(s.Draft+".bak"));
    }
    [Theory]
    [InlineData(false,false)] [InlineData(false,true)]
    [InlineData(true,false)] [InlineData(true,true)]
    public async Task PinDecideEspejoYRestauracionExacta(bool pinSecond,bool selectSecond)
    {
        var s=await SessionAsync();string firstId=s.Snapshot.Timeline.Id;
        string secondDraft=AddSecondTimeline(s,out string secondId);
        string registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var registry=Read(registryPath);registry["main_timeline_id"]=pinSecond?secondId:firstId;Write(registryPath,registry);
        File.Copy(pinSecond?secondDraft:s.Draft,s.RootDraft,true);
        foreach(string path in new[] {s.Draft,secondDraft,s.RootDraft,registryPath})File.Copy(path,path+".bak",true);
        string[] files={s.Draft,s.Draft+".bak",secondDraft,secondDraft+".bak",s.RootDraft,s.RootDraft+".bak",registryPath,registryPath+".bak"};
        var before=files.ToDictionary(p=>p,p=>File.ReadAllBytes(p));
        string selectedId=selectSecond?secondId:firstId;
        var selected=(await s.Catalog.GetTimelinesAsync(s.Project)).Single(t=>t.Id==selectedId);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,selected);
        var result=await s.GenerateAsync();var receipt=await s.Service.ApplyAsync(result);
        bool selectedIsPinned=pinSecond==selectSecond;
        var expected=new List<string>{selected.DraftContentPath,selected.DraftContentPath+".bak"};
        if(selectedIsPinned){expected.Add(s.RootDraft);expected.Add(s.RootDraft+".bak");}
        Assert.Equal(expected.OrderBy(p=>p),receipt.Files.Select(f=>f.Before.Path).OrderBy(p=>p));
        Assert.Equal(expected.Count,result.Plan.ExpectedFiles.Count);
        Assert.Equal(File.ReadAllBytes(selected.DraftContentPath),File.ReadAllBytes(selected.DraftContentPath+".bak"));
        Assert.NotEqual(Convert.ToHexString(before[selected.DraftContentPath]),Convert.ToHexString(File.ReadAllBytes(selected.DraftContentPath)));
        if(selectedIsPinned)Assert.Equal(File.ReadAllBytes(selected.DraftContentPath),File.ReadAllBytes(s.RootDraft));
        foreach(string path in files.Except(expected))Assert.Equal(before[path],File.ReadAllBytes(path));
        Assert.Equal(pinSecond?secondId:firstId,Read(registryPath)["main_timeline_id"]!.GetValue<string>());
        await s.Writer.RestoreAsync(receipt.JournalPath);
        foreach(string path in files)Assert.Equal(before[path],File.ReadAllBytes(path));
        Assert.Null(await s.Store.ReadManagedSubtitlesAsync(s.Project.Id,selectedId));
    }
    [Theory]
    [InlineData("other-id")] [InlineData("different-content")] [InlineData("missing-root")]
    public async Task RaizInconsistenteDeTimelineFijadaImpideTodaEscritura(string scenario)
    {
        var s=await SessionAsync();File.Copy(s.Draft,s.Draft+".bak");File.Copy(s.RootDraft,s.RootDraft+".bak");
        if(scenario=="missing-root")File.Delete(s.RootDraft);
        else
        {
            var root=Read(s.RootDraft);
            if(scenario=="other-id")root["id"]="OTHER-TIMELINE";else root["duration"]=999;
            Write(s.RootDraft,root);
        }
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        string registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var files=s.Files.Append(registryPath).ToArray();
        var before=files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        var run=await s.Store.CreateRunAsync(s.Request());
        var cue=new SubtitleCue(s.Snapshot.Tracks[1].Segments[0].Id,new(0,1_000_000),new[]{new TimedWord("Test",0,1000)});
        var error=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>s.Writer.PrepareAsync(new(run,s.Snapshot,new[]{cue})));
        Assert.Equal(OperationErrorCode.SourceChanged,error.Code);
        foreach(string path in files)Assert.True(EqualBytes(path,before[path]),path);
        Assert.False(File.Exists(Path.Combine(run.RunDirectory,"result","plan.json")));
    }
    [Fact]
    public async Task TimelineNoFijadaConservaRaizAunqueSuIdCoincidaConLaSeleccionada()
    {
        var s=await SessionAsync();AddSecondTimeline(s,out string secondId);
        string registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var registry=Read(registryPath);registry["main_timeline_id"]=secondId;Write(registryPath,registry);
        File.Copy(s.RootDraft,s.RootDraft+".bak");
        var rootBefore=File.ReadAllBytes(s.RootDraft);var bakBefore=File.ReadAllBytes(s.RootDraft+".bak");
        var registryBefore=File.ReadAllBytes(registryPath);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        Assert.False(s.Snapshot.Timeline.IsMain);
        var result=await s.GenerateAsync();var receipt=await s.Service.ApplyAsync(result);
        Assert.Equal(2,receipt.Files.Count);
        Assert.Equal(rootBefore,File.ReadAllBytes(s.RootDraft));Assert.Equal(bakBefore,File.ReadAllBytes(s.RootDraft+".bak"));
        Assert.Equal(registryBefore,File.ReadAllBytes(registryPath));
    }
    [Fact]
    public async Task CambioDePinDespuesDePrepararRechazaAplicacion()
    {
        var s=await SessionAsync();AddSecondTimeline(s,out string secondId);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var result=await s.GenerateAsync();
        string registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var registry=Read(registryPath);registry["main_timeline_id"]=secondId;Write(registryPath,registry);
        var files=s.Files.Append(registryPath).ToArray();
        var before=files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        var error=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>s.Service.ApplyAsync(result));
        Assert.Equal(OperationErrorCode.SourceChanged,error.Code);
        foreach(string path in files)Assert.True(EqualBytes(path,before[path]),path);
        Assert.False(File.Exists(Path.Combine(result.Run.RunDirectory,"journal.json")));
    }
    [Theory]
    [InlineData("missing-pin")] [InlineData("unknown-pin")]
    [InlineData("deleted-pin")] [InlineData("duplicate-pin")]
    public async Task RegistroSinPinValidoNoPreparaEscritura(string scenario)
    {
        var s=await SessionAsync();AddSecondTimeline(s,out string secondId);
        string registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var registry=Read(registryPath);registry["main_timeline_id"]=secondId;
        switch(scenario)
        {
            case "missing-pin":registry.Remove("main_timeline_id");break;
            case "unknown-pin":registry["main_timeline_id"]="UNKNOWN-TIMELINE";break;
            case "deleted-pin":registry["timelines"]![1]!["is_marked_delete"]=true;break;
            case "duplicate-pin":registry["timelines"]!.AsArray().Add(registry["timelines"]![1]!.DeepClone());break;
        }
        Write(registryPath,registry);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var run=await s.Store.CreateRunAsync(s.Request());
        var cue=new SubtitleCue(s.Snapshot.Tracks[1].Segments[0].Id,new(0,1_000_000),new[]{new TimedWord("Test",0,1000)});
        var before=File.ReadAllBytes(s.Draft);var rootBefore=File.ReadAllBytes(s.RootDraft);
        await Assert.ThrowsAsync<InvalidDataException>(()=>s.Writer.PrepareAsync(new(run,s.Snapshot,new[]{cue})));
        Assert.Equal(before,File.ReadAllBytes(s.Draft));Assert.Equal(rootBefore,File.ReadAllBytes(s.RootDraft));
        Assert.False(File.Exists(Path.Combine(run.RunDirectory,"result","plan.json")));
    }
    private static string AddSecondTimeline(Session s,out string secondId)
    {
        secondId="SECOND-TIMELINE";
        string directory=Path.Combine(s.Project.DirectoryPath,"Timelines",secondId);Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"draft_content.json");var draft=Read(s.Draft);draft["id"]=secondId;Write(path,draft);
        string registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var registry=Read(registryPath);var entry=registry["timelines"]![0]!.DeepClone();
        entry["id"]=secondId;entry["name"]="Second timeline";registry["timelines"]!.AsArray().Add(entry);Write(registryPath,registry);
        return path;
    }
    private sealed class CancellingCommitter(CancellationTokenSource cts) : IFileCommitter
    {public async Task ReplaceAsync(string path,byte[] bytes){await new AtomicFileCommitter().ReplaceAsync(path,bytes);cts.Cancel();}}
}

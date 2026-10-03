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
    [InlineData("track-deleted")] [InlineData("own-text-deleted")] [InlineData("foreign-in-own-track")]
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
    private sealed class CancellingCommitter(CancellationTokenSource cts) : IFileCommitter
    {public async Task ReplaceAsync(string path,byte[] bytes){await new AtomicFileCommitter().ReplaceAsync(path,bytes);cts.Cancel();}}
}

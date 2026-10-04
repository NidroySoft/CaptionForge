using System.Text.Json;
using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Infrastructure.Settings;
using CaptionForge.Infrastructure.Workspace;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class WorkspaceAndSettingsEdgeTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData("../escape")] [InlineData("CON")] [InlineData("aux")] [InlineData("COM1")]
    [InlineData("LPT9")] [InlineData("id/child")] [InlineData("id\\child")] [InlineData("名字")]
    public async Task RegistroNoAceptaIdsQuePuedanEscaparOCrearNombresInvalidos(string id)
    {
        var store=new JsonWorkspaceStore(Path.Combine(work,"workspace"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadManagedSubtitlesAsync(id,"timeline"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadManagedSubtitlesAsync("project",id));
    }
    [Theory]
    [InlineData("inside-project")] [InlineData("inside-projects-root")] [InlineData("ancestor")]
    [InlineData("same-project")] [InlineData("mismatch")]
    public async Task WorkspaceDebeEstarSeparadoYCoincidirConPeticion(string scenario)
    {
        var s=await SessionAsync();var root=scenario switch
        {
            "inside-project"=>Path.Combine(s.Project.DirectoryPath,"workspace"),
            "inside-projects-root"=>Path.Combine(Path.GetDirectoryName(s.Project.DirectoryPath)!,"workspace"),
            "ancestor"=>s.Directory,"same-project"=>s.Project.DirectoryPath,_=>Path.Combine(work,"other")
        };
        var request=new GenerateCaptionsRequest(s.Snapshot,s.Request().SelectedSegmentIds,s.Request().Options,scenario=="mismatch"?s.Store.RootDirectory:root);
        await Assert.ThrowsAsync<InvalidDataException>(()=>new JsonWorkspaceStore(root).CreateRunAsync(request));
    }
    [Fact]
    public async Task RelojFijoNoProduceIdsDeEjecucionDuplicados()
    {
        var s=await SessionAsync();var store=new JsonWorkspaceStore(s.Store.RootDirectory,new FixedClock());
        var runs=await Task.WhenAll(Enumerable.Range(0,20).Select(_=>store.CreateRunAsync(s.Request())));
        Assert.Equal(20,runs.Select(r=>r.RunId).Distinct().Count());Assert.Single(runs.Select(r=>r.ProjectDirectory).Distinct());
        Assert.All(runs,r=>Assert.Equal(new DateTimeOffset(2026,10,3,20,0,0,TimeSpan.Zero),r.CreatedAt));
        Assert.All(runs,r=>Assert.All(new[]{"audio","transcription","result","backup","logs"},name=>Assert.True(Directory.Exists(Path.Combine(r.RunDirectory,name)))));
    }
    [Fact]
    public async Task TransicionesConcurrentesSoloUnaGanaLaComparacionDeEstado()
    {
        var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());var update=new RunUpdate(RunStatus.Created,RunStatus.PreparingAudio,DateTimeOffset.UtcNow);
        var outcomes=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>await Record.ExceptionAsync(()=>s.Store.TransitionAsync(run,update))));
        Assert.Single(outcomes,e=>e is null);var error=Assert.IsType<CaptionForgeOperationException>(Assert.Single(outcomes,e=>e is not null));
        Assert.Equal(OperationErrorCode.RunStateConflict,error.Code);Assert.Equal("PreparingAudio",Read(Path.Combine(run.RunDirectory,"run.json"))["status"]!.GetValue<string>());
    }
    [Theory]
    [InlineData(RunStatus.Completed)] [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Failed)] [InlineData(RunStatus.RecoveryRequired)]
    public async Task EstadosTerminalesNoVuelvenAGenerarOAplicar(RunStatus terminal)
    {
        var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());var path=Path.Combine(run.RunDirectory,"run.json");var doc=Read(path);doc["status"]=terminal.ToString();Write(path,doc);var before=File.ReadAllBytes(path);
        foreach(var target in new[]{RunStatus.PreparingAudio,RunStatus.Applying,RunStatus.ReadyToApply})
        {
            var ex=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>s.Store.TransitionAsync(run,new(terminal,target,DateTimeOffset.UtcNow)));
            Assert.Equal(OperationErrorCode.RunStateConflict,ex.Code);Assert.Equal(before,File.ReadAllBytes(path));
        }
    }
    [Theory]
    [InlineData("schema")] [InlineData("run-id")] [InlineData("status")]
    public async Task ManifiestoIncoherenteNoSeReescribe(string flaw)
    {
        var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());var path=Path.Combine(run.RunDirectory,"run.json");var doc=Read(path);
        if(flaw=="schema")doc["schemaVersion"]=2;else if(flaw=="run-id")doc["run"]!["runId"]="foreign-run";else doc["status"]="Completed";Write(path,doc);var before=File.ReadAllBytes(path);
        var error=await Record.ExceptionAsync(()=>s.Store.TransitionAsync(run,new(RunStatus.Created,RunStatus.PreparingAudio,DateTimeOffset.UtcNow)));
        Assert.NotNull(error);if(flaw=="status")Assert.IsType<CaptionForgeOperationException>(error);else Assert.IsType<InvalidDataException>(error);
        Assert.Equal(before,File.ReadAllBytes(path));
    }
    [Theory]
    [InlineData("invalid-json")] [InlineData("schema")] [InlineData("wrong-project")] [InlineData("wrong-timeline")]
    public async Task RegistroPropioInvalidoNoSeUsa(string flaw)
    {
        var store=new JsonWorkspaceStore(Path.Combine(work,"workspace"));var path=Path.Combine(store.RootDirectory,"project","managed","timeline.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var doc=new JsonObject{["schemaVersion"]=flaw=="schema"?2:1,["runId"]="r",["managed"]=new JsonObject{["projectId"]=flaw=="wrong-project"?"other":"project",["timelineId"]=flaw=="wrong-timeline"?"other":"timeline",["objects"]=new JsonArray()}};
        if(flaw=="invalid-json")File.WriteAllText(path,"{");else Write(path,doc);
        var error=await Record.ExceptionAsync(()=>store.ReadManagedSubtitlesAsync("project","timeline"));
        if(flaw=="invalid-json")Assert.IsAssignableFrom<JsonException>(error);else Assert.IsType<InvalidDataException>(error);
    }
    [Theory]
    [InlineData("invalid-json")] [InlineData("schema")] [InlineData("zero-threads")]
    public async Task SettingsCorruptosNoSeSustituyenPorDefaultsSilenciosos(string flaw)
    {
        var path=Path.Combine(work,"settings.json");var store=new JsonApplicationSettingsStore(path);await store.SaveAsync(new());
        if(flaw=="invalid-json")File.WriteAllText(path,"{");else {var doc=Read(path);if(flaw=="schema")doc["schemaVersion"]=2;else doc["settings"]!["cpuThreads"]=0;Write(path,doc);}
        var before=File.ReadAllBytes(path);Assert.NotNull(await Record.ExceptionAsync(()=>store.LoadAsync()));Assert.Equal(before,File.ReadAllBytes(path));
    }
    [Fact]
    public async Task SettingsCanceladosNoCambianBytesYJsonConcurrenteNoQuedaParcial()
    {
        var path=Path.Combine(work,"settings.json");var store=new JsonApplicationSettingsStore(path);await store.SaveAsync(new(language:"en",cpuThreads:2));var before=File.ReadAllBytes(path);
        using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.SaveAsync(new(language:"es"),cts.Token));Assert.Equal(before,File.ReadAllBytes(path));
        await Task.WhenAll(Enumerable.Range(1,10).Select(i=>store.SaveAsync(new(language:i%2==0?"en":"es",cpuThreads:i))));
        var settings=await store.LoadAsync();Assert.InRange(settings.CpuThreads!.Value,1,10);Assert.Contains(settings.Language,new[]{"en","es"});
    }
    private sealed class FixedClock : TimeProvider
    {public override DateTimeOffset GetUtcNow()=>new(2026,10,3,20,0,0,TimeSpan.Zero);}
}

using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class IsolationAndLockTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task LockOcupadoRespetaCancelacionOTiempoLimiteSinModificarEstado(bool cancel)
    {
        var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());var path=Path.Combine(run.RunDirectory,"run.json");var before=File.ReadAllBytes(path);
        using var held=File.Open(Path.Combine(run.RunDirectory,"run.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var cts=new CancellationTokenSource();if(cancel)cts.CancelAfter(TimeSpan.FromMilliseconds(150));
        var error=await Record.ExceptionAsync(()=>s.Store.TransitionAsync(run,new(RunStatus.Created,RunStatus.PreparingAudio,DateTimeOffset.UtcNow),cts.Token));
        if(cancel)Assert.IsAssignableFrom<OperationCanceledException>(error);else Assert.Equal(OperationErrorCode.RunStateConflict,Assert.IsType<CaptionForgeOperationException>(error).Code);
        Assert.Equal(before,File.ReadAllBytes(path));
    }
    [Fact]
    public async Task TimelineSecundariaNoCambiaPrincipalRegistroNiEspejo()
    {
        var s=await SessionAsync();var secondId="SECOND-TIMELINE";var secondDir=Path.Combine(s.Project.DirectoryPath,"Timelines",secondId);Directory.CreateDirectory(secondDir);
        var draft=Read(s.Draft);draft["id"]=secondId;Write(Path.Combine(secondDir,"draft_content.json"),draft);
        var registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");var registry=Read(registryPath);var entry=registry["timelines"]![0]!.DeepClone();entry["id"]=secondId;entry["name"]="Second timeline";registry["timelines"]!.AsArray().Add(entry);Write(registryPath,registry);
        var beforeRoot=File.ReadAllBytes(s.RootDraft);var beforeRegistry=File.ReadAllBytes(registryPath);
        var timelines=await s.Catalog.GetTimelinesAsync(s.Project);Assert.Equal(2,timelines.Count);
        var selected=Assert.Single(timelines,t=>t.Id==secondId);Assert.False(selected.IsMain);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,selected);var result=await s.GenerateAsync();await s.Service.ApplyAsync(result);
        Assert.Equal(2,result.Plan.ExpectedFiles.Count);Assert.Equal(beforeRoot,File.ReadAllBytes(s.RootDraft));Assert.Equal(beforeRegistry,File.ReadAllBytes(registryPath));
        Assert.Equal(File.ReadAllBytes(selected.DraftContentPath),File.ReadAllBytes(selected.DraftContentPath+".bak"));
        Assert.Null(await s.Store.ReadManagedSubtitlesAsync(s.Project.Id,"EFA8ACC8-F181-4059-96C5-6BC82353A0C6"));
    }
}

using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Infrastructure.CapCut;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class AssetAndOwnershipEdgeTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData("empty-template")] [InlineData("missing-template")]
    [InlineData("empty-effect")] [InlineData("missing-animation")] [InlineData("missing-font")]
    public async Task AssetsAusentesOVaciosNoSeAceptan(string flaw)
    {
        var s=await SessionAsync();var a=s.Assets;
        switch(flaw)
        {
            case "empty-template":foreach(var p in Directory.GetFiles(a.TemplateDirectory))File.Delete(p);break;
            case "missing-template":Directory.Delete(a.TemplateDirectory,true);break;
            case "empty-effect":foreach(var p in Directory.GetFiles(a.EffectDirectory))File.Delete(p);break;
            case "missing-animation":Directory.Delete(a.AnimationDirectory,true);break;
            case "missing-font":File.Delete(a.FontFile);break;
        }
        Assert.Equal(OperationErrorCode.ResourceUnavailable,Assert.Throws<CaptionForgeOperationException>(()=>a.Validate()).Code);
    }
    [Fact]
    public async Task ResolverNoEscogeArbitrariamenteEntreHashesAlternativos()
    {
        var s=await SessionAsync();var parent=Directory.GetParent(s.Assets.TemplateDirectory)!.FullName;Directory.Delete(s.Assets.TemplateDirectory,true);
        foreach(var hash in new[]{"hash-one","hash-two"}){var p=Path.Combine(parent,hash);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"asset.txt"),"test");}
        Assert.Equal(OperationErrorCode.ResourceUnavailable,Assert.Throws<CaptionForgeOperationException>(()=>CapCutTemplateAssetResolver.Resolve(Path.Combine(s.Directory,"cache"))).Code);
    }
    [Fact]
    public async Task ResolverUsaAlternativaUnicaCuandoNoEstaElHashConocido()
    {
        var s=await SessionAsync();var destination=Path.Combine(Directory.GetParent(s.Assets.TemplateDirectory)!.FullName,"single-alternative");Directory.Move(s.Assets.TemplateDirectory,destination);
        Assert.Equal(destination,CapCutTemplateAssetResolver.Resolve(Path.Combine(s.Directory,"cache")).TemplateDirectory);
    }
    [Fact]
    public async Task FuenteDesaparecidaDespuesDePrepararNoCambiaOriginales()
    {
        var s=await SessionAsync();var r=await s.GenerateAsync();File.Delete(s.Assets.FontFile);var before=File.ReadAllBytes(s.Draft);
        var ex=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>s.Service.ApplyAsync(r));Assert.Equal(OperationErrorCode.ResourceUnavailable,ex.Code);
        Assert.Equal(before,File.ReadAllBytes(s.Draft));Assert.Equal("ReadyToApply",s.Status());
    }
    [Fact]
    public async Task NumeroDeCaptionsCambiaSinDuplicarPistaNiPerderIdsRetenidos()
    {
        var s=await SessionAsync();var original=await s.GenerateAsync();await s.Service.ApplyAsync(original);
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);s.Transcriber.CaptionLimit=5;var shorter=await s.GenerateAsync();await s.Service.ApplyAsync(shorter);
        Assert.Equal(5,shorter.Captions.Count);Assert.Equal(31,shorter.Plan.ManagedSubtitles.Objects.Count);
        Assert.All(shorter.Plan.ManagedSubtitles.Objects,o=>Assert.Contains(o,original.Plan.ManagedSubtitles.Objects));
        var removed=original.Plan.ManagedSubtitles.Objects.Except(shorter.Plan.ManagedSubtitles.Objects).Select(o=>o.Id).ToArray();var json=File.ReadAllText(s.Draft);
        Assert.All(removed,id=>Assert.DoesNotContain(id,json,StringComparison.Ordinal));
        s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);s.Transcriber.CaptionLimit=null;var longer=await s.GenerateAsync();await s.Service.ApplyAsync(longer);
        Assert.Equal(15,longer.Captions.Count);Assert.All(shorter.Plan.ManagedSubtitles.Objects,o=>Assert.Contains(o,longer.Plan.ManagedSubtitles.Objects));
        Assert.Single(Read(s.Draft)["tracks"]!.AsArray(),t=>t!["type"]!.GetValue<string>()=="text");
    }
    [Fact]
    public async Task DosPlanesConLaMismaCapturaNoPuedenAplicarseSobreUnEstadoYaCambiado()
    {
        var s=await SessionAsync();var first=await s.GenerateAsync();var second=await s.GenerateAsync();await s.Service.ApplyAsync(first);var before=File.ReadAllBytes(s.Draft);
        var ex=await Assert.ThrowsAsync<CaptionForgeOperationException>(()=>s.Service.ApplyAsync(second));Assert.Equal(OperationErrorCode.SourceChanged,ex.Code);
        Assert.Equal(before,File.ReadAllBytes(s.Draft));Assert.Equal(first.Plan.ManagedSubtitles.Objects,(await s.Store.ReadManagedSubtitlesAsync(s.Project.Id,s.Snapshot.Timeline.Id))!.Objects);
    }
    [Fact]
    public async Task ObjetoDuplicadoEnJsonSeRechazaSinEscribir()
    {
        var s=await SessionAsync();var d=Read(s.Draft);var audio=d["materials"]!["audios"]![0]!;var dup=new JsonObject{["id"]=audio["id"]!.GetValue<string>(),["unknown"]=true};
        d["materials"]!["effects"]!.AsArray().Add(dup);Write(s.Draft,d);Write(s.RootDraft,d);s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        var before=File.ReadAllBytes(s.Draft);await Assert.ThrowsAsync<InvalidDataException>(()=>s.GenerateAsync());Assert.Equal(before,File.ReadAllBytes(s.Draft));
    }
}

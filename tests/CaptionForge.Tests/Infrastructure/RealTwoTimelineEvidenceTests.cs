using CaptionForge.Core.Enums;
using Xunit;
using Xunit.Abstractions;

namespace CaptionForge.Tests.Infrastructure;

// Evidencia real del lote recibido; la atribución a raíz sigue el orden de archivos del usuario.
public sealed class RealTwoTimelineEvidenceTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EscrituraYRestauracionAislanAmbasTimelinesDelLoteReal(bool main)
    {
        var s=await SessionAsync();var upload=Path.Combine(fixturePath,"two_timelines");
        string a="EFA8ACC8-F181-4059-96C5-6BC82353A0C6",b="CE87A6ED-3213-4b2e-8BB0-9ADF9530E11E";
        var mapping=new Dictionary<string,string>
        {
            ["draft_content(10).json"]=s.RootDraft,
            ["draft_content.json.bak"]=s.RootDraft+".bak",
            ["project(1).json"]=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json"),
            ["project.json.bak"]=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json.bak"),
            ["draft_content(20261003-231031).json"]=Path.Combine(s.Project.DirectoryPath,"Timelines",a,"draft_content.json"),
            ["draft_content.json(2).bak"]=Path.Combine(s.Project.DirectoryPath,"Timelines",a,"draft_content.json.bak"),
            ["draft_content(20261003-231018).json"]=Path.Combine(s.Project.DirectoryPath,"Timelines",b,"draft_content.json"),
            ["draft_content.json(1).bak"]=Path.Combine(s.Project.DirectoryPath,"Timelines",b,"draft_content.json.bak")
        };
        foreach(var file in mapping){Directory.CreateDirectory(Path.GetDirectoryName(file.Value)!);File.Copy(Path.Combine(upload,file.Key),file.Value,true);}
        var before=mapping.Values.ToDictionary(p=>p,p=>File.ReadAllBytes(p));
        var timelines=await s.Catalog.GetTimelinesAsync(s.Project);Assert.Equal(2,timelines.Count);Assert.Equal(a,Assert.Single(timelines,t=>t.IsMain).Id);
        var selected=Assert.Single(timelines,t=>t.Id==(main?a:b));s.Snapshot=await s.Catalog.ReadTimelineAsync(s.Project,selected);
        Assert.Single(s.Snapshot.Tracks.Where(t=>t.Type==MediaTrackType.Audio).SelectMany(t=>t.Segments));
        var result=await s.GenerateAsync();Assert.Equal(main?4:2,result.Plan.ExpectedFiles.Count);
        var receipt=await s.Service.ApplyAsync(result);
        foreach(var file in before)
        {
            bool touched=receipt.Files.Any(f=>f.Before.Path==file.Key);
            if(touched)Assert.NotEqual(file.Value,File.ReadAllBytes(file.Key));
            else Assert.Equal(file.Value,File.ReadAllBytes(file.Key));
        }
        Assert.Equal(File.ReadAllBytes(selected.DraftContentPath),File.ReadAllBytes(selected.DraftContentPath+".bak"));
        if(main)Assert.Equal(File.ReadAllBytes(s.RootDraft),File.ReadAllBytes(selected.DraftContentPath));
        await s.Writer.RestoreAsync(receipt.JournalPath);
        foreach(var file in before)Assert.Equal(file.Value,File.ReadAllBytes(file.Key));
    }
}

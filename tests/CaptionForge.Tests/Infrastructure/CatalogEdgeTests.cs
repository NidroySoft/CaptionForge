using System.Text;
using System.Text.Json.Nodes;
using CaptionForge.Core.ValueObjects;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class CatalogEdgeTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData("deleted")] [InlineData("id-mismatch")] [InlineData("missing-draft")]
    [InlineData("corrupt-draft")] [InlineData("zero-fps")] [InlineData("negative-fps")]
    [InlineData("high-fps")] [InlineData("precision-fps")] [InlineData("negative-duration")]
    public async Task TimelineNoValidaNoSeOfrece(string scenario)
    {
        var s=await SessionAsync();var registryPath=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var registry=Read(registryPath);var draft=Read(s.Draft);
        switch(scenario)
        {
            case "deleted":registry["timelines"]![0]!["is_marked_delete"]=true;Write(registryPath,registry);break;
            case "id-mismatch":draft["id"]="different";break;
            case "missing-draft":File.Delete(s.Draft);break;
            case "corrupt-draft":File.WriteAllText(s.Draft,"{");break;
            case "zero-fps":draft["fps"]=0;break;
            case "negative-fps":draft["fps"]=-30;break;
            case "high-fps":draft["fps"]=1001;break;
            case "precision-fps":draft["fps"]=29.9700001m;break;
            case "negative-duration":draft["duration"]=-1;break;
        }
        if(scenario is not ("deleted" or "missing-draft" or "corrupt-draft"))Write(s.Draft,draft);
        Assert.Empty(await s.Catalog.GetTimelinesAsync(s.Project));
        if(scenario!="deleted")Assert.NotEmpty(s.Catalog.LastWarnings);
    }
    [Fact]
    public async Task RegistroConTimelineDuplicadaSeRechaza()
    {
        var s=await SessionAsync();var path=Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");var doc=Read(path);
        doc["timelines"]!.AsArray().Add(doc["timelines"]![0]!.DeepClone());Write(path,doc);
        await Assert.ThrowsAsync<InvalidDataException>(()=>s.Catalog.GetTimelinesAsync(s.Project));
    }
    [Theory]
    [InlineData("unresolved")] [InlineData("empty-path")] [InlineData("compound")]
    [InlineData("negative-source")] [InlineData("speed-mismatch")]
    public async Task SegmentoNoInterpretableSeOmiteConDiagnostico(string scenario)
    {
        var s=await SessionAsync();var doc=Read(s.Draft);var segment=doc["tracks"]![1]!["segments"]![0]!;
        var audio=doc["materials"]!["audios"]![0]!;
        switch(scenario)
        {
            case "unresolved":segment["material_id"]="missing-material";break;
            case "empty-path":audio["path"]="";break;
            case "compound":audio["type"]="combination";break;
            case "negative-source":segment["source_timerange"]!["start"]=-1;break;
            case "speed-mismatch":
                var refs=segment["extra_material_refs"]!.AsArray();refs.Add("edge-speed");
                doc["materials"]!["speeds"]!.AsArray().Add(new JsonObject{["id"]="edge-speed",["speed"]=2,["mode"]=0});break;
        }
        Write(s.Draft,doc);var snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        Assert.Empty(snapshot.Tracks[1].Segments);Assert.NotEmpty(s.Catalog.LastWarnings);
    }
    [Fact]
    public async Task DistingueRecortesYDestinoDeVariosArchivos()
    {
        var s=await SessionAsync();var doc=Read(s.Draft);var audio=doc["materials"]!["audios"]![0]!.DeepClone();audio["id"]="second-material";audio["path"]="other.wav";
        doc["materials"]!["audios"]!.AsArray().Add(audio);
        var segment=doc["tracks"]![1]!["segments"]![0]!;segment["source_timerange"]!["start"]=5_000_000;segment["source_timerange"]!["duration"]=1_000_000;
        segment["target_timerange"]!["start"]=10_000_000;segment["target_timerange"]!["duration"]=1_000_000;
        var second=segment.DeepClone();second["id"]="second-segment";second["material_id"]="second-material";second["source_timerange"]!["start"]=1_000_000;second["target_timerange"]!["start"]=20_000_000;
        doc["tracks"]![1]!["segments"]!.AsArray().Add(second);Write(s.Draft,doc);
        var snapshot=await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline);
        Assert.Equal(2,snapshot.Tracks[1].Segments.Count);Assert.Equal("other.wav",snapshot.Tracks[1].Segments[1].SourcePath);
        Assert.Equal(5_000_000,snapshot.Tracks[1].Segments[0].SourceRange.StartUs);Assert.Equal(20_000_000,snapshot.Tracks[1].Segments[1].TargetRange.StartUs);
    }
    [Fact]
    public async Task BomUtf8YCamposDesconocidosSeLeen()
    {
        var s=await SessionAsync();var doc=Read(s.Draft);doc["future_data"]=new JsonObject{["caption"]="mañana 中文"};
        File.WriteAllText(s.Draft,doc.ToJsonString(),new UTF8Encoding(true));
        Assert.Equal(30m,(await s.Catalog.ReadTimelineAsync(s.Project,s.Snapshot.Timeline)).Timeline.FrameRate.FramesPerSecond);
    }
    [Fact]
    public async Task FpsDecimalNoSeConvierteSilenciosamenteEnBroadcast()
    {
        var s=await SessionAsync();var doc=Read(s.Draft);doc["fps"]=29.97m;Write(s.Draft,doc);
        Assert.Equal(new FrameRate(2997,100),(await s.Catalog.GetTimelinesAsync(s.Project)).Single().FrameRate);
    }
    [Theory]
    [InlineData("project")] [InlineData("registry")]
    public async Task CambioDeIdentidadDelCatalogoSeRechaza(string identity)
    {
        var s=await SessionAsync();var path=identity=="project"?Path.Combine(s.Project.DirectoryPath,"draft_meta_info.json"):Path.Combine(s.Project.DirectoryPath,"Timelines","project.json");
        var doc=Read(path);doc[identity=="project"?"draft_id":"id"]="different";Write(path,doc);
        await Assert.ThrowsAsync<InvalidDataException>(()=>s.Catalog.GetTimelinesAsync(s.Project));
    }
}

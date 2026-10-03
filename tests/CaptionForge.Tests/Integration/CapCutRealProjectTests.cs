using System.Security.Cryptography;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Services;
using CaptionForge.Core.Enums;
using CaptionForge.Infrastructure.CapCut;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Infrastructure.Workspace;
using CaptionForge.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace CaptionForge.Tests.Integration;

[Collection("External engines")]
public sealed class CapCutRealProjectTests(ITestOutputHelper output)
{
    [CapCutProjectFact]
    public async Task ProyectoRealSeProcesaEnCopiaYAparecenBakYRestauracionExacta()
    {
        string original=Path.GetFullPath(Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_CAPCUT_PROJECT")!);
        string work=Path.Combine(Path.GetTempPath(),"CaptionForge.Tests.RealProject",Guid.NewGuid().ToString("N"));
        string projectCopy=Path.Combine(work,"capcut","projects","project-copy");Directory.CreateDirectory(projectCopy);
        var sourceHashes=new Dictionary<string,string>();
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        void Copy(string source,string target)
        {
            if(!File.Exists(source))return;
            for(string? ancestor=Path.GetFullPath(source);ancestor is not null;ancestor=Path.GetDirectoryName(ancestor))
                Assert.False((File.GetAttributes(ancestor)&FileAttributes.ReparsePoint)!=0,"Usa rutas físicas para el proyecto de prueba.");
            sourceHashes.Add(source,Hash(source));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target);
        }
        try
        {
            foreach(var name in new[]{"draft_meta_info.json","draft_content.json","draft_content.json.bak","draft_cover.jpg"})Copy(Path.Combine(original,name),Path.Combine(projectCopy,name));
            Copy(Path.Combine(original,"Timelines","project.json"),Path.Combine(projectCopy,"Timelines","project.json"));
            foreach(var directory in Directory.GetDirectories(Path.Combine(original,"Timelines")))
                foreach(var name in new[]{"draft_content.json","draft_content.json.bak","draft_cover.jpg"})
                    Copy(Path.Combine(directory,name),Path.Combine(projectCopy,"Timelines",Path.GetFileName(directory),name));
            var catalog=new CapCutCatalog();var project=Assert.Single(await catalog.FindProjectsAsync(Path.GetDirectoryName(projectCopy)!));
            var timelines=await catalog.GetTimelinesAsync(project);string? selected=Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_TIMELINE");
            var timeline=selected is null?Assert.Single(timelines):Assert.Single(timelines,t=>t.Id==selected);
            var snapshot=await catalog.ReadTimelineAsync(project,timeline);
            var selectable=snapshot.Tracks.Where(t=>t.Type is MediaTrackType.Audio or MediaTrackType.Video).SelectMany(t=>t.Segments).ToArray();
            string? selectedSegment=Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_SEGMENT");
            var segment=selectedSegment is null?Assert.Single(selectable):Assert.Single(selectable,s=>s.Id==selectedSegment);
            Assert.True(File.Exists(segment.SourcePath),"Falta el medio real. Debe estar en la ruta registrada en CapCut para esta prueba.");
            var assets=CapCutTemplateAssetResolver.Resolve(Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_CAPCUT_USER_DATA"));
            var store=new JsonWorkspaceStore(Path.Combine(work,"workspace"));var writer=new GoldenV3SubtitleWriter(assets);
            await using var whisper=new WhisperNetTranscriptionService();var service=new CaptionGenerationService(new FfmpegAudioPreparationService(),whisper,writer,store);
            var language=Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_LANGUAGE")??"en";
            var options=new TranscriptionOptions("real-project-test",Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_MODEL")!,language,Math.Min(2,Environment.ProcessorCount),Environment.ProcessorCount);
            var request=new GenerateCaptionsRequest(snapshot,new[]{segment.Id},options,store.RootDirectory);
            var before=snapshot.SourceFiles.ToDictionary(f=>f.Path,f=>f.Exists?File.ReadAllBytes(f.Path):null);
            var result=await service.GenerateAsync(request);Assert.NotEmpty(result.Captions);
            var receipt=await service.ApplyAsync(result);Assert.Equal(File.ReadAllBytes(timeline.DraftContentPath),File.ReadAllBytes(timeline.DraftContentPath+".bak"));
            await writer.RestoreAsync(receipt.JournalPath);
            foreach(var file in before)
                if(file.Value is null)Assert.False(File.Exists(file.Key));else Assert.Equal(file.Value,File.ReadAllBytes(file.Key));
            output.WriteLine($"Proyecto real {project.Id}, timeline {timeline.Id}, fragmento {segment.Id}: {result.Captions.Count} captions; commit/restauración en copia temporal.");
            foreach(var hash in sourceHashes)Assert.Equal(hash.Value,Hash(hash.Key));
        }
        finally
        {
            try{Directory.Delete(work,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
public sealed class CapCutProjectFactAttribute : FactAttribute
{
    public CapCutProjectFactAttribute()
    {
        var project=Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_CAPCUT_PROJECT");
        if(!Directory.Exists(project)||!File.Exists(EngineConfiguration.ModelPath))Skip="Configura CAPTIONFORGE_TEST_CAPCUT_PROJECT y CAPTIONFORGE_TEST_MODEL para probar una copia de tu proyecto real.";
        else if(!EngineConfiguration.FfmpegAvailable)Skip="Requiere ffmpeg y ffprobe en PATH.";
    }
}

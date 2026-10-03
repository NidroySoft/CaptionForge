using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.CapCut;
using CaptionForge.Infrastructure.Configuration;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Settings;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Infrastructure.Workspace;
using Whisper.net;

using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

sealed class ClosedGuard : ICapCutProcessGuard {public void EnsureClosed() {}}
sealed class OpenGuard : ICapCutProcessGuard {public void EnsureClosed()=>throw new CaptionForgeOperationException(OperationErrorCode.CapCutOpen,"test process open");}
sealed class DelayedCompletionStore(JsonWorkspaceStore store) : IWorkspaceStore
{
 public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public Task<RunContext> CreateRunAsync(GenerateCaptionsRequest r,CancellationToken ct=default)=>store.CreateRunAsync(r,ct);
 public Task<ManagedSubtitleSet?> ReadManagedSubtitlesAsync(string p,string t,CancellationToken ct=default)=>store.ReadManagedSubtitlesAsync(p,t,ct);
 public Task SaveGenerationAsync(GenerationResult r,CancellationToken ct=default)=>store.SaveGenerationAsync(r,ct);
 public Task TransitionAsync(RunContext r,RunUpdate u,CancellationToken ct=default)=>store.TransitionAsync(r,u,ct);
 public async Task CompleteAsync(GenerationResult r,SubtitleApplyResult applied,DateTimeOffset at,CancellationToken ct=default)
 {Entered.SetResult();await Release.Task;await store.CompleteAsync(r,applied,at,ct);}
}
sealed class FailCommitter(int failAt) : IFileCommitter
{private int _count;public Task ReplaceAsync(string path,byte[] bytes) {if(++_count==failAt)throw new IOException("injected partial failure");return new AtomicFileCommitter().ReplaceAsync(path,bytes);}}
sealed class BreakRollbackCommitter(string testRoot) : IFileCommitter
{
 private int _count;
 public async Task ReplaceAsync(string path,byte[] bytes)
 {
  if(++_count==1) {await new AtomicFileCommitter().ReplaceAsync(path,bytes);return;}
  // Busca el backup propio del test y lo daña para verificar journal recuperable sin modificar otros proyectos.
  foreach(string backup in Directory.GetFiles(testRoot,"000.bin",SearchOption.AllDirectories))File.WriteAllText(backup,"damaged backup");
  throw new IOException("injected rollback failure");
 }
}
sealed class FixtureAudio : IAudioPreparationService
{
 public Task<PreparedAudio> PrepareAsync(AudioPreparationRequest request,CancellationToken cancellationToken=default)
 {
  string path=Path.Combine(request.Run.RunDirectory,"audio","fixture.wav");int frames=checked((int)Math.Round((decimal)request.RequiredDurationUs*16000/1_000_000,MidpointRounding.AwayFromZero));
  using(var stream=File.Create(path)) using(var w=new BinaryWriter(stream,Encoding.ASCII))
  {w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+frames*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(16000);w.Write(32000);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(frames*2);w.Write(new byte[frames*2]);}
  return Task.FromResult(new PreparedAudio(request.Segment.Id,path,request.RequiredDurationUs,PcmWaveInfo.Read(path).DurationUs));
 }
}
sealed class FixtureTranscription(string fixtureDirectory) : ITranscriptionService
{
 public bool ChangeFirstWord {get;set;}
 public int? CaptionLimit {get;set;}
 public Task<TranscriptionResult> TranscribeAsync(RunContext run,PreparedAudio audio,TranscriptionOptions options,CancellationToken cancellationToken=default)
 {
  using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureDirectory,"whisper_dtw.json")));var phrases=new List<TranscriptionSegment>();int index=0;
  foreach(var e in data.RootElement.GetProperty("transcription").EnumerateArray().Take(CaptionLimit ?? int.MaxValue))
  {
   var offsets=e.GetProperty("offsets");long from=offsets.GetProperty("from").GetInt64(),to=offsets.GetProperty("to").GetInt64();var tokens=new List<WhisperToken>();
   foreach(var token in e.GetProperty("tokens").EnumerateArray())
   {
    string value=token.GetProperty("text").GetString()!;if(index==9 && value==" words")value="words";if(index==14 && value==",")value=".";if(index==14 && value==" math")value=" Math";
    if(index==0 && ChangeFirstWord && value==" This")value=" That";var o=token.GetProperty("offsets");
    tokens.Add(new WhisperToken {Id=token.GetProperty("id").GetInt32(),Text=value,Start=o.GetProperty("from").GetInt64()/10,End=o.GetProperty("to").GetInt64()/10,DtwTimestamp=token.GetProperty("t_dtw").GetInt64()});
   }
   var native=new SegmentData(e.GetProperty("text").GetString()!,TimeSpan.FromMilliseconds(from),TimeSpan.FromMilliseconds(to),0,0,0,0,"en",tokens.ToArray());
   phrases.Add(WhisperTokenNormalizer.Normalize(native,true));index++;
  }
  return Task.FromResult(new TranscriptionResult(audio.SourceSegmentId,options.ModelName,"en",audio.DurationUs,phrases));
 }
}
sealed class Session
{
 public required string Directory {get;init;}
 public required string Draft {get;init;}
 public required string RootDraft {get;init;}
 public required CaptionForge.Core.Models.CapCut.CapCutProject Project {get;init;}
 public required CaptionForge.Application.Models.CapCut.TimelineSnapshot Snapshot {get;set;}
 public required CapCutCatalog Catalog {get;init;}
 public required TemplateAssetPaths Assets {get;init;}
 public required JsonWorkspaceStore Store {get;init;}
 public required FixtureTranscription Transcriber {get;init;}
 public required GoldenV3SubtitleWriter Writer {get;set;}
 public CaptionGenerationService Service => new(new FixtureAudio(),Transcriber,Writer,Store);
 public string[] Files => new[] {Draft,Draft+".bak",RootDraft,RootDraft+".bak"};
 public GenerateCaptionsRequest Request()=>new(Snapshot,new[] {Snapshot.Tracks.Single(t=>t.Segments.Count>0 && t.Type==CaptionForge.Core.Enums.MediaTrackType.Audio).Segments[0].Id},new("medium.en","fixture-model.bin","en",6,8),Store.RootDirectory);
 public Task<GenerationResult> GenerateAsync()=>Service.GenerateAsync(Request());
 public string Status()=>ReadLocal(Path.Combine(Directory,"workspace",Project.Id,"runs",System.IO.Directory.GetDirectories(Path.Combine(Directory,"workspace",Project.Id,"runs")).Select(Path.GetFileName).Last()!,"run.json"))["status"]!.GetValue<string>();
 public void SetWriter(GoldenV3SubtitleWriter writer)=>Writer=writer;
 private static JsonObject ReadLocal(string path)=>JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
 public static async Task<Session> CreateAsync(string root,string fixtures,string fixture,string? rootFixture)
 {
  string directory=Path.Combine(root,Guid.NewGuid().ToString("N")),projects=Path.Combine(directory,"capcut","projects"),project=Path.Combine(projects,"1003"),tid="EFA8ACC8-F181-4059-96C5-6BC82353A0C6";
  string timeline=Path.Combine(project,"Timelines",tid);System.IO.Directory.CreateDirectory(timeline);
  File.Copy(Path.Combine(fixtures,"draft_meta_info.json"),Path.Combine(project,"draft_meta_info.json"));File.Copy(Path.Combine(fixtures,"timelines_project.json"),Path.Combine(project,"Timelines","project.json"));
  File.Copy(Path.Combine(fixtures,fixture),Path.Combine(timeline,"draft_content.json"));File.Copy(Path.Combine(fixtures,rootFixture ?? fixture),Path.Combine(project,"draft_content.json"));
  File.WriteAllText(Path.Combine(project,"draft_cover.jpg"),"test cover");File.WriteAllText(Path.Combine(timeline,"draft_cover.jpg"),"test cover");
  string cache=Path.Combine(directory,"cache","Cache","effect");string template=Path.Combine(cache,"7535399757947161873","ae92e3cb3480090dfea046d1283495cf"),effect=Path.Combine(cache,"239386920","b7bae96650ebe1f0fbdf8825910dd311"),animation=Path.Combine(cache,"110456508","d62a12a386bf578a8eb03187095d7c7d"),font=Path.Combine(cache,"7517426090072149264","font-hash","BebasNeue-Regular.ttf");
  foreach(string d in new[] {template,effect,animation,Path.GetDirectoryName(font)!}) {System.IO.Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"fixture-resource.txt"),"test visual asset");}File.WriteAllText(font,"test font");
  var assets=CapCutTemplateAssetResolver.Resolve(Path.Combine(directory,"cache"));var catalog=new CapCutCatalog();var p=(await catalog.FindProjectsAsync(projects)).Single();var t=(await catalog.GetTimelinesAsync(p)).Single();
  return new Session {Directory=directory,Draft=t.DraftContentPath,RootDraft=Path.Combine(project,"draft_content.json"),Project=p,Snapshot=await catalog.ReadTimelineAsync(p,t),Catalog=catalog,Assets=assets,Store=new JsonWorkspaceStore(Path.Combine(directory,"workspace")),Transcriber=new(fixtures),Writer=new(assets,new ClosedGuard())};
 }
}

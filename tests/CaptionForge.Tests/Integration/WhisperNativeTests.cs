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

[Collection("External engines")]
public sealed class WhisperNativeTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [WhisperFact]
    public async Task VozRealConDtwGeneraJsonV3YAutoEnReutilizaLaFabrica()
    {
        var audioService = new FfmpegAudioPreparationService();
 var nativeSession=await SessionAsync();var draft=Read(nativeSession.Draft);var seg=draft["tracks"]![1]!["segments"]![0]!;
 seg["source_timerange"]=new JsonObject {["start"]=0,["duration"]=11_000_000};seg["target_timerange"]=new JsonObject {["start"]=20_000_000,["duration"]=11_000_000};
 Write(nativeSession.Draft,draft);Write(nativeSession.RootDraft,draft);nativeSession.Snapshot=await nativeSession.Catalog.ReadTimelineAsync(nativeSession.Project,nativeSession.Snapshot.Timeline);
 await using var whisper=new WhisperNetTranscriptionService();var service=new CaptionGenerationService(audioService,whisper,nativeSession.Writer,nativeSession.Store);
 string sourceId=nativeSession.Snapshot.Tracks[1].Segments[0].Id;var options=new TranscriptionOptions("native-check",Path.GetFullPath(EngineConfiguration.ModelPath!),"en",Math.Min(2,Environment.ProcessorCount),Environment.ProcessorCount);
 var req=new GenerateCaptionsRequest(nativeSession.Snapshot,new[] {sourceId},options,nativeSession.Store.RootDirectory,new[] {new SourcePathOverride(sourceId,Path.GetFullPath(EngineConfiguration.VoicePath!))});
 var watch=Stopwatch.StartNew();var nativeResult=await service.GenerateAsync(req);watch.Stop();
 tests.Check(nativeResult.Captions.Count>0 && nativeResult.Transcriptions.Single().Segments.Count>0,"Whisper.net CPU+DTW real reconoce voz sin CLI");
 tests.Check(nativeResult.Captions.All(c=>c.TimelineRange.StartUs>=20_000_000 && c.TimelineRange.EndUs<=31_000_000),"Pipeline nativo traslada captions al destino 20s y los limita");
 var nativeReceipt=await service.ApplyAsync(nativeResult);tests.Check(nativeReceipt.Files.Count==4,"Pipeline completo nativo aplica JSON v3 a copias");
 string nativeWave=Directory.GetFiles(Path.Combine(nativeResult.Run.RunDirectory,"audio"),"*.wav").Single();
 var automatic=await whisper.TranscribeAsync(nativeResult.Run,new PreparedAudio(sourceId,nativeWave,11_000_000,PcmWaveInfo.Read(nativeWave).DurationUs),new TranscriptionOptions("native-check",Path.GetFullPath(EngineConfiguration.ModelPath!),"auto",Math.Min(2,Environment.ProcessorCount),Environment.ProcessorCount));
 tests.Check(automatic.Language=="en" && automatic.Segments.Count>0,"Whisper.net real usa idioma inglés del modelo .en con auto y reutiliza la fábrica");

    }

}

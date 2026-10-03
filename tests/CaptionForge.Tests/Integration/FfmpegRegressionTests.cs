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
public sealed class FfmpegRegressionTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [FfmpegFact]
    public async Task FfmpegRecortaRellenaYExtraeAudioDeVideo()
    {
        // Audio real con FFmpeg: recorte, padding pequeño, vídeo con audio y vídeo sin audio.
        string source=Path.Combine(work,"source with spaces.wav");WriteWave(source,32_000,880);
        var audioSession=await SessionAsync();var audioRun=await audioSession.Store.CreateRunAsync(audioSession.Request());
        var audioService=new FfmpegAudioPreparationService();
        var audioSegment=new MediaSegment("audio-slice","track","material",source,new(1_000_000,500_000),new(10_000_000,500_000));
        var prepared=await audioService.PrepareAsync(new(audioRun,audioSegment,source));var info=PcmWaveInfo.Read(prepared.Path);
        tests.Check(info.SampleFrames==8000 && prepared.DurationUs==500_000 && prepared.MeasuredDurationUs==500_000,"FFmpeg real recorta a 8000 muestras mono16k");
        tests.Check(File.Exists(Path.ChangeExtension(prepared.Path,null)+"-mapping.json"),"Mapa temporal de audio persistido");
        var padding=new MediaSegment("padding","track","material",source,new(0,2_008_333),new(0,2_008_333));var padded=await audioService.PrepareAsync(new(audioRun,padding,source));
        tests.Check(Math.Abs(padded.MeasuredDurationUs-padded.DurationUs)<=63,"FFmpeg resuelve pequeño desfase con padding/rejilla de muestras");
        var video=Path.Combine(work,"video with audio.mp4");await RunFfmpegAsync(new[] {"-f","lavfi","-i","color=c=black:s=64x64:r=25:d=2","-i",source,"-shortest","-c:v","mpeg4","-c:a","aac","-y",video});
        var videoSegment=new MediaSegment("video-slice","track","material",video,new(500_000,1_000_000),new(0,1_000_000));var videoAudio=await audioService.PrepareAsync(new(audioRun,videoSegment,video));
        tests.Check(PcmWaveInfo.Read(videoAudio.Path).SampleFrames==16000,"FFmpeg extrae audio de vídeo y respeta recorte");
        var silentVideo=Path.Combine(work,"silent.mp4");await RunFfmpegAsync(new[] {"-f","lavfi","-i","color=c=black:s=64x64:r=25:d=1","-an","-c:v","mpeg4","-y",silentVideo});
        await tests.ErrorAsync(()=>audioService.PrepareAsync(new(audioRun,new MediaSegment("silent","t","m",silentVideo,new(0,1_000_000),new(0,1_000_000)),silentVideo)),OperationErrorCode.UnsupportedMedia,"Vídeo sin audio diagnosticado");
        using var cancelAudio=new CancellationTokenSource();cancelAudio.Cancel();await tests.ThrowsAsync<OperationCanceledException>(()=>audioService.PrepareAsync(new(audioRun,audioSegment,source),cancelAudio.Token),"Cancelación de preparación antes de ejecutar proceso");
        await tests.ErrorAsync(()=>new FfmpegAudioPreparationService("missing-ffmpeg","missing-ffprobe").PrepareAsync(new(audioRun,audioSegment,source)),OperationErrorCode.ResourceUnavailable,"Ejecutables ausentes diagnosticados");
        
    }

}

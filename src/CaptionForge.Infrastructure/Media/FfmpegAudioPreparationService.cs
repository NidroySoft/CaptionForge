using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Media;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.Media;

public sealed class FfmpegAudioPreparationService : IAudioPreparationService
{
    private readonly string _ffmpeg,_ffprobe;
    public FfmpegAudioPreparationService(string ffmpegPath="ffmpeg",string ffprobePath="ffprobe")
    { ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);_ffmpeg=ffmpegPath;_ffprobe=ffprobePath; }
    public async Task<PreparedAudio> PrepareAsync(AudioPreparationRequest request,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);cancellationToken.ThrowIfCancellationRequested();var segment=request.Segment;
        if (segment.Speed!=1 || segment.HasVariableSpeed || segment.IsReversed || segment.IsMuted) throw new CaptionForgeOperationException(OperationErrorCode.UnsupportedMedia,"Este recorte necesita un modo de audio aún no soportado.",request.Run.RunId);
        if (!File.Exists(request.ResolvedSourcePath)) throw new FileNotFoundException("Localiza el medio original antes de generar.",request.ResolvedSourcePath);
        if (Math.Abs(segment.SourceRange.DurationUs-segment.TargetRange.DurationUs)>100_000) throw new CaptionForgeOperationException(OperationErrorCode.UnsupportedMedia,"Origen y destino tienen duraciones incompatibles para velocidad 1.",request.Run.RunId);
        PathSafety.RejectLinks(request.Run.RunDirectory);string key=JsonFiles.Hash(Encoding.UTF8.GetBytes(segment.Id))[..24];
        string output=Path.Combine(request.Run.RunDirectory,"audio",key+".wav"),staging=output+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var probe=await ExternalProcessRunner.RunAsync(_ffprobe,new[] {"-v","error","-show_streams","-show_format","-of","json",request.ResolvedSourcePath},cancellationToken).ConfigureAwait(false);
        if (probe.ExitCode!=0) throw new InvalidDataException("FFprobe no pudo leer el medio: "+probe.Error);
        var information=JsonNode.Parse(probe.Output)!;var streams=information["streams"] as JsonArray ?? new JsonArray();
        if (!streams.Any(s=>s?["codec_type"]?.GetValue<string>()=="audio")) throw new CaptionForgeOperationException(OperationErrorCode.UnsupportedMedia,"El medio no contiene una pista de audio.",request.Run.RunId);
        if (decimal.TryParse(information["format"]?["duration"]?.GetValue<string>(),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds))
            if (segment.SourceRange.StartUs>=seconds*1_000_000 || segment.SourceRange.EndUs>seconds*1_000_000+100_000) throw new InvalidDataException("El recorte excede el medio real más allá del margen de redondeo.");
        long frames=Math.Max(1,checked((long)Math.Round((decimal)request.RequiredDurationUs*16000/1_000_000,MidpointRounding.AwayFromZero)));
        string number(long value) => ((decimal)value/1_000_000).ToString("0.######",CultureInfo.InvariantCulture);
        string filter=$"atrim=duration={number(segment.SourceRange.DurationUs)},asetpts=PTS-STARTPTS,aresample=16000,apad=whole_len={frames},atrim=end_sample={frames}";
        var arguments=new[] {"-nostdin","-hide_banner","-loglevel","error","-ss",number(segment.SourceRange.StartUs),"-i",request.ResolvedSourcePath,"-map","0:a:0","-vn","-af",filter,"-ac","1","-ar","16000","-c:a","pcm_s16le","-f","wav","-y",staging};
        try
        {
            var processed=await ExternalProcessRunner.RunAsync(_ffmpeg,arguments,cancellationToken).ConfigureAwait(false);
            await JsonFiles.WriteAsync(Path.Combine(request.Run.RunDirectory,"logs",key+"-ffmpeg.json"),new JsonObject { ["exitCode"]=processed.ExitCode,["stderr"]=processed.Error,["arguments"]=JsonFiles.Node(arguments) },cancellationToken).ConfigureAwait(false);
            if (processed.ExitCode!=0) throw new InvalidDataException("FFmpeg falló al preparar el fragmento: "+processed.Error);
            var wave=PcmWaveInfo.Read(staging);if (wave.SampleFrames!=frames) throw new InvalidDataException("La cantidad de muestras no coincide con el destino.");
            var audio=new PreparedAudio(segment.Id,output,request.RequiredDurationUs,wave.DurationUs);
            cancellationToken.ThrowIfCancellationRequested();File.Move(staging,output,overwrite:true);
            await JsonFiles.WriteAsync(Path.Combine(request.Run.RunDirectory,"audio",key+"-mapping.json"),new JsonObject { ["schemaVersion"]=1,["request"]=JsonFiles.Node(request),["prepared"]=JsonFiles.Node(audio),["wave"]=JsonFiles.Node(wave),["probe"]=information.DeepClone() },cancellationToken).ConfigureAwait(false);
            return audio;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}

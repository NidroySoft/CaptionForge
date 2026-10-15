using System.Text;
using System.Text.Json.Nodes;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Infrastructure.Internal;
using CaptionForge.Infrastructure.Media;
using Whisper.net;

namespace CaptionForge.Infrastructure.Transcription;

/// <summary>Whisper.net directo, CPU y DTW. Reutiliza el modelo entre fragmentos y serializa el acceso a la fábrica nativa.</summary>
public sealed class WhisperNetTranscriptionService : ITranscriptionService,IAsyncDisposable
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly WhisperAlignmentHeadsPreset? _preset;
    private WhisperFactory? _factory;private string? _path;private DateTime _changed;private long _size;private bool _disposed;
    public WhisperNetTranscriptionService(WhisperAlignmentHeadsPreset? alignmentPreset=null) => _preset=alignmentPreset;
    public async Task<TranscriptionResult> TranscribeAsync(RunContext run,PreparedAudio audio,TranscriptionOptions options,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(run);ArgumentNullException.ThrowIfNull(audio);ArgumentNullException.ThrowIfNull(options);
        PathSafety.RequireInside(audio.Path,run.RunDirectory);var wave=PcmWaveInfo.Read(audio.Path);
        if (wave.DurationUs!=audio.MeasuredDurationUs) throw new InvalidDataException("El WAV cambió o sus datos no corresponden.");
        if (!File.Exists(options.ModelPath)) throw new CaptionForgeOperationException(OperationErrorCode.ResourceUnavailable,"Falta el modelo GGML de Whisper.",run.RunId);
        var info=WhisperModelInfo.Read(options.ModelPath,_preset);
        if (info.IsEnglishOnly && options.Language is not ("en" or "auto")) throw new CaptionForgeOperationException(OperationErrorCode.InvalidSelection,"Un modelo .en solo admite inglés. Selecciona un modelo multilingüe.",run.RunId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed,this);string path=PathSafety.Full(options.ModelPath);var file=new FileInfo(path);
            if (_factory is null || _path!=path || _changed!=file.LastWriteTimeUtc || _size!=file.Length)
            {
                _factory?.Dispose();_factory=null;
                _factory=await Task.Run(()=>WhisperFactory.FromPath(path,new WhisperFactoryOptions { UseGpu=false,UseFlashAttention=false,UseDtwTimeStamps=true,HeadsPreset=info.AlignmentPreset }),cancellationToken).ConfigureAwait(false);
                _path=path;_changed=file.LastWriteTimeUtc;_size=file.Length;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var builder=_factory.CreateBuilder().WithThreads(options.CpuThreads).WithTokenTimestamps();
            if (options.Language=="auto" && !info.IsEnglishOnly) builder.WithLanguageDetection();
            else builder.WithLanguage(info.IsEnglishOnly?"en":options.Language);
            await using var processor=builder.Build();await using var stream=File.OpenRead(audio.Path);
            var segments=new List<TranscriptionSegment>();var nativeSegments=new List<SegmentData>();var raw=new JsonArray();
            var pending=new List<(int Index,SegmentData Segment,InvalidDataException Error)>();string language=info.IsEnglishOnly?"en":options.Language;
            string key=JsonFiles.Hash(Encoding.UTF8.GetBytes(audio.SourceSegmentId))[..24];
            Exception? originalFailure=null;
            try
            {
                await foreach (var segment in processor.ProcessAsync(stream,cancellationToken).ConfigureAwait(false))
                {
                    nativeSegments.Add(segment);language=segment.Language;raw.Add(new JsonObject { ["text"]=segment.Text,["startTicks"]=segment.Start.Ticks,["endTicks"]=segment.End.Ticks,["language"]=segment.Language,
                        ["tokens"]=new JsonArray(segment.Tokens.Select(t=>(JsonNode)new JsonObject { ["id"]=t.Id,["text"]=t.Text,["start10Ms"]=t.Start,["end10Ms"]=t.End,["dtwTimestamp"]=t.DtwTimestamp }).ToArray()) });
                    try {
                        var normalized=WhisperTokenNormalizer.Normalize(segment,info.IsEnglishOnly,out var recoveries);
                        if (recoveries.Count>0) raw[^1]!["wordTimingRecoveries"]=JsonFiles.Node(recoveries);
                        segments.Add(normalized);
                    } catch (InvalidDataException error) {
                        pending.Add((segments.Count,segment,error)); segments.Add(null!);
                    }
                }
                // Adjacent native context can recover a word cut by a phrase edge without another inference.
                var merged = new HashSet<int>();
                foreach (var failed in pending) {
                    var next = failed.Index + 1;
                    if (next >= nativeSegments.Count || segments[next] is null || merged.Contains(failed.Index)) continue;
                    var recovered = WhisperTokenNormalizer.RecoverWithFollowing(failed.Segment,nativeSegments[next],info.IsEnglishOnly);
                    if (recovered is null) continue;
                    segments[failed.Index] = recovered; segments[next] = null!; merged.Add(next);
                    raw[failed.Index]!["alignmentContextRecovery"] = new JsonObject { ["followingSegmentIndex"] = next,
                        ["textPreserved"] = true, ["tokens"] = JsonFiles.Node(recovered.Tokens), ["range"] = JsonFiles.Node(recovered.Range) };
                }
                var retries = pending.Where(p => segments[p.Index] is null && !merged.Contains(p.Index)).ToArray();
                // Finish the current native pass before using the same factory for a bounded retry.
                if (retries.Length>3) throw retries[0].Error;
                foreach (var failed in retries) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var retryBuilder=_factory.CreateBuilder().WithThreads(options.CpuThreads).WithTokenTimestamps()
                        .WithLanguage(failed.Segment.Language).WithOffset(failed.Segment.Start)
                        .WithDuration(failed.Segment.End-failed.Segment.Start);
                    await using var retryProcessor=retryBuilder.Build(); await using var retryStream=File.OpenRead(audio.Path);
                    var retrySegments=new List<SegmentData>();
                    await foreach (var candidate in retryProcessor.ProcessAsync(retryStream,cancellationToken).ConfigureAwait(false)) retrySegments.Add(candidate);
                    var recovered=WhisperTokenNormalizer.RecoverFromRetry(failed.Segment,retrySegments,info.IsEnglishOnly);
                    if (recovered is null) throw failed.Error;
                    segments[failed.Index]=recovered;
                    raw[failed.Index]!["alignmentRetry"]=new JsonObject { ["reason"]=failed.Error.Message, ["textPreserved"]=true,
                        ["tokens"]=JsonFiles.Node(recovered.Tokens),["range"]=JsonFiles.Node(recovered.Range) };
                }
                var result=new TranscriptionResult(audio.SourceSegmentId,options.ModelName,language,audio.DurationUs,segments.Where(s=>s is not null).ToArray());
                await JsonFiles.WriteAsync(Path.Combine(run.RunDirectory,"transcription",key+".json"),JsonFiles.Node(result),cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex) { originalFailure=ex;throw; }
            finally
            {
                try { await JsonFiles.WriteAsync(Path.Combine(run.RunDirectory,"transcription",key+"-native.json"),new JsonObject { ["schemaVersion"]=1,["model"]=JsonFiles.Node(info),["options"]=JsonFiles.Node(options),["segments"]=raw },CancellationToken.None).ConfigureAwait(false); }
                catch (Exception diagnosticFailure) when (originalFailure is not null) { throw new AggregateException(originalFailure,diagnosticFailure); }
            }
        }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _factory?.Dispose();_factory=null;_disposed=true; } }
        finally { _gate.Release(); }
    }
}

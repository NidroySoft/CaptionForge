using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Infrastructure.Transcription;
using Xunit;
using Xunit.Abstractions;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class WhisperValidationTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Theory]
    [InlineData("outside")] [InlineData("duration")] [InlineData("missing-model")]
    [InlineData("invalid-header")] [InlineData("english-with-es")] [InlineData("cancelled")]
    [InlineData("disposed")]
    public async Task ValidacionRechazaErroresAntesDeCargarMotorNativo(string flaw)
    {
        var s=await SessionAsync();var run=await s.Store.CreateRunAsync(s.Request());
        var wav=Path.Combine(flaw=="outside"?work:Path.Combine(run.RunDirectory,"audio"),"voice.wav");WriteWave(wav,16000);
        var model=Path.Combine(work,"header-only.bin");if(flaw!="missing-model")BinaryFormatTests.WriteHeader(model);if(flaw=="invalid-header")File.WriteAllBytes(model,new byte[48]);
        var audio=new PreparedAudio("s",wav,1_000_000,flaw=="duration"?1_000_001:1_000_000);
        var options=new TranscriptionOptions("header-test",model,flaw=="english-with-es"?"es":"en",1,1);
        await using var whisper=new WhisperNetTranscriptionService();using var cts=new CancellationTokenSource();
        if(flaw=="cancelled")cts.Cancel();if(flaw=="disposed")await whisper.DisposeAsync();
        var error=await Record.ExceptionAsync(()=>whisper.TranscribeAsync(run,audio,options,cts.Token));Assert.NotNull(error);
        switch(flaw)
        {
            case "missing-model":Assert.Equal(OperationErrorCode.ResourceUnavailable,Assert.IsType<CaptionForgeOperationException>(error).Code);break;
            case "english-with-es":Assert.Equal(OperationErrorCode.InvalidSelection,Assert.IsType<CaptionForgeOperationException>(error).Code);break;
            case "cancelled":Assert.IsAssignableFrom<OperationCanceledException>(error);break;
            case "disposed":Assert.IsType<ObjectDisposedException>(error);break;
            default:Assert.IsType<InvalidDataException>(error);break;
        }
    }
}

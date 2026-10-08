using CaptionForge.Modules.TextToSpeech.Core;
namespace CaptionForge.Modules.TextToSpeech.Core;

public interface ISpeechSynthesisService
{
    Task<SpeechSynthesisResult> GenerateAsync(SpeechSynthesisRequest request, IProgress<SpeechProgress>? progress, CancellationToken cancellationToken);
}

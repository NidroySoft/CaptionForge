using CaptionForge.Application.Models.Speech;
namespace CaptionForge.Application.Abstractions;

public interface ISpeechSynthesisService
{
    Task<SpeechSynthesisResult> GenerateAsync(SpeechSynthesisRequest request, IProgress<SpeechProgress>? progress, CancellationToken cancellationToken);
}

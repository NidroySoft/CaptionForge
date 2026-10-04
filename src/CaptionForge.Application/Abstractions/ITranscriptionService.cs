using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Core.Models.Transcription;

namespace CaptionForge.Application.Abstractions;

public interface ITranscriptionService
{
    /// <summary>
    /// Integración Whisper.net directa con DTW; tokens/frases usan microsegundos desde el origen del WAV.
    /// Conserva espacios del tokenizador, marca tokens de control y registra modelo/idioma efectivos.
    /// Persiste salida normalizada y diagnóstico dentro del run; no interpreta stdout de un CLI.
    /// </summary>
    Task<TranscriptionResult> TranscribeAsync(RunContext run, PreparedAudio audio,
        TranscriptionOptions options, CancellationToken cancellationToken = default);
}

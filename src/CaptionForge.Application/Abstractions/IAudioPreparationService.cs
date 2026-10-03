using CaptionForge.Application.Models.Media;

namespace CaptionForge.Application.Abstractions;

public interface IAudioPreparationService
{
    /// <summary>
    /// Usa source_timerange sobre el medio resuelto. Guarda WAV PCM mono/16 kHz/16 bits dentro del run.
    /// El origen es cero y la duración lógica coincide con target_timerange (incluido redondeo/padding necesario).
    /// Comprueba formato/duración reales y persiste el mapeo, sin tocar el original ni el draft de CapCut.
    /// </summary>
    Task<PreparedAudio> PrepareAsync(AudioPreparationRequest request, CancellationToken cancellationToken = default);
}

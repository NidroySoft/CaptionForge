using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Rules;
using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Application.Services;

/// <summary>Convierte una transcripción local en captions del montaje, utilizando las reglas Core v1 sin duplicarlas.</summary>
public sealed class SubtitleCuePlanner
{
    public IReadOnlyList<SubtitleCue> Build(MediaSegment source, TranscriptionResult transcription, FrameRate frameRate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transcription);
        if (!frameRate.IsValid) throw new ArgumentException("FPS inválidos.", nameof(frameRate));
        if (source.Id != transcription.SourceSegmentId || transcription.PreparedAudioDurationUs != source.TargetRange.DurationUs)
            throw new CaptionForgeOperationException(OperationErrorCode.InvalidAdapterResult,
                "La transcripción no corresponde al fragmento o a su duración de destino.");

        var captions = new List<SubtitleCue>();
        long previousEnd = 0;
        foreach (var phrase in transcription.Segments)
        {
            try
            {
                var quantized = SubtitleTimingCalculator.QuantizeToFrames(phrase.Range, frameRate);
                var local = SubtitleTimingCalculator.ConstrainToFragment(quantized, source.TargetRange.DurationUs);
                if (local is null || TimeRangeUs.RoundToMilliseconds(local.Value.DurationUs) == 0) continue;
                var words = WordTimingBuilder.Build(phrase.Tokens, local.Value);
                if (words.Count == 0) continue;
                var range = SubtitleTimingCalculator.PlaceOnTimeline(local.Value, source.TargetRange);
                if (range.StartUs < previousEnd)
                    throw new InvalidOperationException("Las frases reconocidas se solapan o no están ordenadas.");
                captions.Add(new SubtitleCue(source.Id, range, words));
                previousEnd = range.EndUs;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
            {
                throw new CaptionForgeOperationException(OperationErrorCode.InvalidWordTiming,
                    $"No se pueden conservar los tiempos de palabras del fragmento {source.Id}.", innerException: ex);
            }
        }
        return captions.AsReadOnly();
    }
}

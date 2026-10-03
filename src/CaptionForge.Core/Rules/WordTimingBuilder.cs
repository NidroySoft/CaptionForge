using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Core.Rules;

/// <summary>Reconstruye palabras con el patrón temporal v3 a partir de tokens normalizados.</summary>
public static class WordTimingBuilder
{
    /// <summary>
    /// Los tokens y captionRangeLocal usan el mismo origen: el inicio del WAV preparado.
    /// Devuelve milisegundos relativos al caption. No aplica correcciones lingüísticas.
    /// </summary>
    public static IReadOnlyList<TimedWord> Build(IEnumerable<TranscriptionToken> tokens,
        TimeRangeUs captionRangeLocal)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        long durationMs = TimeRangeUs.RoundToMilliseconds(captionRangeLocal.DurationUs);
        if (durationMs <= 0)
            throw new ArgumentException("El caption necesita una duración representable en milisegundos.", nameof(captionRangeLocal));
        long originMs = TimeRangeUs.RoundToMilliseconds(captionRangeLocal.StartUs);
        var spans = new List<WordSpan>();
        foreach (var token in tokens)
        {
            ArgumentNullException.ThrowIfNull(token);
            if (token.IsControl || string.IsNullOrWhiteSpace(token.Text))
                continue;
            string value = token.Text.Trim();
            if (char.IsWhiteSpace(token.Text[0]) || spans.Count == 0)
            {
                // Cada token de entrada debe representar una unidad del tokenizador,
                // no una frase preunida. Evita fabricar tiempos para varias palabras.
                if (value.Any(char.IsWhiteSpace))
                    throw new ArgumentException("El token contiene varias palabras; se necesitan sus tokens originales.", nameof(tokens));
                spans.Add(new WordSpan(value, token.Range.StartUs, token.Range.EndUs));
            }
            else
            {
                var previous = spans[^1];
                previous.Text += value;
                if (!IsPunctuationOnly(value))
                    previous.EndUs = Math.Max(previous.EndUs, token.Range.EndUs);
            }
        }
        if (spans.Count == 0)
            return Array.Empty<TimedWord>();

        var words = new List<TimedWord>(spans.Count * 2 - 1);
        long previousEndMs = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            long startMs = Math.Clamp(TimeRangeUs.RoundToMilliseconds(span.StartUs) - originMs, 0, durationMs);
            long endMs = Math.Clamp(TimeRangeUs.RoundToMilliseconds(span.EndUs) - originMs, 0, durationMs);
            if (i == 0) startMs = 0;
            if (i == spans.Count - 1) endMs = durationMs;
            if (endMs <= startMs || startMs < previousEndMs)
                throw new InvalidOperationException("La alineación contiene palabras sin duración o solapadas; no se repartirán tiempos artificiales.");
            if (i > 0)
                words.Add(new TimedWord(" ", previousEndMs, previousEndMs));
            words.Add(new TimedWord(span.Text, startMs, endMs));
            previousEndMs = endMs;
        }
        return words.AsReadOnly();
    }

    private static bool IsPunctuationOnly(string text)
    {
        foreach (char c in text)
            if (c is not ('.' or ',' or '!' or '?' or ';' or ':'))
                return false;
        return text.Length > 0;
    }

    private sealed class WordSpan(string text, long startUs, long endUs)
    {
        public string Text { get; set; } = text;
        public long StartUs { get; } = startUs;
        public long EndUs { get; set; } = endUs;
    }
}

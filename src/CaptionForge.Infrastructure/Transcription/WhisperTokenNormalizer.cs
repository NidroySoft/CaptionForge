using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.ValueObjects;
using Whisper.net;

namespace CaptionForge.Infrastructure.Transcription;

/// <summary>
/// Conserva Start/End nativos. Solo para palabras completas colapsadas estima un intervalo
/// con su punto nativo y DTW, limitado por las palabras vecinas; registra cada recuperación.
/// DTW es un punto aproximado de alineación, no una pareja de inicio y final.
/// </summary>
public static class WhisperTokenNormalizer
{
    public static TranscriptionSegment Normalize(SegmentData segment, bool englishOnly)
        => Normalize(segment, englishOnly, out _);

    public static TranscriptionSegment Normalize(SegmentData segment, bool englishOnly,
        out IReadOnlyList<WhisperWordTimingRecovery> recoveries)
    {
        ArgumentNullException.ThrowIfNull(segment);
        long from = segment.Start.Ticks / 10, to = segment.End.Ticks / 10;
        if (from < 0 || to < from)
            throw new InvalidDataException("Frase de Whisper con tiempos inválidos.");

        int eot = englishOnly ? 50256 : 50257;
        var tokens = new List<TranscriptionToken>();
        var anchors = new List<long?>();
        foreach (var token in segment.Tokens)
        {
            bool control = token.Id >= eot;
            string text = token.Text ?? "";
            if (control)
            {
                tokens.Add(new(text, new TimeRangeUs(0, 0), true));
                anchors.Add(null);
                continue;
            }
            if (token.Start < 0 || token.End < token.Start)
                throw new InvalidDataException("Token léxico sin offsets válidos; no se inventará su tiempo.");
            long start = checked(token.Start * 10_000), end = checked(token.End * 10_000);
            tokens.Add(new(text, new TimeRangeUs(start, end - start)));
            anchors.Add(token.DtwTimestamp >= 0 ? checked(token.DtwTimestamp * 10_000) : null);
        }

        recoveries = RecoverCollapsedWords(tokens, anchors, from, to);
        return new(segment.Text, new(from, to - from), tokens);
    }

    private static IReadOnlyList<WhisperWordTimingRecovery> RecoverCollapsedWords(
        List<TranscriptionToken> tokens, List<long?> anchors, long from, long to)
    {
        // Agrupar primero: una subpalabra con duración cero puede pertenecer a una
        // palabra completa que ya tiene duración válida. Esa palabra queda intacta.
        var words = new List<WordSpan>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsControl || string.IsNullOrWhiteSpace(token.Text)) continue;
            string text = token.Text.Trim();
            if (char.IsWhiteSpace(token.Text[0]) || words.Count == 0)
                words.Add(new(text, i, token.Range.StartUs, token.Range.EndUs));
            else
            {
                words[^1].Text += text;
                if (IsPunctuationOnly(text)) continue;
                words[^1].EndUs = Math.Max(words[^1].EndUs, token.Range.EndUs);
            }
            if (anchors[i] is long anchor) words[^1].Anchors.Add(anchor);
        }

        var recovered = new List<WhisperWordTimingRecovery>();
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.EndUs > word.StartUs) continue;
            // WordTimingBuilder ya extiende primera/última palabra al límite del caption.
            if ((i == 0 && word.EndUs > from) || (i == words.Count - 1 && word.StartUs < to))
                continue;

            long lower = i == 0 ? from : Math.Max(from, words[i - 1].EndUs);
            long upper = i == words.Count - 1 ? to : Math.Min(to, words[i + 1].StartUs);
            if (word.Anchors.Count == 0 || word.Anchors.Any(a => a < from || a > to) ||
                lower >= upper || word.StartUs < lower || word.StartUs > upper)
                throw CannotRecover(word);

            long start = Math.Clamp(Math.Min(word.StartUs, word.Anchors.Min()), lower, upper);
            long end = Math.Clamp(Math.Max(word.EndUs, word.Anchors.Max()), lower, upper);
            if (TimeRangeUs.RoundToMilliseconds(end) <= TimeRangeUs.RoundToMilliseconds(start))
                throw CannotRecover(word);

            var original = new TimeRangeUs(word.StartUs, word.EndUs - word.StartUs);
            var range = new TimeRangeUs(start, end - start);
            // La palabra completa recibe el intervalo; no se reparten tiempos entre BPE.
            var first = tokens[word.FirstTokenIndex];
            tokens[word.FirstTokenIndex] = new(first.Text, range, first.IsControl);
            recovered.Add(new(word.Text, original, range, word.Anchors.AsReadOnly()));
            word.StartUs = start;
            word.EndUs = end;
        }
        return recovered.AsReadOnly();
    }

    private static InvalidDataException CannotRecover(WordSpan word) => new(
        $"Whisper devolvió la palabra «{word.Text}» sin duración en {word.StartUs / 1000} ms. " +
        "La alineación DTW no permite recuperar un intervalo entre sus palabras vecinas.");

    // Mismo tratamiento de continuaciones que WordTimingBuilder: la puntuación final
    // acompaña al texto, pero no amplía la duración acústica de la palabra.
    private static bool IsPunctuationOnly(string text)
        => text.Length > 0 && text.All(c => c is '.' or ',' or '!' or '?' or ';' or ':');

    private sealed class WordSpan(string text, int firstTokenIndex, long startUs, long endUs)
    {
        public string Text { get; set; } = text;
        public int FirstTokenIndex { get; } = firstTokenIndex;
        public long StartUs { get; set; } = startUs;
        public long EndUs { get; set; } = endUs;
        public List<long> Anchors { get; } = [];
    }
}

/// <summary>Intervalo estimado para una palabra colapsada; el diagnóstico nativo conserva sus offsets originales.</summary>
public sealed record WhisperWordTimingRecovery(string Text, TimeRangeUs OriginalRange,
    TimeRangeUs RecoveredRange, IReadOnlyList<long> DtwAnchorsUs);

using Whisper.net;

namespace CaptionForge.Modules.MediaTools.Core;

public sealed class MediaTranscriber
{
    public async Task<Transcript> TranscribeAsync(string wave, string modelPath, string language, int threads, IProgress<int>? progress, CancellationToken ct)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Selecciona un modelo GGML de Whisper instalado.", modelPath);
        using (var reader = new BinaryReader(File.OpenRead(modelPath)))
        {
            if (reader.BaseStream.Length < 48 || reader.ReadUInt32() != 0x67676d6c) throw new InvalidDataException("No es un modelo GGML de Whisper válido.");
            int vocabulary = reader.ReadInt32();
            if (vocabulary is < 51864 or > 51866) throw new InvalidDataException("Vocabulario de modelo Whisper no válido.");
            bool englishOnly = vocabulary == 51864;
            if (englishOnly && language is not "auto" and not "en") throw new ArgumentException("Este modelo es solo inglés. Elige un modelo multilingüe para otros idiomas.");
            if (englishOnly) language = "en";
        }
        double duration = AudioWaveform.Read(wave, ct: ct).Duration;
        // No factory is retained: resources are released after each job, including cancellation.
        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = false, UseFlashAttention = false });
            var builder = factory.CreateBuilder().WithThreads(Math.Clamp(threads, 1, Environment.ProcessorCount))
                .WithMaxSegmentLength(84).SplitOnWord().WithProgressHandler(value => progress?.Report(value));
            if (language == "auto") builder.WithLanguageDetection(); else builder.WithLanguage(language);
            using var processor = builder.Build(); await using var input = File.OpenRead(wave);
            List<TranscriptSegment> segments = []; string detected = language;
            await foreach (var segment in processor.ProcessAsync(input, ct))
            {
                ct.ThrowIfCancellationRequested(); detected = segment.Language;
                double start = Math.Clamp(segment.Start.TotalSeconds, 0, duration), end = Math.Clamp(segment.End.TotalSeconds, 0, duration);
                string text = segment.Text.Trim();
                if (text.Length > 0 && end > start) segments.Add(new(start, end, text));
            }
            return new Transcript(segments, detected);
        }, ct);
    }
}

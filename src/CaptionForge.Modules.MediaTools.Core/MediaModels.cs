using System.Globalization;
using System.Text;

namespace CaptionForge.Modules.MediaTools.Core;

public sealed record AudioTrack(int Index, string Codec, string Language, string Title, int Channels, int Number = 0)
{
    public string DisplayName => $"Pista {(Number > 0 ? Number : Index + 1)} · {Language} · {Codec} · {Channels} {(Channels == 1 ? "canal" : "canales")}" + (Title.Length > 0 ? " · " + Title : "");
    public override string ToString() => DisplayName;
}
public sealed record MediaInfo(double Duration, IReadOnlyList<AudioTrack> Tracks);
public sealed record AudioRange(double Start, double End)
{
    public double Duration => End - Start;
    public void Validate(double duration)
    {
        if (!double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End > duration + 0.001 || Duration < 0.02)
            throw new ArgumentException("La selección debe tener inicio y fin válidos dentro del audio (mínimo 0,02 s).");
    }
    public static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    public static bool TryParse(string value, out double seconds)
    {
        if (TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm\:ss\.fff", @"hh\:mm\:ss", @"mm\:ss\.fff", @"mm\:ss" }, CultureInfo.InvariantCulture, out var time))
        { seconds = time.TotalSeconds; return seconds >= 0; }
        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds) && double.IsFinite(seconds) && seconds >= 0;
    }
}
public sealed record AudioWaveform(double Duration, float[] Peaks)
{
    public static AudioWaveform Read(string path, int bins = 1600, CancellationToken ct = default)
    {
        if (bins < 1 || bins > 10000) throw new ArgumentOutOfRangeException(nameof(bins));
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("WAV no válido.");
        reader.ReadUInt32(); if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("WAV no válido.");
        int format = 0, channels = 0, rate = 0, bits = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4)); long size = reader.ReadUInt32(), end = stream.Position + size;
            if (end > stream.Length) throw new InvalidDataException("WAV incompleto.");
            if (chunk == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Formato WAV incompleto.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32(); reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (chunk == "data")
            {
                if (format != 1 || channels != 1 || rate != 16000 || bits != 16 || size % 2 != 0 || size == 0)
                    throw new InvalidDataException("La vista previa requiere PCM16 mono de 16 kHz.");
                long frames = size / 2; var peaks = new float[bins];
                for (long frame = 0; frame < frames; frame++)
                { if (frame % 4096 == 0) ct.ThrowIfCancellationRequested(); int bin = (int)Math.Min(bins - 1, frame * bins / frames); peaks[bin] = Math.Max(peaks[bin], Math.Abs(reader.ReadInt16() / 32768f)); }
                return new(frames / 16000d, peaks);
            }
            stream.Position = end + size % 2;
        }
        throw new InvalidDataException("No hay audio en el WAV.");
    }
}
public sealed record TranscriptSegment(double Start, double End, string Text);
public sealed record Transcript(IReadOnlyList<TranscriptSegment> Segments, string Language)
{
    public string Text => string.Join(Environment.NewLine, Segments.Select(s => s.Text));
    public string Export(string extension, double offset = 0)
    {
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) return Text + Environment.NewLine;
        bool vtt = extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase);
        if (!vtt && !extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Formato de texto no admitido.");
        var output = new StringBuilder(vtt ? "WEBVTT\n\n" : ""); int number = 0;
        foreach (var segment in Segments)
        {
            if (!vtt) output.Append(++number).Append('\n');
            string Stamp(double seconds) => AudioRange.Format(seconds + offset).Replace('.', vtt ? '.' : ',');
            output.Append(Stamp(segment.Start)).Append(" --> ").Append(Stamp(segment.End)).Append('\n')
                .Append(segment.Text.Replace("\r", "").Replace("\n", " ").Trim()).Append("\n\n");
        }
        return output.ToString();
    }
}

using System.Text;

namespace CaptionForge.Modules.TextToSpeech.Core;

public sealed record AudioLevelResult(double GainDb, long ClippedSamples);

public static class AudioLevelProcessor
{
    // Peak normalization leaves 1 dB of headroom. This is not a LUFS measurement.
    public static AudioLevelResult Process(string source, string destination, double? gainDb = null)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("El original debe conservarse.");
        if (gainDb is { } gain && (!double.IsFinite(gain) || gain < -60 || gain > 30))
            throw new ArgumentOutOfRangeException(nameof(gainDb));
        using var input = File.OpenRead(source);
        using var reader = new BinaryReader(input, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("WAV inválido.");
        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("WAV inválido.");
        long offset = 0, length = 0; bool pcm = false;
        while (input.Position + 8 <= input.Length)
        {
            var name = Encoding.ASCII.GetString(reader.ReadBytes(4));
            long size = reader.ReadUInt32(), end = checked(input.Position + size);
            if (end > input.Length) throw new InvalidDataException("WAV incompleto.");
            if (name == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Formato WAV incompleto.");
                int format = reader.ReadUInt16(), channels = reader.ReadUInt16(), rate = reader.ReadInt32();
                reader.ReadInt32(); int alignment = reader.ReadUInt16(), bits = reader.ReadUInt16();
                pcm = format == 1 && bits == 16 && channels > 0 && rate > 0 && alignment == channels * 2;
            }
            else if (name == "data") { offset = input.Position; length = size; break; }
            input.Position = end + size % 2;
        }
        if (!pcm || length == 0 || length % 2 != 0) throw new InvalidDataException("Se requiere WAV PCM de 16 bits con audio.");
        int peak = 0;
        input.Position = offset;
        for (long i = 0; i < length / 2; i++) peak = Math.Max(peak, Math.Abs((int)reader.ReadInt16()));
        double applied = gainDb ?? (peak == 0 ? 0 : 20 * Math.Log10(32767 * Math.Pow(10, -1d / 20) / peak));
        double multiplier = Math.Pow(10, applied / 20);
        input.Position = 0;
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite);
        input.CopyTo(output);
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        input.Position = offset; output.Position = offset;
        long clipped = 0;
        for (long i = 0; i < length / 2; i++)
        {
            double sample = Math.Round(reader.ReadInt16() * multiplier);
            if (sample > short.MaxValue || sample < short.MinValue) clipped++;
            writer.Write((short)Math.Clamp(sample, short.MinValue, short.MaxValue));
        }
        return new(applied, clipped);
    }
}

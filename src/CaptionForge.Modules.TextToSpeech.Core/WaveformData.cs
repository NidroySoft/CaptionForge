using System.Text;

namespace CaptionForge.Modules.TextToSpeech.Core;

public sealed record WaveformData(double DurationSeconds, float[] Peaks)
{
    public static WaveformData Read(string path, int bars = 240)
    {
        if (bars < 1 || bars > 4000) throw new ArgumentOutOfRangeException(nameof(bars));
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Audio WAV inválido.");
        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Audio WAV inválido.");
        int channels = 0, rate = 0, bits = 0, format = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint size = reader.ReadUInt32(); long end = checked(stream.Position + size);
            if (end > stream.Length) throw new InvalidDataException("Audio WAV incompleto.");
            if (chunk == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Formato WAV incompleto.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (chunk == "data")
            {
                if (format != 1 || bits != 16 || channels < 1 || channels > 8 || rate <= 0)
                    throw new InvalidDataException("La vista de audio requiere WAV PCM de 16 bits.");
                long frames = size / (channels * 2);
                if (frames == 0) throw new InvalidDataException("Audio vacío.");
                var peaks = new float[bars];
                for (long frame = 0; frame < frames; frame++)
                {
                    int bin = (int)Math.Min(bars - 1, frame * bars / frames);
                    for (int channel = 0; channel < channels; channel++)
                        peaks[bin] = Math.Max(peaks[bin], Math.Abs(reader.ReadInt16() / 32768f));
                }
                return new(frames / (double)rate, peaks);
            }
            stream.Position = end + (size % 2);
        }
        throw new InvalidDataException("No se encontró audio en el WAV.");
    }
}

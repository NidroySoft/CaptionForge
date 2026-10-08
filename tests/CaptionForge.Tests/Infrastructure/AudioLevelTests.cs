using System.Text;
using CaptionForge.Modules.TextToSpeech.Core;

namespace CaptionForge.Tests.Infrastructure;

public sealed class AudioLevelTests : IDisposable
{
    private readonly string _source = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
    private readonly string _destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
    private void Write(params short[] samples)
    {
        using var writer = new BinaryWriter(File.Create(_source));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(24000); writer.Write(48000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
        foreach (short sample in samples) writer.Write(sample);
    }
    [Theory]
    [InlineData(1000)]
    [InlineData(-32768)]
    public void NormalizationReachesTargetWithoutClippingAndPreservesOriginal(short sample)
    {
        Write(sample, 0, sample);
        var original = File.ReadAllBytes(_source);
        var result = AudioLevelProcessor.Process(_source, _destination);
        Assert.Equal(0, result.ClippedSamples);
        Assert.Equal(original, File.ReadAllBytes(_source));
        double peak = WaveformData.Read(_destination).Peaks.Max();
        Assert.InRange(20 * Math.Log10(peak), -1.002, -0.998);
        Assert.Equal(WaveformData.Read(_source).DurationSeconds, WaveformData.Read(_destination).DurationSeconds);
    }
    [Fact]
    public void ManualGainReportsClippingAndSupportsAttenuation()
    {
        Write(20000, -20000);
        var result = AudioLevelProcessor.Process(_source, _destination, 6);
        Assert.Equal(2, result.ClippedSamples);
        File.Delete(_destination);
        result = AudioLevelProcessor.Process(_source, _destination, -6);
        Assert.Equal(0, result.ClippedSamples);
        Assert.InRange(WaveformData.Read(_destination).Peaks.Max(), 0.305f, 0.307f);
    }
    [Fact]
    public void SilenceRemainsSilentAndSamePathIsRejected()
    {
        Write(0, 0);
        Assert.Equal(0, AudioLevelProcessor.Process(_source, _destination).GainDb);
        Assert.All(WaveformData.Read(_destination).Peaks, peak => Assert.Equal(0, peak));
        Assert.Throws<ArgumentException>(() => AudioLevelProcessor.Process(_source, _source));
    }
    public void Dispose() { File.Delete(_source); File.Delete(_destination); }
}

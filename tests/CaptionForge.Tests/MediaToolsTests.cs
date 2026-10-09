using System.Security.Cryptography;
using CaptionForge.Modules.MediaTools.Core;

namespace CaptionForge.Tests;

public class MediaToolsTests
{
    [Theory]
    [InlineData("00:01:02.350", 62.35)]
    [InlineData("01:02", 62)]
    [InlineData("4", 4)]
    public void ParsesExactSelectionTimes(string input, double seconds)
    { Assert.True(AudioRange.TryParse(input, out double result)); Assert.Equal(seconds, result, 4); }
    [Theory]
    [InlineData(-1, 2)] [InlineData(2, 1)] [InlineData(1, 1)] [InlineData(0, 11)] [InlineData(double.NaN, 2)] [InlineData(0, double.PositiveInfinity)]
    public void RejectsInvalidRanges(double start, double end)
        => Assert.Throws<ArgumentException>(() => new AudioRange(start, end).Validate(10));
    [Fact]
    public void ExportsRelativeAndOriginalSubtitleTimes()
    {
        var transcript = new Transcript([new(.2, 1.5, "Hello\nworld."), new(1.7, 2.5, "A second line.")], "en");
        Assert.StartsWith("1\n00:00:00,200 --> 00:00:01,500\nHello world.", transcript.Export(".srt"));
        Assert.Contains("00:01:00,200 --> 00:01:01,500", transcript.Export(".srt", 60));
        Assert.StartsWith("WEBVTT\n\n00:00:00.200", transcript.Export(".vtt"));
        Assert.Contains("Hello", transcript.Export(".txt"));
        Assert.Throws<ArgumentException>(() => transcript.Export(".html"));
    }
    [Fact]
    public void WaveformHandlesNegativeFullScaleAndCancellation()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("RIFF"u8.ToArray()); writer.Write(36 + 32000); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8.ToArray()); writer.Write(32000);
                for (int sample = 0; sample < 16000; sample++) writer.Write(sample % 2 == 0 ? short.MinValue : short.MaxValue);
            }
            var wave = AudioWaveform.Read(path, 40);
            Assert.Equal(1, wave.Duration); Assert.All(wave.Peaks, peak => Assert.Equal(1, peak));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioWaveform.Read(path, 0));
            Assert.Throws<OperationCanceledException>(() => AudioWaveform.Read(path, ct: new(true)));
        }
        finally { File.Delete(path); }
    }
    [FfmpegFact]
    public async Task MapsChosenTrackCutsAndExportsWithoutChangingOriginal()
    {
        string root = Path.Combine(Path.GetTempPath(), "CaptionForge-media-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "vídeo con dos pistas.mkv");
            await MediaService.RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=s=64x64:d=4", "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-f", "lavfi", "-i", "sine=frequency=880:duration=4", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "mpeg4", "-c:a", "pcm_s16le", "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=spa", "-y", source], null, default);
            byte[] originalHash = SHA256.HashData(await File.ReadAllBytesAsync(source)); var service = new MediaService("ffmpeg", "ffprobe");
            var info = await service.ProbeAsync(source, default); Assert.Equal(2, info.Tracks.Count); Assert.Equal("spa", info.Tracks[1].Language);
            string preview = Path.Combine(root, "preview.wav"); await service.PreparePreviewAsync(source, info.Tracks[1].Index, preview, null, default);
            Assert.InRange(AudioWaveform.Read(preview).Duration, 3.99, 4.01);
            string result = Path.Combine(root, "fragmento.wav"); var range = new AudioRange(1.2, 2.7);
            await service.ExtractAsync(source, info.Tracks[1].Index, result, range, null, default);
            var resultInfo = await service.ProbeAsync(result, default); Assert.InRange(resultInfo.Duration, 1.49, 1.51);
            string pcm = Path.Combine(root, "fragment-pcm.wav"); await service.PreparePreviewAsync(result, 0, pcm, null, default);
            // Inspect the selected signal, not just the metadata: track 2 is 880 Hz.
            byte[] data = await File.ReadAllBytesAsync(pcm); int dataOffset = FindData(data); int crossings = 0;
            for (int i = dataOffset + 2; i + 1 < data.Length; i += 2) if (BitConverter.ToInt16(data, i - 2) <= 0 && BitConverter.ToInt16(data, i) > 0) crossings++;
            Assert.InRange(crossings / 1.5, 878, 882);
            string mp3 = Path.Combine(root, "fragmento.mp3"); await service.ExtractAsync(source, info.Tracks[1].Index, mp3, range, null, default);
            Assert.InRange((await service.ProbeAsync(mp3, default)).Duration, 1.49, 1.60);
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExtractAsync(source, info.Tracks[0].Index, source, range, null, default));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExtractAsync(source, info.Tracks[0].Index, result, new(double.NaN, 2), null, default));
            Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }
    [FfmpegFact]
    public async Task CancellationTerminatesChildAndLeavesNoPartialAudio()
    {
        string root = Path.Combine(Path.GetTempPath(), "CaptionForge-cancel-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaService.RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-re", "-f", "lavfi", "-i", "sine=duration=3600", "-f", "null", "-"], null, ct.Token));
            var service = new MediaService("ffmpeg", "ffprobe"); string dest = Path.Combine(root, "cancelled.wav");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PreparePreviewAsync("missing.wav", 0, dest, null, new CancellationToken(true)));
            Assert.False(File.Exists(dest)); Assert.Empty(Directory.EnumerateFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
    private static int FindData(byte[] bytes)
    { int offset = 12; while (offset + 8 <= bytes.Length) { int length = BitConverter.ToInt32(bytes, offset + 4); if (System.Text.Encoding.ASCII.GetString(bytes, offset, 4) == "data") return offset + 8; offset += 8 + length + length % 2; } throw new InvalidDataException(); }
}
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" };
        bool Exists(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(p => extensions.Any(ext => File.Exists(Path.Combine(p.Trim('"'), name + ext))));
        if (!Exists("ffmpeg") || !Exists("ffprobe")) Skip = "FFmpeg/FFprobe no están instalados en este entorno.";
    }
}

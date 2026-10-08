using System.Text;
using CaptionForge.Modules.TextToSpeech.Core;
namespace CaptionForge.Tests.Infrastructure;

public sealed class SpeechModuleAudioTests
{
    [Fact]
    public void WaveformReadsStereoDurationAndNegativeFullScaleWithoutOverflow()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        using (var file = File.Create(path))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 32000); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(8000); writer.Write(32000); writer.Write((short)4); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(32000);
            for (int frame = 0; frame < 8000; frame++) { writer.Write(short.MinValue); writer.Write((short)0); }
        }
        try { var audio = WaveformData.Read(path, 100); Assert.Equal(1, audio.DurationSeconds); Assert.Equal(100, audio.Peaks.Length); Assert.All(audio.Peaks, peak => Assert.Equal(1, peak)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void InstallerRejectsArchivePathsOutsideItsPrivateDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "CaptionForge-installer");
        Assert.Throws<InvalidDataException>(() => EngineInstaller.SafeDestination(root, "../escape.exe"));
        Assert.Throws<InvalidDataException>(() => EngineInstaller.SafeDestination(root, Path.GetFullPath(Path.Combine(root, "python.exe"))));
        Assert.StartsWith(Path.GetFullPath(root), EngineInstaller.SafeDestination(root, "python/python.exe"));
    }
}

using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;

namespace CaptionForge.Tests.Support;

internal static class EngineConfiguration
{
    internal static string? ModelPath => Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_MODEL");
    internal static string? VoicePath => Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_VOICE");
    internal static bool FfmpegAvailable => ExecutableExists("ffmpeg") && ExecutableExists("ffprobe");
    private static bool ExecutableExists(string name)
    {
        string executable = OperatingSystem.IsWindows() ? name + ".exe" : name;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir.Trim('"'), executable)));
    }
}
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute() { if (!EngineConfiguration.FfmpegAvailable) Skip = "Requiere ffmpeg y ffprobe en PATH."; }
}
public sealed class WhisperFactAttribute : FactAttribute
{
    public WhisperFactAttribute()
    {
        if (!EngineConfiguration.FfmpegAvailable) Skip = "Requiere ffmpeg y ffprobe en PATH.";
        else if (!File.Exists(EngineConfiguration.ModelPath) || !File.Exists(EngineConfiguration.VoicePath))
            Skip = "Configura CAPTIONFORGE_TEST_MODEL (GGML .en) y CAPTIONFORGE_TEST_VOICE (voz en inglés de 11 segundos o más).";
    }
}
[CollectionDefinition("External engines", DisableParallelization = true)]
public sealed class ExternalEnginesCollection { }

public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute() { if (!EngineConfiguration.FfmpegAvailable) Skip = "Requiere ffmpeg y ffprobe en PATH."; }
}

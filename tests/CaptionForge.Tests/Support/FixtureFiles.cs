using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;

namespace CaptionForge.Tests.Support;

internal static class FixtureFiles
{
    internal static string Directory { get; } = Locate();
    private static string Locate()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_FIXTURES");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        { Verify(explicitPath); return Path.GetFullPath(explicitPath); }
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
                foreach (var suffix in new[] { "Fixtures", "CaptionForge.Tests/Fixtures", "tests/CaptionForge.Tests/Fixtures" })
                {
                    var candidate = Path.Combine(dir.FullName, suffix.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(Path.Combine(candidate, "expected_captions_v3.json"))) { Verify(candidate); return candidate; }
                }
        throw new DirectoryNotFoundException("No se encuentra Fixtures. Extrae el ZIP completo dentro de tests/CaptionForge.Tests o configura CAPTIONFORGE_TEST_FIXTURES.");
    }
    private static void Verify(string path)
    {
        foreach (var name in new[] { "expected_captions_v3.json", "whisper_dtw.json", "whisper_regular.json", "snapshot_audio_only.json", "snapshot_with_manual_template.json", "root_draft_content.json", "draft_meta_info.json", "timelines_project.json" })
            if (!File.Exists(Path.Combine(path, name))) throw new FileNotFoundException("Falta un fixture de prueba: " + name);
    }
}

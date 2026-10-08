using CaptionForge.Application.Models.Speech;
namespace CaptionForge.Tests.Application;

public sealed class SpeechCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CaptionForge-speech-tests", Guid.NewGuid().ToString("N"));
    public SpeechCatalogTests() => Directory.CreateDirectory(_directory);

    private string Touch(string relative)
    {
        string path = Path.Combine(_directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture"); return path;
    }

    [Fact]
    public void Voices_are_selected_from_the_correct_language_and_engine()
    {
        Touch("voices/af_heart.pt"); Touch("voices/bf_emma.pt"); Touch("voices/ef_dora.pt");
        Touch("languages/english/embeddings/anna.safetensors"); Touch("languages/spanish/embeddings/lola.safetensors");
        Assert.Equal(new[] { "af_heart", "bf_emma" }, SpeechEngineCatalog.GetVoices(SpeechEngine.Kokoro, "en", _directory));
        Assert.Equal(new[] { "ef_dora" }, SpeechEngineCatalog.GetVoices(SpeechEngine.Kokoro, "es", _directory));
        Assert.Equal(new[] { "lola" }, SpeechEngineCatalog.GetVoices(SpeechEngine.Pocket, "es", _directory));
        Assert.Empty(SpeechEngineCatalog.GetVoices(SpeechEngine.Nano, "en", _directory));
    }

    [Fact]
    public void Missing_or_invalid_engine_configuration_is_rejected_before_start()
    {
        string python = Touch("python.exe"); Touch("voices/af_heart.pt");
        var valid = new SpeechSynthesisRequest(SpeechEngine.Kokoro, "en", "Hello.", "af_heart", null, python, _directory, _directory);
        SpeechEngineCatalog.Validate(valid);
        Assert.Throws<ArgumentException>(() => SpeechEngineCatalog.Validate(valid with { Language = "es" }));
        Assert.Throws<ArgumentException>(() => SpeechEngineCatalog.Validate(valid with { Text = "" }));
        Assert.Throws<ArgumentException>(() => SpeechEngineCatalog.Validate(valid with { Speed = double.NaN }));
        Assert.Throws<ArgumentException>(() => SpeechEngineCatalog.Validate(valid with { Engine = SpeechEngine.Nano, Language = "es" }));
        Assert.Throws<ArgumentException>(() => SpeechEngineCatalog.Validate(valid with { Engine = SpeechEngine.Nano }));
        Assert.Throws<FileNotFoundException>(() => SpeechEngineCatalog.Validate(valid with { PythonExecutable = "not-there" }));
    }
    public void Dispose() { foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories)) File.Delete(file); foreach (var dir in Directory.EnumerateDirectories(_directory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length)) Directory.Delete(dir); Directory.Delete(_directory); }
}

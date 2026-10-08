using System.Diagnostics;
using CaptionForge.Modules.TextToSpeech.Core;
namespace CaptionForge.Tests.Infrastructure;

public sealed class PythonSpeechFactAttribute : FactAttribute
{
    public static string? Python => FindPython();
    public PythonSpeechFactAttribute() { if (Python is null) Skip = "Requiere Python 3 en PATH o CAPTIONFORGE_TEST_PYTHON."; }
    private static string? FindPython()
    {
        var configured = Environment.GetEnvironmentVariable("CAPTIONFORGE_TEST_PYTHON");
        if (File.Exists(configured)) return configured;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (string name in OperatingSystem.IsWindows() ? new[] { "python.exe" } : new[] { "python3", "python" })
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path) && !path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return path;
            }
        return null;
    }
}

public sealed class SpeechProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CaptionForge voice with spaces", Guid.NewGuid().ToString("N"));
    private readonly string _worker;
    public SpeechProcessTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "voices"));
        File.WriteAllText(Path.Combine(_root, "voices", "af_heart.pt"), "fixture");
        _worker = Path.Combine(_root, "worker.py");
    }
    private SpeechSynthesisRequest Request => new(SpeechEngine.Kokoro, "en", "Hello, ¿qué tal?", "af_heart", null, PythonSpeechFactAttribute.Python!, _root, Path.Combine(_root, "output"));
    [PythonSpeechFact]
    public async Task Json_bridge_handles_unicode_spaces_progress_and_audio_export()
    {
        File.WriteAllText(_worker, """
import json,sys,wave
j=json.load(open(sys.argv[1],encoding='utf-8-sig'))
assert '¿qué tal?' in j['text']
print('TTS_EVENT '+json.dumps({'status':'Generating','progress':0.5}),flush=True)
with wave.open(j['output'],'wb') as w:
 w.setnchannels(1);w.setsampwidth(2);w.setframerate(24000);w.writeframes(b'\x01\x00'*2400)
print('TTS_EVENT '+json.dumps({'output':j['output'],'seconds':0.1,'elapsed':1,'sample_rate':24000}),flush=True)
""");
        var progress = new ImmediateProgress();
        var result = await new PythonSpeechSynthesisService(_worker).GenerateAsync(Request, progress, CancellationToken.None);
        Assert.True(File.Exists(result.AudioPath)); Assert.True(File.Exists(Path.ChangeExtension(result.AudioPath, ".json")));
        Assert.Contains(progress.Values, p => p.Fraction == 0.5); Assert.Equal(24000, result.SampleRate);
    }
    [PythonSpeechFact]
    public async Task Cancellation_terminates_the_worker_and_releases_the_generation_gate()
    {
        File.WriteAllText(_worker, """
import json,sys,time
j=json.load(open(sys.argv[1],encoding='utf-8-sig'))
print('TTS_EVENT '+json.dumps({'status':'Ready'}),flush=True)
time.sleep(60)
""");
        using var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new ImmediateProgress(p => { if (p.Message == "Ready") ready.TrySetResult(); });
        var task = new PythonSpeechSynthesisService(_worker).GenerateAsync(Request, progress, cts.Token);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(Directory.EnumerateFiles(Request.OutputDirectory, "*.wav"));
        await Json_bridge_handles_unicode_spaces_progress_and_audio_export();
    }
    [PythonSpeechFact]
    public async Task Backend_errors_are_reported_and_log_is_retained()
    {
        File.WriteAllText(_worker, "import json,sys;print('TTS_EVENT '+json.dumps({'error':'Missing model'}),flush=True);sys.exit(1)");
        var error = await Assert.ThrowsAsync<IOException>(() => new PythonSpeechSynthesisService(_worker).GenerateAsync(Request, null, CancellationToken.None));
        Assert.Contains("Missing model", error.Message); Assert.Single(Directory.EnumerateFiles(Request.OutputDirectory, "*.log"));
    }
    public void Dispose() { foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.Delete(file); foreach (var dir in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length)) Directory.Delete(dir); Directory.Delete(_root); }
    private sealed class ImmediateProgress(Action<SpeechProgress>? callback = null) : IProgress<SpeechProgress>
    {
        public List<SpeechProgress> Values { get; } = [];
        public void Report(SpeechProgress value) { Values.Add(value); callback?.Invoke(value); }
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Models.Speech;

namespace CaptionForge.Infrastructure.Speech;

/// <summary>Supervised JSON/event bridge to an isolated CPU TTS process; never uses a shell.</summary>
public sealed class PythonSpeechSynthesisService(string workerPath) : ISpeechSynthesisService
{
    // Shares the CPU between module instances, without loading multiple speech models concurrently.
    private static readonly SemaphoreSlim Gate = new(1);

    public async Task<SpeechSynthesisResult> GenerateAsync(SpeechSynthesisRequest request, IProgress<SpeechProgress>? progress, CancellationToken cancellationToken)
    {
        SpeechEngineCatalog.Validate(request);
        if (!File.Exists(workerPath)) throw new FileNotFoundException("Falta el backend del módulo de texto a voz.", workerPath);
        progress?.Report(new("Esperando el procesador…"));
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RunAsync(request, progress, cancellationToken).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    private async Task<SpeechSynthesisResult> RunAsync(SpeechSynthesisRequest request, IProgress<SpeechProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        string id = $"{request.Engine.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string output = Path.GetFullPath(Path.Combine(request.OutputDirectory, id + ".wav"));
        string logPath = Path.ChangeExtension(output, ".log");
        string jobDirectory = Path.Combine(Path.GetTempPath(), "CaptionForge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDirectory);
        string jobPath = Path.Combine(jobDirectory, "job.json");
        var job = new
        {
            engine = request.Engine switch { SpeechEngine.Pocket => "Pocket TTS", SpeechEngine.ChatterboxMultilingual => "Chatterbox Multilingual V3", _ => request.Engine.ToString() },
            language = request.Language == "es" ? "Español" : "Inglés", text = request.Text.Trim(), voice = request.Voice,
            reference = request.ReferencePath, seed = request.Seed, speed = request.Speed, exaggeration = request.Exaggeration,
            threads = request.CpuThreads, model_directory = Path.GetFullPath(request.ModelDirectory), output
        };
        using var process = new Process();
        using var log = new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true };
        SpeechSynthesisResult? result = null;
        string? failure = null;
        try
        {
            await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(job), Encoding.UTF8, ct).ConfigureAwait(false);
            process.StartInfo = new ProcessStartInfo(request.PythonExecutable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            process.StartInfo.ArgumentList.Add("-X"); process.StartInfo.ArgumentList.Add("utf8");
            process.StartInfo.ArgumentList.Add("-u"); process.StartInfo.ArgumentList.Add(Path.GetFullPath(workerPath));
            process.StartInfo.ArgumentList.Add(jobPath);
            process.StartInfo.Environment["HF_HUB_OFFLINE"] = "1";
            process.StartInfo.Environment["TRANSFORMERS_OFFLINE"] = "1";
            process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            process.StartInfo.Environment["OMP_NUM_THREADS"] = request.CpuThreads.ToString();
            ct.ThrowIfCancellationRequested();
            progress?.Report(new("Cargando el modelo en CPU…"));
            if (!process.Start()) throw new IOException("No se pudo iniciar el motor de voz.");
            // Cancellation kills the interpreter launcher and every child, including Windows venv Python.
            using var cancellation = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            });
            var stderr = process.StandardError.ReadToEndAsync();
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                await log.WriteLineAsync(line).ConfigureAwait(false);
                if (!line.StartsWith("TTS_EVENT ", StringComparison.Ordinal)) continue;
                using var doc = JsonDocument.Parse(line[10..]);
                var evt = doc.RootElement;
                if (evt.TryGetProperty("status", out var status))
                    progress?.Report(new(status.GetString() ?? "Generando…", evt.TryGetProperty("progress", out var fraction) ? fraction.GetDouble() : 0));
                if (evt.TryGetProperty("error", out var error)) failure = error.GetString();
                if (evt.TryGetProperty("output", out var path))
                {
                    if (!string.Equals(Path.GetFullPath(path.GetString()!), output, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("El motor devolvió una ruta de audio inesperada.");
                    result = new(output, evt.GetProperty("seconds").GetDouble(), evt.GetProperty("elapsed").GetDouble(), evt.GetProperty("sample_rate").GetInt32());
                }
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await log.WriteAsync(await stderr.ConfigureAwait(false)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || result is null || !File.Exists(output) || new FileInfo(output).Length <= 44 ||
                !double.IsFinite(result.DurationSeconds) || result.DurationSeconds <= 0 || result.SampleRate <= 0)
                throw new IOException($"{failure ?? "El motor no produjo un WAV válido."} Registro: {logPath}");
            await File.WriteAllTextAsync(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(new { result, request.Engine, request.Language, request.Voice, request.Seed, request.Speed, request.Exaggeration }), Encoding.UTF8, ct).ConfigureAwait(false);
            progress?.Report(new("Audio listo", 1));
            return result;
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            // Only the explicitly created job file and its empty directory are removed.
            try
            {
                if (File.Exists(jobPath)) File.Delete(jobPath);
                foreach (var language in new[] { "english", "spanish" })
                {
                    string configuration = Path.Combine(jobDirectory, language + ".yaml");
                    if (File.Exists(configuration)) File.Delete(configuration);
                }
                if (Directory.Exists(jobDirectory)) Directory.Delete(jobDirectory, recursive: false);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (ct.IsCancellationRequested && File.Exists(output)) File.Delete(output);
        }
    }
}

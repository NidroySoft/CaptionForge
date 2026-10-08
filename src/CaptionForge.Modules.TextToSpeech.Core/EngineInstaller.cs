using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace CaptionForge.Modules.TextToSpeech.Core;

public sealed record InstalledEngine(string PythonExecutable, string ModelDirectory);

/// <summary>Private, per-user installation. Does not change PATH, registry or system Python.</summary>
public sealed class EngineInstaller(string backendDirectory, string installationRoot)
{
    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CaptionForge-SpeechModule/1.0");
        return client;
    }

    public async Task<InstalledEngine> InstallAsync(SpeechEngine engine, IProgress<SpeechProgress> progress, CancellationToken ct)
    {
        string folder = Path.GetFullPath(Path.Combine(installationRoot, engine.ToString()));
        Directory.CreateDirectory(folder);
        using var installationLock = new FileStream(Path.Combine(folder, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string pythonRoot = Path.Combine(folder, "runtime");
        string bootstrap = Path.Combine(pythonRoot, "python", "python.exe");
        string runtimeReady = Path.Combine(pythonRoot, "ready.txt");
        if (!File.Exists(bootstrap) || !File.Exists(runtimeReady))
        {
            progress.Report(new("Descargando el entorno privado de Python…"));
            using var release = await Http.GetFromJsonAsync<JsonDocument>("https://api.github.com/repos/astral-sh/python-build-standalone/releases/latest", ct)
                ?? throw new IOException("No se pudo consultar el entorno de ejecución.");
            var asset = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a =>
                a.GetProperty("name").GetString() is { } n && n.StartsWith("cpython-3.11.", StringComparison.Ordinal)
                && n.EndsWith("-x86_64-pc-windows-msvc-install_only.tar.gz", StringComparison.Ordinal));
            if (asset.ValueKind == JsonValueKind.Undefined) throw new IOException("No se encuentra un entorno Python 3.11 para Windows x64.");
            string url = asset.GetProperty("browser_download_url").GetString()!;
            string archive = Path.Combine(folder, "python.tar.gz");
            string checksum;
            if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } value && value.StartsWith("sha256:", StringComparison.Ordinal)) checksum = value[7..];
            else
            {
                var sums = release.RootElement.GetProperty("assets").EnumerateArray().First(a => a.GetProperty("name").GetString() == "SHA256SUMS");
                string list = await Http.GetStringAsync(sums.GetProperty("browser_download_url").GetString()!, ct);
                checksum = list.Split('\n').Single(line => line.TrimEnd().EndsWith(asset.GetProperty("name").GetString()!, StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            }
            bool cached = false;
            if (File.Exists(archive))
            {
                using var existing = File.OpenRead(archive);
                cached = Convert.ToHexString(await SHA256.HashDataAsync(existing, ct)).Equals(checksum, StringComparison.OrdinalIgnoreCase);
            }
            if (!cached) await DownloadAsync(url, archive, progress, ct);
            using (var stream = File.OpenRead(archive))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(checksum, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("La descarga del entorno no superó la verificación SHA256.");
            Directory.CreateDirectory(pythonRoot);
            progress.Report(new("Preparando el entorno privado…"));
            await Task.Run(() => ExtractArchive(archive, pythonRoot, ct), ct);
            await File.WriteAllTextAsync(runtimeReady, url, ct);
            File.Delete(archive);
        }
        string environment = Path.Combine(folder, ".venv");
        string python = Path.Combine(environment, "Scripts", "python.exe");
        if (!File.Exists(python)) await RunAsync(bootstrap, ["-m", "venv", environment], folder, progress, ct);
        string models = Path.Combine(folder, "models");
        await RunAsync(python, ["-X", "utf8", "-u", Path.Combine(backendDirectory, "install_engine.py"), engine.ToString(), models], folder, progress, ct);
        return new(python, models);
    }

    public static void ExtractArchive(string archive, string destination, CancellationToken ct)
    {
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            string target = SafeDestination(destination, entry.Name);
            if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(target); continue; }
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new InvalidDataException("Tipo de archivo no admitido en el entorno.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = File.Create(target);
            entry.DataStream?.CopyTo(output);
        }
    }

    public static string SafeDestination(string root, string name)
    {
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(prefix, name.Replace('/', Path.DirectorySeparatorChar)));
        if (Path.IsPathRooted(name) || !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ruta fuera del directorio de instalación.");
        return target;
    }

    private static async Task DownloadAsync(string url, string path, IProgress<SpeechProgress> progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using (var output = File.Create(path + ".partial"))
        {
            var buffer = new byte[1024 * 1024]; long bytes = 0; int count;
            var clock = Stopwatch.StartNew();
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), ct); bytes += count;
                if (clock.ElapsedMilliseconds >= 400)
                {
                    progress.Report(new($"Descargando entorno: {bytes / 1048576} MB…", response.Content.Headers.ContentLength is > 0 ? bytes / (double)response.Content.Headers.ContentLength.Value : 0));
                    clock.Restart();
                }
            }
        }
        File.Move(path + ".partial", path, true);
    }

    private static async Task RunAsync(string executable, string[] arguments, string folder, IProgress<SpeechProgress> progress, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar la instalación.");
        try
        {
        using var cancel = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        await using var log = new StreamWriter(Path.Combine(folder, "installation.log"), append: true);
        var errors = process.StandardError.ReadToEndAsync();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            await log.WriteLineAsync(line);
            if (line.StartsWith("TTS_EVENT ", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(line[10..]);
                var item = doc.RootElement;
                if (item.TryGetProperty("status", out var status)) progress.Report(new(status.GetString()!, item.TryGetProperty("progress", out var fraction) ? fraction.GetDouble() : 0));
            }
            else if (line.StartsWith("Collecting ", StringComparison.Ordinal) || line.StartsWith("Downloading ", StringComparison.Ordinal) || line.StartsWith("Installing collected", StringComparison.Ordinal))
                progress.Report(new("Preparando dependencias: " + line[..Math.Min(line.Length, 140)]));
        }
        await process.WaitForExitAsync(CancellationToken.None);
        string error = await errors; await log.WriteLineAsync(error);
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException("La instalación no terminó. Consulta " + Path.Combine(folder, "installation.log"));
        }
        finally
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
    }
}

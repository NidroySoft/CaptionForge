using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CaptionForge.Modules.MediaTools.Core;

public sealed class MediaService(string ffmpeg, string ffprobe)
{
    public async Task<MediaInfo> ProbeAsync(string source, CancellationToken ct)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("No se encuentra el archivo.", source);
        string json = await RunAsync(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", source], null, ct);
        using var doc = JsonDocument.Parse(json); var tracks = new List<AudioTrack>(); double duration = 0;
        static string Text(JsonElement parent, string key) => parent.TryGetProperty(key, out var value) ? value.ToString() : "";
        static double Seconds(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
        if (doc.RootElement.TryGetProperty("format", out var format)) duration = Seconds(Text(format, "duration"));
        foreach (var stream in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            if (Text(stream, "codec_type") != "audio") continue;
            stream.TryGetProperty("tags", out var tags);
            string language = tags.ValueKind == JsonValueKind.Object ? Text(tags, "language") : "";
            string title = tags.ValueKind == JsonValueKind.Object ? Text(tags, "title") : "";
            tracks.Add(new(stream.GetProperty("index").GetInt32(), Text(stream, "codec_name"), string.IsNullOrEmpty(language) ? "sin idioma" : language, title, stream.TryGetProperty("channels", out var channels) ? channels.GetInt32() : 0, tracks.Count + 1));
            duration = Math.Max(duration, Seconds(Text(stream, "duration")));
        }
        if (tracks.Count == 0) throw new InvalidDataException("El archivo no contiene pistas de audio.");
        return new(duration, tracks);
    }
    public Task PreparePreviewAsync(string source, int track, string destination, IProgress<double>? progress, CancellationToken ct)
        => ConvertAsync(source, track, destination, null, true, progress, ct);
    public Task ExtractAsync(string source, int track, string destination, AudioRange range, IProgress<double>? progress, CancellationToken ct)
        => ConvertAsync(source, track, destination, range, false, progress, ct);
    public Task PrepareTranscriptionAsync(string source, int track, string destination, AudioRange range, IProgress<double>? progress, CancellationToken ct)
        => ConvertAsync(source, track, destination, range, true, progress, ct);
    private async Task ConvertAsync(string source, int track, string destination, AudioRange? range, bool preview, IProgress<double>? progress, CancellationToken ct)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("El destino no puede ser el original.");
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not ".wav" and not ".mp3") throw new ArgumentException("Elige WAV o MP3.");
        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        List<string> args = ["-nostdin", "-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats"];
        string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
        if (range is not null) { range.Validate(double.MaxValue); args.AddRange(["-ss", Number(range.Start)]); }
        args.AddRange(["-i", source, "-map", "0:" + track, "-vn"]);
        if (range is not null) args.AddRange(["-t", Number(range.Duration)]);
        if (preview) args.AddRange(["-ac", "1", "-ar", "16000"]);
        args.AddRange(extension == ".mp3" ? ["-c:a", "libmp3lame", "-q:a", "2", "-f", "mp3"] : ["-c:a", "pcm_s16le", "-f", "wav"]);
        args.AddRange(["-y", staging]);
        try
        {
            await RunAsync(ffmpeg, args, seconds => progress?.Report(range is { Duration: > 0 } ? Math.Min(1, seconds / range.Duration) : seconds), ct);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(staging) || new FileInfo(staging).Length < 44) throw new InvalidDataException("FFmpeg no produjo audio.");
            File.Move(staging, destination, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, Action<double>? seconds, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException("No se pudo iniciar " + executable);
            var errorTask = process.StandardError.ReadToEndAsync();
            var output = new StringBuilder();
            async Task Read()
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (seconds is not null)
                    {
                        if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out long time)) seconds(time / 1_000_000d);
                    }
                    else { if (output.Length > 16_000_000) throw new InvalidDataException("Respuesta de herramienta demasiado grande."); output.AppendLine(line); }
                }
            }
            var outputTask = Read();
            void Kill() { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
            using var registration = ct.Register(Kill);
            try
            {
                var exitTask = process.WaitForExitAsync(ct);
                // A failed pipe reader must also terminate the child to avoid filling its pipe.
                if (await Task.WhenAny(exitTask, outputTask) == outputTask && outputTask.IsFaulted) Kill();
                await exitTask; await outputTask;
            }
            finally
            {
                Kill(); await process.WaitForExitAsync();
                try { await outputTask; } catch when (ct.IsCancellationRequested) { }
                await errorTask;
            }
            string error = await errorTask; ct.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidDataException("La herramienta no pudo procesar el archivo: " + error[..Math.Min(error.Length, 2000)]);
            return output.ToString();
        }
        finally { try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
    }
}

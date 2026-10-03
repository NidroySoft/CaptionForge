using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.CapCut;

namespace CaptionForge.Infrastructure.Internal;

internal static class JsonFiles
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() }
    };
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static JsonObject Parse(byte[] bytes)
    {
        ReadOnlySpan<byte> value = bytes;
        if (value.StartsWith(new byte[] { 239, 187, 191 })) value = value[3..];
        return JsonNode.Parse(value) as JsonObject ?? throw new InvalidDataException("Se esperaba un objeto JSON.");
    }
    internal static JsonNode Node<T>(T value) => JsonSerializer.SerializeToNode(value, Options)!;
    internal static T Read<T>(JsonNode node) => node.Deserialize<T>(Options) ?? throw new InvalidDataException("Documento vacío.");
    internal static byte[] Bytes(JsonNode node) => JsonSerializer.SerializeToUtf8Bytes(node, Options);
    internal static async Task<JsonObject> ReadObjectAsync(string path, CancellationToken ct = default) => Parse(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
    internal static async Task<SourceFileStamp> StampAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return new(path, false);
        return new(path, true, Hash(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false)));
    }
    internal static async Task VerifyAsync(IEnumerable<SourceFileStamp> stamps, CancellationToken ct = default)
    {
        foreach (var expected in stamps)
        {
            var actual = await StampAsync(expected.Path, ct).ConfigureAwait(false);
            if (expected.Exists != actual.Exists || !string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged, $"Cambió el archivo: {expected.Path}");
        }
    }
    internal static async Task AtomicWriteAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(staging, path, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    internal static Task WriteAsync(string path, JsonNode node, CancellationToken ct = default) => AtomicWriteAsync(path, Bytes(node), ct);
    internal static string String(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? throw new InvalidDataException($"Falta {key}.");
    internal static long Long(JsonNode? node, string key) => node?[key]?.GetValue<long>() ?? throw new InvalidDataException($"Falta {key}.");
    internal static JsonArray Array(JsonNode? node, string key) => node?[key] as JsonArray ?? throw new InvalidDataException($"Falta el array {key}.");
    internal static SourceFileStamp ReadStamp(JsonNode node) => new(String(node,"path"), node["exists"]!.GetValue<bool>(), node["sha256"]?.GetValue<string>());
    internal static bool Equivalent(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is JsonObject ao && b is JsonObject bo)
            return ao.Count == bo.Count && ao.All(p => bo.TryGetPropertyValue(p.Key, out var other) && Equivalent(p.Value, other));
        if (a is JsonArray aa && b is JsonArray ba)
            return aa.Count == ba.Count && aa.Zip(ba).All(p => Equivalent(p.First,p.Second));
        if (a.GetValueKind() == JsonValueKind.Number && b.GetValueKind() == JsonValueKind.Number)
        {
            decimal x = a.GetValue<decimal>(), y = b.GetValue<decimal>();
            return x == y || (x != decimal.Truncate(x) && y != decimal.Truncate(y) && Math.Abs(x-y) <= 0.0000000001m);
        }
        return JsonNode.DeepEquals(a,b);
    }
}

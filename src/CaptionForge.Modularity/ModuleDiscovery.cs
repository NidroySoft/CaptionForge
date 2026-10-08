using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace CaptionForge.Modularity;

public sealed record ModuleManifest(string Id, string DisplayName, string Assembly, string EntryType, int ContractVersion = 1);

/// <summary>Discovers installed modules. The host depends only on this contract, never on their implementation.</summary>
public sealed class ModuleDiscovery
{
    private readonly List<string> _directories = [];
    public ModuleDiscovery() => AssemblyLoadContext.Default.Resolving += Resolve;

    private Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is null || name.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        foreach (string directory in _directories)
        {
            string file = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(file)) return context.LoadFromAssemblyPath(file);
        }
        return null;
    }

    public IReadOnlyList<string> RegisterFrom(string directory, ModuleRegistry registry)
    {
        List<string> errors = [];
        if (!Directory.Exists(directory)) return errors;
        foreach (var folder in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            string manifestFile = Path.Combine(folder, "module.json");
            if (!File.Exists(manifestFile)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(manifestFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("Manifest vacío.");
                if (manifest.ContractVersion != 1) throw new InvalidDataException("Contrato de módulo incompatible.");
                var assembly = LocalFile(folder, manifest.Assembly);
                if (!File.Exists(assembly)) throw new FileNotFoundException("Falta el ensamblado del módulo.", assembly);
                ArgumentException.ThrowIfNullOrWhiteSpace(manifest.EntryType);
                _directories.Add(Path.GetFullPath(folder));
                registry.Register(new(manifest.Id, manifest.DisplayName, () =>
                {
                    var type = AssemblyLoadContext.Default.LoadFromAssemblyPath(assembly).GetType(manifest.EntryType, throwOnError: true)!;
                    if (!typeof(IApplicationModule).IsAssignableFrom(type)) throw new InvalidDataException("El módulo no implementa el contrato.");
                    return (IApplicationModule)(Activator.CreateInstance(type) ?? throw new InvalidDataException("No se pudo crear el módulo."));
                }));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            { errors.Add($"{Path.GetFileName(folder)}: {e.Message}"); }
        }
        return errors;
    }

    public static string LocalFile(string directory, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La ruta del módulo debe estar dentro de su carpeta.");
        return path;
    }
}

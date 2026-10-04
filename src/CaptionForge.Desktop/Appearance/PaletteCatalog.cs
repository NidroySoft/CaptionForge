using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaptionForge.Desktop.Appearance;

public sealed record PaletteColors(string Background, string Surface, string Raised, string Input, string Border,
    string Text, string Muted, string Accent, string AccentText, string Selection, string Warning, string ScrollThumb);

public sealed record AppearancePalette(string Id, string Name, Dictionary<AppearanceMode, PaletteColors> Variants)
{
    public bool Supports(AppearanceMode mode) => Variants.ContainsKey(mode);
    public string DisplayName => Variants.Count == 2 ? $"{Name} · común" : Name;
}

public sealed class PaletteCatalog
{
    private sealed record Document(int SchemaVersion, AppearancePalette[] Palettes);
    public IReadOnlyList<AppearancePalette> Palettes { get; }
    public const string DefaultId = "shared-teal";
    private PaletteCatalog(AppearancePalette[] palettes) => Palettes = Array.AsReadOnly(palettes);

    public static PaletteCatalog Load(Stream stream)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var document = JsonSerializer.Deserialize<Document>(stream, options) ?? throw new InvalidDataException("No se pudieron leer las paletas.");
        if (document.SchemaVersion != 1 || document.Palettes is not { Length: > 0 }) throw new InvalidDataException("Catálogo de paletas inválido.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var palette in document.Palettes)
        {
            if (string.IsNullOrWhiteSpace(palette.Id) || string.IsNullOrWhiteSpace(palette.Name) || !ids.Add(palette.Id) ||
                palette.Variants is null || palette.Variants.Count == 0 || palette.Variants.Keys.Any(m => !Enum.IsDefined(m)))
                throw new InvalidDataException("Una paleta tiene una identidad o compatibilidad inválida.");
            foreach (var colors in palette.Variants.Values)
            {
                if (colors is null || typeof(PaletteColors).GetProperties().Any(p => p.GetValue(colors) is not string color ||
                    color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)))
                    throw new InvalidDataException($"Los colores de la paleta {palette.Name} no son válidos.");
            }
        }
        if (!document.Palettes.Any(p => p.Id == DefaultId && p.Supports(AppearanceMode.Dark) && p.Supports(AppearanceMode.Light)))
            throw new InvalidDataException("Falta la paleta predeterminada compatible con ambos modos.");
        return new(document.Palettes);
    }

    public AppearancePalette Resolve(string? id, AppearanceMode mode) =>
        Palettes.FirstOrDefault(p => p.Id == id && p.Supports(mode)) ?? Palettes.Single(p => p.Id == DefaultId);
}

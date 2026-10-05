using System.Collections;
using System.IO;
using System.Reflection;
using System.Resources;
namespace CaptionForge.Desktop.Localization;

/// <summary>Lee la copia integrada sin la resolución de URI de WPF, que prioriza Content.</summary>
public static class EmbeddedLanguageResource
{
    public static Stream Open(Assembly assembly)
    {
        using var resources = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources")
            ?? throw new InvalidDataException("Languages/es.json: falta el recurso integrado.");
        using var reader = new ResourceReader(resources);
        foreach (DictionaryEntry entry in reader)
        {
            if (entry.Key is not string key || key != "languages/es.json" || entry.Value is not Stream source)
                continue;
            var copy = new MemoryStream();
            source.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }
        throw new InvalidDataException("Languages/es.json: falta el recurso integrado.");
    }
}

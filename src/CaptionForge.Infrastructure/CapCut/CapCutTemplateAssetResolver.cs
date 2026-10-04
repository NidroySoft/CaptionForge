using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Infrastructure.Configuration;

namespace CaptionForge.Infrastructure.CapCut;

public static class CapCutTemplateAssetResolver
{
    /// <summary>Busca la caché de los cuatro recursos golden; si hay ambigüedad necesita rutas explícitas.</summary>
    public static TemplateAssetPaths Resolve(string? capCutUserData = null)
    {
        string effect=Path.Combine(capCutUserData ?? InfrastructurePaths.DefaultCapCutUserData,"Cache","effect");
        string template=DirectoryFor(effect,"7535399757947161873","ae92e3cb3480090dfea046d1283495cf");
        string flower=DirectoryFor(effect,"239386920","b7bae96650ebe1f0fbdf8825910dd311");
        string animation=DirectoryFor(effect,"110456508","d62a12a386bf578a8eb03187095d7c7d");
        string fonts=Path.Combine(effect,"7517426090072149264");
        var candidates=Directory.Exists(fonts) ? Directory.GetFiles(fonts,"BebasNeue-Regular.ttf",SearchOption.AllDirectories) : Array.Empty<string>();
        if (candidates.Length!=1) throw new CaptionForgeOperationException(OperationErrorCode.ResourceUnavailable,"No se encontró una única fuente Bebas Neue del recurso golden. Indica sus rutas explícitas.");
        var result=new TemplateAssetPaths(template,candidates[0],flower,animation);result.Validate();return result;
    }
    private static string DirectoryFor(string root,string container,string knownHash)
    {
        string parent=Path.Combine(root,container),known=Path.Combine(parent,knownHash);
        if (Directory.Exists(known)) return known;
        var candidates=Directory.Exists(parent)?Directory.GetDirectories(parent):Array.Empty<string>();
        if (candidates.Length==1) return candidates[0];
        throw new CaptionForgeOperationException(OperationErrorCode.ResourceUnavailable,$"No se encontró un recurso único en {parent}. Aplica/descarga la plantilla golden en CapCut o indica sus rutas.");
    }
}

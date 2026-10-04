using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.CapCut;

public sealed record TemplateAssetPaths
{
    public string TemplateDirectory { get; }
    public string FontFile { get; }
    public string EffectDirectory { get; }
    public string AnimationDirectory { get; }
    public TemplateAssetPaths(string templateDirectory,string fontFile,string effectDirectory,string animationDirectory)
    { TemplateDirectory=PathSafety.Full(templateDirectory);FontFile=PathSafety.Full(fontFile);EffectDirectory=PathSafety.Full(effectDirectory);AnimationDirectory=PathSafety.Full(animationDirectory); }
    public void Validate()
    {
        foreach (string directory in new[] {TemplateDirectory,EffectDirectory,AnimationDirectory})
            if (!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any())
                throw new CaptionForgeOperationException(OperationErrorCode.ResourceUnavailable,$"Falta el recurso de CapCut: {directory}");
        if (!File.Exists(FontFile) || new FileInfo(FontFile).Length==0)
            throw new CaptionForgeOperationException(OperationErrorCode.ResourceUnavailable,$"Falta la fuente Bebas Neue: {FontFile}");
    }
}

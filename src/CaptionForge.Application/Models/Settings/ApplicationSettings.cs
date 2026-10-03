using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Settings;

/// <summary>Preferencias MVP, sin imponer directorios o modelos antes de concretarlos con Infrastructure.</summary>
public sealed record ApplicationSettings
{
    public string? ProjectsRoot { get; }
    public string? WorkspaceRoot { get; }
    public string? ModelPath { get; }
    public string Language { get; }
    public int? CpuThreads { get; }

    public ApplicationSettings(string? projectsRoot = null, string? workspaceRoot = null, string? modelPath = null,
        string language = "auto", int? cpuThreads = null)
    {

        if (projectsRoot is not null) Guard.Text(projectsRoot, nameof(projectsRoot));
        if (workspaceRoot is not null) Guard.Text(workspaceRoot, nameof(workspaceRoot));
        if (modelPath is not null) Guard.Text(modelPath, nameof(modelPath));
        Guard.Text(language, nameof(language));
        if (cpuThreads is < 1) throw new ArgumentOutOfRangeException(nameof(cpuThreads));
        ProjectsRoot = projectsRoot;
        WorkspaceRoot = workspaceRoot;
        ModelPath = modelPath;
        Language = language;
        CpuThreads = cpuThreads;
    }

}

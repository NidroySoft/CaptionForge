using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Generation;

/// <summary>Opciones explícitas para Whisper.net; auto se permite como idioma y DTW se exige en esta versión.</summary>
public sealed record TranscriptionOptions
{
    public string ModelName { get; }
    public string ModelPath { get; }
    public string Language { get; }
    public int CpuThreads { get; }
    public int LogicalProcessorCount { get; }

    public TranscriptionOptions(string modelName, string modelPath, string language, int cpuThreads, int logicalProcessorCount)
    {

        Guard.Text(modelName, nameof(modelName));
        Guard.Text(modelPath, nameof(modelPath));
        Guard.Text(language, nameof(language));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalProcessorCount);
        if (cpuThreads < 1 || cpuThreads > logicalProcessorCount)
            throw new ArgumentOutOfRangeException(nameof(cpuThreads));
        ModelName = modelName;
        ModelPath = modelPath;
        Language = language;
        CpuThreads = cpuThreads;
        LogicalProcessorCount = logicalProcessorCount;
    }

    /// <summary>El adaptador debe activar alineación DTW y devolver tokens completos normalizados.</summary>
    public bool RequireDtwAlignment => true;

}

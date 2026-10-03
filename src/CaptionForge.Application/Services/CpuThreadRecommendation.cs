namespace CaptionForge.Application.Services;

public static class CpuThreadRecommendation
{
    /// <summary>75 % de los hilos lógicos, redondeado hacia abajo; siempre al menos uno.</summary>
    public static int Suggest(int logicalProcessorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalProcessorCount);
        return Math.Max(1, (int)((long)logicalProcessorCount * 3 / 4));
    }

    /// <summary>Revalida una preferencia al cambiar de equipo, sin crear opciones fuera del rango disponible.</summary>
    public static int ResolveSaved(int? savedThreads, int logicalProcessorCount)
    {
        int suggested = Suggest(logicalProcessorCount);
        return savedThreads is >= 1 ? Math.Min(savedThreads.Value, logicalProcessorCount) : suggested;
    }
}

using CaptionForge.Application.Enums;

namespace CaptionForge.Application.Exceptions;

/// <summary>Error accionable para el futuro ViewModel. Conserva causa y ejecución cuando existen.</summary>
public sealed class CaptionForgeOperationException : Exception
{
    public OperationErrorCode Code { get; }
    public string? RunId { get; }

    public CaptionForgeOperationException(OperationErrorCode code, string message,
        string? runId = null, Exception? innerException = null) : base(message, innerException)
    {
        if (!Enum.IsDefined(code)) throw new ArgumentOutOfRangeException(nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        RunId = runId;
    }
}

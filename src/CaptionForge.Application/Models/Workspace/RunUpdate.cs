using CaptionForge.Application.Enums;
namespace CaptionForge.Application.Models.Workspace;

/// <summary>Transición persistida con estado esperado: impide aplicar dos veces la misma ejecución.</summary>
public sealed record RunUpdate
{
    public RunStatus ExpectedStatus { get; }
    public RunStatus Status { get; }
    public DateTimeOffset At { get; }
    public string? Error { get; }

    public RunUpdate(RunStatus expectedStatus, RunStatus status, DateTimeOffset at, string? error = null)
    {

        if (!Enum.IsDefined(expectedStatus)) throw new ArgumentOutOfRangeException(nameof(expectedStatus));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ExpectedStatus = expectedStatus;
        Status = status;
        At = at;
        Error = error;
    }

}

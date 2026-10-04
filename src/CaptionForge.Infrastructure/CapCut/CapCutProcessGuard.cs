using System.Diagnostics;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;

namespace CaptionForge.Infrastructure.CapCut;

public sealed class CapCutProcessGuard : ICapCutProcessGuard
{
    public void EnsureClosed()
    {
        foreach (string name in new[] { "CapCut","CapCut.exe","CapCutEditor" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process) throw new CaptionForgeOperationException(OperationErrorCode.CapCutOpen,"Cierra CapCut antes de aplicar o restaurar el proyecto.");
    }
}

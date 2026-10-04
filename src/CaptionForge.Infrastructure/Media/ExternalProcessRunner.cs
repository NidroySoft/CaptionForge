using System.Diagnostics;
using System.ComponentModel;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;

namespace CaptionForge.Infrastructure.Media;

internal static class ExternalProcessRunner
{
    internal sealed record Result(int ExitCode,string Output,string Error);
    internal static async Task<Result> RunAsync(string executable,IEnumerable<string> arguments,CancellationToken ct)
    {
        var start=new ProcessStartInfo(executable) { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process=new Process { StartInfo=start };
        try { if (!process.Start()) throw new IOException("No se pudo iniciar el proceso."); }
        catch (Win32Exception ex) { throw new CaptionForgeOperationException(OperationErrorCode.ResourceUnavailable,$"No se puede ejecutar {executable}. Comprueba su ruta.",innerException:ex); }
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree:true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);await Task.WhenAll(output,error).ConfigureAwait(false);throw;
        }
        return new(process.ExitCode,await output.ConfigureAwait(false),await error.ConfigureAwait(false));
    }
}

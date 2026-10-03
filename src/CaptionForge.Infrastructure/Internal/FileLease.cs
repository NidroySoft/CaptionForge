using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;

namespace CaptionForge.Infrastructure.Internal;

internal sealed class FileLease : IDisposable
{
    private readonly FileStream _stream;
    private FileLease(FileStream stream) => _stream=stream;
    internal static async Task<FileLease> AcquireAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (int attempt=0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return new(new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)); }
            catch (IOException) when (attempt < 100) { await Task.Delay(25,ct).ConfigureAwait(false); }
            catch (IOException ex) { throw new CaptionForgeOperationException(OperationErrorCode.RunStateConflict,"Otra operación está usando este proyecto o ejecución.",innerException:ex); }
        }
    }
    public void Dispose() => _stream.Dispose();
}

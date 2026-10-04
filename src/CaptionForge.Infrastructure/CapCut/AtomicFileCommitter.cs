using CaptionForge.Infrastructure.Internal;
namespace CaptionForge.Infrastructure.CapCut;

public sealed class AtomicFileCommitter : IFileCommitter
{ public Task ReplaceAsync(string path,byte[] bytes) => JsonFiles.AtomicWriteAsync(path,bytes); }

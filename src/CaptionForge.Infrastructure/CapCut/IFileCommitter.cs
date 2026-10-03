namespace CaptionForge.Infrastructure.CapCut;

/// <summary>Reemplazo de un archivo durante commit, sin cancelación intermedia. Permite verificar fallos parciales.</summary>
public interface IFileCommitter { Task ReplaceAsync(string path,byte[] bytes); }

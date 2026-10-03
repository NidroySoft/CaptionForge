namespace CaptionForge.Infrastructure.CapCut;

/// <summary>Seam para comprobar procesos sin introducir una dependencia Windows en las pruebas de archivos.</summary>
public interface ICapCutProcessGuard { void EnsureClosed(); }

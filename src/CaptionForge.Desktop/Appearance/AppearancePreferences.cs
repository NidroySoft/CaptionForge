namespace CaptionForge.Desktop.Appearance;

public enum AppearanceMode { Dark, Light }

/// <summary>Preferencias de escritorio; independientes del idioma y modelo de transcripción.</summary>
public sealed record AppearancePreferences(bool FollowSystem = false, AppearanceMode ManualMode = AppearanceMode.Dark,
    string DarkPaletteId = "shared-teal", string LightPaletteId = "shared-teal");

namespace CaptionForge.Desktop.Appearance;

/// <summary>Selección y compatibilidad, sin dependencia de WPF ni del registro de Windows.</summary>
public sealed class AppearanceState
{
    private readonly PaletteCatalog _catalog;
    public AppearancePreferences Preferences { get; private set; }
    public AppearanceMode Mode { get; private set; }
    public AppearancePalette Palette { get; private set; }
    public AppearanceState(PaletteCatalog catalog, AppearancePreferences preferences, AppearanceMode systemMode)
    {
        _catalog = catalog;
        Preferences = preferences with { ManualMode = Enum.IsDefined(preferences.ManualMode) ? preferences.ManualMode : AppearanceMode.Dark };
        Mode = Preferences.FollowSystem ? systemMode : Preferences.ManualMode;
        Palette = catalog.Resolve(SavedId(Mode), Mode);
        Remember();
    }
    public IReadOnlyList<AppearancePalette> Available => _catalog.Palettes.Where(p => p.Supports(Mode)).ToArray();
    public void SetFollowSystem(bool follow, AppearanceMode systemMode)
    {
        Preferences = Preferences with { FollowSystem = follow, ManualMode = follow ? Preferences.ManualMode : Mode };
        ChangeMode(follow ? systemMode : Mode);
    }
    public void Toggle()
    {
        var next = Mode == AppearanceMode.Dark ? AppearanceMode.Light : AppearanceMode.Dark;
        Preferences = Preferences with { FollowSystem = false, ManualMode = next };
        ChangeMode(next);
    }
    public void UpdateSystemMode(AppearanceMode mode)
    { if (Preferences.FollowSystem) ChangeMode(mode); }
    public void Select(AppearancePalette palette)
    {
        if (!_catalog.Palettes.Any(p => ReferenceEquals(p, palette)) || !palette.Supports(Mode))
            throw new ArgumentException("La paleta no es compatible con el modo activo.", nameof(palette));
        Palette = palette;
        Remember();
    }
    private string SavedId(AppearanceMode mode) => mode == AppearanceMode.Dark ? Preferences.DarkPaletteId : Preferences.LightPaletteId;
    private void ChangeMode(AppearanceMode mode)
    {
        Mode = mode;
        if (!Palette.Supports(mode)) Palette = _catalog.Resolve(SavedId(mode), mode);
        Remember();
    }
    private void Remember() => Preferences = Mode == AppearanceMode.Dark
        ? Preferences with { DarkPaletteId = Palette.Id } : Preferences with { LightPaletteId = Palette.Id };
}

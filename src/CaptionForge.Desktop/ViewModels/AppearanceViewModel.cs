using CaptionForge.Desktop.Localization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CaptionForge.Desktop.Appearance;
using CaptionForge.Desktop.Mvvm;
using CaptionForge.Desktop.Services;
using Microsoft.Win32;

namespace CaptionForge.Desktop.ViewModels;

public sealed class PaletteChoice(AppearancePalette palette,string accent) : ObservableObject
{
    public AppearancePalette Palette {get;}=palette;
    public string Accent {get;}=accent;
    public string DisplayName => Palette.Variants.Count==2?L.F("palette.common",L.T("palette."+Palette.Id)):L.T("palette."+Palette.Id);
}

/// <summary>Recursos dinámicos y preferencias de apariencia, sin tocar el resultado ni las opciones de Whisper.</summary>
public sealed class AppearanceViewModel : ObservableObject
{
    private readonly AppearanceState _state;
    private readonly AppearancePreferencesStore _store;
    private IReadOnlyList<PaletteChoice> _choices = [];
    private string _notice = "";
    public event EventHandler? ThemeChanged;
    public bool IsDark => _state.Mode == AppearanceMode.Dark;
    public bool FollowSystem
    {
        get => _state.Preferences.FollowSystem;
        set { if (value != FollowSystem) { _state.SetFollowSystem(value, ReadSystemMode()); Apply(); } }
    }
    public IReadOnlyList<PaletteChoice> AvailablePalettes => _choices;
    public PaletteChoice SelectedPalette
    {
        get => _choices.Single(p => ReferenceEquals(p.Palette, _state.Palette));
        set
        {
            if (value is not null && !ReferenceEquals(value.Palette, _state.Palette))
            { _state.Select(value.Palette); Apply(); }
        }
    }
    public string ModeLabel => L.F("ui.modo01", (IsDark ? L.T("appearance.dark") : L.T("appearance.light")), (FollowSystem ? L.T("appearance.system") : L.T("appearance.manual")));
    public string ToggleLabel => IsDark ? L.T("ui.claro") : L.T("ui.oscuro");
    public string ToggleHint => L.F("ui.usarModo0DesactivaElSeguimientoDelTemaDe", (IsDark ? L.T("appearance.light") : L.T("appearance.dark")));
    public string PreferenceNotice { get => L.Render(_notice); private set => Set(ref _notice, value); }
    public ICommand ToggleTheme { get; }

    public AppearanceViewModel()
    {
        _store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptionForge", "appearance.json"));
        AppearancePreferences preferences;
        try { preferences = _store.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { AppLog.Write(ex); preferences = new(); PreferenceNotice = L.T("ui.noSePudieronLeerLasPreferenciasDeAparienciaSe"); }
        using var stream = System.Windows.Application.GetResourceStream(new Uri("Resources/Palettes.json", UriKind.Relative))?.Stream
            ?? throw new InvalidDataException(L.T("ui.noSeEncuentraElCatalogoDePaletas"));
        _state = new(PaletteCatalog.Load(stream), preferences, ReadSystemMode());
        ToggleTheme = new RelayCommand(_ => { _state.Toggle(); Apply(); });
        Apply(save: false);
    }

    public void RefreshSystemTheme()
    {
        if (!FollowSystem) return;
        var mode = ReadSystemMode();
        if (mode == _state.Mode) return;
        _state.UpdateSystemMode(mode);
        Apply();
    }

    private static AppearanceMode ReadSystemMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? AppearanceMode.Dark : AppearanceMode.Light;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { AppLog.Write(ex); return AppearanceMode.Dark; }
    }

    private void Apply(bool save = true)
    {
        var c = _state.Palette.Variants[_state.Mode];
        var resources = System.Windows.Application.Current.Resources;
        void Brush(object key, string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze(); resources[key] = brush;
        }
        foreach (var pair in new Dictionary<string, string>
        {
            ["BackgroundBrush"] = c.Background, ["SurfaceBrush"] = c.Surface, ["RaisedBrush"] = c.Raised,
            ["InputBrush"] = c.Input, ["BorderBrush"] = c.Border, ["TextBrush"] = c.Text, ["MutedBrush"] = c.Muted,
            ["AccentBrush"] = c.Accent, ["AccentTextBrush"] = c.AccentText, ["SelectionBrush"] = c.Selection,
            ["WarningBrush"] = c.Warning, ["ScrollThumbBrush"] = c.ScrollThumb, ["AlternateRowBrush"] = c.Raised,
            ["CoverBrush"] = c.Input, ["BrandTextBrush"] = IsDark ? "#C4A8FF" : "#6D28D9"
        }) Brush(pair.Key, pair.Value);
        Brush(SystemColors.WindowBrushKey, c.Input); Brush(SystemColors.WindowTextBrushKey, c.Text);
        Brush(SystemColors.ControlBrushKey, c.Surface); Brush(SystemColors.ControlTextBrushKey, c.Text);
        Brush(SystemColors.HighlightBrushKey, c.Selection); Brush(SystemColors.HighlightTextBrushKey, c.Text);
        Brush(SystemColors.InactiveSelectionHighlightBrushKey, c.Selection);
        Brush(SystemColors.InactiveSelectionHighlightTextBrushKey, c.Text);
        _choices = _state.Available.Select(p => new PaletteChoice(p, p.Variants[_state.Mode].Accent)).ToArray();
        Raise(nameof(IsDark)); Raise(nameof(FollowSystem)); Raise(nameof(AvailablePalettes)); Raise(nameof(SelectedPalette));
        Raise(nameof(ModeLabel)); Raise(nameof(ToggleLabel)); Raise(nameof(ToggleHint));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (!save) return;
        try { _store.Save(_state.Preferences); PreferenceNotice = ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write(ex); PreferenceNotice = L.T("ui.elTemaSeHaAplicadoPeroNoSePudo"); }
    }
}

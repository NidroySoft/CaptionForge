using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CaptionForge.Desktop.ViewModels;
using CaptionForge.Desktop.Guidance;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.Views;
using CaptionForge.Modularity;
using CaptionForge.Desktop.Modules;
using System.Windows.Controls;

namespace CaptionForge.Desktop;

public partial class MainWindow : Window
{
    private const double PreferredWidth = 1080, PreferredHeight = 740;
    private const double MinimumWidth = 860, MinimumHeight = 600;
    private bool _readyToClose, _closing, _fitPending;
    private HwndSource? _source;
    private AppearanceViewModel? _appearance;
    private TutorialCoordinator? _tutorial;
    private ModuleRegistry? _modules;
    private readonly ModuleDiscovery _discovery = new();
    private IApplicationModule? _activeModule;
    private ModuleDefinition? _activeDefinition;
    private readonly HashSet<INotifyPropertyChanged> _moduleNotifiers = [];
    private bool _changingModule;
    private readonly Dictionary<string, TabItem> _openTabs = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        // Fit before Show(), so even the first frame includes the native caption bar.
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinimumWidth, Math.Max(1, work.Width - 32));
        MinHeight = Math.Min(MinimumHeight, Math.Max(1, work.Height - 32));
        Width = Math.Min(PreferredWidth, Math.Max(1, work.Width - 32));
        Height = Math.Min(PreferredHeight, Math.Max(1, work.Height - 32));
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        DataContextChanged += (_, _) =>
        {
            if (_appearance is not null) _appearance.ThemeChanged -= OnThemeChanged;
            _appearance = (DataContext as MainViewModel)?.Appearance;
            if (_appearance is not null) _appearance.ThemeChanged += OnThemeChanged;
            if (_source is not null) ApplyTitleBarTheme(_source.Handle);
            // Reparent after WPF finishes propagating the new DataContext to the existing tree.
            // Moving the view inside DataContextChanged can leave child bindings on the old context.
            if (DataContext is MainViewModel vm && _modules is null)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (_modules is null && !_readyToClose && ReferenceEquals(DataContext, vm)) InitializeModules(vm);
                }));
        };
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        StateChanged += (_, _) => { if (WindowState == WindowState.Normal) ScheduleFit(); };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _tutorial?.Dispose();
            _source?.RemoveHook(WindowMessages);
            if (_appearance is not null) _appearance.ThemeChanged -= OnThemeChanged;
            foreach (var notifier in _moduleNotifiers) notifier.PropertyChanged -= ModuleStateChanged;
        };
    }

    private void InitializeModules(MainViewModel vm)
    {
        // Only the built-in subtitle workflow belongs to the host. Optional tools are discovered.
        _modules = new ModuleRegistry();
        RootLayout.DataContext = vm;
        _modules.Register(new("subtitles", "Obtener subtítulos", () => new SubtitleModule(vm, RootLayout)));
        foreach (var error in _discovery.RegisterFrom(System.IO.Path.Combine(AppContext.BaseDirectory, "Modules"), _modules))
            Services.AppLog.Write(new InvalidOperationException(error));
        ModuleContainer.Children.Remove(RootLayout);
        ModuleSelector.ItemsSource = _modules.Definitions;
        ModuleSelector.SelectedIndex = 0;
    }

    private void ModuleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingModule || _modules is null || ModuleSelector.SelectedItem is not ModuleDefinition definition) return;
        try
        {
            if (!_openTabs.TryGetValue(definition.Id, out var tab))
            {
                if (_modules.Get(definition.Id).View is not FrameworkElement) throw new InvalidOperationException("El módulo no proporciona una vista de escritorio.");
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                header.Children.Add(new TextBlock { Text = definition.DisplayName, VerticalAlignment = VerticalAlignment.Center });
                var close = new Button { Content = "×", Width = 26, Height = 26, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(12,0,0,0), ToolTip = "Cerrar pestaña" };
                tab = new TabItem { Header = header, Tag = definition };
                close.Click += (_, args) => { args.Handled = true; CloseModuleTab(definition.Id); };
                header.Children.Add(close); _openTabs.Add(definition.Id, tab); ModuleTabs.Items.Add(tab);
            }
            ModuleTabs.SelectedItem = tab;
        }
        catch (Exception ex)
        {
            Services.AppLog.Write(ex);
            MessageBox.Show(this, ex.Message, "Módulos", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _changingModule = true; ModuleSelector.SelectedIndex = -1; _changingModule = false; }
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, ModuleTabs) || _modules is null) return;
        _activeModule?.Deactivate();
        _activeModule = null; _activeDefinition = null;
        if (ModuleTabs.SelectedItem is TabItem { Tag: ModuleDefinition definition })
        {
            var module = _modules.Get(definition.Id);
            _activeModule = module; _activeDefinition = definition;
            ModuleHost.Content = module.View;
            if ((module.View as FrameworkElement)?.DataContext is INotifyPropertyChanged notifier && _moduleNotifiers.Add(notifier))
                notifier.PropertyChanged += ModuleStateChanged;
        }
        else ModuleHost.Content = null;
        EmptyModules.Visibility = _activeModule is null ? Visibility.Visible : Visibility.Collapsed;
        ModuleStateChanged(null, new(null));
    }

    public void CloseModuleTab(string id)
    {
        if (_modules is null || !_openTabs.TryGetValue(id, out var tab)) return;
        if (_modules.Get(id).IsBusy) return;
        _modules.Get(id).Deactivate();
        _openTabs.Remove(id); ModuleTabs.Items.Remove(tab);
        if (ModuleTabs.SelectedIndex < 0 && ModuleTabs.Items.Count > 0) ModuleTabs.SelectedIndex = 0;
    }

    private void ModuleStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closing || _modules is null) return;
        foreach (var (id, tab) in _openTabs)
            if (tab.Header is StackPanel header && header.Children.OfType<Button>().FirstOrDefault() is { } close)
                close.IsEnabled = !_modules.Get(id).IsBusy;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessages);
        ApplyTitleBarTheme(handle);
        FitToMonitor(center: true);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    { if (_source is not null) ApplyTitleBarTheme(_source.Handle); }

    private void ApplyTitleBarTheme(nint handle)
    {
        var enabled = _appearance?.IsDark == false ? 0 : 1;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) < 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        int ColorRef(string resource)
        {
            var color = ((System.Windows.Media.SolidColorBrush)FindResource(resource)).Color;
            return color.R | color.G << 8 | color.B << 16;
        }
        var caption = ColorRef("BackgroundBrush");
        var text = ColorRef("TextBrush");
        var border = ColorRef("BorderBrush");
        // Windows 11 admite colores explícitos; Windows 10 conserva el modo nativo claro/oscuro.
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    private nint WindowMessages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Let WPF handle DPI first, then fit its resulting rectangle to this monitor.
        if (message is 0x001A or 0x031A or 0x0320)
        {
            _appearance?.RefreshSystemTheme();
            ApplyTitleBarTheme(hwnd);
        }
        if (message is 0x02E0 or 0x007E or 0x001A) ScheduleFit();
        return 0;
    }

    private void ScheduleFit()
    {
        if (_fitPending || _readyToClose) return;
        _fitPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _fitPending = false;
            if (!_readyToClose) FitToMonitor(center: false);
        }));
    }

    private void FitToMonitor(bool center)
    {
        if (WindowState != WindowState.Normal || _source?.CompositionTarget is not { } target) return;
        var handle = _source.Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info) || !GetWindowRect(handle, out var current)) return;
        var scale = target.TransformToDevice;
        var scaleX = Math.Max(0.1, scale.M11);
        var scaleY = Math.Max(0.1, scale.M22);
        var gapX = (int)Math.Round(16 * scaleX);
        var gapY = (int)Math.Round(16 * scaleY);
        var maxWidth = Math.Max(1, info.Work.Right - info.Work.Left - 2 * gapX);
        var maxHeight = Math.Max(1, info.Work.Bottom - info.Work.Top - 2 * gapY);
        MinWidth = Math.Min(MinimumWidth, maxWidth / scaleX);
        MinHeight = Math.Min(MinimumHeight, maxHeight / scaleY);
        // Physical pixels include the caption bar and frame, unlike a content-size check.
        var width = Math.Min(maxWidth, Math.Max((int)Math.Ceiling(MinWidth * scaleX),
            center ? (int)Math.Round(PreferredWidth * scaleX) : current.Right - current.Left));
        var height = Math.Min(maxHeight, Math.Max((int)Math.Ceiling(MinHeight * scaleY),
            center ? (int)Math.Round(PreferredHeight * scaleY) : current.Bottom - current.Top));
        var left = center ? info.Work.Left + (info.Work.Right - info.Work.Left - width) / 2
            : Math.Clamp(current.Left, info.Work.Left + gapX, info.Work.Right - gapX - width);
        var top = center ? info.Work.Top + (info.Work.Bottom - info.Work.Top - height) / 2
            : Math.Clamp(current.Top, info.Work.Top + gapY, info.Work.Bottom - gapY - height);
        if (current.Left != left || current.Top != top || current.Right - current.Left != width || current.Bottom - current.Top != height)
            SetWindowPos(handle, 0, left, top, width, height, 0x0014); // NOZORDER | NOACTIVATE
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if(DataContext is not MainViewModel vm)return;
        await vm.InitializeAsync();
        if(_readyToClose || _closing)return;
        _tutorial=new TutorialCoordinator(RootLayout,TutorialLayer,vm,TutorialCatalog.CreateDefault());
        await Dispatcher.InvokeAsync(()=>
        {
            if(_closing || _readyToClose)return;
            var locale=LocalizationService.Current;
            if(!locale.TutorialOffered)
            {
                locale.MarkOffered(_tutorial.UnknownSteps);
                if(MessageBox.Show(this,L.T("tutorial.offer"),L.T("tutorial.title"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.Yes)==MessageBoxResult.Yes)_tutorial.Start();
            }
            else if(_tutorial.UnknownSteps is { Count:>0 } unknown)
            {
                locale.MarkOffered(unknown);
                if(MessageBox.Show(this,L.T("tutorial.newOffer"),L.T("tutorial.title"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)_tutorial.Start(true,unknown);
            }
        },DispatcherPriority.ApplicationIdle);
    }

    private void OpenSettings(object sender,RoutedEventArgs e)
    { if(DataContext is MainViewModel vm)new ApplicationSettingsWindow(vm,this).ShowDialog(); }
    private void OpenHelp(object sender,RoutedEventArgs e)
    {
        var help=new HelpWindow(this,_tutorial?.HasNewSteps==true);
        if(help.ShowDialog()==true)_tutorial?.Start(help.OnlyNewSteps);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_readyToClose) return;
        e.Cancel = true;
        if (_closing) return;
        if (DataContext is MainViewModel vm)
        {
            if ((vm.IsBusy || _modules?.IsBusy == true) && MessageBox.Show(L.T("ui.hayUnaOperacionEnCursoSeSolicitaraLaCancelacion"),
                    "CaptionForge", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _closing = true;
            _tutorial?.Dispose();
            TutorialLayer.Visibility=Visibility.Collapsed;
            IsEnabled = false;
            try { if (_modules is not null) await _modules.ShutdownAsync(); else await vm.ShutdownAsync(); }
            catch (Exception ex) { Services.AppLog.Write(ex); }
        }
        _readyToClose = true;
        await Dispatcher.InvokeAsync(new Action(Close), DispatcherPriority.Normal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

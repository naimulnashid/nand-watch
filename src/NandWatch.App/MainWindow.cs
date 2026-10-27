using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NandWatch.App.Controls;
using NandWatch.App.State;
using NandWatch.App.Theme;
using NandWatch.App.Views;
using NandWatch.Core;
using Windows.Graphics;

namespace NandWatch.App;

/// <summary>
/// The shell: title bar, the top bar, and the page area, built in code (there
/// is no MainWindow.xaml; see the project file). Screen Time's window, with
/// its tabs, zoom, theme and capture hooks; the drives replace the apps.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly AppState _state;
    private readonly UiSettings _settings;
    private readonly Action _exit;
    private readonly PageContext _ctx;
    private readonly Grid Root = new();

    private readonly Dictionary<PageKind, Button> _tabs = [];
    private readonly Button _sync;
    private readonly TextBlock _syncText = Ui.Text("Sync now", 15, 500, Palette.TextMutedBrush, selectable: false);
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _syncDot = new() { Width = 8, Height = 8, Fill = Palette.AccentBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressRing _syncRing = new() { Width = 13, Height = 13, IsActive = false, Visibility = Visibility.Collapsed, Foreground = Palette.AccentBrightBrush };
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ContentControl _pageHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private Grid _frame = null!;
    private Border _topBar = null!;

    private TrayHost? _tray;
    private bool _inTray;
    private bool _launchedToTray;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _stallTimer;

    private Route _route = new(PageKind.Overview);
    private IPage? _page;
    private int _buildToken;
    private bool _syncing;

    public MainWindow(AppState state, UiSettings settings, Action exit)
    {
        _state = state;
        _settings = settings;
        _exit = exit;
        Content = Root;
        _ctx = new PageContext { State = state, Navigate = Navigate, Redraw = () => Rebuild(keepScroll: true), SyncNow = SyncNowAsync, Window = this };
        _sync = BuildSyncButton();

        Zoom.Set(settings.Zoom);
        Zoom.Changed += OnZoomChanged;
        // Before anything is built, so the first frame is already the right theme.
        Palette.Apply(ResolveTheme(settings.Theme));
        _systemColors.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_settings.Theme == "system") Palette.Apply(ResolveTheme("system"));
        });
        Palette.Changed += OnThemeChanged;
        ConfigureWindow();
        BuildShell();
        Root.RequestedTheme = Palette.IsLight ? ElementTheme.Light : ElementTheme.Dark;

        state.Changed += () =>
        {
            _ = UpdateTrayAsync();
            // In the tray there is no page to rebuild: it was dropped to free its memory.
            if (_inTray) return;
            UpdateChrome();
            Rebuild(keepScroll: true);
        };
        UpdateChrome();

        // Closing with the tray on leaves the app there; without it, closing exits.
        AppWindow.Closing += (_, e) =>
        {
            if (_tray is null) return;
            e.Cancel = true;
            EnterTray();
        };
        state.Watch(DispatcherQueue);
        _stallTimer = DispatcherQueue.CreateTimer();
        // On a clock only, never on opening: the app's own first read lands
        // first, and a PC that was simply off for days has not stalled.
        _stallTimer.Interval = TimeSpan.FromMinutes(10);
        _stallTimer.Tick += (_, _) => CheckStall();
        _stallTimer.Start();
    }

    /* --------------------------------------------------------------- Tray */

    /// <summary>Started at sign-in with --tray: the tray only, no window until it is opened.</summary>
    public async Task StartInTrayAsync()
    {
        _launchedToTray = true;
        ApplyTray();
        _inTray = true;
        await _state.ReloadAsync();
    }

    /// <summary>The tray exists while the owner wants it (Settings) or the app was started into it.</summary>
    private void ApplyTray()
    {
        var want = _settings.CloseToTray || _launchedToTray;
        if (want && _tray is null)
        {
            try
            {
                _tray = new TrayHost(a => DispatcherQueue.TryEnqueue(() => a()), ShowAndActivate, () => _ = SyncNowAsync(), Exit);
                _ = UpdateTrayAsync();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _tray = null;
            }
        }
        else if (!want && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    private async Task UpdateTrayAsync()
    {
        if (_tray is null) return;
        try { _tray?.SetToolTip(await _state.TrayLinesAsync()); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException) { }
    }

    /// <summary>Hides the window and drops the page, so the tray holds as little as it can.</summary>
    private void EnterTray()
    {
        _inTray = true;
        AppWindow.Hide();
        _pageHost.Content = null;
        _page = null;
        GC.Collect();
    }

    private void Exit()
    {
        _tray?.Dispose();
        _tray = null;
        _state.Dispose();
        _exit();
    }

    /// <summary>
    /// A stall, announced once: no good run for six hours while the app is
    /// running. The hourly task catches up after sleep, so six hours means it
    /// is not running at all. The collector cannot say this itself.
    /// </summary>
    private void CheckStall()
    {
        if (!_settings.Notify || _state.LastOk is not { } last || DateTimeOffset.UtcNow - last < TimeSpan.FromHours(6)) return;
        if (_settings.NotifiedStallSince == last) return;
        _settings.NotifiedStallSince = last;
        _settings.Save();
        NandWatch.Windows.Notify.Toasts.Show($"NAND Watch has not read the drives for {(int)(DateTimeOffset.UtcNow - last).TotalHours} hours",
            "The hourly task may have stopped. Open Sync Status to see why.");
    }

    /* ------------------------------------------------------------- Window */

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /* -------------------------------------------------------------- Theme */

    private readonly global::Windows.UI.ViewManagement.UISettings _systemColors = new();

    /// <summary>Whether a theme choice means light, reading Windows' app mode for "system".</summary>
    private bool ResolveTheme(string choice) => choice switch
    {
        "light" => true,
        "system" => _systemColors.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background) is { R: > 128 },
        _ => false,
    };

    private void SetTheme(string choice)
    {
        _settings.Theme = choice;
        _settings.Save();
        Palette.Apply(ResolveTheme(choice));
    }

    /// <summary>
    /// The shared brushes have already been retinted (Palette.Apply); what is
    /// left is what baked a colour in when it was built: the caption buttons,
    /// the controls' own theme, the page's charts. So the page is rebuilt.
    /// </summary>
    private void OnThemeChanged()
    {
        Root.RequestedTheme = Palette.IsLight ? ElementTheme.Light : ElementTheme.Dark;
        PaintCaptionButtons();
        UpdateChrome();
        Rebuild(keepScroll: true);
    }

    private void PaintCaptionButtons()
    {
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Palette.TextMuted;
        bar.ButtonInactiveForegroundColor = Palette.TextFaint;
        bar.ButtonHoverBackgroundColor = Palette.SurfaceHover;
        bar.ButtonHoverForegroundColor = Palette.Text;
        bar.ButtonPressedBackgroundColor = Palette.BorderBright;
        bar.ButtonPressedForegroundColor = Palette.Text;
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        PaintCaptionButtons();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Title = "NAND Watch";

        // A restored size for when the user un-maximises: 1440 x 960 at the
        // display's scale, never larger than its work area.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1440 * scale), area.Width - 40);
        var height = Math.Min((int)(960 * scale), area.Height - 40);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(760 * scale);
            presenter.PreferredMinimumHeight = (int)(520 * scale);
        }
    }

    public void ShowAndActivate()
    {
        var wasInTray = _inTray;
        _inTray = false;
        AppWindow.Show();
        Maximize();
        Activate();
        if (wasInTray)
        {
            UpdateChrome();
            Rebuild(keepScroll: false);
        }
    }

    /// <summary>The window always opens maximised, as the siblings' do.</summary>
    public void OpenMaximized()
    {
        Maximize();
        Activate();
        Rebuild(keepScroll: false);
    }

    private void Maximize()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State != OverlappedPresenterState.Maximized) presenter.Maximize();
    }

    /// <summary>
    /// A reading when the app opens (the owner's decision, 2026-10-24): the
    /// window shows what the history holds at once, then this run's reading
    /// lands and the page refreshes in place.
    /// </summary>
    public async Task ReadOnOpenAsync()
    {
        await _state.ReloadAsync();
        if (_state.DataDir is not null && Environment.GetEnvironmentVariable("NANDWATCH_DEBUG_VIEW") is not { Length: > 0 })
            await _state.CollectAsync("app", backup: false);
        // Again here: on a first run the live folder exists only after this read.
        _state.Watch(DispatcherQueue);
    }

    /* -------------------------------------------------------------- Shell */

    private ZoomBox _zoomBox = null!;

    private void BuildShell()
    {
        Root.Background = Palette.BgBrush;
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Title bar: draggable, the app's mark and name, room for the caption buttons.
        var titleBar = new Grid { Background = Palette.BgBrush, Padding = new Thickness(16, 0, 150, 0) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new Image { Width = 16, Height = 16, Source = new BitmapImage(new Uri("ms-appx:///Assets/app-icon.png")) });
        title.Children.Add(Ui.Text("NAND Watch", 12.5, 500, Palette.TextFaintBrush, selectable: false));
        titleBar.Children.Add(title);
        Root.Children.Add(titleBar);
        SetTitleBar(titleBar);

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // Everything below the title bar zooms, as a browser zooms the page and
        // not its own chrome. See Controls/ZoomBox.
        _zoomBox = new ZoomBox(main) { Zoom = Zoom.Level };
        Grid.SetRow(_zoomBox, 1);
        Root.Children.Add(_zoomBox);
        Root.Children.Add(ZoomIndicator());

        _topBar = BuildTopBar();
        main.Children.Add(_topBar);

        var column = new StackPanel { MaxWidth = 1320 + 80, Padding = new Thickness(40, 48, 40, 40) };
        column.Children.Add(_pageHost);
        column.Children.Add(Footer());
        // The frame is pinned to the viewport's width and the column centres in
        // it: a ScrollViewer centres a MaxWidth column by its DESIRED width.
        _frame = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        _frame.Children.Add(column);
        _scroller.SizeChanged += (_, e) => _frame.Width = e.NewSize.Width;
        _scroller.Content = _frame;
        // Behind the page, a surface for the light theme's card shadows to fall
        // on (see Ui.ShadowReceiver): the page's own ancestors cannot receive.
        var shadowReceiver = new Grid { Background = Palette.BgBrush };
        Grid.SetRow(shadowReceiver, 1);
        main.Children.Add(shadowReceiver);
        Ui.ShadowReceiver = shadowReceiver;
        Grid.SetRow(_scroller, 1);
        main.Children.Add(_scroller);

        AddKeys();
    }

    private void AddKeys()
    {
        // F5 re-reads the history, as a browser reloads.
        var f5 = new KeyboardAccelerator { Key = global::Windows.System.VirtualKey.F5 };
        f5.Invoked += (sender, e) =>
        {
            e.Handled = true;
            _ = _state.ReloadAsync();
        };
        Root.KeyboardAccelerators.Add(f5);

        // Page zoom, with a browser's keys: Ctrl with =/+ (and Shift, which "+"
        // needs on most layouts), the numpad's + and -, and 0 to reset.
        const global::Windows.System.VirtualKey Plus = (global::Windows.System.VirtualKey)0xBB, Minus = (global::Windows.System.VirtualKey)0xBD;
        void ZoomKey(global::Windows.System.VirtualKey key, Func<bool> action, bool shift = false)
        {
            var accelerator = new KeyboardAccelerator
            {
                Key = key,
                Modifiers = global::Windows.System.VirtualKeyModifiers.Control | (shift ? global::Windows.System.VirtualKeyModifiers.Shift : 0),
            };
            accelerator.Invoked += (_, e) =>
            {
                e.Handled = true;
                action();
            };
            Root.KeyboardAccelerators.Add(accelerator);
        }
        ZoomKey(Plus, Zoom.In);
        ZoomKey(Plus, Zoom.In, shift: true);
        ZoomKey(global::Windows.System.VirtualKey.Add, Zoom.In);
        ZoomKey(Minus, Zoom.Out);
        ZoomKey(global::Windows.System.VirtualKey.Subtract, Zoom.Out);
        ZoomKey(global::Windows.System.VirtualKey.Number0, Zoom.Reset);
        ZoomKey(global::Windows.System.VirtualKey.NumberPad0, Zoom.Reset);
        // Ctrl+wheel. handledEventsToo: the scroller marks wheel events handled.
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            // KeyModifiers was seen to arrive empty with Ctrl held, so the key's
            // own state is checked too.
            var ctrl = e.KeyModifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Control)
                || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(global::Windows.System.VirtualKey.Control)
                    .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (!ctrl) return;
            var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
            if (delta > 0) Zoom.In();
            else if (delta < 0) Zoom.Out();
            e.Handled = true;
        }), handledEventsToo: true);
        // WinUI shows a root accelerator's key as a tooltip over any spot without
        // one of its own - "F5" floated over the title bar. The keys still work.
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private static Border Footer()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Ui.Text("Local only · no telemetry, no outbound requests · reads drives, never writes to them", 13, 400, Palette.TextFaintBrush));
        var right = Ui.Text($"© 2026 Naimul Nashid · v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}", 13, 400, Palette.TextFaintBrush);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border
        {
            Margin = new Thickness(0, 54, 0, 0),
            Padding = new Thickness(0, 22, 0, 0),
            BorderBrush = Palette.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
    }

    /* ------------------------------------------------------------ Top bar */

    private Border BuildTopBar()
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (kind, label) in new[] { (PageKind.Overview, "Overview"), (PageKind.Events, "Events"), (PageKind.Sync, "Sync Status") })
        {
            var tab = new Button
            {
                Content = Ui.Text(label, 15, 500, Palette.TextMutedBrush, selectable: false),
                Padding = new Thickness(14.4, 7.2, 14.4, 7.2),
                CornerRadius = new CornerRadius(Ui.RadiusSmall),
                BorderThickness = new Thickness(0),
            };
            tab.Resources["ButtonBackgroundPointerOver"] = Palette.SurfaceHoverBrush;
            tab.Resources["ButtonBackgroundPressed"] = Palette.SurfaceHoverBrush;
            var target = kind;
            tab.Click += (_, _) => Navigate(new Route(target));
            _tabs[kind] = tab;
            nav.Children.Add(tab);
        }
        grid.Children.Add(nav);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_sync);
        right.Children.Add(SettingsButton());
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return new Border
        {
            Padding = new Thickness(40, 12, 40, 12),
            MinHeight = 61,
            BorderBrush = Palette.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Background = Palette.BgBrush,
            Child = grid,
        };
    }

    private Button BuildSyncButton()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(_syncDot);
        content.Children.Add(_syncRing);
        content.Children.Add(_syncText);
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(13.6, 5.4, 13.6, 6.4),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Background = Palette.TransparentBrush,
            BorderBrush = Palette.BorderBrightBrush,
        };
        button.Resources["ButtonBackgroundPointerOver"] = Palette.TransparentBrush;
        button.Resources["ButtonBackgroundPressed"] = Palette.TransparentBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = Palette.TextFaintBrush;
        button.Resources["ButtonBackgroundDisabled"] = Palette.TransparentBrush;
        button.Resources["ButtonBorderBrushDisabled"] = Palette.BorderBrightBrush;
        button.PointerEntered += (_, _) => { if (!_syncing) _syncText.Foreground = Palette.TextBrush; };
        button.PointerExited += (_, _) => { if (!_syncing) _syncText.Foreground = Palette.TextMutedBrush; };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Sync now: read every drive and write the backup");
        ToolTipService.SetToolTip(button, Ui.TipContent("Read every drive now, as the hourly task does, and write the backup. It takes well under a second."));
        button.Click += async (_, _) => await SyncNowAsync();
        return button;
    }

    /// <summary>Sync now: one collector run in this process, with the backup.</summary>
    public async Task SyncNowAsync()
    {
        if (_syncing || _state.DataDir is null) return;
        _syncing = true;
        _sync.IsEnabled = false;
        _syncDot.Visibility = Visibility.Collapsed;
        _syncRing.Visibility = Visibility.Visible;
        _syncRing.IsActive = true;
        _syncText.Text = "Reading…";
        _syncText.Foreground = Palette.AccentBrightBrush;
        var result = await _state.CollectAsync("sync", backup: true);
        var ok = result is { ExitCode: 0 };
        _syncing = false;
        _sync.IsEnabled = true;
        _syncRing.IsActive = false;
        _syncRing.Visibility = Visibility.Collapsed;
        _syncDot.Visibility = Visibility.Visible;
        _syncDot.Fill = ok ? Palette.AccentBrush : Palette.WarnBrush;
        _syncText.Text = ok ? "Up to date" : "Sync failed";
        _syncText.Foreground = ok ? Palette.TextMutedBrush : Palette.WarnBrush;
        var detail = result is null ? "No data folder is set." : $"{result.Status}: {result.DrivesRead} drive(s), backup {result.Backup ?? "not due"}, {result.DurationMs} ms. {string.Join(" ", result.Notes)}";
        ToolTipService.SetToolTip(_sync, Ui.TipContent(detail));
        // A dialog needs the window: from the tray, the failure waits in Sync Status.
        if (!ok && !_inTray) await Shell.ShowMessage(this, "Sync failed", detail);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (_syncing) return;
            _syncText.Text = "Sync now";
            _syncText.Foreground = Palette.TextMutedBrush;
            _syncDot.Fill = Palette.AccentBrush;
        };
        timer.Start();
    }

    private readonly TextBlock _zoomText = Ui.Text("", 14, 600, Palette.TextBrush, numeric: true, selectable: false);
    private Border? _zoomPill;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _zoomTimer;

    /// <summary>The level, shown for a moment after it changes - as a browser's address bar does.</summary>
    private Border ZoomIndicator()
    {
        _zoomPill = new Border
        {
            Child = _zoomText,
            Padding = new Thickness(14, 7, 14, 7),
            CornerRadius = new CornerRadius(Ui.RadiusSmall),
            Background = Palette.TooltipBgBrush,
            BorderBrush = Palette.BorderBrightBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 0, 0),
            IsHitTestVisible = false,
            Opacity = 0,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) },
        };
        Grid.SetRow(_zoomPill, 1);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_zoomText, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        return _zoomPill;
    }

    private void OnZoomChanged()
    {
        _zoomBox.Zoom = Zoom.Level;
        _settings.Zoom = Zoom.Level;
        _settings.Save();
        if (_zoomPill is null) return;
        _zoomText.Text = Zoom.Label;
        _zoomPill.Opacity = 1;
        if (_zoomTimer is null)
        {
            _zoomTimer = DispatcherQueue.CreateTimer();
            _zoomTimer.Interval = TimeSpan.FromMilliseconds(1300);
            _zoomTimer.IsRepeating = false;
            _zoomTimer.Tick += (_, _) => _zoomPill.Opacity = 0;
        }
        _zoomTimer.Stop();
        _zoomTimer.Start();
    }

    private Button SettingsButton()
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = "\uE713", FontSize = 15, Foreground = Palette.TextMutedBrush },
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(17),
            BorderThickness = new Thickness(1),
            Background = Palette.TransparentBrush,
            BorderBrush = Palette.BorderBrightBrush,
        };
        button.Resources["ButtonBackgroundPointerOver"] = Palette.SurfaceHoverBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = Palette.TextFaintBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Settings");
        ToolTipService.SetToolTip(button, "Settings");
        var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };

        // Theme: three choices, one checked. Dark by default; System follows Windows' app mode.
        var theme = new MenuFlyoutSubItem { Text = "Theme", Icon = new FontIcon { Glyph = "\uE790" } };
        var themeItems = new List<(RadioMenuFlyoutItem Item, string Value)>();
        foreach (var (label, value) in new[] { ("Dark", "dark"), ("Light", "light"), ("Use Windows setting", "system") })
        {
            var item = new RadioMenuFlyoutItem { Text = label, GroupName = "theme" };
            item.Click += (_, _) => SetTheme(value);
            theme.Items.Add(item);
            themeItems.Add((item, value));
        }
        flyout.Opening += (_, _) =>
        {
            foreach (var (item, value) in themeItems) item.IsChecked = _settings.Theme == value;
        };
        flyout.Items.Add(theme);
        flyout.Items.Add(new MenuFlyoutSeparator());

        // The zoom keys, findable. The shortcut text is shown, not bound: the
        // real accelerators live on the window.
        var zoomIn = new MenuFlyoutItem { Text = "Zoom in", KeyboardAcceleratorTextOverride = "Ctrl+Plus", Icon = new FontIcon { Glyph = "\uE8A3" } };
        zoomIn.Click += (_, _) => Zoom.In();
        var zoomOut = new MenuFlyoutItem { Text = "Zoom out", KeyboardAcceleratorTextOverride = "Ctrl+Minus", Icon = new FontIcon { Glyph = "\uE71F" } };
        zoomOut.Click += (_, _) => Zoom.Out();
        var zoomReset = new MenuFlyoutItem { KeyboardAcceleratorTextOverride = "Ctrl+0" };
        zoomReset.Click += (_, _) => Zoom.Reset();
        flyout.Opening += (_, _) => zoomReset.Text = $"Reset zoom ({Zoom.Label})";
        flyout.Items.Add(zoomIn);
        flyout.Items.Add(zoomOut);
        flyout.Items.Add(zoomReset);
        flyout.Items.Add(new MenuFlyoutSeparator());

        var data = new MenuFlyoutItem { Text = "Open data folder", Icon = new FontIcon { Glyph = "\uE8B7" } };
        data.Click += (_, _) => { if (_state.DataDir is { } d) Shell.OpenFolder(d); };
        flyout.Items.Add(data);
        var logs = new MenuFlyoutItem { Text = "Open collector logs", Icon = new FontIcon { Glyph = "\uE9F9" } };
        logs.Click += (_, _) => Shell.OpenFolder(AppPaths.LogsDir);
        flyout.Items.Add(logs);
        flyout.Items.Add(new MenuFlyoutSeparator());
        // Notifications and the tray. Collection never depends on either.
        var notify = new ToggleMenuFlyoutItem { Text = "Show notifications", Icon = new FontIcon { Glyph = "\uEA8F" } };
        notify.Click += (_, _) =>
        {
            _settings.Notify = notify.IsChecked;
            _settings.Save();
        };
        var tray = new ToggleMenuFlyoutItem { Text = "Keep in the tray when closed", Icon = new FontIcon { Glyph = "\uE8A0" } };
        tray.Click += (_, _) =>
        {
            _settings.CloseToTray = tray.IsChecked;
            _settings.Save();
            ApplyTray();
        };
        var startup = new ToggleMenuFlyoutItem { Text = "Start in the tray at sign-in", Icon = new FontIcon { Glyph = "\uE768" } };
        startup.Click += (_, _) =>
        {
            try { StartupRegistration.Set(startup.IsChecked); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            startup.IsChecked = StartupRegistration.IsEnabled;
        };
        flyout.Opening += (_, _) =>
        {
            notify.IsChecked = _settings.Notify;
            tray.IsChecked = _settings.CloseToTray;
            startup.IsChecked = StartupRegistration.IsEnabled;
        };
        flyout.Items.Add(notify);
        flyout.Items.Add(tray);
        flyout.Items.Add(startup);
        flyout.Items.Add(new MenuFlyoutSeparator());

        var exit = new MenuFlyoutItem { Text = "Exit", Icon = new FontIcon { Glyph = "\uE7E8" } };
        exit.Click += (_, _) => Exit();
        flyout.Items.Add(exit);

        button.Flyout = flyout;
        return button;
    }

    /* --------------------------------------------------------- Navigation */

    private void Navigate(Route route)
    {
        var same = route == _route;
        _route = route;
        if (!same) _page = null;
        UpdateChrome();
        Rebuild(keepScroll: same);
    }

    private IPage Create(Route route) => route.Kind switch
    {
        PageKind.Sync => new SyncPage(_ctx),
        PageKind.Events => new EventsPage(_ctx),
        PageKind.Drive => new DrivePage(_ctx, route.DriveId ?? 0),
        _ => new OverviewPage(_ctx),
    };

    /// <summary>
    /// Loads the page's data off the UI thread, then builds it. The previous
    /// page stays on screen meanwhile, so a refresh never flashes blank; a
    /// newer rebuild supersedes an older one still loading.
    /// </summary>
    private async void Rebuild(bool keepScroll)
    {
        var token = ++_buildToken;
        var offset = _scroller.VerticalOffset;
        _topBar.Visibility = _state.DataDir is null ? Visibility.Collapsed : Visibility.Visible;

        if (_state.DataDir is null)
        {
            _pageHost.Content = Shell.Setup(_ctx, () =>
            {
                _state.Open();
                _ = ReadOnOpenAsync();
            });
            return;
        }
        if (!_state.HasData)
        {
            _pageHost.Content = Shell.NoData(_ctx);
            return;
        }

        // A new page (navigation, or the first load) shows its skeleton while
        // its data loads; a refresh of the page already up keeps it on screen,
        // so a collection landing never flashes anything.
        var fresh = _page is null;
        var page = _page ??= Create(_route);
        if (Environment.GetEnvironmentVariable("NANDWATCH_DEBUG_SKELETON") == "1")
        {
            _pageHost.Content = SkeletonFor(_route);
            _scroller.UpdateLayout();
            ApplyDebugView();
            return;
        }
        if (fresh || _pageHost.Content is null) _pageHost.Content = SkeletonFor(_route);
        try
        {
            await Task.Run(page.Load);
            if (token != _buildToken) return;
            _pageHost.Content = page.Build();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            if (token != _buildToken) return;
            _pageHost.Content = RenderError(ex);
        }

        _scroller.UpdateLayout();
        _scroller.ChangeView(null, keepScroll ? offset : 0, null, disableAnimation: true);
        ApplyDebugView();
    }

    /// <summary>The route's own page, built from stand-in data and turned into a skeleton.</summary>
    private UIElement SkeletonFor(Route route)
    {
        var page = Create(route);
        page.Placeholder();
        return page.Build() is FrameworkElement built ? Skeleton.Apply(built) : new Grid();
    }

    private static UIElement RenderError(Exception ex)
    {
        var detail = string.Join("\n", ex.ToString().Split('\n').Take(12));
        return Parts.Alert("This page could not be drawn", "Press F5 to try again.", detail);
    }

    private void UpdateChrome()
    {
        Title = "NAND Watch";
        foreach (var (kind, tab) in _tabs)
        {
            // A drive's page belongs to the Overview it opens from.
            var active = kind == (_route.Kind == PageKind.Drive ? PageKind.Overview : _route.Kind);
            tab.Background = active ? Palette.AccentDimBrush : Palette.TransparentBrush;
            if (tab.Content is TextBlock text) text.Foreground = active ? Palette.AccentBrightBrush : Palette.TextMutedBrush;
        }
    }

    /* ------------------------------------------------ Development captures */

    private bool _debugApplied;

    /// <summary>
    /// Development only: <c>NANDWATCH_DEBUG_VIEW=page,scroll</c> (a drive's
    /// page is <c>drive,id,scroll</c>) opens a
    /// page at a scroll offset once its data is in, so tools\Capture-Window.ps1
    /// can photograph any section. <c>NANDWATCH_DEBUG_FULLPAGE=width</c> then
    /// grows the window to the page's whole height for a full-page capture.
    /// </summary>
    private void ApplyDebugView()
    {
        if (_debugApplied || Environment.GetEnvironmentVariable("NANDWATCH_DEBUG_VIEW") is not { Length: > 0 } spec) return;
        var parts = spec.Split(',');
        var kind = Enum.TryParse<PageKind>(parts[0], true, out var k) ? k : PageKind.Overview;
        var target = new Route(kind, kind == PageKind.Drive && parts.Length > 2 && long.TryParse(parts[1], out var id) ? id : null);
        if (_route != target)
        {
            Navigate(target);
            return;
        }
        _debugApplied = true;
        // NANDWATCH_DEBUG_SWITCH_THEME=light|dark switches after the first
        // build, through the menu's own path, to photograph a live switch.
        if (Environment.GetEnvironmentVariable("NANDWATCH_DEBUG_SWITCH_THEME") is { Length: > 0 } theme)
        {
            SetTheme(theme);
            return;
        }
        var scroll = double.TryParse(parts[^1], out var s) ? s : 0;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _scroller.UpdateLayout();
            _scroller.ChangeView(null, scroll, null, disableAnimation: true);
            if (Environment.GetEnvironmentVariable("NANDWATCH_DEBUG_FULLPAGE") is { Length: > 0 } width && double.TryParse(width, out var w)) FitWindowToPage(w);
        });
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr previous, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private WndProc? _unboundedProc;
    private IntPtr _previousProc;
    private int _fitPasses;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _fitTimer;

    /// <summary>
    /// Sizes the window to a width and the page's whole height. Windows caps a
    /// window at about the screen's size through WM_GETMINMAXINFO, so the cap is
    /// lifted first; re-measured until the page stops growing.
    /// </summary>
    private void FitWindowToPage(double widthDip)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_unboundedProc is null)
        {
            _unboundedProc = (h, msg, w, l) =>
            {
                var result = CallWindowProcW(_previousProc, h, msg, w, l);
                if (msg == 0x0024) // WM_GETMINMAXINFO: ptMaxTrackSize is the fifth POINT.
                {
                    Marshal.WriteInt32(l, 32, 30000);
                    Marshal.WriteInt32(l, 36, 30000);
                }
                return result;
            };
            _previousProc = SetWindowLongPtr(hwnd, -4, Marshal.GetFunctionPointerForDelegate(_unboundedProc));
        }
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State != OverlappedPresenterState.Restored) presenter.Restore();

        var scale = GetDpiForWindow(hwnd) / 96.0;
        _scroller.UpdateLayout();
        var height = Root.ActualHeight - _scroller.ViewportHeight + _scroller.ExtentHeight;
        AppWindow.Move(new PointInt32(0, 0));
        AppWindow.ResizeClient(new SizeInt32((int)Math.Round(widthDip * scale), (int)Math.Ceiling(height * scale)));
        if (++_fitPasses >= 6) return;
        var timer = _fitTimer ??= DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(600);
        timer.IsRepeating = false;
        if (_fitPasses == 1)
        {
            timer.Tick += (_, _) =>
            {
                _scroller.UpdateLayout();
                if (_scroller.ExtentHeight > _scroller.ViewportHeight + 0.5 || Math.Abs(Root.ActualWidth - widthDip) > 0.5) FitWindowToPage(widthDip);
            };
        }
        timer.Start();
    }
}

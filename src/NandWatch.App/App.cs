using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using NandWatch.App.State;

namespace NandWatch.App;

/// <summary>
/// The application, built in code: there is no App.xaml (see the project file
/// for why). What App.xaml did in the siblings is done here: the dark theme as
/// the default, WinUI's control resources merged in, and the metadata provider
/// WinUI asks for its own control types, which the XAML compiler would
/// otherwise have generated.
/// </summary>
public sealed class App : Application, IXamlMetadataProvider
{
    private static App? _instance;
    private static DispatcherQueue? _ui;

    private readonly Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider _controls = new();
    private MainWindow? _window;
    private AppState? _state;

    public App()
    {
        _instance = this;
        RequestedTheme = ApplicationTheme.Dark;
    }

    public IXamlType GetXamlType(Type type) => _controls.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _controls.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _controls.GetXmlnsDefinitions();

    /// <summary>
    /// A second launch was redirected here: the tray's Exit
    /// (<c>--exit</c>), or else someone asking for the window.
    /// </summary>
    internal static void OnRedirected(string? arguments) => _ui?.TryEnqueue(() =>
    {
        if (_instance is not { _window: { } window } app) return;
        var exit = (arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("--exit", StringComparer.OrdinalIgnoreCase);
        if (exit) app.Quit();
        else window.ShowAndActivate();
    });

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _ui = DispatcherQueue.GetForCurrentThread();
        var settings = UiSettings.Load();
        _state = new AppState();
        _window = new MainWindow(_state, settings, QuitAll);
        // The window is the process: closing it gives WinUI's memory back.
        // The tray, when the owner wants one, is another process.
        _window.Closed += (_, _) => Quit();
        _window.TrayChoiceChanged += () => ApplyTray(settings);
        // The tray watches the history of the data folder it knows: tell it
        // when the setup screen chooses one.
        var dataDir = _state.DataDir;
        _state.Changed += () =>
        {
            if (_state.DataDir == dataDir) return;
            dataDir = _state.DataDir;
            TrayLink.Send("settings");
        };
        ApplyTray(settings);
        _window.OpenMaximized();
        // A reading when the app opens (the owner's decision), then the pages.
        _ = _window.ReadOnOpenAsync();
    }

    /// <summary>
    /// "Keep in the tray when closed": on, a tray process runs beside the
    /// window (started here unless one already is); off, a tray that was
    /// started for the window leaves, and one started at sign-in stays.
    /// </summary>
    private static void ApplyTray(UiSettings settings)
    {
        if (TrayLink.Send("settings") || !settings.CloseToTray) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--tray --for-window") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No tray: closing the window then ends the app, as with it off.
        }
    }

    /// <summary>Exit, chosen in the window: the tray goes too.</summary>
    private void QuitAll()
    {
        TrayLink.Send("exit");
        Quit();
    }

    private bool _quitting;

    private void Quit()
    {
        // Exit() closes the window, whose Closed comes back here.
        if (_quitting) return;
        _quitting = true;
        _state?.Dispose();
        Exit();
        Environment.Exit(0);
    }
}

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

    /// <summary>A second launch was redirected here: bring the window back.</summary>
    internal static void OnRedirected() => _ui?.TryEnqueue(() => _instance?._window?.ShowAndActivate());

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _ui = DispatcherQueue.GetForCurrentThread();
        var settings = UiSettings.Load();
        _state = new AppState();
        _window = new MainWindow(_state, settings, Quit);
        // Started at sign-in by the Run key: the tray only. The collector's
        // own sign-in run reads the drives, so this does not.
        if (Environment.GetCommandLineArgs().Contains("--tray"))
        {
            _ = _window.StartInTrayAsync();
            return;
        }
        _window.OpenMaximized();
        // A reading when the app opens (the owner's decision), then the pages.
        _ = _window.ReadOnOpenAsync();
    }

    private void Quit()
    {
        Exit();
        Environment.Exit(0);
    }
}

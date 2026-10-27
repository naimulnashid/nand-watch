using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NandWatch.App.Theme;
using NandWatch.Core;

namespace NandWatch.App.Views;

/// <summary>Small bridges to Windows (Explorer, a message box) and the shell's own screens.</summary>
public static class Shell
{
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Explorer not starting is not worth a crash.
        }
    }

    public static async Task ShowMessage(Window window, string title, string message)
    {
        if (window.Content?.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = Ui.Paragraph(message, 14.5, Palette.TextMutedBrush),
            CloseButtonText = "OK",
            XamlRoot = window.Content.XamlRoot,
            RequestedTheme = Palette.IsLight ? ElementTheme.Light : ElementTheme.Dark,
        };
        await dialog.ShowAsync();
    }

    /// <summary>A data folder, but no reading in it yet.</summary>
    public static UIElement NoData(PageContext ctx)
    {
        var sync = Ui.Button("Read the drives now", primary: true);
        sync.Click += async (_, _) => await ctx.SyncNow();
        return Parts.Empty("No readings yet",
            "The hourly task reads every drive into this folder. Reading now records the first one. If nothing appears, the task may not be registered: run tools\\Install.ps1 once.",
            sync);
    }

    /// <summary>
    /// First run without a data folder: where the history lives. Off the system
    /// drive, because a database there is erased by the reset this history is
    /// meant to outlive; choosing C: anyway takes a deliberate tick.
    /// </summary>
    public static UIElement Setup(PageContext ctx, Action chosen)
    {
        var stack = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 56, 16, 56), Spacing = 14 };
        stack.Children.Add(Ui.Text("Where should the history live?", 35, 600, spacing: -0.02, wrap: true));
        stack.Children.Add(Ui.Paragraph(
            "Your drives keep their own lifetime totals, but not how they got there. This app keeps that history, in a folder that must not be on the drive Windows is installed on. Point it at the same folder after a reset and the whole history is back.",
            15.5, Palette.TextMutedBrush));

        var drive = Path.GetPathRoot(AppPaths.DefaultDataDir)!;
        var box = new TextBox { Text = Directory.Exists(drive) ? AppPaths.DefaultDataDir : "", FontFamily = Fonts.Sans, FontSize = 15, PlaceholderText = AppPaths.DefaultDataDir };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Data folder");
        var browse = Ui.Button("Browse…");
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(box);
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        stack.Children.Add(row);

        var systemDrive = new CheckBox { Content = Ui.Text("Keep it on the system drive anyway. I understand a Windows reset will erase it.", 14.5, 400, Palette.WarnBrush, wrap: true), Visibility = Visibility.Collapsed };
        stack.Children.Add(systemDrive);
        var error = Ui.Text("", 14, 500, Palette.WarnBrush, wrap: true);
        stack.Children.Add(error);
        var go = Ui.Button("Use this folder", primary: true);
        stack.Children.Add(go);
        stack.Children.Add(Ui.Paragraph("An existing history in the folder is used as it is. Restore from its nand-watch.db, which is always one complete file.", 13.5, Palette.TextFaintBrush));

        void Check()
        {
            var text = box.Text.Trim();
            var onSystem = text.Length > 0 && Path.IsPathFullyQualified(text) && AppPaths.IsOnSystemDrive(text);
            systemDrive.Visibility = onSystem ? Visibility.Visible : Visibility.Collapsed;
            go.IsEnabled = text.Length > 0 && (!onSystem || systemDrive.IsChecked == true);
        }
        box.TextChanged += (_, _) => Check();
        systemDrive.Click += (_, _) => Check();
        Check();

        browse.Click += async (_, _) =>
        {
            var picker = new global::Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(ctx.Window));
            if (await picker.PickSingleFolderAsync() is { } folder) box.Text = folder.Path;
        };
        go.Click += (_, _) =>
        {
            try
            {
                var dir = Path.GetFullPath(box.Text.Trim());
                Directory.CreateDirectory(dir);
                AppPaths.WriteLocation(dir, AppPaths.IsOnSystemDrive(dir));
                chosen();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                error.Text = ex.Message;
            }
        };
        return stack;
    }
}

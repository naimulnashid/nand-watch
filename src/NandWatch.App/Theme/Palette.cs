using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using NandWatch.Core.View;
using Windows.UI;

namespace NandWatch.App.Theme;

/// <summary>
/// Every colour the app paints, per theme, and nothing that is not an Onyx
/// token: the values come from <see cref="Onyx"/>, which a test holds to Onyx's
/// own file. <b>Never write a colour literal in a page or a chart.</b>
/// </summary>
/// <remarks>
/// <para><b>How a theme switch reaches the screen.</b> Every <c>...Brush</c>
/// here is ONE shared object, retinted in place by <see cref="Apply"/>, so
/// everything painted with one repaints at once. A <see cref="Color"/> read
/// while building (a chart's gradient) is baked in, which is why the window
/// also rebuilds the page after a switch. The siblings' pattern, unchanged.</para>
/// <para><b>Red means something broke</b> (Onyx rule 3): <see cref="WarnBrush"/>
/// is Onyx's <c>danger</c>, for failures and a drive's warnings only, never a
/// chart series. <see cref="CautionBrush"/> is <c>warning</c>: wear past 80%,
/// a drive running hot.</para>
/// </remarks>
public static class Palette
{
    private static IReadOnlyDictionary<string, string> _t = Onyx.Dark;

    /// <summary>Whether the light theme is applied.</summary>
    public static bool IsLight { get; private set; }

    public static Color Token(string name)
    {
        var (a, r, g, b) = Onyx.Argb(_t[name]);
        return Color.FromArgb(a, r, g, b);
    }

    public static Color Bg => Token("canvas");
    public static Color Surface => Token("surface");
    public static Color SurfaceHover => Token("surface-hover");
    public static Color BorderBright => Token("border-strong");
    public static Color Text => Token("text");
    public static Color TextMuted => Token("text-dim");
    public static Color TextFaint => Token("text-faint");
    public static Color Accent => Token("accent");
    public static Color AccentBright => Token("accent-bright");
    public static Color Grid => Token("chart-grid");

    /// <summary>Categorical chart series, Onyx's series-1 to series-7.</summary>
    public static Color Series(int index) => Token($"series-{index % 7 + 1}");

    public static readonly SolidColorBrush BgBrush = new();
    public static readonly SolidColorBrush SurfaceBrush = new();
    public static readonly SolidColorBrush SurfaceHoverBrush = new();
    public static readonly SolidColorBrush InsetBrush = new();
    public static readonly SolidColorBrush TooltipBgBrush = new();
    public static readonly SolidColorBrush BorderBrush = new();
    public static readonly SolidColorBrush BorderBrightBrush = new();
    public static readonly SolidColorBrush BorderInputBrush = new();
    public static readonly SolidColorBrush RowBorderBrush = new();
    public static readonly SolidColorBrush TextBrush = new();
    public static readonly SolidColorBrush TextMutedBrush = new();
    public static readonly SolidColorBrush TextFaintBrush = new();
    public static readonly SolidColorBrush AccentBrush = new();
    public static readonly SolidColorBrush AccentBrightBrush = new();
    public static readonly SolidColorBrush AccentFillBrush = new();
    public static readonly SolidColorBrush OnAccentFillBrush = new();
    public static readonly SolidColorBrush AccentDimBrush = new();
    public static readonly SolidColorBrush AccentGlowBrush = new();
    public static readonly SolidColorBrush FocusRingBrush = new();
    public static readonly SolidColorBrush WarnBrush = new();
    public static readonly SolidColorBrush WarnDimBrush = new();
    public static readonly SolidColorBrush CautionBrush = new();
    public static readonly SolidColorBrush CautionDimBrush = new();
    public static readonly SolidColorBrush GoodBrush = new();
    public static readonly SolidColorBrush GoodDimBrush = new();
    public static readonly SolidColorBrush GridBrush = new();
    public static readonly SolidColorBrush ChartCursorBrush = new();
    public static readonly SolidColorBrush SeriesOtherBrush = new();
    public static readonly SolidColorBrush HeatNoneBrush = new();
    public static readonly SolidColorBrush HeatNoneRingBrush = new();
    public static readonly SolidColorBrush TopBarBrush = new();
    public static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    /// <summary>The heat ramp, hm-0 (a real but quiet day) to hm-5.</summary>
    public static readonly SolidColorBrush[] Heat = Enumerable.Range(0, 6).Select(_ => new SolidColorBrush()).ToArray();

    /// <summary>Raised after <see cref="Apply"/> changed the theme.</summary>
    public static event Action? Changed;

    static Palette() => Paint();

    /// <summary>
    /// Switches theme: retints every shared brush in place, then raises
    /// <see cref="Changed"/> so the window can rebuild what baked a colour in.
    /// UI thread only.
    /// </summary>
    public static void Apply(bool light)
    {
        if (light == IsLight) return;
        IsLight = light;
        _t = light ? Onyx.Light : Onyx.Dark;
        Paint();
        Changed?.Invoke();
    }

    private static void Paint()
    {
        BgBrush.Color = Token("canvas");
        SurfaceBrush.Color = Token("surface");
        SurfaceHoverBrush.Color = Token("surface-hover");
        InsetBrush.Color = Token("surface-inset");
        TooltipBgBrush.Color = Token("popover");
        BorderBrush.Color = Token("border");
        BorderBrightBrush.Color = Token("border-strong");
        BorderInputBrush.Color = Token("border-input");
        RowBorderBrush.Color = Token("border");
        TextBrush.Color = Token("text");
        TextMutedBrush.Color = Token("text-dim");
        TextFaintBrush.Color = Token("text-faint");
        AccentBrush.Color = Token("accent");
        AccentBrightBrush.Color = Token("accent-bright");
        AccentFillBrush.Color = Token("accent-fill");
        OnAccentFillBrush.Color = Token("on-accent");
        AccentDimBrush.Color = Token("accent-dim");
        AccentGlowBrush.Color = Token("accent-glow");
        FocusRingBrush.Color = Token("focus-ring");
        WarnBrush.Color = Token("danger");
        WarnDimBrush.Color = Token("danger-dim");
        CautionBrush.Color = Token("warning");
        CautionDimBrush.Color = Token("warning-dim");
        GoodBrush.Color = Token("success");
        GoodDimBrush.Color = Token("success-dim");
        GridBrush.Color = Token("chart-grid");
        ChartCursorBrush.Color = Token("chart-cursor");
        SeriesOtherBrush.Color = Token("series-other");
        HeatNoneBrush.Color = Token("hm-none");
        HeatNoneRingBrush.Color = Token("hm-none-ring");
        TopBarBrush.Color = Token("topbar-bg");
        for (var i = 0; i < Heat.Length; i++) Heat[i].Color = Token($"hm-{i}");
    }

    /// <summary>A colour at a given opacity, for a chart's area fill under its own line.</summary>
    public static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);
}

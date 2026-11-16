using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace NandWatch.App.Theme;

/// <summary>
/// The Onyx tokens for an app that builds its UI in code. Every value comes
/// from <c>dist/windows/OnyxPalette.cs</c>, which is generated from the
/// tokens: no colour is written here or in anything that draws.
/// </summary>
/// <remarks>
/// <para><b>How a theme switch reaches the screen.</b> Every <c>...Brush</c>
/// is ONE shared object, retinted in place by <see cref="Apply"/>, so
/// everything painted with one repaints at once. A <see cref="Color"/> read
/// while building (a gradient, a thing's own colour) is baked into what was
/// built, so the window rebuilds its page on <see cref="Changed"/>.</para>
/// <para>Copy this file with <c>OnyxPalette.cs</c>. An app that needs a
/// colour the tokens lack adds it to the tokens, not here.</para>
/// </remarks>
public static class OnyxTheme
{
    private static OnyxPalette _p = OnyxPalette.For(true, OnyxAccent.Blue);

    public static bool IsLight { get; private set; }
    public static OnyxAccent Accent { get; private set; } = OnyxAccent.Blue;

    /// <summary>Raised after <see cref="Apply"/> changed the theme or the accent.</summary>
    public static event Action? Changed;

    /// <summary>
    /// The page zoom, for what is drawn outside the zoomed page (a tooltip's
    /// popup). An app with Ctrl+Plus zoom points this at its level.
    /// </summary>
    public static Func<double> PopupScale { get; set; } = () => 1;

    public static Color From(System.Drawing.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);
    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>"#2f80ed" or "2f80ed" to a colour; null when it is not one.</summary>
    public static Color? Parse(string? hex)
    {
        var h = hex?.TrimStart('#');
        if (h is not { Length: 6 } || !uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>A second measure of the same thing (upload beside download) is a tint of its colour, never a second hue.</summary>
    public static Color Tint(Color color) => Mix(SurfaceColor, color, 0.52);

    public static Color SurfaceColor => From(_p.Surface);
    public static Color AccentColor => From(_p.Accent);
    public static Color SeriesOtherColor => From(_p.SeriesOther);

    public static readonly SolidColorBrush CanvasBrush = new();
    public static readonly SolidColorBrush SurfaceBrush = new();
    public static readonly SolidColorBrush SurfaceHoverBrush = new();
    public static readonly SolidColorBrush SurfaceInsetBrush = new();
    public static readonly SolidColorBrush PopoverBrush = new();
    public static readonly SolidColorBrush BorderBrush = new();
    public static readonly SolidColorBrush BorderStrongBrush = new();
    public static readonly SolidColorBrush BorderInputBrush = new();
    public static readonly SolidColorBrush TextBrush = new();
    public static readonly SolidColorBrush TextDimBrush = new();
    public static readonly SolidColorBrush TextFaintBrush = new();
    public static readonly SolidColorBrush AccentBrush = new();
    public static readonly SolidColorBrush AccentBrightBrush = new();
    public static readonly SolidColorBrush AccentFillBrush = new();
    public static readonly SolidColorBrush OnAccentBrush = new();
    public static readonly SolidColorBrush AccentDimBrush = new();
    public static readonly SolidColorBrush DangerBrush = new();
    public static readonly SolidColorBrush SuccessBrush = new();
    public static readonly SolidColorBrush ChartGridBrush = new();
    public static readonly SolidColorBrush ChartCursorBrush = new();
    public static readonly SolidColorBrush HeatNoneBrush = new();
    public static readonly SolidColorBrush HeatNoneRingBrush = new();
    public static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    /// <summary>The heat ramp, <c>hm-0</c> (a recorded zero) to <c>hm-5</c>.</summary>
    public static readonly SolidColorBrush[] Heat = [.. Enumerable.Range(0, 6).Select(_ => new SolidColorBrush())];

    /// <summary>The categorical series colours, <c>series-1</c> to <c>series-7</c>, for things with no colour of their own.</summary>
    public static Color Series(int index)
    {
        System.Drawing.Color[] all = [_p.Series1, _p.Series2, _p.Series3, _p.Series4, _p.Series5, _p.Series6, _p.Series7];
        return From(all[((index % all.Length) + all.Length) % all.Length]);
    }

    static OnyxTheme() => Paint();

    /// <summary>Switches theme or accent. UI thread only.</summary>
    public static void Apply(bool light, OnyxAccent accent = OnyxAccent.Blue)
    {
        if (light == IsLight && accent == Accent) return;
        IsLight = light;
        Accent = accent;
        _p = OnyxPalette.For(!light, accent);
        Paint();
        Changed?.Invoke();
    }

    private static void Paint()
    {
        CanvasBrush.Color = From(_p.Canvas);
        SurfaceBrush.Color = From(_p.Surface);
        SurfaceHoverBrush.Color = From(_p.SurfaceHover);
        SurfaceInsetBrush.Color = From(_p.SurfaceInset);
        PopoverBrush.Color = From(_p.Popover);
        BorderBrush.Color = From(_p.Border);
        BorderStrongBrush.Color = From(_p.BorderStrong);
        BorderInputBrush.Color = From(_p.BorderInput);
        TextBrush.Color = From(_p.Text);
        TextDimBrush.Color = From(_p.TextDim);
        TextFaintBrush.Color = From(_p.TextFaint);
        AccentBrush.Color = From(_p.Accent);
        AccentBrightBrush.Color = From(_p.AccentBright);
        AccentFillBrush.Color = From(_p.AccentFill);
        OnAccentBrush.Color = From(_p.OnAccent);
        AccentDimBrush.Color = From(_p.AccentDim);
        DangerBrush.Color = From(_p.Danger);
        SuccessBrush.Color = From(_p.Success);
        ChartGridBrush.Color = From(_p.ChartGrid);
        ChartCursorBrush.Color = From(_p.ChartCursor);
        HeatNoneBrush.Color = From(_p.HmNone);
        HeatNoneRingBrush.Color = From(_p.HmNoneRing);
        System.Drawing.Color[] heat = [_p.Hm0, _p.Hm1, _p.Hm2, _p.Hm3, _p.Hm4, _p.Hm5];
        for (var i = 0; i < Heat.Length; i++) Heat[i].Color = From(heat[i]);
    }
}

/// <summary>Geist, bundled in <c>Assets/Fonts</c> (SIL Open Font License), so nothing is fetched.</summary>
public static class OnyxFonts
{
    public static readonly FontFamily Sans = new("ms-appx:///Assets/Fonts/Geist-Variable.ttf#Geist");
    public static readonly FontFamily Mono = new("ms-appx:///Assets/Fonts/GeistMono-Variable.ttf#Geist Mono");

    public static FontWeight Weight(int weight) => new((ushort)weight);
}

/// <summary>Whether to animate: the system's "animation effects" setting.</summary>
public static class OnyxMotion
{
    private static readonly global::Windows.UI.ViewManagement.UISettings Settings = new();

    public static bool Enabled
    {
        get
        {
            try { return Settings.AnimationsEnabled; }
            catch { return true; }
        }
    }
}

/// <summary>The type styles of the tokens. Text is set in one of these and in no other size.</summary>
public enum TextStyle { Hero, Stat, H1, H2, Body, Small, Label, Caption, Mono }

public static class OnyxText
{
    public static double Size(TextStyle style) => style switch
    {
        TextStyle.Hero => OnyxMetrics.FontSizeHero,
        TextStyle.Stat => OnyxMetrics.FontSizeStat,
        TextStyle.H1 => OnyxMetrics.FontSizeH1,
        TextStyle.H2 => OnyxMetrics.FontSizeH2,
        TextStyle.Body => OnyxMetrics.FontSizeBody,
        TextStyle.Small => OnyxMetrics.FontSizeSmall,
        TextStyle.Label => OnyxMetrics.FontSizeLabel,
        TextStyle.Caption => OnyxMetrics.FontSizeCaption,
        _ => OnyxMetrics.FontSizeMono,
    };

    /// <summary>
    /// A text block in a type style. Text is selectable, as on the web; pass
    /// <paramref name="selectable"/> false for a label on something that is
    /// pressed or hovered, where a drag should not start a selection.
    /// </summary>
    public static TextBlock Of(
        string text, TextStyle style = TextStyle.Body, Brush? brush = null, int weight = 400,
        bool numeric = false, bool selectable = true, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = style == TextStyle.Label ? text.ToUpperInvariant() : text,
            FontFamily = style == TextStyle.Mono ? OnyxFonts.Mono : OnyxFonts.Sans,
            FontSize = Size(style),
            FontWeight = OnyxFonts.Weight(weight),
            Foreground = brush ?? OnyxTheme.TextBrush,
            IsTextSelectionEnabled = selectable,
            SelectionHighlightColor = OnyxTheme.AccentBrush,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            CharacterSpacing = style == TextStyle.Label ? 90 : 0,
        };
        if (numeric) Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }
}

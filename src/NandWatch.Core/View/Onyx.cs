namespace NandWatch.Core.View;

/// <summary>
/// The Onyx design system's colour tokens this app uses, per theme, as Onyx
/// resolves them for the blue accent (<c>dist/tokens.resolved.json</c> in the
/// Onyx repo). The app paints only from these, through its Palette.
/// </summary>
/// <remarks>
/// <b>Generated from Onyx, never edited by hand.</b> The test
/// <c>OnyxTests</c> compares every value with the copy of Onyx's file vendored
/// in the tests, so a typo or a drift fails the build. To take a new Onyx:
/// copy its <c>dist/tokens.resolved.json</c> over the vendored one, and update
/// the values here until the test passes.
/// </remarks>
public static class Onyx
{
    public static IReadOnlyDictionary<string, string> Dark { get; } = new Dictionary<string, string>
    {
        ["canvas"] = "#000000",
        ["surface"] = "#0a0a0b",
        ["surface-hover"] = "#111114",
        ["surface-inset"] = "#08080a",
        ["popover"] = "#131317",
        ["border"] = "#1e1e22",
        ["border-strong"] = "#2c2c33",
        ["border-input"] = "#5d5d64",
        ["text"] = "#f5f5f7",
        ["text-dim"] = "#a1a1aa",
        ["text-faint"] = "#7c7c88",
        ["accent"] = "#2f80ed",
        ["accent-bright"] = "#4d97ff",
        ["accent-fill"] = "#1570eb",
        ["on-accent"] = "#ffffff",
        ["accent-dim"] = "rgba(47, 128, 237, 0.16)",
        ["accent-glow"] = "rgba(47, 128, 237, 0.28)",
        ["focus-ring"] = "#2f80ed",
        ["danger"] = "#ff4d4f",
        ["danger-dim"] = "rgba(255, 77, 79, 0.14)",
        ["success"] = "#22c55e",
        ["success-dim"] = "rgba(34, 197, 94, 0.14)",
        ["warning"] = "#ffb300",
        ["warning-dim"] = "rgba(255, 179, 0, 0.14)",
        ["series-1"] = "#2e8eff",
        ["series-2"] = "#ffb300",
        ["series-3"] = "#9b5cff",
        ["series-4"] = "#ff6bbf",
        ["series-5"] = "#00d4f5",
        ["series-6"] = "#ff7a00",
        ["series-7"] = "#00ddb8",
        ["series-other"] = "#7a8090",
        ["chart-grid"] = "#1a1a1e",
        ["chart-cursor"] = "rgba(255, 255, 255, 0.04)",
        ["hm-none"] = "#0b0b0d",
        ["hm-none-ring"] = "#1e1e22",
        ["hm-0"] = "#131519",
        ["hm-1"] = "#12325c",
        ["hm-2"] = "#17488a",
        ["hm-3"] = "#1f62bd",
        ["hm-4"] = "#2f80ed",
        ["hm-5"] = "#6fb0ff",
        ["topbar-bg"] = "rgba(0, 0, 0, 0.82)",
        ["scrim"] = "rgba(0, 0, 0, 0.72)",
        ["plate"] = "#ececf1",
    };

    public static IReadOnlyDictionary<string, string> Light { get; } = new Dictionary<string, string>
    {
        ["canvas"] = "#f4f5f7",
        ["surface"] = "#ffffff",
        ["surface-hover"] = "#eceff4",
        ["surface-inset"] = "#eef0f3",
        ["popover"] = "#ffffff",
        ["border"] = "#e3e5ea",
        ["border-strong"] = "#cfd2d9",
        ["border-input"] = "#8a8a96",
        ["text"] = "#16171a",
        ["text-dim"] = "#52525b",
        ["text-faint"] = "#6b6b76",
        ["accent"] = "#1664d9",
        ["accent-bright"] = "#0f5bc4",
        ["accent-fill"] = "#1664d9",
        ["on-accent"] = "#ffffff",
        ["accent-dim"] = "rgba(22, 100, 217, 0.1)",
        ["accent-glow"] = "rgba(22, 100, 217, 0.24)",
        ["focus-ring"] = "#1664d9",
        ["danger"] = "#b42318",
        ["danger-dim"] = "rgba(217, 45, 32, 0.1)",
        ["success"] = "#15803d",
        ["success-dim"] = "rgba(21, 128, 61, 0.1)",
        ["warning"] = "#9a5b00",
        ["warning-dim"] = "rgba(154, 91, 0, 0.1)",
        ["series-1"] = "#1c6fd6",
        ["series-2"] = "#a16207",
        ["series-3"] = "#7c3aed",
        ["series-4"] = "#c0267e",
        ["series-5"] = "#0277bd",
        ["series-6"] = "#c2410c",
        ["series-7"] = "#0f766e",
        ["series-other"] = "#6b7280",
        ["chart-grid"] = "#e3e5ea",
        ["chart-cursor"] = "rgba(16, 24, 40, 0.05)",
        ["hm-none"] = "#f6f7f9",
        ["hm-none-ring"] = "#cdd1d8",
        ["hm-0"] = "#e1e6ed",
        ["hm-1"] = "#c7dcf8",
        ["hm-2"] = "#94bdf2",
        ["hm-3"] = "#5b99e8",
        ["hm-4"] = "#2a74dc",
        ["hm-5"] = "#1252b0",
        ["topbar-bg"] = "rgba(255, 255, 255, 0.86)",
        ["scrim"] = "rgba(22, 23, 26, 0.4)",
        ["plate"] = "#ffffff00",
    };

    /// <summary>A token's colour as (alpha, red, green, blue): <c>#rrggbb</c>, <c>#rrggbbaa</c> or <c>rgba(r, g, b, a)</c>.</summary>
    public static (byte A, byte R, byte G, byte B) Argb(string css)
    {
        css = css.Trim();
        if (css.StartsWith('#') && css.Length == 7)
            return (255, Convert.ToByte(css[1..3], 16), Convert.ToByte(css[3..5], 16), Convert.ToByte(css[5..7], 16));
        // CSS order: the alpha byte comes LAST (#rrggbbaa), unlike XAML's #aarrggbb.
        if (css.StartsWith('#') && css.Length == 9)
            return (Convert.ToByte(css[7..9], 16), Convert.ToByte(css[1..3], 16), Convert.ToByte(css[3..5], 16), Convert.ToByte(css[5..7], 16));
        if (css.StartsWith("rgba(", StringComparison.Ordinal) && css.EndsWith(')'))
        {
            var p = css[5..^1].Split(',').Select(s => s.Trim()).ToArray();
            var alpha = double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture);
            return ((byte)Math.Round(alpha * 255), byte.Parse(p[0]), byte.Parse(p[1]), byte.Parse(p[2]));
        }
        throw new FormatException($"Not an Onyx colour: {css}");
    }
}

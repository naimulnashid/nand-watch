// Onyx for WinForms and GDI+ (tray icons, flyouts). Generated from tokens/tokens.json by scripts/build.mjs. Do not edit by hand.
// C# 7.3 compatible: fields and object initialisers, no records or init-only properties.
using System.Drawing;

namespace NandWatch.App.Theme
{
    public enum OnyxAccent { Blue, Green, Violet, Teal }

    public sealed class OnyxPalette
    {
        public bool IsDark;
        public Color Canvas;
        public Color Surface;
        public Color SurfaceHover;
        public Color SurfaceInset;
        public Color Popover;
        public Color Border;
        public Color BorderStrong;
        public Color BorderInput;
        public Color Text;
        public Color TextDim;
        public Color TextFaint;
        public Color AccentBlue;
        public Color AccentBlueBright;
        public Color AccentBlueFill;
        public Color OnAccentBlue;
        public Color AccentBlueDim;
        public Color AccentBlueGlow;
        public Color AccentGreen;
        public Color AccentGreenBright;
        public Color AccentGreenFill;
        public Color OnAccentGreen;
        public Color AccentGreenDim;
        public Color AccentGreenGlow;
        public Color AccentViolet;
        public Color AccentVioletBright;
        public Color AccentVioletFill;
        public Color OnAccentViolet;
        public Color AccentVioletDim;
        public Color AccentVioletGlow;
        public Color AccentTeal;
        public Color AccentTealBright;
        public Color AccentTealFill;
        public Color OnAccentTeal;
        public Color AccentTealDim;
        public Color AccentTealGlow;
        public Color Accent;
        public Color AccentBright;
        public Color AccentFill;
        public Color OnAccent;
        public Color AccentDim;
        public Color AccentGlow;
        public Color FocusRing;
        public Color Danger;
        public Color DangerDim;
        public Color Success;
        public Color SuccessDim;
        public Color Warning;
        public Color WarningDim;
        public Color Series1;
        public Color Series2;
        public Color Series3;
        public Color Series4;
        public Color Series5;
        public Color Series6;
        public Color Series7;
        public Color SeriesOther;
        public Color ChartGrid;
        public Color ChartCursor;
        public Color HmNone;
        public Color HmNoneRing;
        public Color Hm0;
        public Color Hm1;
        public Color Hm2;
        public Color Hm3;
        public Color Hm4;
        public Color Hm5;
        public Color TopbarBg;
        public Color Scrim;
        public Color Plate;

        public static readonly OnyxPalette Dark = new OnyxPalette
        {
            IsDark = true,
            Canvas = Color.FromArgb(255, 0, 0, 0),
            Surface = Color.FromArgb(255, 10, 10, 11),
            SurfaceHover = Color.FromArgb(255, 17, 17, 20),
            SurfaceInset = Color.FromArgb(255, 8, 8, 10),
            Popover = Color.FromArgb(255, 19, 19, 23),
            Border = Color.FromArgb(255, 30, 30, 34),
            BorderStrong = Color.FromArgb(255, 44, 44, 51),
            BorderInput = Color.FromArgb(255, 93, 93, 100),
            Text = Color.FromArgb(255, 245, 245, 247),
            TextDim = Color.FromArgb(255, 161, 161, 170),
            TextFaint = Color.FromArgb(255, 125, 125, 137),
            AccentBlue = Color.FromArgb(255, 47, 128, 237),
            AccentBlueBright = Color.FromArgb(255, 77, 151, 255),
            AccentBlueFill = Color.FromArgb(255, 21, 112, 235),
            OnAccentBlue = Color.FromArgb(255, 255, 255, 255),
            AccentBlueDim = Color.FromArgb(41, 47, 128, 237),
            AccentBlueGlow = Color.FromArgb(71, 47, 128, 237),
            AccentGreen = Color.FromArgb(255, 0, 230, 118),
            AccentGreenBright = Color.FromArgb(255, 0, 255, 132),
            AccentGreenFill = Color.FromArgb(255, 0, 230, 118),
            OnAccentGreen = Color.FromArgb(255, 0, 0, 0),
            AccentGreenDim = Color.FromArgb(41, 0, 230, 118),
            AccentGreenGlow = Color.FromArgb(71, 0, 230, 118),
            AccentViolet = Color.FromArgb(255, 139, 92, 246),
            AccentVioletBright = Color.FromArgb(255, 167, 139, 250),
            AccentVioletFill = Color.FromArgb(255, 124, 58, 237),
            OnAccentViolet = Color.FromArgb(255, 255, 255, 255),
            AccentVioletDim = Color.FromArgb(41, 139, 92, 246),
            AccentVioletGlow = Color.FromArgb(71, 139, 92, 246),
            AccentTeal = Color.FromArgb(255, 45, 212, 191),
            AccentTealBright = Color.FromArgb(255, 127, 227, 214),
            AccentTealFill = Color.FromArgb(255, 45, 212, 191),
            OnAccentTeal = Color.FromArgb(255, 4, 33, 29),
            AccentTealDim = Color.FromArgb(41, 45, 212, 191),
            AccentTealGlow = Color.FromArgb(71, 45, 212, 191),
            Accent = Color.FromArgb(255, 47, 128, 237),
            AccentBright = Color.FromArgb(255, 77, 151, 255),
            AccentFill = Color.FromArgb(255, 21, 112, 235),
            OnAccent = Color.FromArgb(255, 255, 255, 255),
            AccentDim = Color.FromArgb(41, 47, 128, 237),
            AccentGlow = Color.FromArgb(71, 47, 128, 237),
            FocusRing = Color.FromArgb(255, 47, 128, 237),
            Danger = Color.FromArgb(255, 255, 77, 79),
            DangerDim = Color.FromArgb(36, 255, 77, 79),
            Success = Color.FromArgb(255, 34, 197, 94),
            SuccessDim = Color.FromArgb(36, 34, 197, 94),
            Warning = Color.FromArgb(255, 255, 179, 0),
            WarningDim = Color.FromArgb(36, 255, 179, 0),
            Series1 = Color.FromArgb(255, 46, 142, 255),
            Series2 = Color.FromArgb(255, 255, 179, 0),
            Series3 = Color.FromArgb(255, 155, 92, 255),
            Series4 = Color.FromArgb(255, 255, 107, 191),
            Series5 = Color.FromArgb(255, 0, 212, 245),
            Series6 = Color.FromArgb(255, 255, 122, 0),
            Series7 = Color.FromArgb(255, 0, 221, 184),
            SeriesOther = Color.FromArgb(255, 122, 128, 144),
            ChartGrid = Color.FromArgb(255, 26, 26, 30),
            ChartCursor = Color.FromArgb(10, 255, 255, 255),
            HmNone = Color.FromArgb(255, 11, 11, 13),
            HmNoneRing = Color.FromArgb(255, 30, 30, 34),
            Hm0 = Color.FromArgb(255, 19, 21, 25),
            Hm1 = Color.FromArgb(255, 18, 50, 92),
            Hm2 = Color.FromArgb(255, 23, 72, 138),
            Hm3 = Color.FromArgb(255, 31, 98, 189),
            Hm4 = Color.FromArgb(255, 47, 128, 237),
            Hm5 = Color.FromArgb(255, 111, 176, 255),
            TopbarBg = Color.FromArgb(209, 0, 0, 0),
            Scrim = Color.FromArgb(184, 0, 0, 0),
            Plate = Color.FromArgb(255, 236, 236, 241),
        };

        public static readonly OnyxPalette Light = new OnyxPalette
        {
            IsDark = false,
            Canvas = Color.FromArgb(255, 230, 230, 234),
            Surface = Color.FromArgb(255, 255, 255, 255),
            SurfaceHover = Color.FromArgb(255, 236, 239, 244),
            SurfaceInset = Color.FromArgb(255, 238, 240, 243),
            Popover = Color.FromArgb(255, 255, 255, 255),
            Border = Color.FromArgb(255, 227, 229, 234),
            BorderStrong = Color.FromArgb(255, 207, 210, 217),
            BorderInput = Color.FromArgb(255, 128, 128, 139),
            Text = Color.FromArgb(255, 22, 23, 26),
            TextDim = Color.FromArgb(255, 82, 82, 91),
            TextFaint = Color.FromArgb(255, 100, 100, 110),
            AccentBlue = Color.FromArgb(255, 22, 100, 217),
            AccentBlueBright = Color.FromArgb(255, 15, 91, 196),
            AccentBlueFill = Color.FromArgb(255, 22, 100, 217),
            OnAccentBlue = Color.FromArgb(255, 255, 255, 255),
            AccentBlueDim = Color.FromArgb(26, 22, 100, 217),
            AccentBlueGlow = Color.FromArgb(61, 22, 100, 217),
            AccentGreen = Color.FromArgb(255, 0, 122, 61),
            AccentGreenBright = Color.FromArgb(255, 0, 102, 51),
            AccentGreenFill = Color.FromArgb(255, 0, 122, 61),
            OnAccentGreen = Color.FromArgb(255, 255, 255, 255),
            AccentGreenDim = Color.FromArgb(26, 0, 122, 61),
            AccentGreenGlow = Color.FromArgb(61, 0, 122, 61),
            AccentViolet = Color.FromArgb(255, 124, 58, 237),
            AccentVioletBright = Color.FromArgb(255, 109, 40, 217),
            AccentVioletFill = Color.FromArgb(255, 124, 58, 237),
            OnAccentViolet = Color.FromArgb(255, 255, 255, 255),
            AccentVioletDim = Color.FromArgb(26, 124, 58, 237),
            AccentVioletGlow = Color.FromArgb(61, 124, 58, 237),
            AccentTeal = Color.FromArgb(255, 0, 117, 106),
            AccentTealBright = Color.FromArgb(255, 0, 95, 86),
            AccentTealFill = Color.FromArgb(255, 0, 117, 106),
            OnAccentTeal = Color.FromArgb(255, 255, 255, 255),
            AccentTealDim = Color.FromArgb(26, 0, 117, 106),
            AccentTealGlow = Color.FromArgb(61, 0, 117, 106),
            Accent = Color.FromArgb(255, 22, 100, 217),
            AccentBright = Color.FromArgb(255, 15, 91, 196),
            AccentFill = Color.FromArgb(255, 22, 100, 217),
            OnAccent = Color.FromArgb(255, 255, 255, 255),
            AccentDim = Color.FromArgb(26, 22, 100, 217),
            AccentGlow = Color.FromArgb(61, 22, 100, 217),
            FocusRing = Color.FromArgb(255, 22, 100, 217),
            Danger = Color.FromArgb(255, 180, 35, 24),
            DangerDim = Color.FromArgb(26, 217, 45, 32),
            Success = Color.FromArgb(255, 21, 128, 61),
            SuccessDim = Color.FromArgb(26, 21, 128, 61),
            Warning = Color.FromArgb(255, 154, 91, 0),
            WarningDim = Color.FromArgb(26, 154, 91, 0),
            Series1 = Color.FromArgb(255, 28, 111, 214),
            Series2 = Color.FromArgb(255, 161, 98, 7),
            Series3 = Color.FromArgb(255, 124, 58, 237),
            Series4 = Color.FromArgb(255, 192, 38, 126),
            Series5 = Color.FromArgb(255, 2, 119, 189),
            Series6 = Color.FromArgb(255, 194, 65, 12),
            Series7 = Color.FromArgb(255, 15, 118, 110),
            SeriesOther = Color.FromArgb(255, 107, 114, 128),
            ChartGrid = Color.FromArgb(255, 227, 229, 234),
            ChartCursor = Color.FromArgb(13, 16, 24, 40),
            HmNone = Color.FromArgb(255, 246, 247, 249),
            HmNoneRing = Color.FromArgb(255, 205, 209, 216),
            Hm0 = Color.FromArgb(255, 225, 230, 237),
            Hm1 = Color.FromArgb(255, 199, 220, 248),
            Hm2 = Color.FromArgb(255, 148, 189, 242),
            Hm3 = Color.FromArgb(255, 91, 153, 232),
            Hm4 = Color.FromArgb(255, 42, 116, 220),
            Hm5 = Color.FromArgb(255, 18, 82, 176),
            TopbarBg = Color.FromArgb(219, 255, 255, 255),
            Scrim = Color.FromArgb(102, 22, 23, 26),
            Plate = Color.FromArgb(0, 255, 255, 255),
        };

        /// <summary>The palette for a theme with the accent roles pointed at one registry hue.</summary>
        public static OnyxPalette For(bool dark, OnyxAccent accent = OnyxAccent.Blue)
        {
            return (dark ? Dark : Light).WithAccent(accent);
        }

        public OnyxPalette WithAccent(OnyxAccent accent)
        {
            var p = (OnyxPalette)MemberwiseClone();
            switch (accent)
            {
                case OnyxAccent.Blue:
                    p.Accent = AccentBlue;
                    p.AccentBright = AccentBlueBright;
                    p.AccentFill = AccentBlueFill;
                    p.OnAccent = OnAccentBlue;
                    p.AccentDim = AccentBlueDim;
                    p.AccentGlow = AccentBlueGlow;
                    break;
                case OnyxAccent.Green:
                    p.Accent = AccentGreen;
                    p.AccentBright = AccentGreenBright;
                    p.AccentFill = AccentGreenFill;
                    p.OnAccent = OnAccentGreen;
                    p.AccentDim = AccentGreenDim;
                    p.AccentGlow = AccentGreenGlow;
                    break;
                case OnyxAccent.Violet:
                    p.Accent = AccentViolet;
                    p.AccentBright = AccentVioletBright;
                    p.AccentFill = AccentVioletFill;
                    p.OnAccent = OnAccentViolet;
                    p.AccentDim = AccentVioletDim;
                    p.AccentGlow = AccentVioletGlow;
                    break;
                case OnyxAccent.Teal:
                    p.Accent = AccentTeal;
                    p.AccentBright = AccentTealBright;
                    p.AccentFill = AccentTealFill;
                    p.OnAccent = OnAccentTeal;
                    p.AccentDim = AccentTealDim;
                    p.AccentGlow = AccentTealGlow;
                    break;
            }
            p.FocusRing = p.Accent;
            return p;
        }
    }

    public static class OnyxMetrics
    {
        public const float RadiusXs = 4f;
        public const float RadiusSm = 9f;
        public const float Radius = 14f;
        public const float RadiusPill = 999f;
        public const float FontSizeHero = 86.4f;
        public const float FontSizeStat = 48f;
        public const float FontSizeH1 = 35.2f;
        public const float FontSizeH2 = 20.8f;
        public const float FontSizeBody = 17f;
        public const float FontSizeSmall = 15f;
        public const float FontSizeLabel = 13f;
        public const float FontSizeCaption = 13f;
        public const float FontSizeMono = 15f;
        public const float Space1 = 4f;
        public const float Space2 = 8f;
        public const float Space3 = 12f;
        public const float Space4 = 16f;
        public const float Space5 = 20f;
        public const float Space6 = 24f;
        public const float Space8 = 32f;
        public const float Space12 = 48f;
        public const float PadCard = 25.6f;
        public const float GapGrid = 18.4f;
        public const float GutterPage = 40f;
        public const float ContentMax = 1480f;
        public const float SideW = 232f;
        public const float SideWCollapsed = 62f;
        public const float TopbarMinH = 61f;
        public const float ColMin = 260f;
        public const float ColMinWide = 340f;
        public const float HmCellMin = 11f;
    }
}

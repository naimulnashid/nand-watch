using System.Text.Json;
using NandWatch.Core.View;

namespace NandWatch.Core.Tests;

/// <summary>
/// The app's colours are Onyx's, exactly. Onyx/tokens.resolved.json is a copy
/// of the Onyx repo's dist/tokens.resolved.json (taken at its commit bcbcb42,
/// 2026-10-21); replace it to take a newer Onyx, and this says what moved.
/// </summary>
public class OnyxTests
{
    private static JsonElement Themes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Onyx", "tokens.resolved.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("themes");
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Every_token_matches_Onyx(string theme)
    {
        var onyx = Themes().GetProperty(theme);
        var ours = theme == "dark" ? Onyx.Dark : Onyx.Light;
        Assert.All(ours, t => Assert.Equal(onyx.GetProperty(t.Key).GetString(), t.Value));
    }

    [Fact]
    public void Both_themes_define_the_same_tokens()
    {
        Assert.Equal(Onyx.Dark.Keys.Order(), Onyx.Light.Keys.Order());
    }

    [Fact]
    public void Colours_parse_from_hex_and_rgba()
    {
        Assert.Equal(((byte)255, (byte)0x2f, (byte)0x80, (byte)0xed), Onyx.Argb("#2f80ed"));
        Assert.Equal(((byte)41, (byte)47, (byte)128, (byte)237), Onyx.Argb("rgba(47, 128, 237, 0.16)"));
        Assert.Equal(((byte)0, (byte)255, (byte)255, (byte)255), Onyx.Argb("#ffffff00"));
        Assert.All(Onyx.Dark.Values.Concat(Onyx.Light.Values), v => Onyx.Argb(v));
    }
}

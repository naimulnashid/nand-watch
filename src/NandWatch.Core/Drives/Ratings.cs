namespace NandWatch.Core.Drives;

/// <summary>
/// A manufacturer's endurance rating, where it was read, and the model's name
/// as the maker writes it (Windows reports part numbers such as
/// "Micron_2400_MTFDKBA512QFM").
/// </summary>
public sealed record Rating(decimal Tbw, string Source, string Checked, string? Name = null);

/// <summary>
/// Built-in TBW ratings by model, as Windows reports the model. Each entry
/// names its source and the date it was checked; a rating set for a drive in
/// the app (<c>drive_settings.rated_tbw</c>) overrides this list.
/// </summary>
public static class Ratings
{
    private static readonly Dictionary<string, Rating> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Micron_2400_MTFDKBA512QFM"] = new(150, "Micron 2400 SSD product page (micron.com)", "2026-10-24", "Micron 2400 512 GB"),
        ["Samsung SSD 970 EVO Plus 2TB"] = new(1200, "Samsung 970 EVO Plus 2TB product page (samsung.com)", "2026-10-24", "Samsung 970 EVO Plus 2 TB"),
    };

    public static Rating? For(string model) => Known.GetValueOrDefault(model.Trim());

    /// <summary>The model's readable name when it is known, else the model as Windows reports it.</summary>
    public static string DisplayName(string model) => For(model)?.Name ?? model.Trim();
}

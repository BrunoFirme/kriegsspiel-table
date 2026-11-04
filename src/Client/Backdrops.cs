using System.Text.RegularExpressions;
using KTable.Core.Protocol;

namespace KTable.Client;

public sealed record Backdrop(string Id, string Name, string File, bool SkyOnly = false, string? Light = null, bool Mirrored = false);

public static class Backdrops
{
    public const string None = "";

    public static readonly IReadOnlyList<Backdrop> All = new Backdrop[]
    {
        new("lodge-by-firelight", "Lodge by firelight", "lodge-by-firelight.webp"),
        new("warm-lounge", "Warm lounge", "warm-lounge.webp"),
        new("pub", "Pub", "pub.webp"),
        new("saloon", "Saloon", "saloon.webp"),
        new("ballroom", "Ballroom", "ballroom.webp"),
        new("state-ballroom", "State ballroom", "state-ballroom.webp"),
        new("grand-hall", "Grand hall", "grand-hall.webp"),
        new("open-sky-day", "Open sky by day", "open-sky-day.jpg", true, LightPresets.CoolDaylight, true),
        new("sundown", "Sundown", "sundown.jpg", true, LightPresets.Sundown, true),
        new("open-sky-night", "Open sky at night", "open-sky-night.jpg", true, LightPresets.MoonlitNight, true),
        new("starry-night", "Starry night", "starry-night.jpg", true, LightPresets.MoonlitNight),
        new("starry-desert", "Starry desert", "starry-desert.webp", false, LightPresets.MoonlitNight),
        new("beach", "By the beach", "beach.webp"),
        new("rolling-farmland", "Rolling farmland", "rolling-farmland.webp"),
        new("open-steppe", "Open steppe", "open-steppe.webp"),
        new("green-hills", "Green hills", "green-hills.webp"),
    };

    static readonly Regex Colour = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static Backdrop? Find(string id) => All.FirstOrDefault(b => b.Id == id);

    public static bool IsPicture(string backdrop) => UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, backdrop);

    public static bool IsColour(string backdrop) => Colour.IsMatch(backdrop);

    public static bool IsKnown(string backdrop) => backdrop.Length == 0 || IsColour(backdrop) || Find(backdrop) != null || IsPicture(backdrop);
}

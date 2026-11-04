using System.Text.RegularExpressions;
using KTable.Core.Protocol;

namespace KTable.Client;

public sealed record Backdrop(string Id, string Name, string File, string? Ground = null, string? Light = null);

public static class Backdrops
{
    public const string None = "";

    public static readonly IReadOnlyList<Backdrop> All = new Backdrop[]
    {
        new("drawing-room", "Drawing room", "drawing-room.webp"),
        new("baroque-hall", "Baroque hall", "baroque-hall.webp"),
        new("castle-vaults", "Castle vaults", "castle-vaults.webp"),
        new("chinese-garden", "Chinese garden", "chinese-garden.webp"),
        new("great-wall", "Great Wall watchtower", "great-wall.webp"),
        new("island-beach", "Island beach", "island-beach.webp"),
        new("shipwreck-coast", "Shipwreck coast", "shipwreck-coast.webp"),
        new("saxon-meadow", "Saxon meadow", "saxon-meadow.webp"),
        new("wooded-hills", "Wooded hills", "wooded-hills.webp"),
        new("orchard", "Orchard", "orchard.webp"),
        new("open-steppe", "Open steppe", "open-steppe.webp"),
        new("open-sky-day", "Open sky by day", "open-sky-day.webp", "open-sky-day-ground.webp", LightPresets.CoolDaylight),
        new("sundown", "Sundown", "sundown.webp", "sundown-ground.webp", LightPresets.Sundown),
        new("open-sky-night", "Open sky at night", "open-sky-night.webp", "open-sky-night-ground.webp", LightPresets.MoonlitNight),
        new("starry-night", "Starry night", "starry-night.webp", Light: LightPresets.MoonlitNight),
        new("starry-desert", "Starry desert", "starry-desert.webp", Light: LightPresets.MoonlitNight),
    };

    static readonly Regex Colour = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static Backdrop? Find(string id) => All.FirstOrDefault(b => b.Id == id);

    public static bool IsPicture(string backdrop) => UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, backdrop);

    public static bool IsColour(string backdrop) => Colour.IsMatch(backdrop);

    public static bool IsKnown(string backdrop) => backdrop.Length == 0 || IsColour(backdrop) || Find(backdrop) != null || IsPicture(backdrop);
}

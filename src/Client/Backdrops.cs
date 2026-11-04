using KTable.Core.Protocol;

namespace KTable.Client;

public sealed record Backdrop(string Id, string Name);

public static class Backdrops
{
    public const string None = "";
    public const string FileExtension = ".webp";

    public static readonly IReadOnlyList<Backdrop> All = new Backdrop[]
    {
        new("fireplace-lounge", "Fireplace lounge"),
        new("pine-attic", "Pine attic"),
        new("wooden-lounge", "Wooden lounge"),
        new("rec-room", "Rec room"),
        new("billiard-hall", "Billiard hall"),
        new("lounge-bar", "Lounge bar"),
        new("ballroom", "Ballroom"),
        new("open-sky-day", "Open sky by day"),
        new("open-sky-night", "Open sky at night"),
        new("starry-night", "Starry night"),
        new("beach", "By the beach"),
        new("field", "On the field"),
    };

    public static Backdrop? Find(string id) => All.FirstOrDefault(b => b.Id == id);

    public static bool IsPicture(string backdrop) => UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, backdrop);

    public static bool IsKnown(string backdrop) => backdrop.Length == 0 || Find(backdrop) != null || IsPicture(backdrop);
}

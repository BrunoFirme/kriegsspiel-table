using System.Globalization;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed class TokenOutlines(AssetStore assets)
{
    readonly AssetStore _assets = assets;

    const double UsualMergeDistance = 15;
    readonly Dictionary<(string, double), List<List<Vec2>>?> _outlines = new();

    public static string MergeKey(ModuleObj o) =>
        o.StateList().states.Any(s => s.Name == "Custom_Token" && s.MergeDistance != UsualMergeDistance)
            ? "|m" + string.Join(",", o.StateList().states.Select(s => s.MergeDistance.ToString(CultureInfo.InvariantCulture)))
            : "";

    public List<List<Vec2>>? Of(string? url, double mergeDistance)
    {
        if ((url == null ? null : _assets.Get(url)) is not { Sha: { } sha } e || _assets.FilePath(e) is not { } file || !AssetStore.IsPicture(e.Ext)) return null;
        if (_outlines.TryGetValue((sha, mergeDistance), out var known)) return known;
        using var bmp = BestEffort.Read(() => SKBitmap.Decode(file), fallback: null);
        var outline = bmp == null ? null : TokenOutline.Of(bmp, mergeDistance);
        _outlines[(sha, mergeDistance)] = outline;
        return outline;
    }
}

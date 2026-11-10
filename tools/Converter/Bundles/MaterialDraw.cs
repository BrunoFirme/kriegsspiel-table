using AssetsTools.NET;
using AssetsTools.NET.Extra;
using KTable.Core.Model;

namespace KTable.Converter.Bundles;

public sealed record ShaderFacts(bool BothSides, SurfaceAlpha Alpha, bool FollowsMode = false);

public static class MaterialDraw
{
    const string BuiltinShaders = "unity_builtin_extra";
    const string Mode = "_Mode", Cutoff = "_Cutoff";
    const int CullOff = 0, OneBlend = 1, ZeroBlend = 0;
    const int OpaqueMode = 0, CutoutMode = 1;
    const string CutoutType = "TransparentCutout", AlphaTestQueue = "AlphaTest";
    static readonly string[] SkippedPasses = ["SHADOWCASTER", "META", "DEFERRED"];

    public static readonly ShaderFacts Standard = new(false, SurfaceAlpha.Opaque, FollowsMode: true);
    static readonly ShaderFacts Solid = new(false, SurfaceAlpha.Opaque), Cut = new(false, SurfaceAlpha.Cut), Blended = new(false, SurfaceAlpha.Blend);
    static readonly ShaderFacts LeafCut = new(true, SurfaceAlpha.Cut), Flat = new(true, SurfaceAlpha.Blend);

    public static readonly IReadOnlyDictionary<long, ShaderFacts> Builtin = new Dictionary<long, ShaderFacts>
    {
        [45] = Standard, [46] = Standard,
        [3] = Solid, [6] = Solid, [7] = Solid,
        [34] = Blended, [50] = Cut, [53] = Cut,
        [200] = Flat, [201] = Flat, [202] = Flat, [203] = Flat, [205] = Flat, [206] = Flat, [207] = Flat, [208] = Flat,
        [10505] = Solid, [10509] = Solid, [10600] = Solid, [10620] = Solid, [10623] = Solid,
        [10511] = LeafCut, [10601] = Cut, [10606] = Cut, [14000] = Cut, [14001] = Cut,
        [10750] = Blended, [10751] = Cut, [10752] = Solid, [10755] = Solid,
        [10757] = Flat, [10760] = Flat, [10761] = Flat, [10764] = Flat, [10765] = Flat, [10766] = Flat, [10770] = Flat, [10800] = Flat,
    };

    public static SurfaceDraw Drawn(ShaderFacts facts, IReadOnlyDictionary<string, double> floats)
    {
        var alpha = facts.FollowsMode
            ? (int)floats.GetValueOrDefault(Mode, OpaqueMode) switch { OpaqueMode => SurfaceAlpha.Opaque, CutoutMode => SurfaceAlpha.Cut, _ => SurfaceAlpha.Blend }
            : facts.Alpha;
        return new SurfaceDraw(!facts.BothSides, alpha, floats.GetValueOrDefault(Cutoff, SurfaceDraw.HalfCut));
    }

    public static ShaderFacts FromState(double cull, double source, double destination, string renderType, string queue, bool hasMode)
    {
        if (hasMode) return Standard with { BothSides = cull == CullOff };
        var alpha = source != OneBlend || destination != ZeroBlend ? SurfaceAlpha.Blend
            : renderType == CutoutType || queue == AlphaTestQueue ? SurfaceAlpha.Cut
            : SurfaceAlpha.Opaque;
        return new ShaderFacts(cull == CullOff, alpha);
    }

    public static SurfaceDraw? Of(AssetsManager manager, AssetsFileInstance file, AssetTypeValueField? material)
    {
        if (BundleScene.Follow(manager, file, material) is not { } found) return null;
        var floats = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var entry in found.Field["m_SavedProperties"]["m_Floats.Array"].Children)
            floats[entry["first"].AsString] = entry["second"].AsDouble;
        return Drawn(Facts(manager, found.File, found.Field["m_Shader"], floats), floats);
    }

    static ShaderFacts Facts(AssetsManager manager, AssetsFileInstance file, AssetTypeValueField pointer, Dictionary<string, double> floats)
    {
        if (pointer.IsDummy) return Standard;
        int fileId = pointer["m_FileID"].AsInt;
        long pathId = pointer["m_PathID"].AsLong;
        if (fileId > 0 && fileId <= file.file.Metadata.Externals.Count
            && file.file.Metadata.Externals[fileId - 1].PathName.EndsWith(BuiltinShaders, StringComparison.OrdinalIgnoreCase))
            return Builtin.GetValueOrDefault(pathId, Standard);
        if (BundleScene.Follow(manager, file, pointer) is not { } shader) return Standard;
        var form = shader.Field["m_ParsedForm"];
        if (form.IsDummy) return Standard;
        var subs = form["m_SubShaders.Array"].Children;
        if (subs.Count == 0) return Standard;
        var passes = subs[0]["m_Passes.Array"].Children;
        var pass = passes.FirstOrDefault(p => !SkippedPasses.Contains(Tag(p["m_State"]["m_Tags"], "LIGHTMODE"))) ?? passes.FirstOrDefault();
        if (pass == null) return Standard;
        var state = pass["m_State"];
        double Value(AssetTypeValueField f, double fallback) =>
            f["name"].AsString is { } bound && bound != "<noninit>" ? floats.GetValueOrDefault(bound, fallback) : f["val"].AsDouble;
        bool hasMode = form["m_PropInfo"]["m_Props.Array"].Children.Any(p => p["m_Name"].AsString == Mode);
        string Tagged(string key) => Tag(state["m_Tags"], key) is { Length: > 0 } t ? t : Tag(subs[0]["m_Tags"], key);
        return FromState(Value(state["culling"], 2), Value(state["rtBlend0"]["srcBlend"], OneBlend), Value(state["rtBlend0"]["destBlend"], ZeroBlend),
                         Tagged("RenderType"), Tagged("QUEUE"), hasMode);
    }

    static string Tag(AssetTypeValueField tags, string key)
    {
        if (tags.IsDummy) return "";
        foreach (var entry in tags["tags.Array"].Children)
            if (string.Equals(entry["first"].AsString, key, StringComparison.OrdinalIgnoreCase)) return entry["second"].AsString;
        return "";
    }
}

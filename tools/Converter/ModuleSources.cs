using KTable.Core.Model;

namespace KTable.Converter;

public static class ModuleSources
{
    public static void Fill(PieceDef def, ModuleObj b)
    {
        def.ModuleType = b.Name;
        def.ModuleTags = b.Tags.Count > 0 ? b.Tags : null;
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string? url)
        {
            if (url != null) links[key] = url;
        }
        Add("ImageURL", b.ImageUrl);
        Add("ImageSecondaryURL", b.BackUrl);
        Add("MeshURL", b.MeshUrl);
        Add("DiffuseURL", b.DiffuseUrl);
        Add("NormalURL", b.NormalUrl);
        Add("AssetbundleURL", b.BundleUrl);
        def.Sources = links.Count > 0 ? links : null;
        def.ModuleTop = b.Name is "Custom_Tile" or "Custom_Token" ? Math.Round(b.Thickness / Half, Digits) : null;
        def.ModuleObjectTag = ObjectTag(b);
    }

    const double Half = 2;
    const int Digits = 4;

    static readonly string[] MeshTags = ["Generic", "Figurine", "Dice", "Coin", "Board", "Chip", "Bag", "Infinite"];

    static string? ObjectTag(ModuleObj b) => b.Name switch
    {
        "Custom_Token" => b.StandUp ? "Figurine" : "Tile",
        "Custom_Model" or "Custom_Model_Bag" or "Custom_Model_Infinite_Bag" or "Custom_Assetbundle" => MeshTags[Math.Clamp(b.MeshType, 0, MeshTags.Length - 1)],
        _ => null,
    };
}

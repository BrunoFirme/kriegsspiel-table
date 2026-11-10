using System.Text.Json.Nodes;
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
        def.ModuleTop = b.Name switch
        {
            "Custom_Tile" => Math.Round(b.Thickness, Digits),
            "Custom_Token" => Math.Round(b.Thickness / Half, Digits),
            _ => null,
        };
        def.ModuleObjectTag = ObjectTag(b);
    }

    public static void States(PieceDef def, List<ModuleObj> states, Func<string, string, string> urls, string where)
    {
        for (int i = 0; i < def.States.Count && i < states.Count; i++) def.States[i].Module = StateData(states[i], urls, $"{where} state {i + 1} script state");
    }

    static string? Text(string s) => s.Length > 0 ? s : null;

    static ModuleStateData StateData(ModuleObj s, Func<string, string, string> urls, string where) => new()
    {
        Type = s.Name, Guid = s.Guid, Description = Text(s.Description), GmNotes = Text(s.GmNotes), Memo = Text(s.Memo), ScriptState = Text(urls(s.LuaState, where)),
        Tags = s.Tags.Count > 0 ? s.Tags : null,
    };

    const double Half = 2;
    const int Digits = 4;

    static readonly string[] MeshTags = ["Generic", "Figurine", "Dice", "Coin", "Board", "Chip", "Bag", "Infinite"];


    static string? ObjectTag(ModuleObj b) => b.Name switch
    {
        "Custom_Token" => b.StandsUp ? "Figurine" : "Tile",
        "Custom_Model" or "Custom_Model_Bag" or "Custom_Model_Infinite_Bag" or "Custom_Assetbundle" => MeshTags[Math.Clamp(b.MeshType, 0, MeshTags.Length - 1)],
        _ => null,
    };
}

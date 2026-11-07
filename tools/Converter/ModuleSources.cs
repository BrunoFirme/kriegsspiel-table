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
    }
}

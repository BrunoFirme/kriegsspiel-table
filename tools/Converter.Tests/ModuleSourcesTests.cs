using System.Text.Json.Nodes;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class ModuleSourcesTests
{
    static PieceDef Filled(string type, JsonObject extra)
    {
        var j = new JsonObject { ["Name"] = type, ["GUID"] = "abc123", ["Transform"] = new JsonObject { ["posX"] = 0, ["posY"] = 1, ["posZ"] = 0 } };
        foreach (var (k, v) in extra) j[k] = v?.DeepClone();
        var def = new PieceDef();
        ModuleSources.Fill(def, new ModuleObj(j));
        return def;
    }

    [Fact]
    public void A_kind_keeps_its_top_surface_and_the_kind_of_object_the_tabletop_called_it()
    {
        var standing = Filled("Custom_Token", new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/t.png", ["CustomToken"] = new JsonObject { ["Thickness"] = 0.3, ["StandUp"] = true } } });
        var lying = Filled("Custom_Tile", new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/t.png", ["CustomTile"] = new JsonObject { ["Thickness"] = 0.4 } } });
        var board = Filled("Custom_Model", new JsonObject { ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/m.obj", ["TypeIndex"] = 4 } });
        var odd = Filled("Custom_Model", new JsonObject { ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/m.obj", ["TypeIndex"] = 99 } });
        Assert.Equal((0.15, "Figurine"), (standing.ModuleTop, standing.ModuleObjectTag));
        Assert.Equal((0.2, (string?)null), (lying.ModuleTop, lying.ModuleObjectTag));
        Assert.Equal(((double?)null, "Board"), (board.ModuleTop, board.ModuleObjectTag));
        Assert.Equal("Infinite", odd.ModuleObjectTag);
    }
}

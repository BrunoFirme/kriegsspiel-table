using System.Text.Json.Nodes;

namespace KTable.Converter.Tests;

public sealed class SurfaceTests
{
    static ModuleObj Obj(string type, string lua = "", JsonObject? extra = null)
    {
        var j = new JsonObject { ["Name"] = type, ["GUID"] = "abc123", ["LuaScript"] = lua, ["Transform"] = new JsonObject { ["posX"] = 0, ["posY"] = 1, ["posZ"] = 0 } };
        if (extra != null) foreach (var (k, v) in extra) j[k] = v?.DeepClone();
        return new ModuleObj(j);
    }

    [Fact]
    public void The_ledger_ships_with_the_converter()
    {
        Assert.True(File.Exists(Surface.LedgerPath), Surface.LedgerPath);
        Assert.True(Surface.Loaded);
    }

    [Fact]
    public void What_a_module_uses_that_is_not_taken_exactly_is_named_with_what_to_do_instead()
    {
        var hand = Obj("HandTrigger");
        var token = Obj("Custom_Token", "function onLoad() self.setHiddenFrom({'Red'}) self.Scale({x=1.1,y=1,z=1}) end", new JsonObject { ["HideWhenFaceDown"] = true, ["Autoraise"] = true });
        var save = new ModuleSave { SaveName = "Test", Objects = new() { hand, token }, Lua = "function onLoad() Grid.type = 1 Lighting.apply() end" };
        var uses = Surface.NotExact(save);
        var names = uses.Select(u => (u.Row.Area, u.Row.Name)).ToList();
        Assert.Contains(("types", "HandTrigger"), names);
        Assert.Contains(("object", "setHiddenFrom"), names);
        Assert.Contains(("namespaces", "Lighting.apply"), names);
        Assert.Contains(("save", "ObjectState.HideWhenFaceDown"), names);
        Assert.DoesNotContain(("object", "scale"), names);
        Assert.DoesNotContain(("types", "Custom_Token"), names);
        Assert.DoesNotContain(("save", "ObjectState.Autoraise"), names);
        var handRow = uses.Single(u => u.Row.Name == "HandTrigger").Row;
        Assert.Equal("dropped", handRow.Status);
        Assert.Contains("seats", handRow.Instead);
    }

    [Fact]
    public void The_report_lists_the_rows_under_their_own_heading()
    {
        var rep = new Report();
        rep.NotExact(new Surface.Row("types", "HandTrigger", "dropped", "hands are seats here", "sit players at their seats"), 3);
        rep.NotExact(new Surface.Row("object", "roll", "planned", "Phase C item 3: standard dice", ""), 1);
        var path = Path.Combine(Path.GetTempPath(), "ktable-surface-report-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            rep.Write(path, "import test", announce: false);
            var text = File.ReadAllText(path);
            Assert.Contains("### Not taken from the module exactly (2)", text);
            Assert.Contains("- 3× `HandTrigger` (types): left out; sit players at their seats", text);
            Assert.Contains("- 1× `roll` (object): not here yet: standard dice", text);
        }
        finally { File.Delete(path); }
    }
}

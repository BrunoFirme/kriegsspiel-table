using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class FidelityTests
{
    public static readonly TheoryData<string> EveryPlainType = new() { "BlockSquare", "BlockRectangle", "BlockTriangle", "Custom_Tile", "Custom_Token", "Checker_white", "PlayerPawn" };

    sealed record Marks(string Name, string Note, string Memo, string Tag, string State, string Guid, string Colour, (double R, double G, double B) Rgb);

    static Marks MarksFor(string place, int i) => new($"{place} name", $"{place} note", $"{place} memo", $"{place}-tag", $"{place} state", $"{place[..3]}{i:D3}",
        place switch { "top" => "#336699", "look" => "#993366", "bag" => "#669933", _ => "#CC3300" },
        place switch { "top" => (0.2, 0.4, 0.6), "look" => (0.6, 0.2, 0.4), "bag" => (0.4, 0.6, 0.2), _ => (0.8, 0.2, 0.0) });

    static JsonObject Marked(string type, Marks m, double x)
    {
        var extra = type switch
        {
            "Custom_Tile" => new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/tile.png", ["CustomTile"] = new JsonObject { ["Thickness"] = 0.2 } } },
            "Custom_Token" => new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/token.png", ["CustomToken"] = new JsonObject { ["Thickness"] = 0.2 } } },
            _ => new JsonObject(),
        };
        var o = Obj(type, m.Name, 1, extra);
        o["GUID"] = m.Guid;
        o["Description"] = m.Note;
        o["Memo"] = m.Memo;
        o["Tags"] = new JsonArray(m.Tag);
        o["Locked"] = true;
        o["LuaScriptState"] = m.State;
        o["ColorDiffuse"] = new JsonObject { ["r"] = m.Rgb.R, ["g"] = m.Rgb.G, ["b"] = m.Rgb.B };
        (o["Transform"]!["posX"], o["Transform"]!["rotY"]) = (x, 0.0);
        return o;
    }

    static JsonObject Holder(string name, double x, JsonObject? extra = null)
    {
        var o = Obj("BlockSquare", name, 1, extra);
        (o["Transform"]!["posX"], o["Transform"]!["rotY"]) = (x, 0.0);
        return o;
    }

    [Theory]
    [MemberData(nameof(EveryPlainType))]
    public void Every_type_keeps_its_name_notes_memo_tags_lock_script_state_guid_and_colour_where_the_game_keeps_them_and_a_glued_part_keeps_only_its_colour(string type)
    {
        TempConfig();
        InstallBoxes(Packages);
        var (top, look, bag, glued) = (MarksFor("top", 1), MarksFor("look", 2), MarksFor("bag", 3), MarksFor("glued", 4));
        var unit = Holder("Unit", 4);
        unit["States"] = new JsonObject { ["2"] = Marked(type, look, 4) };
        var stand = Holder("Stand", 8, new JsonObject { ["ChildObjects"] = new JsonArray(Marked(type, glued, 0)) });
        var sack = Obj("Bag", "Sack", 1, new JsonObject { ["ContainedObjects"] = new JsonArray(Marked(type, bag, 0)) });
        sack["Transform"]!["posX"] = 12.0;
        var module = Module("place-" + type.ToLowerInvariant().Replace('_', '-'), Marked(type, top, 0), unit, stand, sack);
        var main = typeof(Config).Assembly.EntryPoint!;
        Assert.Equal(0, (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!);
        var name = Path.GetFileNameWithoutExtension(module);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", name, "scenario.json"));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts." + name, "pack.json"));
        PieceDef KindOf(string id) => pack.Pieces.Single(d => d.Id == id);

        void Kept(Marks m, string? label, string? note, string memo, IEnumerable<string>? tags, bool locked, string state, string? guid, string colour, string where)
        {
            Assert.True((m.Name, m.Note, m.Memo, m.State, m.Guid) == (label, note, memo, state, guid), $"{type} {where}: {(label, note, memo, state, guid)}");
            Assert.True(tags?.SequenceEqual([m.Tag]) == true, $"{type} {where}: tags {string.Join(",", tags ?? [])}");
            Assert.True(locked, $"{type} {where}: locked");
            Assert.True(colour.StartsWith(m.Colour, StringComparison.OrdinalIgnoreCase), $"{type} {where}: colour {colour}");
        }

        var loose = table.UmpireBoard.Single(p => p.Label == top.Name);
        Kept(top, loose.Label, loose.Note, loose.Memo, loose.Tags, loose.Locked, loose.ScriptState, loose.Guid, loose.TintOverride ?? KindOf(loose.Def).Tint, "on the table");

        var u = table.UmpireBoard.Single(p => p.Label == "Unit");
        var l = u.Looks![1];
        var lookKind = KindOf(u.Def).States[1];
        Kept(look, l.Label, l.Note, l.Memo, l.Tags, l.Locked, l.ScriptState, l.Guid, lookKind.Tint ?? KindOf(u.Def).Tint, "as a look");

        var held = Assert.Single(table.UmpireBoard.Single(p => p.Label == "Sack").Stowed!);
        Kept(bag, held.Label, held.Note, held.Memo, held.Tags, held.Locked, held.ScriptState, held.Guid, held.TintOverride ?? KindOf(held.Def).Tint, "in a bag");

        var s = table.UmpireBoard.Single(p => p.Label == "Stand");
        Assert.DoesNotContain(table.UmpireBoard, p => p.Label == glued.Name);
        var part = Assert.Single(KindOf(s.Def).Parts!);
        Assert.True((part.Tint ?? KindOf(part.Def).Tint).StartsWith(glued.Colour, StringComparison.OrdinalIgnoreCase), $"{type} glued: colour {part.Tint}");
        Assert.True((s.Note, s.Memo, s.Locked) == (null, "", false), $"{type} glued: the stand took the part's own fields");
    }
}

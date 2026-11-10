using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class AgentTableExtraTests
{
    static Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args) => AgentRecordTests.Ask(agent, tool, args);

    [NodeTest]
    static async Task The_agent_selects_pieces_on_the_person_s_screen_and_clears_them(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var (error, said) = await Ask(agent, "select_pieces", """{"ids":["p1","p2"]}""");
        Check.That(!error && h.Table.SelectedNodes.Count() == 2, "both are selected: " + said);
        await Ask(agent, "select_pieces", """{"none":true}""");
        Check.That(!h.Table.SelectedNodes.Any(), "nothing is selected");
        var (refused, _) = await Ask(agent, "select_pieces", """{"ids":["nobody"]}""");
        Check.That(refused, "a piece that is not there is refused");
    }

    [NodeTest]
    static async Task The_agent_adds_a_decal_stamps_it_on_the_table_and_a_piece_and_lifts_it_off(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var file = Path.Combine(Path.GetTempPath(), "ktable-nodetest-decal-" + System.Guid.NewGuid().ToString("N") + ".png");
        var img = Image.CreateEmpty(8, 4, false, Image.Format.Rgba8);
        img.Fill(Colors.Red);
        img.SavePng(file);
        try
        {
            var (addError, added) = await Ask(agent, "decals", $$"""{"action":"add","picture":"{{file.Replace('\\', '/')}}","name":"Blood","size":2}""");
            Check.That(!addError && h.S.View.DecalPalette.Any(d => d.Name == "Blood"), "the decal is added: " + added);
            var (stampError, stamped) = await Ask(agent, "decals", """{"action":"stamp","name":"blood","x":3,"z":4,"turn":45}""");
            var onTable = h.S.View.DecalLayer(Layers.Umpire).SingleOrDefault();
            Check.That(!stampError && onTable is { X: 3, Z: 4, Rot: 45 }, "it is stamped on the table where asked: " + stamped);
            Check.That(onTable != null && Mathf.IsEqualApprox((float)onTable.W, 4, 0.01f), $"wide as its picture: {onTable?.W}");
            await Ask(agent, "decals", """{"action":"stamp","name":"Blood","on":"p1"}""");
            Check.Equal(1, h.S.View.Find(Layers.Umpire, "p1")!.Decals.Count, "and on the piece");
            var (_, listed) = await Ask(agent, "decals", """{"action":"list"}""");
            Check.That(listed.Contains("on_pieces") && listed.Contains("p1"), "the list shows both: " + listed);
            await Ask(agent, "decals", """{"action":"lift","on":"p1","number":1}""");
            Check.Equal(0, h.S.View.Find(Layers.Umpire, "p1")!.Decals.Count, "the piece's stamp is lifted");
            await Ask(agent, "decals", """{"action":"lift","all":true}""");
            Check.Equal(0, h.S.View.DecalLayer(Layers.Umpire).Count, "every table stamp is lifted");
            await Ask(agent, "decals", """{"action":"remove","name":"Blood"}""");
            Check.Equal(0, h.S.View.DecalPalette.Count, "the decal is taken out");
            var (refused, _) = await Ask(agent, "decals", """{"action":"stamp","name":"Blood","x":1,"z":1}""");
            Check.That(refused, "a decal that is gone is not stamped");
        }
        finally { File.Delete(file); }
    }

    [NodeTest]
    static async Task The_agent_gives_the_invitation_and_sets_the_pointer_rule_but_never_touches_the_password(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        h.Net.Door(new KTable.Core.Protocol.SetPassword { Password = "fox" });
        await r.Frames(2);
        var (error, said) = await Ask(agent, "joining", """{"password":"wolf","pointers_always_shown":true}""");
        Check.That(!error && h.S.View.PointersAlwaysShown, "every umpire's pointer is shown: " + said);
        Check.Equal("fox", h.S.Access?.Password ?? "", "the password stays as the people set it");
        Check.That(!said.Contains("fox") && said.Contains("\"password\": \"set\"") && said.Contains("password: the host gives it"), "it says a password is set, never what it is: " + said);
    }

    [NodeTest]
    static async Task The_agent_hides_a_sidebar_button_the_compass_and_keeps_a_camera_view(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var cfg = h.Table.Settings;
        var (error, said) = await Ask(agent, "set_view", """{"compass":false,"sidebar":{"Decals":false}}""");
        Check.That(!error && !cfg.CompassShown && cfg.StripHidden.Contains("decals"), "the compass and the button are hidden: " + said);
        await Ask(agent, "set_view", """{"compass":true,"sidebar":{"decals":true}}""");
        Check.That(cfg.CompassShown && !cfg.StripHidden.Contains("decals"), "and shown again");
        var (fixedRefused, fixedSaid) = await Ask(agent, "set_view", """{"sidebar":{"Map":false}}""");
        Check.That(fixedRefused && fixedSaid.Contains("always stays"), "Map always stays: " + fixedSaid);
        h.S.View.ScenarioId = "agent-view-test";
        var (saveError, saved) = await Ask(agent, "set_view", """{"save_view":3}""");
        Check.That(!saveError && h.Table.Cam.Views.ContainsKey(3), "view 3 is kept: " + saved.Replace('\n', ' '));
        var (goError, _) = await Ask(agent, "set_view", """{"go_to_view":3}""");
        Check.That(!goError, "and gone to");
        await Ask(agent, "set_view", """{"forget_view":3}""");
        Check.That(!h.Table.Cam.Views.ContainsKey(3), "and forgotten");
        var (missing, _) = await Ask(agent, "set_view", """{"go_to_view":4}""");
        Check.That(missing, "a view never saved is refused");
    }

    [NodeTest]
    static async Task The_agent_turns_a_sheet_with_its_pieces_and_cannot_join_while_a_game_is_open(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        h.S.Do(new KTable.Core.Protocol.AddSheet { Sheet = new SheetDef { Id = "s9", Size = new Size2(20, 10) } });
        await r.Frames(2);
        var (error, said) = await Ask(agent, "set_map_sheet", """{"id":"s9","turn_with_pieces":90}""");
        Check.That(!error && Mathf.IsEqualApprox((float)h.S.View.Sheets.First(s => s.Id == "s9").RotDeg, 90), "the sheet is turned: " + said);
        var (refused, _) = await Ask(agent, "set_map_sheet", """{"id":"s9","turn_with_pieces":45}""");
        Check.That(refused, "only quarter and half turns");
        var (joinRefused, joinSaid) = await Ask(agent, "join_game", """{"address":"192.168.0.9:7777"}""");
        Check.That(joinRefused && joinSaid.Contains("leave_game"), "joining waits for leave_game: " + joinSaid);
    }

    static void DicePack(KTable.Core.PackageStore store)
    {
        TiltTests.Prism(store);
        var pack = new PackManifest { Id = "tilt", Name = "Tilt" };
        pack.Assets["prism"] = new AssetEntry { File = "assets/prism.obj", Kind = "mesh" };
        pack.Pieces.Add(store.Def("d2")!);
        File.WriteAllText(Path.Combine(store.PackageDir("tilt"), "pack.json"), KTable.Core.Json.Serialize(pack));
    }

    [ScreenTest]
    static async Task The_agent_throws_a_die_piece_and_reads_the_side_it_rests_on(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r, packs: new[] { "tilt" }, prepare: DicePack);
        var (placeError, placed) = await Ask(agent, "place_piece", """{"kind":"d2","x":4,"z":0}""");
        Check.That(!placeError, "the die is placed: " + placed);
        var die = h.S.View.Layer(Layers.Umpire).Last();
        await r.Frames(10);
        var (error, thrown) = await Ask(agent, "throw_dice", $$"""{"ids":["{{die.Id}}"]}""");
        Check.That(!error && thrown.Contains("\"side\""), "the die rests on a side: " + thrown);
        var (refused, _) = await Ask(agent, "throw_dice", """{"ids":["p1"]}""");
        Check.That(refused, "a block is not thrown");
    }
}

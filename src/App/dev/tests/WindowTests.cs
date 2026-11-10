using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class WindowTests
{
    const string U = Layers.Umpire;

    internal static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    static string AllText(Node n) =>
        string.Join("\n", Descendants(n).Select(d => d switch { Button b => b.Text, Label l => l.Text, RichTextLabel rt => rt.GetParsedText(), LineEdit le => le.PlaceholderText, _ => "" }));

    static AcceptDialog? Dialog(HudTests.Rig h, Func<string, bool> title) =>
        Descendants(h.Hud).OfType<AcceptDialog>().FirstOrDefault(d => d.Visible && title(d.Title));

    [NodeTest]
    static async Task The_scenario_dialog_shows_the_game_and_its_pointer_rule_reaches_the_table(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        ScenarioDialog.Open(h.Hud.Ctx);
        await r.Frames(2);
        var dlg = Dialog(h, t => t.StartsWith("Scenario"));
        Check.That(dlg != null, "the scenario dialog is up");
        var text = AllText(dlg!);
        Check.That(text.Contains("Map sheets") && text.Contains("Briefing"), "its pages are built: " + text[..Math.Min(text.Length, 200)]);
        var rule = Descendants(dlg!).OfType<CheckBox>().First(c => c.Text.StartsWith("Umpires cannot hide"));
        rule.ButtonPressed = true;
        await r.Frames(2);
        Check.That(h.S.View.PointersAlwaysShown, "the pointer rule reached the game");
    }

    [NodeTest]
    static async Task The_properties_dialog_edits_a_piece_s_label_and_note(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var node = h.Table.Pieces.NodeOf(U, "p1")!;
        PropertiesDialog.Open(h.Hud.Ctx, node);
        await r.Frames(2);
        var dlg = Dialog(h, t => t == "Block");
        Check.That(dlg != null, "the properties dialog is up, titled by the kind");
        var label = Descendants(dlg!).OfType<LineEdit>().First();
        label.Text = "1st Foot";
        label.EmitSignal(LineEdit.SignalName.TextSubmitted, "1st Foot");
        await r.Frames(2);
        Check.Equal("1st Foot", h.S.View.Find(U, "p1")!.Label, "the label reached the game");
        var note = Descendants(dlg!).OfType<TextEdit>().First();
        note.Text = "Veterans of the last war";
        dlg!.Hide();
        await r.Frames(2);
        Check.Equal("Veterans of the last war", h.S.View.Find(U, "p1")!.Note, "the note is sent when the dialog closes");
    }

    [NodeTest]
    static async Task The_help_opens_a_guide_at_a_heading_and_finds_words_across_the_guides(TestRunner r)
    {
        var h = await HudTests.Host(r);
        Check.That(GuideReader.Show(h.Hud.Ctx.Root, "umpire-guide.md", "saves"), "the help opens");
        await r.Frames(3);
        var win = h.Hud.Ctx.Root.GetChildren().OfType<Window>().FirstOrDefault(w => w.Title == "Help");
        Check.That(win != null, "a help window is under the hud");
        var shown = AllText(win!);
        Check.That(shown.Contains("Saves", StringComparison.Ordinal), "the umpire guide is on the page");
        var toc = Descendants(win!).OfType<Tree>().First();
        Check.That(toc.GetRoot()?.GetChildCount() > 3, "the page's headings are listed");
        GuideReader.Show(h.Hud.Ctx.Root, "scripting.md");
        await r.Frames(3);
        Check.That(AllText(win!).Contains("on_load", StringComparison.Ordinal), "another guide replaces it in the same window");
        GuideReader.Search("password");
        await r.Frames(3);
        Check.That(AllText(win!).Contains("password", StringComparison.OrdinalIgnoreCase), "the search shows where the word is");
        win!.QueueFree();
        await r.Frames(2);
    }

    [NodeTest]
    static async Task The_right_click_card_offers_what_a_piece_or_the_table_can_do(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Hud.Menus.Show(new Vector2(300, 300), h.Table.Pieces.NodeOf(U, "p1"));
        await r.Frames(2);
        var card = Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible);
        Check.That(card != null, "the piece card is up");
        var rows = AllText(card!);
        Check.That(rows.Contains("Properties") && rows.Contains("Turn around 180°"), "piece rows: " + rows.Replace("\n", " | "));
        card!.Hide();
        h.Table.Selection.Set(Array.Empty<string>());
        h.Hud.Menus.Show(new Vector2(300, 300), null);
        await r.Frames(2);
        var table = Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible);
        Check.That(table != null && !AllText(table).Contains("Turn around 180°"), "the table card has no piece rows");
    }

    [NodeTest]
    static async Task The_saves_window_lists_the_save_on_the_table_and_saves_a_copy_under_a_name(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var store = SaveStore.Default;
        var scenarioDir = "window-test-" + Guid.NewGuid().ToString("N")[..8];
        var campaign = store.MakeFolder(scenarioDir, store.SavesRoot(scenarioDir), "Campaign");
        var live = store.NewSavePath(scenarioDir, "Game 1", campaign);
        KTable.Core.Json.SaveBig(live, h.Net.Server!.ToSave());
        h.S.SavePath = live;
        h.Net.Saves.BeginHosting(live, 0);
        Check.That(h.Net.Saves.SaveInPlay != null, "the table is one of a scenario's saves");
        SavesWindow.Open(h.Hud.Ctx);
        await r.Frames(3);
        var win = h.Hud.Ctx.Root.GetChildren().OfType<Window>().FirstOrDefault(w => w.Title == "Saves");
        Check.That(win != null, "the saves window is up");
        var tree = Descendants(win!).OfType<Tree>().First();
        var names = new List<string>();
        for (var item = tree.GetRoot(); item != null; item = item.GetNextVisible()) names.Add(item.GetText(0));
        Check.That(names.Contains("Saves") && names.Contains("Campaign") && names.Any(n => n.StartsWith("Trash")), "the folders listed: " + string.Join(", ", names));
        var labels = Descendants(win!).OfType<Label>().Select(l => l.Text).ToList();
        Check.That(labels.Contains("Game 1 · on the table"), "the save on the table is marked: " + string.Join(", ", labels));
        var name = Descendants(win!).OfType<LineEdit>().First(e => e.PlaceholderText.StartsWith("Save the table"));
        name.Text = "Before the assault";
        name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text);
        await r.Seconds(1.5);
        Check.That(Directory.GetFiles(campaign, "*" + SaveStore.Ext).Any(f => Path.GetFileName(f).StartsWith("Before the assault")), "the copy is beside the save on the table: " + string.Join(", ", Directory.GetFiles(campaign)));
        win!.QueueFree();
        await r.Frames(2);
    }

    [NodeTest]
    static async Task Another_umpire_s_pointer_is_a_hand_on_the_table_that_goes_when_they_leave(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        t.Table.Pointers.ShowRemote(7, "Other umpire", new Vec2(3, 3));
        await r.Frames(3);
        Check.That(t.Table.Pointers.Peers().Contains(7), "the peer's pointer is kept");
        Check.That(Descendants(t.Table).OfType<PointerHand>().Any(), "a hand stands on the table");
        t.Table.Pointers.ShowRemote(7, "Other umpire", new Vec2(6, 1));
        await r.Seconds(0.4);
        t.Table.Pointers.Forget(7);
        await r.Frames(2);
        Check.That(!t.Table.Pointers.Peers().Any(), "forgotten when they leave");
    }

    [NodeTest]
    static async Task A_die_from_the_standard_pack_rolls_and_comes_to_rest_on_a_face(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { "std.dice" }, prepare: StandardPacks.Ensure);
        await r.Frame();
        t.S.Do(new KTable.Core.Protocol.SpawnPiece { Layer = U, Def = "std.dice.d6", Id = "d1", Pos = new Vec2(0, 0) });
        await r.Frames(3);
        var die = t.Node("d1");
        Check.That(die.IsDie, "the piece is a die");
        t.Table.Selection.Set(new[] { "d1" });
        Check.That(t.Table.RollDiceAtHand(), "the die rolls");
        await r.Frames(2);
        Check.That(die.Rolling, "it is in the air");
        await r.Seconds(5);
        Check.That(!die.Rolling, "it has come to rest");
        int face = t.S.View.Find(U, "d1")!.ShownFace;
        Check.That(face >= 0 && face < 6, $"it shows one of its six faces ({face})");
        Check.Equal(face, die.FaceUp(die.ModelWorldBasis), "the side drawn on top is the side it shows");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_die_copied_into_a_pack_and_made_bigger_rolls_as_a_bigger_die(TestRunner r)
    {
        string mine = "";
        void Prepare(KTable.Core.PackageStore store)
        {
            StandardPacks.Ensure(store);
            mine = PackEdit.CreatePack(store, "My dice", "Tester");
            var pack = store.Pack(mine)!;
            var copy = PackEdit.CopyInto(store, store.Pack(StandardPacks.Dice)!, store.Def(StandardPacks.Dice + ".d6")!, pack);
            copy.Size = new Size2(1.2, 1.2);
            copy.Thickness = 1.2;
            PackEdit.Save(store, pack);
        }
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { StandardPacks.Dice }, prepare: Prepare, arrange: g => g.Packs.Add(mine));
        await r.Frame();
        var copyId = t.S.Store.Pack(mine)!.Pieces[0].Id;
        t.S.Do(new KTable.Core.Protocol.SpawnPiece { Layer = U, Def = StandardPacks.Dice + ".d6", Id = "small", Pos = new Vec2(-3, 0) });
        t.S.Do(new KTable.Core.Protocol.SpawnPiece { Layer = U, Def = copyId, Id = "big", Pos = new Vec2(3, 0) });
        await r.Frames(3);
        var small = t.Node("small");
        var big = t.Node("big");
        Check.That(big.IsDie, "the copy is a die");
        Check.That(Mathf.IsEqualApprox(big.Footprint.X, small.Footprint.X * 2, 0.05f), $"twice as wide as the standard d6: {big.Footprint.X} against {small.Footprint.X}");
        t.Table.Selection.Set(new[] { "big" });
        Check.That(t.Table.RollDiceAtHand(), "it rolls");
        await r.Frames(2);
        Check.That(big.Rolling, "it is in the air");
        for (int i = 0; i < 20 && big.Rolling; i++) await r.Seconds(0.5);
        int face = t.S.View.Find(U, "big")!.ShownFace;
        Check.That(!big.Rolling && face is >= 0 and < 6, $"it comes to rest on one of its six faces ({face})");
        Check.Equal(face, big.FaceUp(big.ModelWorldBasis), "the side drawn on top is the side it shows");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_die_turned_to_a_side_comes_to_rest_with_that_side_on_top(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { "std.dice" }, prepare: StandardPacks.Ensure);
        await r.Frame();
        t.S.Do(new KTable.Core.Protocol.SpawnPiece { Layer = U, Def = "std.dice.d20", Id = "d1", Pos = new Vec2(0, 0) });
        await r.Frames(3);
        var die = t.Node("d1");
        Check.That(die.IsDie, "the d20 is a die");
        t.S.Do(new KTable.Core.Protocol.TurnDie { Layer = U, Id = "d1", Face = 13 });
        for (int i = 0; i < 16 && die.FaceUp(die.ModelWorldBasis) != 13; i++) await r.Seconds(0.5);
        Check.Equal(13, t.S.View.Find(U, "d1")!.ShownFace, "it shows side 14");
        Check.Equal(13, die.FaceUp(die.ModelWorldBasis), "with side 14 drawn on top");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task An_effect_piece_builds_its_emitters_when_placed(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        await r.Frame();
        var effects = t.S.Store.Pack(FirmeEffects.PackId)!.Pieces.Where(d => d.Tags.Contains("effect")).ToList();
        Check.That(effects.Count > 10, $"the standard effects are installed ({effects.Count})");
        var smoke = effects.First(d => d.Name.Contains("smoke", StringComparison.OrdinalIgnoreCase));
        t.S.Do(new KTable.Core.Protocol.SpawnPiece { Layer = U, Def = smoke.Id, Id = "fx1", Pos = new Vec2(2, 2) });
        await r.Seconds(0.5);
        var fx = t.Node("fx1");
        Check.That(fx.IsEffect && fx.CarriesEffect, "the piece is an effect with emitters");
        int running = fx.Emitters;
        t.S.Do(new KTable.Core.Protocol.SetPieceOption { Layer = U, Id = "fx1", Name = KTable.Core.Protocol.PieceOptions.EffectOff, Value = true });
        await r.Frames(3);
        Check.That(running > 0 && t.Node("fx1").Emitters == 0, $"stopped, it sends nothing out ({running} before)");
        t.S.Do(new KTable.Core.Protocol.SetPieceOption { Layer = U, Id = "fx1", Name = KTable.Core.Protocol.PieceOptions.EffectOff, Value = false });
        await r.Frames(3);
        Check.Equal(running, t.Node("fx1").Emitters, "started again, it runs as before");
        t.S.Do(new KTable.Core.Protocol.DeletePiece { Layer = U, Id = "fx1" });
        await r.Frames(2);
        Check.That(t.Table.Pieces.NodeOf(U, "fx1") == null, "it goes with the piece");
    }

    [NodeTest]
    static async Task The_transform_ring_resizes_the_selection_as_its_knob_is_dragged(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1", "p2" });
        t.Table.SetTool(Tool.Transform);
        await r.Frames(2);
        var g = t.Table.Gizmo;
        var nodes = t.Table.Selection.Nodes.ToList();
        g.Measure(nodes, null);
        var cam = t.Table.Rig.Cam;
        var center = nodes.Aggregate(Vector3.Zero, (a, n) => a + (n.Position with { Y = 0 })) / nodes.Count;
        var press = cam.UnprojectPosition(g.ScaleKnobWorld());
        Check.Equal(Gizmo.Handle.Scale, g.At(press), "the scale knob is where it is drawn");
        var grip = t.Table.Rig.GroundAt(press)!.Value;
        g.Begin(Gizmo.Handle.Scale, press);
        g.Drag(cam.UnprojectPosition(center + (grip - center) * 2), snap: false);
        Check.That(g.Dragging, "the knob is held");
        g.End();
        t.Table.Poses.Flush();
        await r.Frames(2);
        var scale = t.S.View.Find(U, "p1")!.Scale;
        Check.That(Math.Abs(scale - 2) < 0.15, $"the pieces are twice their size ({scale:0.00})");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceMenuTests
{
    const string U = Layers.Umpire;
    static readonly Vector2 At = new(300, 300);

    static PopupPanel Card(HudTests.Rig h) =>
        WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible) ?? throw new TestFailure("no right-click card is up");

    static Button Row(PopupPanel card, string text) =>
        WindowTests.Descendants(card).OfType<Button>().FirstOrDefault(b => WindowTests.Descendants(b).OfType<Label>().Any(l => l.Text == text))
        ?? throw new TestFailure($"no row reads {text}");

    static readonly string[] Everyday = { "Centre on it", "Rotate 180°", "Flip", "Lock", "Options", "Cut", "Copy", "Delete", "Properties…" };
    static readonly string[] OnTheOptionsPage = { "Flying", "Collision", "Holds pieces", "Carry stack", "Box-select", "Move distance", "Shadow", "Favourite" };

    static float Tall(PopupPanel card) => WindowTests.Descendants(card).OfType<ScrollContainer>().First().GetChild<Control>(0).GetCombinedMinimumSize().Y;

    [NodeTest]
    static async Task Centre_on_it_glides_the_view_onto_the_piece_and_keeps_the_zoom(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(40, 25) });
        await r.Frames(3);
        float zoom = h.Table.Rig.Distance;
        var node = h.Table.Pieces.NodeOf(U, "p2") ?? throw new TestFailure("the piece is on the table");
        h.Table.Selection.Set(new[] { "p2" });
        h.Hud.Menus.Show(At, node);
        await r.Frames(2);
        Row(Card(h), "Centre on it").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Seconds(2);
        var aim = Coords.ToWorld(new Vec2(40, 25));
        Check.That(new Vector2(h.Table.Rig.Target.X - aim.X, h.Table.Rig.Target.Z - aim.Z).Length() < 0.01f, $"the view is on the piece: {h.Table.Rig.Target} for {aim}");
        Check.That(Mathf.IsEqualApprox(h.Table.Rig.Distance, zoom, 0.01f), $"at the zoom it had: {zoom} then {h.Table.Rig.Distance}");
    }

    [NodeTest]
    static async Task Lock_on_the_card_pins_the_piece_and_the_card_then_offers_Unlock(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        var node = h.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table");
        h.Hud.Menus.Show(At, node);
        await r.Frames(2);
        Row(Card(h), "Lock").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.S.View.Find(U, "p1")!.Locked, "Lock pins the piece");
        h.Hud.Menus.Show(At, h.Table.Pieces.NodeOf(U, "p1"));
        await r.Frames(2);
        var card = Card(h);
        Check.That(Row(card, "Unlock").IsVisibleInTree() && !WindowTests.Descendants(card).OfType<Label>().Any(l => l.Text == "Lock"), "a locked piece's card offers Unlock in its place");
        Row(card, "Unlock").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(!h.S.View.Find(U, "p1")!.Locked, "Unlock frees it");
    }

    [NodeTest]
    static async Task The_piece_card_keeps_the_everyday_rows_and_turns_to_an_options_page_for_the_rest(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        var node = h.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table");
        h.Hud.Menus.Show(At, node);
        await r.Frames(2);
        var card = Card(h);
        var size = Fmt.Footprint(node.Footprint.X, node.Footprint.Y, h.S.View);
        Check.That(size.Contains(" × ", StringComparison.Ordinal) && Reads(card, size), "beside its name the card says how big the piece is: " + size);
        foreach (var row in Everyday) Check.That(Row(card, row).IsVisibleInTree(), $"{row} is on the card");
        foreach (var tick in OnTheOptionsPage) Check.That(!Row(card, tick).IsVisibleInTree(), $"{tick} waits on the Options page");
        Check.That(WindowTests.Descendants(card).OfType<LineEdit>().Count(e => e.IsVisibleInTree()) == 1 && WindowTests.Descendants(card).OfType<TextEdit>().Count(e => e.IsVisibleInTree()) == 1,
            "the label and description sit on the card");
        float main = Tall(card);
        Row(card, "Options").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(card.Visible && !Row(card, "Rotate 180°").IsVisibleInTree(), "Options turns the same card to its page");
        foreach (var tick in OnTheOptionsPage)
            Check.That(Row(card, tick).IsVisibleInTree() && Row(card, tick).TooltipText.Length > 0, $"{tick} is on the page and says what it does on hover");
        var flying = Row(card, "Flying");
        var collision = Row(card, "Collision");
        Check.That(flying.GetParent() is GridContainer && collision.GetParent() == flying.GetParent(), "the ticks share one grid");
        Check.That(Mathf.IsEqualApprox(flying.Position.Y, collision.Position.Y) && collision.Position.X > flying.Position.X, $"two to a row: {flying.Position} and {collision.Position}");
        Row(card, "‹  Options").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(Row(card, "Rotate 180°").IsVisibleInTree() && !flying.IsVisibleInTree(), "the back row turns it back");
        Check.That(Mathf.IsEqualApprox(Tall(card), main), $"to the card as it was: {main} then {Tall(card)}");
        card.Hide();
    }

    [NodeTest]
    static async Task The_label_and_description_typed_on_the_card_land_on_the_piece(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        var node = h.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table");
        h.Hud.Menus.Show(At, node);
        await r.Frames(2);
        var card = Card(h);
        var box = WindowTests.Descendants(card).OfType<LineEdit>().Single();
        box.Text = "3rd Battalion";
        box.EmitSignal(LineEdit.SignalName.TextSubmitted, box.Text);
        await r.Frames(2);
        Check.Equal("3rd Battalion", h.S.View.Find(U, "p1")!.Label, "Enter in the label box names the piece");
        Check.That(!GodotObject.IsInstanceValid(card) || !card.Visible, "and closes the card");
        h.Hud.Menus.Show(At, h.Table.Pieces.NodeOf(U, "p1"));
        await r.Frames(2);
        card = Card(h);
        Check.Equal("3rd Battalion", WindowTests.Descendants(card).OfType<LineEdit>().Single().Text, "the card shows the label it has");
        WindowTests.Descendants(card).OfType<TextEdit>().Single().Text = "Held in reserve";
        card.Hide();
        await r.Frames(2);
        Check.Equal("Held in reserve", h.S.View.Find(U, "p1")!.Note, "closing the card keeps the description typed");
    }

    static bool Reads(PopupPanel card, string text) => WindowTests.Descendants(card).OfType<Label>().Any(l => l.Text == text);

    static async Task<PopupPanel> OptionsOf(TestRunner r, HudTests.Rig h, PieceNode node)
    {
        h.Hud.Menus.Show(At, node);
        await r.Frames(2);
        var card = Card(h);
        Row(card, "Options").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        return card;
    }

    [NodeTest]
    static async Task The_piece_card_offers_snapping_only_over_a_grid_and_a_tick_for_the_shadow(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        var node = h.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table");
        h.S.Do(new SetGrid { Grid = new GridDef { Kind = "none" } });
        await r.Frames(2);
        var card = await OptionsOf(r, h, node);
        Check.That(!Reads(card, "Snap to grid"), "with no grid there is no snapping to offer");
        Row(card, "Shadow").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.S.View.Find(U, "p1")!.NoShadow, "unticked, the piece casts no shadow");
        if (GodotObject.IsInstanceValid(card)) card.Hide();
        h.S.Do(new SetGrid { Grid = new GridDef { Kind = "square", Pitch = 10 } });
        await r.Frames(2);
        card = await OptionsOf(r, h, node);
        Check.That(Row(card, "Snap to grid").IsVisibleInTree(), "over a grid each piece has its own snapping");
        Row(card, "Snap to grid").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(true, h.S.View.Find(U, "p1")!.Snaps, "on a grid that does not snap, the tick makes this piece snap");
        if (GodotObject.IsInstanceValid(card)) card.Hide();
    }

    static async Task<PopupPanel> CardOf(TestRunner r, HudTests.Rig h, PieceNode node)
    {
        h.Hud.Menus.Show(At, node);
        await r.Frames(2);
        return Card(h);
    }

    static void Close(PopupPanel card)
    {
        if (GodotObject.IsInstanceValid(card)) card.Hide();
    }

    static bool Shows(PopupPanel card, string text) => WindowTests.Descendants(card).OfType<Label>().Any(l => l.Text == text && l.IsVisibleInTree());

    [NodeTest]
    static async Task A_unit_s_card_offers_taking_control_and_directing_only_once_someone_is_at_the_table_in_person(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        var node = h.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table");
        var card = await CardOf(r, h, node);
        Check.That(!Shows(card, "Take control"), "on a plain table a unit's card has no Take control to misclick");
        Close(card);
        card = await OptionsOf(r, h, node);
        Check.That(Shows(card, "Take control"), "it waits on the Options page");
        Check.That(!Reads(card, Ui.Spaced("Directed by")), "with no directing there");
        Close(card);
        h.S.OnPeers(new PeerList { Peers = [.. h.S.Peers, new PeerInfo { PeerId = 5, Name = "Ann", Role = Role.Player }] });
        await r.Frames(2);
        card = await CardOf(r, h, node);
        Check.That(Shows(card, "Take control") && Shows(card, "Assign to Ann"), "with a player at the table the unit can be taken or given from the card");
        Close(card);
        card = await OptionsOf(r, h, node);
        Check.That(!Shows(card, "Take control"), "and the Options page no longer repeats it");
        Check.That(Reads(card, Ui.Spaced("Directed by")), "and directed");
        Close(card);
    }

    static void LifeSized(PackageStore store) =>
        store.RegisterDef(new PackManifest { Id = "test", Name = "Test" }, new PieceDef { Id = "figure", Name = "Figure", Tags = new() { PieceDef.LifeSizeTag } });

    [NodeTest]
    static async Task A_life_size_figure_s_card_offers_taking_control_on_any_table(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, prepare: LifeSized);
        h.S.Do(new SpawnPiece { Layer = U, Def = "figure", Id = "f1", Pos = new Vec2(0, 0) });
        await r.Frames(3);
        h.Table.Selection.Set(new[] { "f1" });
        var card = await CardOf(r, h, h.Table.Pieces.NodeOf(U, "f1") ?? throw new TestFailure("the figure is on the table"));
        Check.That(Reads(card, "Take control"), "a life-size figure can be walked with no player at the table");
        Close(card);
    }

    const int Least = 0, Most = 7, Start = 6;

    static void Counted(PackageStore store) =>
        store.RegisterDef(new PackManifest { Id = "test", Name = "Test" },
            new PieceDef { Id = "counted", Name = "Counted", Counters = new() { new CounterDef { Name = "Strength", Min = Least, Max = Most, Default = Start } } });

    [NodeTest]
    static async Task A_counter_steps_up_and_down_on_one_row_while_the_card_stays_up(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, prepare: Counted);
        h.S.Do(new SpawnPiece { Layer = U, Def = "counted", Id = "c1", Pos = new Vec2(0, 0) });
        await r.Frames(3);
        h.Table.Selection.Set(new[] { "c1" });
        h.Hud.Menus.Show(At, h.Table.Pieces.NodeOf(U, "c1") ?? throw new TestFailure("the counted piece is on the table"));
        await r.Frames(2);
        var card = Card(h);
        Button Step(string tip) => WindowTests.Descendants(card).OfType<Button>().FirstOrDefault(b => b.TooltipText == tip) ?? throw new TestFailure($"no {tip} button");
        int Value() => h.S.View.Find(U, "c1")!.Counters.GetValueOrDefault("Strength", Start);
        Check.That(Reads(card, "Strength") && Reads(card, Start.ToString()), "one row names the counter and its value");
        Step("Strength +1").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(Most, Value(), "+ raises it");
        Check.That(card.Visible && Step("Strength +1").Disabled && Reads(card, Most.ToString()), "the card stays up, shows the new value and + stops at the top");
        Step("Strength −1").EmitSignal(BaseButton.SignalName.Pressed);
        Step("Strength −1").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(Start - 1, Value(), "− lowers it, a step a press");
        card.Hide();
    }

    [NodeTest]
    static async Task A_flying_piece_s_altitude_can_be_typed_beside_its_slider(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.S.Do(new SetPieceOption { Layer = U, Id = "p1", Name = PieceOptions.Flies, Value = true });
        await r.Frames(2);
        h.Table.Selection.Set(new[] { "p1" });
        var card = await OptionsOf(r, h, h.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table"));
        var box = WindowTests.Descendants(card).OfType<LineEdit>().FirstOrDefault(b => b.Editable && b.IsVisibleInTree()) ?? throw new TestFailure("the altitude has no box to type in");
        int steps = h.S.MyUndoable.Count;
        box.Text = "12.5";
        box.EmitSignal(Control.SignalName.FocusExited);
        await r.Frames(2);
        Check.Equal(12.5, h.S.View.Find(U, "p1")!.Altitude, "the typed altitude is the piece's");
        Check.Equal(steps + 1, h.S.MyUndoable.Count, "and it is one step to undo");
        if (GodotObject.IsInstanceValid(card)) card.Hide();
    }
}

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

    static readonly string[] Everyday = { "Rotate 180°", "Flip", "Locked", "Options", "Cut", "Copy", "Delete", "Properties…" };
    static readonly string[] OnTheOptionsPage = { "Flying", "Collision", "Container", "Carry stack", "Box-select", "Move distance", "Shadow", "Favourite" };

    static float Tall(PopupPanel card) => WindowTests.Descendants(card).OfType<ScrollContainer>().First().GetChild<Control>(0).GetCombinedMinimumSize().Y;

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
        Check.That(!WindowTests.Descendants(card).OfType<LineEdit>().Any(e => e.IsVisibleInTree()), "the label and description live in Properties");
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

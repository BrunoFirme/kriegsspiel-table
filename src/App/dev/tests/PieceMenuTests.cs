using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

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

    [NodeTest]
    static async Task The_piece_card_says_how_big_the_piece_is_and_keeps_the_seldom_changed_ticks_folded_away(TestRunner r)
    {
        var h = await HudTests.Host(r);
        bool was = h.Hud.Cfg.PieceMenuUnfolded;
        h.Hud.Cfg.PieceMenuUnfolded = false;
        h.Table.Selection.Set(new[] { "p1" });
        var node = h.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table");
        h.Hud.Menus.Show(At, node);
        await r.Frames(2);
        var card = Card(h);
        var size = Fmt.Footprint(node.Footprint.X, node.Footprint.Y, h.S.View);
        Check.That(size.Contains(" × ", StringComparison.Ordinal) && WindowTests.Descendants(card).OfType<Label>().Any(l => l.Text == size), "under its name the card says how big the piece is: " + size);
        Check.That(Row(card, "Locked").IsVisibleInTree(), "Locked is one click away");
        Check.That(!Row(card, "Collides with other pieces").IsVisibleInTree() && !Row(card, "Favourite").IsVisibleInTree(), "the ticks seldom changed are folded away");
        float Rows() => WindowTests.Descendants(card).OfType<ScrollContainer>().First().GetChild<Control>(0).GetCombinedMinimumSize().Y;
        float folded = Rows();
        Row(card, "More options").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(Row(card, "Collides with other pieces").IsVisibleInTree() && card.Visible, "More options unfolds them in the card, which stays up");
        Check.That(Rows() > folded, $"and its rows take more room: {folded} → {Rows()}");
        Check.That(h.Hud.Cfg.PieceMenuUnfolded, "it is kept unfolded for the next card");
        card.Hide();
        h.Hud.Cfg.PieceMenuUnfolded = was;
    }

    [NodeTest]
    static async Task The_table_card_takes_an_exact_zoom(TestRunner r)
    {
        const float Typed = 12.5f;
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(Array.Empty<string>());
        h.Hud.Menus.Show(At, null);
        await r.Frames(2);
        var card = Card(h);
        var zoom = WindowTests.Descendants(card).OfType<SpinBox>().Single();
        Check.That(Mathf.Abs((float)zoom.Value - h.Table.Rig.ZoomGoal) < 0.06f, $"the field shows how far the eye is: {zoom.Value} for {h.Table.Rig.ZoomGoal}");
        zoom.Value = Typed;
        Check.Equal(Typed, h.Table.Rig.ZoomGoal, "a typed distance is where the view goes");
        Check.That(card.Visible, "and the card stays up to type another");
        card.Hide();
    }
}

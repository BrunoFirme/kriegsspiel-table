using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class EffectLabelTests
{
    const string U = Layers.Umpire;
    const string Smoke = "smoke", Given = "Burning mill";
    const double PastTheHoverWait = 0.5;

    [NodeTest]
    static async Task An_effect_takes_a_label_on_its_right_click_and_tells_it_under_the_pointer(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        h.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf("Signal smoke"), Id = Smoke, Pos = new Vec2(2, 2) });
        await r.Seconds(0.3);
        var node = h.Table.Pieces.NodeOf(U, Smoke) ?? throw new TestFailure("the smoke is on the table");
        Check.That(node.IsEffect, "the smoke is an effect");

        h.Table.Selection.Set(new[] { Smoke });
        h.Hud.Menus.Show(new Vector2(300, 300), node);
        await r.Frames(2);
        var card = WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().First(p => p.Visible);
        var box = WindowTests.Descendants(card).OfType<LineEdit>().FirstOrDefault(e => e.PlaceholderText.StartsWith("Label")) ?? throw new TestFailure("the effect's card has a label box");
        Check.That(WindowTests.Descendants(card).OfType<TextEdit>().Any(e => e.PlaceholderText.StartsWith("Description")), "and a description box");
        box.Text = Given;
        box.EmitSignal(LineEdit.SignalName.TextSubmitted, Given);
        await r.Frames(2);
        Check.Equal(Given, h.S.View.Find(U, Smoke)!.Label, "the label reached the game");
        Check.Equal(Given, node.Piece.Label, "and the piece on the table");
        Check.That(!GodotObject.IsInstanceValid(card) || !card.Visible, "Enter closes the card");
        Check.Equal(Given, node.HoverText(), "the effect tells its label");

        string? told = null;
        void Hear(string? text, Vector2 at) => told = text ?? told;
        h.Table.HoverChanged += Hear;
        var over = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(node.Piece.Pos));
        h.Table.Hover.Update(over, 0);
        Check.That(h.Table.Hover.Node == node, "the pointer finds the effect");
        h.Table.Hover.Update(over, PastTheHoverWait);
        h.Table.HoverChanged -= Hear;
        Check.Equal(Given, told, "its label shows under the pointer");

        h.S.UndoLast();
        await r.Frames(2);
        Check.Equal(null, h.S.View.Find(U, Smoke)!.Label, "undo takes the label off");
    }
}

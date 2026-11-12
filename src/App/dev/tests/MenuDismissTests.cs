using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class MenuDismissTests
{
    const string U = Layers.Umpire;
    static readonly Vector2 Opened = new(300, 300), Beside = new(90, 60);

    static PopupPanel? Up(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible);

    static async Task Press(TestRunner r, MouseButton button, Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
        await r.Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
        await r.Frames(3);
    }

    [ScreenTest]
    static async Task A_press_outside_the_right_click_card_closes_it_whichever_button_it_is(TestRunner r)
    {
        var h = await HudTests.Host(r);
        foreach (var button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
        {
            h.Table.Selection.Set(new[] { "p1" });
            h.Hud.Menus.Show(Opened, h.Table.Pieces.NodeOf(U, "p1"));
            await r.Frames(3);
            var card = Up(h);
            Check.That(card != null, "the piece card is up");
            var outside = new Vector2(card!.Position.X + card.Size.X, card.Position.Y) + Beside;
            Check.That(h.Hud.GetViewport().GetVisibleRect().HasPoint(outside), $"the press lands in the window: {outside}");
            await Press(r, button, outside);
            Check.That(!GodotObject.IsInstanceValid(card) || !card.Visible, $"a {button} press outside the card closes it");
            foreach (var later in WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().Where(p => p.Visible).ToList()) later.Hide();
            await r.Frames(2);
        }
    }

    [ScreenTest]
    static async Task A_press_on_the_right_click_card_leaves_it_open(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Hud.Menus.Show(Opened, h.Table.Pieces.NodeOf(U, "p1"));
        await r.Frames(3);
        var card = Up(h);
        Check.That(card != null, "the piece card is up");
        var head = (Vector2)card!.Position + new Vector2(card.Size.X / 2, 14);
        await Press(r, MouseButton.Right, head);
        Check.That(GodotObject.IsInstanceValid(card) && card.Visible, "a press on its heading does not close it");
        card.Hide();
    }
}

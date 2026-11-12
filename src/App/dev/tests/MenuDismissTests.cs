using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class MenuDismissTests
{
    const string U = Layers.Umpire;
    const int Tries = 3;
    static readonly Vector2 Opened = new(300, 300), Beside = new(90, 60);

    static PopupPanel? Up(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible);

    static async Task Press(TestRunner r, MouseButton button, Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
        await r.Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
        await r.Frames(3);
    }

    static async Task WithTheWindowInFront(TestRunner r, Func<Task> body)
    {
        for (int attempt = 1; ; attempt++)
        {
            r.GetWindow().GrabFocus();
            await r.Frames(2);
            try
            {
                await body();
                return;
            }
            catch (TestFailure) when (attempt < Tries && !r.GetWindow().HasFocus())
            {
                await r.Frames(2);
            }
        }
    }

    static async Task<PopupPanel> Card(TestRunner r, HudTests.Rig h)
    {
        foreach (var left in WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().Where(p => p.Visible).ToList()) left.Hide();
        h.Table.Selection.Set(new[] { "p1" });
        h.Hud.Menus.Show(Opened, h.Table.Pieces.NodeOf(U, "p1"));
        await r.Frames(3);
        return Up(h) ?? throw new TestFailure("the piece card is up");
    }

    [ScreenTest]
    static async Task A_press_outside_the_right_click_card_closes_it_whichever_button_it_is(TestRunner r)
    {
        var h = await HudTests.Host(r);
        foreach (var button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
            await WithTheWindowInFront(r, async () =>
            {
                var card = await Card(r, h);
                var outside = new Vector2(card.Position.X + card.Size.X, card.Position.Y) + Beside;
                Check.That(h.Hud.GetViewport().GetVisibleRect().HasPoint(outside), $"the press lands in the window: {outside}");
                Check.That(card.Visible, "the card stays up until something is pressed");
                await Press(r, button, outside);
                Check.That(!GodotObject.IsInstanceValid(card) || !card.Visible, $"a {button} press outside the card closes it");
            });
    }

    [ScreenTest]
    static async Task A_press_on_the_right_click_card_leaves_it_open(TestRunner r)
    {
        var h = await HudTests.Host(r);
        await WithTheWindowInFront(r, async () =>
        {
            var card = await Card(r, h);
            var head = (Vector2)card.Position + new Vector2(card.Size.X / 2, 14);
            await Press(r, MouseButton.Right, head);
            Check.That(GodotObject.IsInstanceValid(card) && card.Visible, "a press on its heading does not close it");
            card.Hide();
        });
    }
}

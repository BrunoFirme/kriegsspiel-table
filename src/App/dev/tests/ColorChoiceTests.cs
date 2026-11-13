using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ColorChoiceTests
{
    static readonly string[] Names = ColorChoices.All.Select(c => c.Name).ToArray();

    static List<string> Offered(Node under) =>
        WindowTests.Descendants(under).OfType<Button>().Where(b => !b.IsQueuedForDeletion() && Names.Contains(b.TooltipText.Split('\n')[0])).Select(b => b.TooltipText.Split('\n')[0]).ToList();

    [NodeTest]
    static async Task The_pen_and_a_pieces_tint_offer_the_same_colours_in_the_same_order(TestRunner r)
    {
        var h = await HudTests.Host(r);
        IHudShell shell = h.Hud;
        shell.TogglePanel("paint");
        await r.Frames(3);
        Check.Equal(string.Join(", ", Names), string.Join(", ", Offered(shell.Panels["paint"].Body)), "the Draw panel's inks");
        shell.TogglePanel("paint");
        h.Table.Selection.Set(new[] { "p1" });
        var node = h.Table.Pieces.NodeOf(Layers.Umpire, "p1") ?? throw new TestFailure("the piece is on the table");
        h.Hud.Menus.Show(new Vector2(300, 300), node);
        await r.Frames(2);
        var card = WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().First(p => p.Visible);
        Check.Equal(string.Join(", ", Names), string.Join(", ", Offered(card)), "the tints on a piece's right-click");
        var red = WindowTests.Descendants(card).OfType<Button>().First(b => b.TooltipText == "Red");
        red.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var tinted = h.S.View.Find(Layers.Umpire, "p1")!.TintOverride;
        Check.Equal(ColorChoices.All.First(c => c.Name == "Red").Hex, tinted, "a tint picked there is that very colour");
        Check.That(ColorChoices.All.Select(c => c.Hex).Distinct(StringComparer.OrdinalIgnoreCase).Count() == ColorChoices.All.Length, "and no colour is offered twice");
    }
}

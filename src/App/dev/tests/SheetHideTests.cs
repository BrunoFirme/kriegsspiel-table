using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class SheetHideTests
{
    static readonly Vec2 Middle = new(0, 0);

    static Button Eye(HudTests.Rig h, string sheet) =>
        h.Hud.Drawer.Pages["map"].page!.FindChildren("*", "HBoxContainer", true, false).OfType<HBoxContainer>()
            .Where(row => row.GetChildren().OfType<Button>().Any(b => b.Text == sheet))
            .Select(row => row.GetChildren().OfType<Button>().First())
            .Single(b => !b.IsQueuedForDeletion());

    [NodeTest]
    static async Task The_eye_beside_a_sheet_hides_it_on_this_screen_only_and_the_sheet_under_it_is_the_one_picked(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 2, pictured: true);
        h.Hud.Drawer.ShowPage("map");
        h.Hud.Map.Rebuild();
        await r.Frames(2);
        var top = h.Table.Sheets.TopAt(Middle)!.Def.Id;
        var under = h.S.View.Sheets.Single(s => s.Id != top).Id;
        h.Table.SelectSheet(top);
        h.Hud.Map.Rebuild();
        await r.Frames(2);
        int undos = h.S.MyUndoable.Count;
        Check.That(Eye(h, top).TooltipText.StartsWith("Hide this sheet", System.StringComparison.Ordinal), "a shown sheet's eye offers to hide it");
        Eye(h, top).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.Table.Sheets.TryGet(top, out var node) && !node.Visible, "the sheet is no longer drawn");
        Check.Equal(under, h.Table.Sheets.TopAt(Middle)?.Def.Id, "the sheet under it is the one a click finds");
        Check.That(h.Table.SelectedSheet == null, "and the hidden sheet is let go");
        Check.That(h.S.View.Sheets.Count == 2 && h.S.MyUndoable.Count == undos, "the table itself is unchanged: nothing was sent");
        Check.That(h.Table.HiddenSheets.Contains(top) && h.Table.Settings.HiddenSheets.Values.Any(set => set.Contains(top)), "it is kept on this PC for this table");
        Check.Equal("Show it again", Eye(h, top).TooltipText, "its eye now offers to show it");
        Eye(h, top).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(node.Visible && h.Table.Sheets.TopAt(Middle)?.Def.Id == top, "shown again, it is drawn and picked as before");
        Check.That(h.Table.Settings.HiddenSheets.Count == 0, "and nothing is left kept");
    }
}

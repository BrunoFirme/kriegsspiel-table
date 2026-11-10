using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class SheetOpacityTests
{
    [NodeTest]
    static async Task The_opacity_slider_fades_a_sheet_on_the_table_in_one_undoable_step(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1, pictured: true);
        var id = h.S.View.Sheets[0].Id;
        h.Table.SelectSheet(id);
        h.Hud.Drawer.ShowPage("map");
        h.Hud.Map.Rebuild();
        await r.Frames(2);
        var slider = h.Hud.Drawer.Pages["map"].page!.FindChildren("*", "HSlider", true, false).OfType<HSlider>().Single(s => s.TooltipText.Contains("through this one"));
        Check.That(h.Table.Sheets.TryGet(id, out var node) && !node.SeenThrough, "a sheet starts solid");
        int undos = h.S.MyUndoable.Count;
        slider.EmitSignal(Slider.SignalName.DragStarted);
        slider.Value = 70;
        slider.Value = 40;
        await r.Frames(2);
        Check.That(Mathf.IsEqualApprox(node.Opacity, 0.4f) && node.SeenThrough, $"the sheet fades while the slider moves, not {node.Opacity}");
        Check.Equal(1.0, h.S.View.Sheets[0].Seen, "opacity sent before the drag ends");
        slider.EmitSignal(Slider.SignalName.DragEnded, true);
        await r.Frames(2);
        Check.Equal(0.4, h.S.View.Sheets[0].Seen, "opacity kept with the scenario");
        Check.Equal(undos + 1, h.S.MyUndoable.Count, "things to undo after one drag");
        h.S.UndoLast();
        await r.Frames(2);
        Check.That(h.Table.Sheets.TryGet(id, out node) && !node.SeenThrough && Mathf.IsEqualApprox(node.Opacity, 1f), "undo makes the sheet solid again");
    }
}

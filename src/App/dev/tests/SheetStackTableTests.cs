using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class SheetStackTableTests
{
    static readonly Vec2 Middle = new(0, 0);
    const float Close = 0.03f;

    static SheetNode Node(HudTests.Rig h, string id) => h.Table.Sheets.TryGet(id, out var n) ? n : throw new TestFailure($"no sheet {id} on the table");

    static float Raised(SheetNode n) => n.GetChildren().OfType<ReliefSurface>().Single().Position.Y;

    static List<string> Rows(HudTests.Rig h) =>
        h.Hud.Drawer.Pages["map"].page!.FindChildren("*", "HBoxContainer", true, false).OfType<HBoxContainer>().Where(row => !row.IsQueuedForDeletion())
            .Select(row => row.GetChildren().OfType<Button>().Where(b => b.ToggleMode).Select(b => b.Text).FirstOrDefault()).Where(name => name != null).Select(name => name!).ToList();

    static ToolIcon Eye(HudTests.Rig h, string sheet) =>
        h.Hud.Drawer.Pages["map"].page!.FindChildren("*", "HBoxContainer", true, false).OfType<HBoxContainer>()
            .Where(row => !row.IsQueuedForDeletion() && row.GetChildren().OfType<Button>().Any(b => b.Text == sheet))
            .Select(row => row.GetChildren().OfType<Button>().First().GetChildren().OfType<ToolIcon>().Single()).Single();

    static async Task<HudTests.Rig> Overlapping(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, sheets: 3, pictured: true);
        h.S.Do(new MoveSheet { Id = "s0", Pos = Middle, RotDeg = 0, Z = 0, Size = new Size2(20, 20) });
        h.S.Do(new MoveSheet { Id = "s1", Pos = Middle, RotDeg = 0, Z = 0, Size = new Size2(60, 60) });
        h.S.Do(new MoveSheet { Id = "s2", Pos = new Vec2(200, 0), RotDeg = 0, Z = 7, Size = new Size2(20, 20) });
        h.Hud.Drawer.ShowPage("map");
        h.Hud.Map.Rebuild();
        h.Table.Rig.Show(new CameraRig.View(Vector3.Zero, 30, 0, h.Table.Rig.Pitch, true));
        await r.Seconds(0.5);
        return h;
    }

    static async Task<Color> Seen(TestRunner r)
    {
        await r.Seconds(0.4);
        var shot = r.GetViewport().GetTexture().GetImage();
        return shot.GetPixel(shot.GetWidth() / 2, shot.GetHeight() / 2);
    }

    static bool Same(Color a, Color b) => Math.Abs(a.R - b.R) < Close && Math.Abs(a.G - b.G) < Close && Math.Abs(a.B - b.B) < Close;

    [NodeTest]
    static async Task The_map_page_lists_sheets_from_the_top_down_and_a_restack_gives_each_a_place_of_its_own(TestRunner r)
    {
        var h = await Overlapping(r);
        Check.Equal("s2 s0 s1", string.Join(" ", Rows(h)), "top first, and of two with one order the earlier lies on top");
        Check.Equal("s0", h.Table.Sheets.TopAt(Middle)?.Def.Id, "the upper of the two is the one a click finds");
        Check.That(Raised(Node(h, "s0")) > Raised(Node(h, "s1")) && Raised(Node(h, "s1")) == 0 && Raised(Node(h, "s2")) == 0,
            $"only the sheet lying over another is raised: {Raised(Node(h, "s0"))} {Raised(Node(h, "s1"))} {Raised(Node(h, "s2"))}");
        int undos = h.S.MyUndoable.Count;
        h.Table.Sheets.Restack("s1", 0);
        await r.Frames(3);
        Check.Equal("s1 s2 s0", string.Join(" ", Rows(h)), "brought to the front, it heads the list");
        Check.Equal(3, h.S.View.Sheets.Select(s => s.Z).Distinct().Count(), "and no two sheets share an order");
        Check.Equal("s1", h.Table.Sheets.TopAt(Middle)?.Def.Id, "it is now the one on top");
        Check.That(Raised(Node(h, "s1")) > Raised(Node(h, "s0")), "and it is drawn over the other");
        Check.Equal(undos + 1, h.S.MyUndoable.Count, "one step to undo");
        h.S.UndoLast();
        await r.Frames(3);
        Check.Equal("s2 s0 s1", string.Join(" ", Rows(h)), "undone, the stack is as it was");
    }

    static Button RowOf(HudTests.Rig h, string sheet) =>
        h.Hud.Drawer.Pages["map"].page!.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => !b.IsQueuedForDeletion() && b.ToggleMode && b.Text == sheet);

    [NodeTest]
    static async Task A_row_dragged_onto_another_takes_its_place_in_the_stack(TestRunner r)
    {
        var h = await Overlapping(r);
        var carried = Variant.From("sheet-row:s1");
        RowOf(h, "s1").ForceDrag(carried, new Control());
        await r.Frame();
        Check.That(!h.Hud.Map.Offered(RowOf(h, "s1"), "s1", carried), "a row is not dropped on itself");
        Check.That(!h.Hud.Map.Offered(RowOf(h, "s2"), "s2", Variant.From("something else")), "nor is anything that is no sheet's row");
        Check.That(h.Hud.Map.Offered(RowOf(h, "s2"), "s2", carried) && RowOf(h, "s2").HasThemeStyleboxOverride("normal"), "the row it is over is marked as where it will go");
        await r.Frames(2);
        Check.That(RowOf(h, "s2").HasThemeStyleboxOverride("normal"), "and stays marked while the drag lasts");
        h.Hud.Map.Dropped(0, carried);
        await r.Frames(4);
        Check.Equal("s1 s2 s0", string.Join(" ", Rows(h)), "let go on the top row, the dragged sheet is on top");
        Check.That(!RowOf(h, "s2").HasThemeStyleboxOverride("normal"), "and no row stays marked");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await r.Frames(2);
    }

    [ScreenTest]
    static async Task A_solid_sheet_hides_the_one_under_it_and_faded_it_lets_that_one_show(TestRunner r)
    {
        var h = await Overlapping(r);
        h.Table.Sheets.Restack("s1", 0);
        await r.Frames(3);
        var solid = await Seen(r);
        Check.That(Node(h, "s1").Hides, "at 100% the upper sheet hides what is under it");
        h.S.Do(new SetSheetOpacity { Id = "s1", Opacity = 0.5 });
        var faded = await Seen(r);
        Check.That(!Node(h, "s1").Hides && Same(solid, faded), $"faded to half over a sheet of its own colour, the lower one shows through and the table does not: {solid} then {faded}");
        h.S.Do(new SetSheetOpacity { Id = "s0", Opacity = 0.05 });
        h.S.Do(new SetSheetOpacity { Id = "s1", Opacity = 1 });
        var over = await Seen(r);
        Check.That(Same(solid, over), $"a solid sheet over one at 5% looks as it does alone: {solid} then {over}");
    }

    [NodeTest]
    static async Task A_sheet_at_no_opacity_is_hidden_on_the_table_and_shown_so_in_the_list(TestRunner r)
    {
        var h = await Overlapping(r);
        Check.Equal("eye", Eye(h, "s0").Kind, "a sheet in sight has an open eye");
        h.S.Do(new SetSheetOpacity { Id = "s0", Opacity = 0 });
        await r.Frames(3);
        Check.That(!Node(h, "s0").Drawn && Node(h, "s0").Visible, "at 0% it is not drawn, though nothing hid it on this screen");
        Check.Equal("s1", h.Table.Sheets.TopAt(Middle)?.Def.Id, "a click finds the sheet under it");
        Check.Equal("eye-off", Eye(h, "s0").Kind, "its row shows a closed eye");
        ((Button)Eye(h, "s0").GetParent()).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(h.S.View.Sheets.Single(s => s.Id == "s0").Seen == 1 && Node(h, "s0").Drawn, "the eye brings it back at 100%");
    }
}

using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class GroundLayerPaintTests
{
    const double Side = 40, MapScale = 0.02, RiverMetres = 4;
    const int WaitFrames = 300;
    const string Rivers = "g-rivers";

    static async Task<HudTests.Rig> Open(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0);
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, h.S.Store.Root);
        h.S.Relief.Remember(sha, flat);
        h.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        h.S.Do(new SetCalibration { UnitsPerMeter = MapScale });
        await r.Frames(8);
        h.Table.SetTool(Tool.Relief);
        return h;
    }

    static async Task Landed(TestRunner r, HudTests.Rig h, string? before)
    {
        for (int i = 0; i < WaitFrames && h.S.View.Sheets[0].Relief == before; i++) await r.Frame();
        await r.Frames(2);
    }

    static string? Relief(HudTests.Rig h) => h.S.View.Sheets[0].Relief;

    static async Task River(TestRunner r, HudTests.Rig h)
    {
        var cfg = h.Table.Settings;
        (cfg.ReliefMode, cfg.ReliefPaint, cfg.ReliefBand, cfg.ReliefDensity) = ("paint:water", ReliefTool.Line, (float)RiverMetres, 1);
        var before = Relief(h);
        h.Table.Relief.Press(new Vec2(-15, 0), false, false);
        h.Table.Relief.Motion(new Vec2(15, 0));
        h.Table.Relief.Release();
        await Landed(r, h, before);
    }

    [NodeTest]
    static async Task A_line_lays_a_band_of_its_width_on_cells_made_fine_enough_to_hold_it(TestRunner r)
    {
        var h = await Open(r);
        int coarse = h.S.Relief.Under(h.S.View.Sheets[0])!.Cols;
        await River(r, h);
        var grid = h.S.Relief.Under(h.S.View.Sheets[0])!;
        double metre = MapScale;
        Check.That(grid.Cols > coarse, $"the sheet's cells grew finer for a narrow band: {coarse} to {grid.Cols} across");
        Check.Equal(GroundKinds.Water, h.S.Ground.KindAt(new Vec2(0, 0)), "water down the middle");
        Check.Equal(GroundKinds.Water, h.S.Ground.KindAt(new Vec2(0, RiverMetres * 0.35 * metre)), "water within the half width");
        Check.Equal(GroundKinds.None, h.S.Ground.KindAt(new Vec2(0, RiverMetres * 1.0 * metre)), "nothing a width away");
    }

    [NodeTest]
    static async Task A_river_on_a_locked_layer_above_stays_whole_under_any_painting_below_and_its_layer_refuses_paint(TestRunner r)
    {
        var h = await Open(r);
        h.S.Do(new AddGroundLayer { Id = Rivers, Name = "Rivers", Index = 0 });
        await r.Frames(2);
        h.S.Relief.Layer = Rivers;
        await River(r, h);
        h.S.Do(new LockGroundLayer { Id = Rivers, Locked = true });
        await r.Frames(2);
        var cfg = h.Table.Settings;
        (cfg.ReliefMode, cfg.ReliefPaint) = ("paint:grass", "brush");
        var before = Relief(h);
        h.Table.Relief.Press(new Vec2(0, 0), false, false);
        h.Table.Relief.Release();
        await r.Frames(4);
        Check.Equal(before, Relief(h), "the locked layer takes no paint");
        h.S.Relief.Layer = GroundLayerDef.BaseId;
        (cfg.ReliefPaint, cfg.ReliefBrush) = ("rect", 1);
        h.Table.Relief.Press(new Vec2(-19, -19), false, false);
        h.Table.Relief.Motion(new Vec2(19, 19));
        h.Table.Relief.Release();
        await Landed(r, h, before);
        Check.Equal(GroundKinds.Water, h.S.Ground.KindAt(new Vec2(0, 0)), "the river shows over the grass painted below it");
        Check.Equal(GroundKinds.Grass, h.S.Ground.KindAt(new Vec2(0, 10)), "grass beside it");
        var grid = h.S.Relief.Under(h.S.View.Sheets[0])!;
        Check.That(grid.PaintedOn(Rivers) && grid.PaintedOn(GroundLayerDef.BaseId), "each layer holds its own ground");
        h.S.Relief.Show(Rivers, false);
        await r.Frames(2);
        Check.Equal(GroundKinds.Grass, h.S.Ground.KindAt(new Vec2(0, 0)), "with the rivers hidden here the grass under them shows");
        Check.Equal(GroundKinds.Water, grid.KindAt(0.5, 0.5), "the table's ground keeps its river");
        h.S.Relief.Show(Rivers, true);
    }

    [NodeTest]
    static async Task Removing_a_layer_takes_its_ground_off_every_sheet_and_one_undo_brings_both_back(TestRunner r)
    {
        var h = await Open(r);
        h.S.Do(new AddGroundLayer { Id = Rivers, Name = "Rivers", Index = 0 });
        await r.Frames(2);
        h.S.Relief.Layer = Rivers;
        await River(r, h);
        var panel = h.Hud.Relief!;
        panel.Rebuild();
        var before = Relief(h);
        new GroundLayerOps(h.S, h.Net, _ => { }).Remove(Rivers);
        await Landed(r, h, before);
        Check.That(h.S.View.GroundLayers.All(l => l.Id != Rivers), "the layer is gone");
        Check.Equal(GroundKinds.None, h.S.Ground.KindAt(new Vec2(0, 0)), "its river went with it");
        var gone = Relief(h);
        h.S.UndoLast();
        await Landed(r, h, gone);
        Check.That(h.S.View.GroundLayers.Any(l => l.Id == Rivers), "undo brings the layer back");
        Check.Equal(GroundKinds.Water, h.S.Ground.KindAt(new Vec2(0, 0)), "and its river");
    }

    [NodeTest]
    static async Task The_Paint_page_holds_Draw_s_tools_the_ground_types_and_a_layer_list_with_a_lock_per_row(TestRunner r)
    {
        var h = await Open(r);
        var cfg = h.Table.Settings;
        (cfg.ReliefMode, cfg.ReliefPaint) = ("paint:wood", "brush");
        h.S.Do(new AddGroundLayer { Id = Rivers, Name = "Rivers", Index = 0 });
        await r.Frames(2);
        var panel = h.Hud.Relief!;
        panel.Rebuild();
        await r.Frame();
        var body = panel.Panel.Body;
        var tools = body.FindChildren("*", nameof(GridContainer), true, false).OfType<GridContainer>().First(g => g.Columns == 8);
        Check.Equal(8, tools.GetChildCount(), "brush, line, curve, area, rectangle, ellipse, fill and rub out");
        var labels = body.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).ToList();
        Check.That(labels.Contains(Ui.Spaced("Ground")) && labels.Contains(Ui.Spaced("Layers")), "Ground and Layers heads: " + string.Join(", ", labels));
        var buttons = body.FindChildren("*", nameof(Button), true, false).OfType<Button>().ToList();
        Check.That(GroundKinds.Forests.All(f => buttons.Any(b => b.Text == f.Name)), "wood offers its forests");
        Check.That(buttons.Any(b => b.Text == "Rivers") && buttons.Any(b => b.Text == GroundLayerDef.BaseName), "a row for each ground layer");
        var padlocks = buttons.Where(b => b.GetChildren().OfType<ToolIcon>().Any(i => i.Kind is "lock" or "lock-open")).ToList();
        Check.Equal(2, padlocks.Count, "a lock on each row");
        padlocks[0].EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(h.S.View.GroundLayers[0].Locked, "its lock locks the layer for the table");
    }
}

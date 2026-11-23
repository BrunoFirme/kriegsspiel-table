using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class GroundLayerTests
{
    const int Side = 33, Middle = 16;
    const string Rivers = "g-rivers", Woods = "g-woods";
    static readonly byte Grass = GroundKinds.Grass, Water = GroundKinds.Water, Wood = GroundKinds.Wood;
    static readonly IReadOnlyList<string> RiversOnTop = [Rivers, GroundLayerDef.BaseId];

    static Brush Paint(byte kind) => new(BrushMode.Paint, 0, 1, kind, Salt: 7);

    static double Uv(int cell) => cell / (double)(Side - 1);

    static int Cell(int x, int y) => y * Side + x;

    static ReliefGrid River()
    {
        var g = new ReliefGrid(Side, Side);
        g.Aim(Rivers, RiversOnTop);
        ReliefBrush.Stroke(g, [(Uv(0), Uv(Middle)), (Uv(Side - 1), Uv(Middle))], 1.2, Paint(Water));
        g.Settle(RiversOnTop, Rivers);
        return g;
    }

    [Fact]
    public void Grass_brushed_over_a_river_on_a_lower_layer_leaves_the_river_as_it_was()
    {
        var g = River();
        var river = Enumerable.Range(0, g.Kind.Length).Where(i => g.Kind[i] == Water).ToList();
        Assert.NotEmpty(river);
        g.Aim(GroundLayerDef.BaseId, RiversOnTop);
        ReliefBrush.Dab(g, Uv(Middle), Uv(Middle), Side, Paint(Grass));
        g.Settle(RiversOnTop, GroundLayerDef.BaseId);
        Assert.All(river, i => Assert.Equal(Water, g.Kind[i]));
        Assert.Equal(Grass, g.Kind[Cell(Middle, 2)]);
        Assert.Equal(Grass, g.Cells(GroundLayerDef.BaseId).Kind[river[0]]);
    }

    [Fact]
    public void Rubbing_out_a_layer_shows_the_ground_painted_under_it_again()
    {
        var g = River();
        g.Aim(GroundLayerDef.BaseId, RiversOnTop);
        ReliefBrush.Dab(g, Uv(Middle), Uv(Middle), Side, Paint(Grass));
        g.Aim(Rivers, RiversOnTop);
        ReliefBrush.Dab(g, Uv(Middle), Uv(Middle), Side, Paint(GroundKinds.None));
        g.Settle(RiversOnTop, Rivers);
        Assert.Equal(Grass, g.Kind[Cell(Middle, Middle)]);
        Assert.Null(g.Layers);
    }

    [Fact]
    public void Ground_painted_on_one_layer_alone_needs_no_layers_in_the_grid()
    {
        var g = new ReliefGrid(Side, Side);
        g.Aim(GroundLayerDef.BaseId, [GroundLayerDef.BaseId, Rivers]);
        Assert.Null(g.Painting);
        ReliefBrush.Dab(g, Uv(Middle), Uv(Middle), 3, Paint(Wood));
        g.Settle([GroundLayerDef.BaseId, Rivers], GroundLayerDef.BaseId);
        Assert.Null(g.Layers);
        Assert.Equal(Wood, g.Kind[Cell(Middle, Middle)]);
    }

    [Fact]
    public void Moving_a_layer_down_lets_the_ground_above_it_show_through()
    {
        var g = River();
        g.Aim(GroundLayerDef.BaseId, RiversOnTop);
        ReliefBrush.Dab(g, Uv(Middle), Uv(Middle), Side, Paint(Wood));
        g.Settle(RiversOnTop, GroundLayerDef.BaseId);
        Assert.Equal(Water, g.Kind[Cell(Middle, Middle)]);
        g.Settle([GroundLayerDef.BaseId, Rivers], GroundLayerDef.BaseId);
        Assert.Equal(Wood, g.Kind[Cell(Middle, Middle)]);
    }

    [Fact]
    public void A_layer_taken_off_the_table_leaves_the_grid_and_its_ground_goes_with_it()
    {
        var g = River();
        g.Settle([GroundLayerDef.BaseId], GroundLayerDef.BaseId);
        Assert.Null(g.Layers);
        Assert.DoesNotContain(Water, g.Kind);
    }

    [Fact]
    public void A_change_by_another_tool_lands_on_the_layer_that_shows_there_and_bare_ground_on_the_chosen_one()
    {
        var g = River();
        int onRiver = Cell(Middle, Middle), aside = Cell(Middle, 2);
        g.Kind[onRiver] = GroundKinds.Town;
        g.Kind[aside] = Wood;
        g.Settle(RiversOnTop, GroundLayerDef.BaseId);
        Assert.Equal(GroundKinds.Town, g.Cells(Rivers).Kind[onRiver]);
        Assert.Equal(Wood, g.Cells(GroundLayerDef.BaseId).Kind[aside]);
        Assert.Equal((GroundKinds.Town, Wood), (g.Kind[onRiver], g.Kind[aside]));
        g.Kind[onRiver] = GroundKinds.None;
        g.Settle(RiversOnTop, GroundLayerDef.BaseId);
        Assert.Equal(GroundKinds.None, g.Kind[onRiver]);
    }

    [Fact]
    public void Layers_are_saved_and_read_back_with_the_ground_they_show()
    {
        var g = River();
        g.Aim(GroundLayerDef.BaseId, RiversOnTop);
        ReliefBrush.Dab(g, Uv(Middle), Uv(Middle), Side, Paint(Grass));
        g.Settle(RiversOnTop, GroundLayerDef.BaseId);
        var dir = Directory.CreateTempSubdirectory("ktable-layers-").FullName;
        try
        {
            var (sha, _) = ReliefFiles.Save(dir, g);
            var index = ReliefFiles.LoadIndex(Path.Combine(dir, ReliefFiles.HeaderRel(sha)));
            Assert.Equal(RiversOnTop, index.Layers!.Select(l => l.Id));
            Assert.All(ReliefFiles.Files(sha, index), rel => Assert.True(File.Exists(Path.Combine(dir, rel))));
            var back = ReliefFiles.Load(index, rel => Path.Combine(dir, rel));
            Assert.Equal(g.Kind, back.Kind);
            Assert.Equal(g.Cells(Rivers).Kind, back.Cells(Rivers).Kind);
            Assert.Equal(g.Cells(GroundLayerDef.BaseId).Kind, back.Cells(GroundLayerDef.BaseId).Kind);
            Assert.Equal(RiversOnTop, back.Order);
            var plain = new ReliefGrid(Side, Side);
            plain.Kind[0] = Grass;
            var (bare, _) = ReliefFiles.Save(dir, plain);
            var plainIndex = ReliefFiles.LoadIndex(Path.Combine(dir, ReliefFiles.HeaderRel(bare)));
            Assert.Null(plainIndex.Layers);
            Assert.Null(ReliefFiles.Load(plainIndex, rel => Path.Combine(dir, rel)).Layers);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_finer_grid_keeps_each_layer_s_ground()
    {
        var g = River();
        var fine = g.Finer();
        Assert.Equal(g.Order, fine.Order);
        Assert.Equal(Water, fine.Cells(Rivers).Kind[Middle * 2 * fine.Cols + Middle * 2]);
        Assert.Equal(Water, fine.Kind[Middle * 2 * fine.Cols + Middle * 2]);
    }

    [Fact]
    public void Ground_layers_are_added_renamed_locked_moved_and_removed_and_each_comes_back_on_undo()
    {
        var g = new GameState();
        g.EnsureBoard();
        Assert.Equal([GroundLayerDef.BaseId], g.GroundOrder);
        Assert.Null(StateMachine.Apply(g, new AddGroundLayer { Id = Rivers, Name = "Rivers", Index = 0 }, out var unAdd));
        Assert.Null(StateMachine.Apply(g, new AddGroundLayer { Id = Woods, Name = "Woods", Index = 2 }, out _));
        Assert.Equal([Rivers, GroundLayerDef.BaseId, Woods], g.GroundOrder);
        Assert.Null(StateMachine.Apply(g, new LockGroundLayer { Id = Rivers, Locked = true }, out var unLock));
        Assert.True(g.GroundLayers[0].Locked);
        Assert.Null(StateMachine.Apply(g, unLock!, out _));
        Assert.False(g.GroundLayers[0].Locked);
        Assert.Null(StateMachine.Apply(g, new RenameGroundLayer { Id = Rivers, Name = "  " }, out _));
        Assert.Equal("Ground 1", g.GroundLayers[0].Name);
        Assert.Null(StateMachine.Apply(g, new MoveGroundLayer { Id = Woods, Index = 0 }, out var unMove));
        Assert.Equal([Woods, Rivers, GroundLayerDef.BaseId], g.GroundOrder);
        Assert.Null(StateMachine.Apply(g, unMove!, out _));
        Assert.Null(StateMachine.Apply(g, new RemoveGroundLayer { Id = GroundLayerDef.BaseId }, out var unRemove));
        Assert.Null(StateMachine.Apply(g, unRemove!, out _));
        Assert.Equal([Rivers, GroundLayerDef.BaseId, Woods], g.GroundOrder);
        Assert.Null(StateMachine.Apply(g, new RemoveGroundLayer { Id = Woods }, out _));
        Assert.Null(StateMachine.Apply(g, unAdd!, out _));
        Assert.Equal(GroundLayerEdits.LastStays, StateMachine.Apply(g, new RemoveGroundLayer { Id = GroundLayerDef.BaseId }, out _));
        Assert.NotNull(StateMachine.Apply(g, new AddGroundLayer { Id = "bad id!" }, out _));
    }

    [Fact]
    public void A_wood_keeps_its_forest_beside_its_trees()
    {
        byte levels = GroundKinds.Levels(80, 0);
        foreach (var (id, _) in GroundKinds.Forests)
        {
            int forest = GroundKinds.ForestNamed(id);
            byte detail = GroundKinds.WithForest(levels, forest);
            Assert.Equal(forest, GroundKinds.ForestOf(detail));
            Assert.Equal(80, GroundKinds.LevelsOf(detail).First);
        }
        Assert.Equal(GroundKinds.Regional, GroundKinds.ForestOf(GroundKinds.DefaultLevels(Wood)));
    }

    [Fact]
    public void Every_building_is_offered_in_every_age_its_age_marked()
    {
        var every = Catalogue.Every().SelectMany(s => s.Entries).Select(e => e.Id).ToList();
        Assert.Equal(Catalogue.All.Select(e => e.Id).Order(), every.Order());
        Assert.All(Catalogue.Every(), s => Assert.Equal(s.Entries.Where(e => e.Era == Catalogue.Any), s.Entries.TakeWhile(e => e.Era == Catalogue.Any)));
        Assert.Equal("Ancient", Catalogue.EraName(Catalogue.OldOnly));
        Assert.Null(Catalogue.EraName(Catalogue.Any));
    }
}

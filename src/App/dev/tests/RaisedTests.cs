using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedTests
{
    const float Upm = 0.1f, SideM = 9, LongM = 14, ReachM = 0.9f;

    static readonly string[] Classes = ["house", "townhouse", "cottage", "barn", "shed", "steeple", "keep", "tower", "warehouse", "insula", "domus", "flats", "factory", "terrace",
        "public", "mill", "silo", "dovecote", "roundhouse", "markethall", "baths", "tomb", "bunker", "hangar"];

    static BuiltPainter.Shape House(string cls, int culture, Vector2 uv) =>
        new(PartKinds.Block, cls, [uv, uv + new Vector2(0.01f, 0), uv + new Vector2(0.01f, 0.01f), uv + new Vector2(0, 0.01f)], new Rect2(uv, Vector2.One * 0.01f), 0, 0, Upm, culture);

    static Vector3[] Footprint(Vector3 at) =>
        [at, at + new Vector3(LongM * Upm, 0, 0), at + new Vector3(LongM * Upm, 0, SideM * Upm), at + new Vector3(0, 0, SideM * Upm)];

    static bool Within(Vector3 p, Vector3[] ring, float grown)
    {
        float x0 = ring.Min(q => q.X) - grown, x1 = ring.Max(q => q.X) + grown, z0 = ring.Min(q => q.Z) - grown, z1 = ring.Max(q => q.Z) + grown;
        return p.X >= x0 && p.X <= x1 && p.Z >= z0 && p.Z <= z1;
    }

    const float SlopeLow = 10, SlopeHigh = 14, Unit = 1;

    [NodeTest]
    static async Task A_raised_house_on_a_slope_stands_on_the_footing_of_its_culture(TestRunner r)
    {
        await r.Frame();
        Vector3[] corners = [new(0, SlopeLow, 0), new(8, SlopeLow, 0), new(8, SlopeHigh, 10), new(0, SlopeHigh, 10)];
        float Floor(Footing foot, out float bottom) => TableRaised.Footing(corners, Unit, out bottom, (int)foot).Y;
        float pad = Floor(Footing.Pad, out float padBottom), yard = Floor(Footing.RoofYard, out _), bank = Floor(Footing.Bank, out _), stilts = Floor(Footing.Stilts, out float stiltBottom);
        float facingUp = TableRaised.Footing([corners[2], corners[3], corners[0], corners[1]], Unit, out _, (int)Footing.Bank).Y;
        Check.That(pad > SlopeLow && pad < (SlopeLow + SlopeHigh) / 2 + 1, $"a pad floor {pad} is cut into the slope");
        Check.That(yard < pad, $"a roof-yard floor {yard} lies lowest");
        Check.That(Mathf.IsEqualApprox(bank, facingUp - (float)Footings.StoreyM, 1e-3f), $"a bank house fronting the lower street is sunk one storey into the slope ({bank}, {facingUp})");
        Check.That(facingUp >= SlopeHigh && facingUp < SlopeHigh + 1, $"a bank house fronting the upper street has its floor at that street ({facingUp})");
        Check.That(padBottom < SlopeLow && stiltBottom >= SlopeHigh - 1e-3f && stiltBottom < stilts, $"walls reach the ground under a pad ({padBottom}) and stand on posts on stilts ({stiltBottom})");
    }

    [NodeTest]
    static Task Every_raised_building_stands_on_the_outline_the_map_draws(TestRunner r)
    {
        var at = new Vector3(3, 0, 5);
        var ring = Footprint(at);
        int raised = 0;
        foreach (var culture in Manners.All.Select(m => m.Number))
            foreach (var cls in Classes)
            {
                var mesh = new RaisedMesh { PerMetre = Upm };
                var shape = House(cls, culture, new Vector2(0.3f + culture * 0.01f, 0.4f));
                string style = MapStyles.All.First(s => Manners.Age(s.Id) == Manners.Named(culture).Epoch).Id;
                RaisedBuildings.Add(mesh, shape, new RaisedBuildings.Site(ring, 0.2f, 0.1f, style));
                bool round = RaisedLooks.Of(shape, style, SideM, LongM).Form is Form.Cone or Form.Dome;
                Check.That(mesh.Count > 0, $"a {cls} of the {Manners.Id(culture)} culture stands up");
                Check.That(mesh.Points.All(p => Within(p, ring, ReachM * Upm)), $"a {cls} of the {Manners.Id(culture)} culture stays on its outline, eaves aside");
                foreach (var corner in ring)
                    Check.That(mesh.Points.Any(p => new Vector2(p.X - corner.X, p.Z - corner.Z).Length() < 1e-3f) || round,
                        $"a {cls} of the {Manners.Id(culture)} culture has a wall at each corner the map draws");
                raised++;
            }
        Check.That(raised == Manners.All.Count * Classes.Length, "every culture raised every class");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Row_houses_close_the_gap_between_them_and_a_house_further_off_stands_alone(TestRunner r)
    {
        var left = Footprint(Vector3.Zero);
        var right = Footprint(new Vector3((LongM + 0.6f) * Upm, 0, 0));
        var gaps = RaisedRows.Of([left, right], Upm);
        Check.That(gaps[0] is { } g && !float.IsNaN(g[1]) && Mathf.Abs(g[1] - 0.6f * Upm) < 1e-4f, "the house on the left finds its neighbour 0.6 m away");
        var a = RaisedRows.Closed(left, gaps[0]);
        var b = RaisedRows.Closed(right, gaps[1]);
        Check.That(Mathf.Abs(a[1].X - b[0].X) < 1e-4f && Mathf.Abs(a[2].X - b[3].X) < 1e-4f, "their walls meet halfway across the gap");
        Check.That(RaisedRows.Of([left, Footprint(new Vector3((LongM + 3) * Upm, 0, 0))], Upm)[0] == null, "a house 3 m off stands alone");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_whole_metre_wall_gets_every_window_its_length_holds(TestRunner r)
    {
        var mesh = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        var arched = new RaisedMesh.Paint(Colors.White, (int)Fabric.Plaster + 16 * (int)Glazing.Arched, Door: 0.3f, Storeys: 2);
        mesh.Wall(Vector3.Zero, new Vector3(13 * Upm, 0, 0), 0, 7 * Upm, new Vector3(0, 0, -1), arched);
        const int CornersAWindow = 16, Windows = 8;
        Check.That(mesh.Fine!.Count == Windows * CornersAWindow, $"a 13 m wall of arched windows takes four a storey on two storeys ({mesh.Fine.Count / CornersAWindow})");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Painted_wood_raises_its_trees_where_the_map_paints_them_and_nowhere_else(TestRunner r)
    {
        const int Cells = 40;
        var sheet = new SheetDef { Id = "w", Name = "Wood", Image = "", Size = new Size2(40, 40), Height = 1 };
        var grid = new ReliefGrid(Cells, Cells);
        for (int y = 0; y < Cells; y++)
            for (int x = 0; x < Cells; x++)
            {
                int i = y * Cells + x;
                grid.Kind[i] = x < Cells / 2 ? GroundKinds.Wood : GroundKinds.Grass;
                grid.Salt[i] = 7;
                grid.Detail[i] = (byte)GroundKinds.LevelSteps;
            }
        var ground = new Ground([sheet], _ => grid);
        var chunks = new Dictionary<(int, int), RaisedChunk>();
        foreach (var _ in RaisedForest.Plant(sheet, ground, 0, Upm, 16, chunks, new List<BuiltPainter.Shape>())) { }
        var trees = chunks.Values.SelectMany(c => c.TreesAt).ToList();
        Check.That(trees.Count > 50, $"a closed wood of 20 by 40 units holds its trees ({trees.Count})");
        Check.That(trees.All(t => Ground.UvOf(sheet, Coords.FromWorld(t)).u < 0.5 + 1e-6), "no tree stands on the grass half");
        return Task.CompletedTask;
    }

    static string StyleOf(int culture) => MapStyles.All.First(s => Manners.Age(s.Id) == Manners.Named(culture).Epoch).Id;

    [NodeTest]
    static Task A_far_house_keeps_its_walls_and_roof_and_leaves_the_small_work_behind(TestRunner r)
    {
        var shape = House("townhouse", Manners.Gothic, new Vector2(0.31f, 0.42f));
        var site = new RaisedBuildings.Site(Footprint(Vector3.Zero), 0.2f, 0.1f, StyleOf(Manners.Gothic));
        var near = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        var plain = new RaisedMesh { PerMetre = Upm, Plain = true };
        RaisedBuildings.Add(near, shape, site);
        RaisedBuildings.Add(plain, shape, site);
        int nearTriangles = near.Triangles + near.Fine!.Triangles;
        Check.That(plain.Count > 0 && plain.Triangles * 2 < nearTriangles, $"the far house draws less than half the triangles of the near one ({plain.Triangles} of {nearTriangles})");
        Check.That(plain.Points.Max(p => p.Y) > 0.2f + RaisedLooks.StoreyM * Upm, "the far house still rises above its first storey under a roof");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Near_map_tiles_leave_raised_buildings_unpainted_and_far_ones_keep_them(TestRunner r)
    {
        var shape = House("house", Manners.Continental, new Vector2(0.305f, 0.405f));
        var uv = new Rect2(0.3f, 0.4f, 0.02f, 0.02f);
        const float Pixels = 256, FullWidth = 100;
        int Batches(BuiltLayer.Raising raised, float pixels)
        {
            var layer = new BuiltLayer([shape], uv, pixels, pixels / (uv.Size.X * FullWidth), 1, 0, raised);
            layer.Make();
            int made = layer.Batches;
            layer.Free();
            return made;
        }
        Check.That(Batches(BuiltLayer.Raising.None, Pixels) > 0, "a plain map paints the house");
        Check.That(Batches(new BuiltLayer.Raising(2, false), Pixels) == 0, "a near tile of a raised map leaves it to the raised house");
        Check.That(Batches(new BuiltLayer.Raising(2, false), Pixels / 16) > 0, "a far tile still paints it");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_near_tile_shades_the_foot_of_a_raised_house_only_while_walking_asks_for_it(TestRunner r)
    {
        var shape = House("house", Manners.Continental, new Vector2(0.305f, 0.405f));
        var uv = new Rect2(0.3f, 0.4f, 0.02f, 0.02f);
        const float Pixels = 256, FullWidth = 100;
        int Points(BuiltLayer.Raising raised)
        {
            var layer = new BuiltLayer([shape], uv, Pixels, Pixels / (uv.Size.X * FullWidth), 1, 0, raised);
            layer.Make();
            int made = 0;
            for (int i = 0; i < layer.Batches; i++) made += layer.PointsIn(i);
            layer.Free();
            return made;
        }
        Check.That(Points(new BuiltLayer.Raising(2, true)) == 0, "a near tile of a raised map leaves the house to the raised one");
        int footed = Points(new BuiltLayer.Raising(2, true, Footed: true));
        Check.That(footed > 0, "with the footing asked for it lays a soft shade where the raised house stands");
        Check.That(Points(new BuiltLayer.Raising(0, false, Footed: true)) == Points(BuiltLayer.Raising.None), "a plain map draws the house as it always did");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Every_ground_photo_the_tiles_sample_is_shipped_and_sized(TestRunner r)
    {
        string shader = FileAccess.GetFileAsString("res://shaders/built.gdshader");
        var sizes = System.Text.RegularExpressions.Regex.Match(shader, @"PHOTO_M\[(\d+)\]");
        var means = System.Text.RegularExpressions.Regex.Match(shader, @"photo_means\[(\d+)\]");
        Check.That(sizes.Success && int.Parse(sizes.Groups[1].Value) == GroundPhotos.Layers.Length, "the shader sizes every ground photo");
        Check.That(means.Success && int.Parse(means.Groups[1].Value) == GroundPhotos.Layers.Length, "and takes a mean for each");
        foreach (var id in GroundPhotos.Layers) Check.That(ResourceLoader.Exists("res://textures/ground/" + id + "_diff.jpg"), $"{id} is shipped");
        return Task.CompletedTask;
    }

    static BuiltPainter.Shape Shaped(string kind, string cls, Vector2[] uv, int culture, float ravaged = 0) =>
        new(kind, cls, uv, new Rect2(uv[0], Vector2.One * 0.01f), 0, 0, Upm, culture, Ravaged: ravaged);

    [NodeTest]
    static Task Theatres_and_arenas_rise_in_tiers_and_an_aqueduct_arches_over_a_dip(TestRunner r)
    {
        var chunk = new RaisedChunk(Vector3.Zero);
        string style = StyleOf(Manners.Roman);
        var bowl = Enumerable.Range(0, 9).Select(i => new Vector3(Mathf.Cos(Mathf.Pi * i / 8) * 3, 0, Mathf.Sin(Mathf.Pi * i / 8) * 3)).ToArray();
        var theatre = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        RaisedAreas.Add(chunk, theatre, Shaped(PartKinds.Area, Hallmarks.Theatre, [new(0.3f, 0.3f)], Manners.Roman), bowl, _ => 0, style);
        float tiers = theatre.Points.Max(p => p.Y) / Upm;
        Check.That(tiers > 8 && tiers < 10, $"a theatre's seats climb four tiers to about 9 m ({tiers:0.0} m)");
        var oval = Enumerable.Range(0, 16).Select(i => new Vector3(Mathf.Cos(Mathf.Tau * i / 16) * 4, 0, Mathf.Sin(Mathf.Tau * i / 16) * 3)).ToArray();
        var arena = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        RaisedAreas.Add(chunk, arena, Shaped(PartKinds.Area, "arena", [new(0.3f, 0.3f)], Manners.Roman), oval, _ => 0, style);
        Check.That(Mathf.Abs(arena.Points.Max(p => p.Y) / Upm - 12) < 0.01f, "an arena's outer wall stands 12 m");
        var aqueduct = new RaisedMesh { PerMetre = Upm };
        float Dip(Vector3 p) => -Mathf.Max(0, 1 - Mathf.Abs(p.X - 5) / 2) * 1.2f;
        RaisedArcades.Aqueduct(aqueduct, [new Vector3(0, 0, 0), new Vector3(10, 0, 0)], Dip, new RaisedMesh.Paint(Colors.White, (int)Fabric.Stone));
        var over = aqueduct.Points.Where(p => Mathf.Abs(p.X - 5) < 0.3f).ToList();
        Check.That(over.Count > 0 && over.Max(p => p.Y) - Dip(new Vector3(5, 0, 0)) > 12 * Upm, "over a 12 m dip the channel stays up on its arches");
        Check.That(aqueduct.Triangles > 300, $"a 100 m aqueduct is built of piers and arches ({aqueduct.Triangles} triangles)");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_bridge_spans_its_stored_banks_and_a_tree_above_the_snow_line_stands_bare(TestRunner r)
    {
        const int Cells = 40;
        var sheet = new SheetDef { Id = "w", Name = "River", Image = "", Size = new Size2(40, 40), Height = 1 };
        var grid = new ReliefGrid(Cells, Cells);
        for (int y = 0; y < Cells; y++)
            for (int x = 0; x < Cells; x++)
            {
                grid.Kind[y * Cells + x] = x is >= 18 and < 22 ? GroundKinds.Water : GroundKinds.Grass;
                grid.Detail[y * Cells + x] = (byte)GroundKinds.LevelSteps;
            }
        var ground = new Ground([sheet], _ => grid);
        var mid = Coords.ToWorld(Ground.FromUv(sheet, 0.5, 0.5));
        var east = Coords.ToWorld(Ground.FromUv(sheet, 0.6, 0.5)) - mid;
        var chunk = new RaisedChunk(mid);
        var bank = Coords.ToWorld(Ground.FromUv(sheet, 0.45, 0.5));
        var far = Coords.ToWorld(Ground.FromUv(sheet, 0.55, 0.5));
        var bridge = Shaped(PartKinds.Sign, "bridge", [new(0.5f, 0.5f), new(0.45f, 0.5f), new(0.55f, 0.5f)], Manners.Continental);
        chunk.Add(bridge, [mid, bank, far], StyleOf(Manners.Continental), ground, 0, Upm, east.Normalized());
        float reach = chunk.Built.Max(p => p.Dot(east.Normalized())) - chunk.Built.Min(p => p.Dot(east.Normalized()));
        Check.That(reach / Upm > 40, $"the bridge spans the 40 m river bank to bank ({reach / Upm:0} m)");
        RaisedChunk.SnowY = 1;
        chunk.Tree(mid + Vector3.Up * 2, TreeKind.Broad, Upm, 0.3f);
        chunk.Tree(mid + Vector3.Up * 2, TreeKind.Conifer, Upm, 0.3f);
        RaisedChunk.SnowY = float.PositiveInfinity;
        Check.That(chunk.Grows(TreeKind.Bare) && !chunk.Grows(TreeKind.Broad) && chunk.Grows(TreeKind.Conifer), "above the snow line a broadleaf stands bare and a conifer keeps its needles");
        return Task.CompletedTask;
    }

    static Dictionary<(int, int), RaisedChunk> Wooded(int side, float chunk)
    {
        var chunks = new Dictionary<(int, int), RaisedChunk>();
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                var piece = new RaisedChunk(new Vector3((x + 0.5f) * chunk, 0, (z + 0.5f) * chunk));
                piece.Tree(new Vector3((x + 0.2f) * chunk, 1, (z + 0.7f) * chunk), TreeKind.Broad, Upm, 0.3f);
                piece.Tree(new Vector3((x + 0.6f) * chunk, 2, (z + 0.4f) * chunk), TreeKind.Conifer, Upm, 0.6f);
                chunks[(x, z)] = piece;
            }
        return chunks;
    }

    [NodeTest]
    static Task A_forest_too_big_for_the_renderer_is_drawn_in_fewer_bigger_groves(TestRunner r)
    {
        const int CrashedSide = 245;
        var chunks = Wooded(CrashedSide, 1);
        int span = RaisedGroves.Span(chunks);
        Check.That(RaisedGroves.Batches(chunks, 1) > RaisedGroves.Budget, "one batch per piece and kind would pass the budget");
        Check.That(span > 1 && RaisedGroves.Batches(chunks, span) <= RaisedGroves.Budget, $"grouped by {span} the forest keeps under the budget ({RaisedGroves.Batches(chunks, span)})");
        Check.That(RaisedGroves.Span(Wooded(4, 1)) == 1, "a small wood keeps one grove per piece");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Grouped_groves_keep_every_tree_where_it_was_planted(TestRunner r)
    {
        const float ChunkM = 160;
        var chunks = Wooded(4, ChunkM * Upm);
        var planted = chunks.Values.SelectMany(c => c.TreesAt).ToList();
        var drawn = RaisedGroves.Nodes(chunks, ChunkM, Upm, new StandardMaterial3D(), 2).OfType<RaisedGroves.Placed>().Select(p => p.Drawn).ToList();
        Check.That(drawn.Count == 16, $"four groves of two kinds, near and far ({drawn.Count})");
        var groves = RaisedGroves.Packed(chunks, ChunkM * Upm, 2);
        var at = groves.SelectMany(g => Enumerable.Range(0, g.Data.Length / RaisedGroves.PerInstance)
            .Select(i => g.Origin + new Vector3(g.Data[i * RaisedGroves.PerInstance + 3], g.Data[i * RaisedGroves.PerInstance + 7], g.Data[i * RaisedGroves.PerInstance + 11]))).ToList();
        Check.That(groves.Count == 8 && at.Count == planted.Count && planted.All(p => at.Any(q => q.DistanceTo(p) < 1e-4f)), $"every tree stands where it was planted ({at.Count} of {planted.Count})");
        Check.That(drawn.All(d => d.BeginM < RaisedChunk.TreeNearM || d.EndM > RaisedChunk.TreeNearM), "near and far groves overlap instead of leaving a gap");
        foreach (var d in drawn) d.Node.Free();
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Building_detail_moves_where_buildings_and_trees_turn_plain_and_the_reach_still_ends_them(TestRunner r)
    {
        var built = new RaisedChunk.Drawn(null!, 0, RaisedChunk.PlainFromM, Handed: true);
        var plain = new RaisedChunk.Drawn(null!, RaisedChunk.PlainFromM, float.PositiveInfinity, Handed: true);
        var fine = new RaisedChunk.Drawn(null!, 0, RaisedChunk.FineM);
        var tree = new RaisedChunk.Drawn(null!, 0, RaisedChunk.TreeNearM);
        float usual = 1, most = Settings.RaisedDetailMost / Settings.RaisedDetailUsual, open = float.PositiveInfinity;
        Check.That(TableRaised.ShownOf(built, open, usual).EndM == RaisedChunk.PlainFromM && TableRaised.ShownOf(plain, open, usual).BeginM == RaisedChunk.PlainFromM,
            "at the usual detail buildings turn plain where they always did");
        Check.That(TableRaised.ShownOf(plain, open, usual).EndM == 0, "plain buildings have no end of their own");
        Check.That(TableRaised.ShownOf(built, open, most).EndM == Settings.RaisedDetailMost && TableRaised.ShownOf(plain, open, most).BeginM == Settings.RaisedDetailMost,
            "at the most detail buildings keep their detail as far as the slider says");
        Check.That(TableRaised.ShownOf(fine, open, most).EndM == RaisedChunk.FineM * most && TableRaised.ShownOf(tree, open, most).EndM == RaisedChunk.TreeNearM * most,
            "small details and near trees go as far in step");
        float reach = Settings.RaisedDetailUsual;
        var cut = TableRaised.ShownOf(built, reach, most);
        Check.That(cut.EndM == reach && cut.EndMarginM > 0, "the raised reach still ends detailed buildings first, fading");
        return Task.CompletedTask;
    }

    static ReliefGrid Woods(int cells)
    {
        var grid = new ReliefGrid(cells, cells);
        for (int i = 0; i < cells * cells; i++)
        {
            grid.Kind[i] = GroundKinds.Wood;
            grid.Salt[i] = 7;
            grid.Detail[i] = (byte)GroundKinds.LevelSteps;
        }
        return grid;
    }

    [NodeTest]
    static Task A_stroke_on_the_ground_marks_only_the_blocks_it_touched_and_a_moved_house_its_old_and_new_place(TestRunner r)
    {
        const int Cells = 100;
        var grid = Woods(Cells);
        var before = RaisedChanges.Blocks(grid);
        grid.Kind[70 * Cells + 40] = GroundKinds.Grass;
        grid.H[70 * Cells + 41] = 0.5f;
        var changed = RaisedChanges.Changed(before, RaisedChanges.Blocks(grid), Cells, Cells).ToList();
        Check.That(changed.Count == 1 && changed[0].HasPoint(new Vector2I(40, 70)) && changed[0].HasPoint(new Vector2I(41, 70)), $"one block holds the stroke ({changed.Count})");
        var changes = new RaisedChanges();
        var house = new Rect2(1, 1, 2, 2);
        changes.Keep([new RaisedChanges.Grid("w", Cells, Cells, before)], new Dictionary<string, (long Key, Rect2 Box)> { ["a"] = (1, house), ["b"] = (2, new Rect2(9, 9, 1, 1)) });
        var moved = changes.ThingsChanged(new Dictionary<string, (long Key, Rect2 Box)> { ["a"] = (3, new Rect2(5, 1, 2, 2)), ["c"] = (4, new Rect2(20, 20, 1, 1)) }).ToList();
        Check.That(moved.Count == 3 && moved.Any(m => m.Encloses(house) && m.HasPoint(new Vector2(6, 2))), "the moved house marks where it was and where it is");
        Check.That(moved.Any(m => m.HasPoint(new Vector2(9.5f, 9.5f))) && moved.Any(m => m.HasPoint(new Vector2(20.5f, 20.5f))), "a removed and an added thing mark their places");
        Check.That(changes.GroundChanged(new RaisedChanges.Grid("w", Cells, Cells, before), out bool unknown).Count == 0 && !unknown, "unchanged ground marks nothing");
        changes.GroundChanged(new RaisedChanges.Grid("w", Cells + 1, Cells, before), out unknown);
        Check.That(unknown, "a grid of another size is laid again whole");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_wood_planted_again_in_a_few_pieces_grows_the_same_trees_there_and_none_elsewhere(TestRunner r)
    {
        const int Cells = 40;
        const float Chunk = 6;
        var sheet = new SheetDef { Id = "w", Name = "Wood", Image = "", Size = new Size2(40, 40), Height = 1 };
        var ground = new Ground([sheet], _ => Woods(Cells));
        var whole = new Dictionary<(int, int), RaisedChunk>();
        foreach (var _ in RaisedForest.Plant(sheet, ground, 0, Upm, Chunk, whole, new List<BuiltPainter.Shape>())) { }
        var only = whole.Keys.Where(k => k.Item1 == 1 || k == (-2, 2)).ToHashSet();
        var again = new Dictionary<(int, int), RaisedChunk>();
        foreach (var _ in RaisedForest.Plant(sheet, ground, 0, Upm, Chunk, again, new List<BuiltPainter.Shape>(), Manners.Continental, only)) { }
        Check.That(only.Count > 3 && again.Keys.All(only.Contains), $"only the chosen pieces grow trees ({again.Count} of {only.Count})");
        foreach (var key in only)
        {
            var was = whole[key].TreesAt.ToList();
            var now = again.TryGetValue(key, out var piece) ? piece.TreesAt.ToList() : [];
            Check.That(was.Count == now.Count && was.Zip(now).All(p => p.First.DistanceTo(p.Second) < 1e-5f), $"piece {key} grows the same {was.Count} trees ({now.Count})");
        }
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_ravaged_town_scorches_its_houses_and_its_ruins_keep_charred_rafters(TestRunner r)
    {
        var ring = Footprint(Vector3.Zero);
        string style = StyleOf(Manners.Gothic);
        int scorched = 0;
        for (int i = 0; i < 20; i++)
        {
            var house = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
            RaisedBuildings.Add(house, House("house", Manners.Gothic, new Vector2(0.3f + i * 0.013f, 0.4f)) with { Ravaged = 1 }, new RaisedBuildings.Site(ring, 0.2f, 0.1f, style));
            if (house.Scorch > 0) scorched++;
        }
        Check.That(scorched is > 5 and < 20, $"a town ravaged whole scorches most of its houses, not all ({scorched} of 20)");
        var ruin = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        RaisedBuildings.Add(ruin, House(Settlements.Ruin, Manners.Gothic, new Vector2(0.35f, 0.4f)), new RaisedBuildings.Site(ring, 0.2f, 0.1f, style));
        Check.That(ruin.Scorch == 1 && ruin.Fine!.Count > 100, $"a medieval ruin is burnt and keeps rafters and fallen timber ({ruin.Fine.Count} corners)");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_square_courtyard_house_keeps_its_court_open_under_a_level_roof_and_a_pitched_one(TestRunner r)
    {
        const float CourtM = 12, Floor = 0.2f;
        var centre = new Vector3(4, 0, 6);
        foreach (int culture in new[] { Manners.NorthAfrican, Manners.Western, Manners.Roman })
            for (int turn = 0; turn < 12; turn++)
            {
                float a = turn * 0.13f;
                Vector3 x = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (CourtM / 2 * Upm), z = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * (CourtM / 2 * Upm);
                Vector3[] ring = [centre - x - z, centre + x - z, centre + x + z, centre - x + z];
                var mesh = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
                RaisedBuildings.Add(mesh, House(culture == Manners.Roman ? "domus" : "villa", culture, new Vector2(0.31f + turn * 0.017f, 0.42f)), new RaisedBuildings.Site(ring, Floor, 0.1f, StyleOf(culture)));
                Check.That(!Blocked(mesh, centre with { Y = 30 * Upm }, centre with { Y = Floor + 0.3f * Upm }), $"the court of a square {Manners.Id(culture)} house turned {turn} lies open to the sky");
                Check.That(Blocked(mesh, (centre + x * 0.8f) with { Y = 30 * Upm }, (centre + x * 0.8f) with { Y = Floor + 0.3f * Upm }), "and its wings are roofed");
            }
        var level = new RaisedMesh { PerMetre = Upm };
        var plot = Footprint(new Vector3(3, 0, 5));
        var villa = House("villa", Manners.NorthAfrican, new Vector2(0.3f, 0.4f));
        RaisedBuildings.Add(level, villa, new RaisedBuildings.Site(plot, Floor, 0.1f, StyleOf(Manners.NorthAfrican)));
        float eaves = Floor + RaisedLooks.Of(villa, StyleOf(Manners.NorthAfrican), SideM, LongM).EavesM * Upm;
        Check.That(level.Points.Max(p => p.Y) > eaves + 0.4f * Upm, "a level courtyard roof has a parapet round it");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Each_culture_s_sacred_place_is_modelled_and_what_the_towns_lay_has_a_model(TestRunner r)
    {
        var at = new Vector3(3, 0, 5);
        var ring = Footprint(at);
        RaisedMesh Raise(int culture)
        {
            var mesh = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
            RaisedBuildings.Add(mesh, House("church", culture, new Vector2(0.33f, 0.41f)), new RaisedBuildings.Site(ring, 0.2f, 0.1f, StyleOf(culture)));
            return mesh;
        }
        var temple = Raise(Manners.Egyptian);
        var top = temple.Points.MaxBy(p => p.Y);
        Check.That(top.X > at.X + 0.85f * LongM * Upm, "an Egyptian temple is highest at its pylon, at its front");
        Check.That(temple.Points.Where(p => p.X < at.X + 0.2f * LongM * Upm).Max(p => p.Y) < top.Y * 0.7f, "and lowest at its sanctuary");
        Check.That(temple.Points.All(p => Within(p, ring, ReachM * Upm)), "and stays on its outline");
        var hall = Raise(Manners.Persian);
        Check.That(hall.Fine!.Count > 0 && hall.Points.All(p => Within(p, ring, ReachM * Upm)), "a Persian hall stands on columns within its terrace");
        Check.That(Raise(Manners.Tribal).Count > 0, "a Tribal holy place raises its standing stones");
        var mosque = Raise(Manners.NorthAfrican);
        var plain = Raise(Manners.Ottoman);
        Check.That(mosque.Points.Max(p => p.Y) > 0.2f + 15 * Upm && plain.Points.Max(p => p.Y) > 0.2f + 15 * Upm, "a mosque has its minaret");
        foreach (string cls in new[] { "dog", "hens", "geese", "pigs", "sheep", "cattle", "horses", Streetlife.Donkeys, Streetlife.Camels, "racks", "nets", "anvil", "wheels", "sweep", "cistern",
                     "watertower", "clamp", "ovoo", "stocks", "roadblock", "lookout", "tree", "woodpile", "well" })
            Check.That(RaisedProps.Modelled(cls), $"{cls} has a model");
        foreach (string cls in new[] { Streetlife.Puddle, "bridge", "pits", Streetlife.SteppingStones })
            Check.That(!RaisedProps.Modelled(cls), $"{cls} stays drawn on the ground");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_house_takes_the_storeys_roof_and_covering_set_on_it_by_hand(TestRunner r)
    {
        const float Floor = 0.2f;
        var ring = Footprint(new Vector3(3, 0, 5));
        string style = StyleOf(Manners.Continental);
        var plain = House("house", Manners.Continental, new Vector2(0.3f, 0.4f));
        var set = plain with { Storeys = 5, Roof = "flat", Cover = "lead" };
        var look = RaisedLooks.Of(set, style, SideM, LongM);
        Check.That(look.Storeys == 5 && Mathf.IsEqualApprox(look.EavesM, 5 * RaisedLooks.StoreyM) && look.Form == Form.Flat && look.Cover == Cover.Lead, "a house stands as tall and roofed as it was set by hand");
        var own = RaisedLooks.Of(plain, style, SideM, LongM);
        Check.That(own.Storeys < 5, $"where its culture alone gives it {own.Storeys} storeys");
        var drawn = RoofBook.Set(set, RoofBook.Of("house", Manners.Continental, Manners.Continental, SideM, LongM, 0.3f, 0.3f));
        Check.That(drawn == (look.Form, look.Cover), "and the map draws the same roof the raised house has");
        var mesh = new RaisedMesh { PerMetre = Upm };
        RaisedBuildings.Add(mesh, set, new RaisedBuildings.Site(ring, Floor, 0.1f, style));
        float top = mesh.Points.Max(p => p.Y), eaves = Floor + look.EavesM * Upm;
        Check.That(top >= eaves - 1e-3f && top < eaves + 1.5f * Upm, $"its flat roof lies at its eaves ({(top - eaves) / Upm:0.0} m above)");
        Check.That(RaisedLooks.Of(plain with { Roof = "flat", Cover = "thatch" }, style, SideM, LongM).Form != Form.Flat, "a covering that cannot lie flat keeps its pitch");
        Check.That(RaisedLooks.Of(plain with { Roof = "nonsense" }, style, SideM, LongM).Form == own.Form, "and a roof the app does not know leaves the culture's own");
        return Task.CompletedTask;
    }

    static bool Blocked(RaisedMesh m, Vector3 from, Vector3 to)
    {
        for (int i = 0; i + 2 < m.Corners.Count; i += 3)
            if (Geometry3D.SegmentIntersectsTriangle(from, to, m.Points[m.Corners[i]], m.Points[m.Corners[i + 1]], m.Points[m.Corners[i + 2]]).VariantType != Variant.Type.Nil) return true;
        return false;
    }

    [NodeTest]
    static Task Roman_towns_stand_low_with_square_towers_and_a_gatehouse_lets_the_road_through(TestRunner r)
    {
        string roman = StyleOf(Manners.Roman);
        var storeys = Enumerable.Range(0, 40).Select(i => RaisedLooks.Of(House("insula", Manners.Roman, new Vector2(0.2f + i * 0.0137f, 0.4f)), roman, 14, 18).Storeys).ToList();
        Check.That(storeys.Max() <= 3, $"a small Roman town's insulae stand two or three storeys (mean {storeys.Average():0.0})");
        var rome = Enumerable.Range(0, 40)
            .Select(i => RaisedLooks.Of(House("insula", Manners.Roman, new Vector2(0.2f + i * 0.0137f, 0.4f)) with { ReachM = 1500, Core = i % 2 == 0 }, roman, 14, 18).Storeys).ToList();
        Check.That(rome.Min() >= 4 && rome.Max() == 6, $"Rome's insulae stand four to six storeys, within Augustus's seventy feet ({rome.Min()} to {rome.Max()})");
        var square = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        RaisedTowers.Add(square, Manners.Roman, Vector3.Zero, Vector3.Right, 0.4f);
        var round = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        RaisedTowers.Add(round, Manners.Gothic, Vector3.Zero, Vector3.Right, 0.4f);
        Check.That(square.Points.Max(p => p.Y) < round.Points.Max(p => p.Y), "a Roman wall tower has a low tiled roof, a Gothic one a tall cone");
        var ring = Footprint(Vector3.Zero);
        var gate = new RaisedMesh { PerMetre = Upm, Fine = new RaisedMesh { PerMetre = Upm } };
        RaisedBuildings.Add(gate, House("gatehouse", Manners.Gothic, new Vector2(0.37f, 0.41f)), new RaisedBuildings.Site(ring, 0.2f, 0.1f, StyleOf(Manners.Gothic)));
        var centre = RaisedFrame.Middle(ring) with { Y = 0.2f + 2 * Upm };
        var crossings = new[] { Vector3.Right, Vector3.Back }.Select(d => !Blocked(gate, centre - d * 20 * Upm, centre + d * 20 * Upm)).ToList();
        Check.That(crossings.Count(open => open) == 1, "a gatehouse opens one passage straight through at walking height and stays shut across it");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Houses_stand_taller_as_the_town_grows_and_in_its_centre_as_their_culture_built(TestRunner r)
    {
        float[] reaches = [0, 300, 700, 1500], picks = Enumerable.Range(0, 10).Select(i => i / 10f + 0.05f).ToArray();
        double Mean(int culture, float reachM, bool core = false) => picks.Average(p => RaisedLooks.StoreysOf("house", culture, p, reachM, core));
        foreach (var culture in Manners.All.Select(m => m.Number))
            for (int i = 1; i < reaches.Length; i++)
                Check.That(Mean(culture, reaches[i]) >= Mean(culture, reaches[i - 1]), $"{Manners.Id(culture)} houses never stand lower in a bigger town ({reaches[i - 1]} m to {reaches[i]} m)");
        Check.That(Mean(Manners.Continental, 0) <= 2 && Mean(Manners.Continental, 1500) >= 5, "a Continental village stands one or two storeys, a great city five or six");
        Check.That(Mean(Manners.Continental, 700, true) > Mean(Manners.Continental, 700), "houses in a city's centre stand taller than at its edge");
        Check.That(Mean(Manners.Tribal, 1500, true) == 1 && Mean(Manners.Japanese, 1500, true) == 2, "tribal steadings stay one storey and Edo houses two however big the place");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_bridge_at_a_medieval_gatehouse_is_a_drawbridge_on_the_map_and_in_3D_and_an_ancient_one_is_not(TestRunner r)
    {
        BuiltThing Gate(int culture, double away)
        {
            var thing = new BuiltThing { Kind = BuiltKinds.Settlement, Settings = { [Manners.Setting] = culture } };
            var at = new Spot(20, 20);
            thing.Parts.Add(BuiltSite.Block("gatehouse", at, new Spot(1, 0), 0.2, 0.15));
            thing.Parts.Add(BuiltSite.Sign("bridge", at + new Spot(0, away), 0));
            return thing;
        }
        string Bridged(int culture, double away = 0.3) => BuiltPainter.ShapesOf(Gate(culture, away), 40, 40, 0.02f).Single(s => s.Kind == PartKinds.Sign).Class;
        Check.That(Bridged(Manners.Gothic) == BuiltShapes.Drawbridge, "a Gothic gate bridge is a drawbridge");
        Check.That(Bridged(Manners.Roman) == "bridge", "a Roman gate bridge stays a bridge");
        Check.That(Bridged(Manners.Gothic, away: 2) == "bridge", "a Gothic bridge a hundred metres from the gate stays a bridge");
        return Task.CompletedTask;
    }

    [NodeTest]
    static async Task Ground_and_building_pictures_are_packed_by_the_app_itself_so_a_release_keeps_them_small(TestRunner r)
    {
        Image Picture(Color c)
        {
            var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
            img.Fill(c);
            img.GenerateMipmaps();
            return img;
        }
        var packed = RaisedTextures.Packed([Picture(Colors.Red), Picture(Colors.Blue)]);
        Check.That(packed.All(p => p.GetFormat() == Image.Format.Dxt1 && p.HasMipmaps()), $"every picture is block-packed with its mipmaps: {string.Join(", ", packed.Select(p => p.GetFormat()))}");
        var bare = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        var mixed = new List<Image> { Picture(Colors.Red), bare };
        Check.That(ReferenceEquals(RaisedTextures.Packed(mixed)[1], bare) && RaisedTextures.Packed(mixed)[0].GetFormat() == Image.Format.Rgb8, "a set that cannot all be packed stays as it was, so the array keeps one format");
        var layers = new Texture2DArray();
        Check.Equal(Error.Ok, layers.CreateFromImages(new Godot.Collections.Array<Image>(packed)), "the packed pictures make a texture array");
        await r.Frames(1);
    }

    [NodeTest]
    static async Task The_3D_map_is_laid_again_whole_when_the_map_scale_or_a_sheet_s_relief_or_its_height_changes(TestRunner r)
    {
        var view = new GameState();
        view.Calibration.UnitsPerMeter = 0.02;
        view.Sheets.Add(new SheetDef { Id = "g", Size = new Size2(40, 40), Relief = "a", Height = 1 });
        var none = new HashSet<string>();
        int before = TableRaised.LayoutOf(view, none);
        Check.Equal(before, TableRaised.LayoutOf(view, none), "the same table keeps its layout");
        view.Sheets[0].Height = 2;
        int taller = TableRaised.LayoutOf(view, none);
        Check.That(taller != before, "a sheet's relief raised higher lays the map again whole");
        view.Sheets[0].Relief = "b";
        int other = TableRaised.LayoutOf(view, none);
        Check.That(other != taller, "a sheet given another relief lays the map again whole");
        view.Calibration.UnitsPerMeter = 0.04;
        Check.That(TableRaised.LayoutOf(view, none) != other, "a new map scale lays the map again whole");
        await r.Frames(1);
    }
}

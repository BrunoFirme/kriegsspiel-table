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
        float old = TableRaised.Footing(corners, Unit, out _).Y;
        Check.That(pad > SlopeLow && pad < (SlopeLow + SlopeHigh) / 2 + 1, $"a pad floor {pad} is cut into the slope");
        Check.That(yard < pad && bank >= SlopeHigh && Mathf.IsEqualApprox(old, bank), $"a roof-yard floor {yard} lies lowest and a bank house {bank} at its uphill corner, as before {old}");
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
}

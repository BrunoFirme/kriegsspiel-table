using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class RaisedWalkTests
{
    const double Side = 40, PerMetre = 0.02;
    const float WalkSeconds = 14;

    static async Task<OfflineTable> Raised(TestRunner r, params (string Class, Vec2 At, double LongM, double DeepM)[] stood)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = PerMetre });
        var medieval = Json.Clone(t.S.View.Lighting);
        medieval.MapStyle = MapStyles.Medieval;
        medieval.Raised = true;
        t.S.Do(new SetLighting { Lighting = medieval });
        await r.Frames(4);
        var sheet = t.S.View.Sheets[0];
        var site = BuildTool.Site(t.S, sheet, manner: Manners.Gothic);
        foreach (var (cls, at, longM, deepM) in stood)
            t.S.Do(new LayBuilt { Thing = BuiltThings.Building(site, sheet.Id, cls, Spot.Of(BuiltGeometry.ToSheet(sheet, at)), new Spot(1, 0), longM * PerMetre, deepM * PerMetre) });
        for (int i = 0; i < 400 && (TableRaised.Busy || TableRaised.Buildings < stood.Length); i++) await r.Frames(1);
        Check.That(TableRaised.Buildings == stood.Length, $"the map raised its {stood.Length} buildings ({TableRaised.Buildings})");
        return t;
    }

    static async Task<float> Walk(TestRunner r, OfflineTable t, Vec2 from, Vec2 to)
    {
        var walk = t.Table.Walk;
        Vector3 start = Coords.ToWorld(from, (float)t.S.Ground.At(from)), goal = Coords.ToWorld(to, (float)t.S.Ground.At(to));
        float yaw = Mathf.Atan2(goal.X - start.X, goal.Z - start.Z);
        if (!walk.Walking) walk.Enter(start);
        walk.MoveTo(start);
        walk.Steer = () => new Steering(yaw, 0, Vector2.Zero, false);
        await r.Seconds(0.8);
        walk.Steer = () => new Steering(yaw, 0, new Vector2(0, 1), true);
        float Left() => new Vector2(walk.Feet.X - goal.X, walk.Feet.Z - goal.Z).Length();
        for (float seconds = 0; seconds < WalkSeconds && Left() > walk.Height * 0.4f; seconds += (float)t.Table.GetProcessDeltaTime()) await r.Frames(1);
        walk.Steer = null;
        return Left() / (float)PerMetre;
    }

    [NodeTest]
    static async Task A_walker_is_stopped_by_a_raised_house_and_walks_through_an_open_gatehouse(TestRunner r)
    {
        const float M = (float)PerMetre;
        Vec2 house = new(-4, 0), gate = new(4, 0);
        var t = await Raised(r, ("house", house, 14, 9), ("gatehouse", gate, 9, 7));
        float stopped = await Walk(r, t, new Vec2(house.X, house.Z - 20 * M), new Vec2(house.X, house.Z + 20 * M));
        Check.That(t.Table.Walk.RaisedSolids > 0, "a walker has the raised map's houses and walls to walk against");
        Check.That(stopped > 24 && stopped < 26.5f, $"a walker running at a raised house stops at its wall ({stopped:0.0} m short of the far side)");
        float through = await Walk(r, t, new Vec2(gate.X, gate.Z - 16 * M), new Vec2(gate.X, gate.Z + 16 * M));
        Check.That(through < 1, $"and runs through a gatehouse by its passage ({through:0.0} m left)");
        float beside = await Walk(r, t, new Vec2(gate.X + 2.7 * M, gate.Z - 16 * M), new Vec2(gate.X + 2.7 * M, gate.Z + 16 * M));
        Check.That(beside > 19, $"but not through its wall beside the passage ({beside:0.0} m left)");
        t.Table.Walk.Leave();
    }
}

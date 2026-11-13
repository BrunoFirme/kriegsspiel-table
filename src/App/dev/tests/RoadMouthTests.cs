using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RoadMouthTests
{
    const float PerMetre = 10, RoadM = 6, LaneM = 4, RoadLong = 400, LaneLong = 300, Near = 0.5f, SurfaceFromPx = 5, RutsAcross = 4;
    static readonly Vector2[] Road = [new(-RoadLong, 0), new(RoadLong, 0)];

    static RoadMouths.Way Way(string cls, float metres, params Vector2[] px) =>
        new(new(PartKinds.Line, cls, px, default, 0, 0, 1, 0), px, new(Colors.Black, Colors.Wheat, metres, 1), PerMetre);

    [NodeTest]
    static async Task A_lane_that_ends_on_a_road_opens_into_it_and_its_tracks_turn_onto_the_road(TestRunner r)
    {
        await r.Frame();
        float half = LaneM * PerMetre / 2, roadHalf = RoadM * PerMetre / 2, round = RoadMouths.RoundM * PerMetre, gauge = RoadMouths.GaugeM * PerMetre;
        var mouths = RoadMouths.Of([Way("road", RoadM, Road), Way("lane", LaneM, new(0, LaneLong), new(0, 0))], SurfaceFromPx).ToList();
        Check.That(mouths.Count == 1 && mouths[0].Kind == Surface.Road, $"the lane has one mouth of its own earth, the road none: {mouths.Count}");
        var open = mouths[0].Open;
        Check.That(open.From.DistanceTo(new(0, roadHalf + RoadMouths.LongM * PerMetre)) < Near && open.Centre.Length() < Near, $"it opens from {open.From} to the road's middle {open.Centre}");
        Check.That(Mathf.Abs(open.Ring.Max(p => Mathf.Abs(p.X)) - half - round) < Near, $"it is widest at the road's edge: {open.Ring.Max(p => Mathf.Abs(p.X)):0.0}");
        Check.That(open.Ring.Where(p => p.Y > roadHalf + round + Near).All(p => Mathf.Abs(p.X) <= half + Near), "and no wider than the lane before its corners");
        Check.That(open.Corners.Length == 2 && open.Corners.All(c => c.All(p => Mathf.Abs(p.X) >= half - Near && p.Y >= roadHalf - Near)), "its two corners are rounded outside the lane and the road");
        Check.That(open.Ring.All(p => Mathf.Abs(p.X - mouths[0].Origin.X) > half + round + gauge * RutsAcross), "its earth is laid clear of the lane's straight ruts");
        Check.That(mouths[0].Tracks.Length == 4, $"four tracks leave it: {mouths[0].Tracks.Length}");
        foreach (var track in mouths[0].Tracks)
        {
            Check.That(Mathf.Abs(Mathf.Abs(track[0].X) - gauge) < Near && Mathf.Abs(track[0].Y - open.From.Y) < Near, $"a track starts in the lane's own rut: {track[0]}");
            Check.That(Mathf.Abs(Mathf.Abs(track[^1].Y) - gauge) < Near && Mathf.Abs(track[^1].X) > half, $"and ends in the road's: {track[^1]}");
        }
        Check.That(mouths[0].Tracks.Count(t => t[^1].X > 0) == 2, "two turn each way");
        RoadMouths.Way[] forked = [Way("road", RoadM, Road), Way("lane", LaneM, new(0, LaneLong), new(0, 0)), Way("lane", LaneM, new(LaneLong, LaneLong), new(0, 0))];
        var corners = RoadMouths.Of(forked, SurfaceFromPx).Select(m => m.Open.Corners.Length).ToList();
        Check.That(corners.Count == 2 && corners.All(c => c == 1), $"two lanes that end at one spot round only their outer corners: {string.Join(" ", corners)}");
    }

    [NodeTest]
    static async Task Roads_that_meet_end_to_end_cross_or_run_side_by_side_get_no_mouth(TestRunner r)
    {
        await r.Frame();
        float roadHalf = RoadM * PerMetre / 2;
        Check.That(RoadMouths.Met(Road, roadHalf, new(0, 0), roadHalf * 2) != null, "an end on the road's line is met");
        Check.That(RoadMouths.Met(Road, roadHalf, Road[0], roadHalf * 2) == null, "an end at the road's own end is not");
        Check.That(RoadMouths.Met(Road, roadHalf, new(0, LaneLong), roadHalf * 2) == null, "nor one that stops short of it");
        Check.That(!RoadMouths.Of([Way("road", RoadM, Road), Way("lane", LaneM, new(0, LaneLong), new(0, -LaneLong))], SurfaceFromPx).Any(), "a lane that crosses the road has no mouth");
        Check.That(RoadMouths.Laid(new(0, 0), Vector2.Right, roadHalf, new(0, 0), Vector2.Right, roadHalf, PerMetre) == null, "a road running along another opens nowhere");
        Check.That(!RoadMouths.Of([Way("road", RoadM, Road), Way("lane", LaneM / PerMetre / 2, new(0, LaneLong), new(0, 0))], SurfaceFromPx).Any(), "a lane too small to show its surface has none");
    }
}

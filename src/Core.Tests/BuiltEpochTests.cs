using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class BuiltEpochTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(int manner, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others, Manner = manner };

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static BuiltThing Town(int manner, double radius = 6, double houses = 0.75, double walls = 0, int seed = 4) =>
        Settlements.Lay(Site(manner), "s", Middle, radius, seed, houses, walls, Plans.Value(Plans.Offered(manner)[0]));

    static int Farming(BuiltThing t) => t.Parts.Count(p => p.Class is "garden" or "orchard" or "coop" or "pigsty" or "barn" or "byre" or "stable" or "rick" or "dungheap");

    [Fact]
    public void Each_age_has_its_own_works_and_none_of_another_age()
    {
        Assert.Equal(Defences.Camp, Defences.Fort(Manners.Roman).Id);
        Assert.Equal(Defences.Hillfort, Defences.Fort(Manners.Tribal).Id);
        Assert.All(new[] { Manners.Persian, Manners.Egyptian, Manners.Chinese }, m => Assert.Equal(Defences.Fortress, Defences.Fort(m).Id));
        Assert.All(new[] { Manners.Continental, Manners.Northern, Manners.Ottoman, Manners.American }, m => Assert.Equal(Defences.Star, Defences.Fort(m).Id));
        Assert.Equal(Defences.Castle, Defences.Fort(Manners.Japanese).Id);
        Assert.All(Manners.In(Manners.Modern), m => Assert.Equal(Defences.Strongpoint, Defences.Fort(m).Id));
        Assert.Null(Defences.Fieldwork(Manners.Roman));
        Assert.Equal(Defences.Redoubt, Defences.Fieldwork(Manners.Northern)!.Id);
        Assert.Equal(Defences.Battery, Defences.Fieldwork(Manners.Soviet)!.Id);
        Assert.Contains(Defences.Rings(Manners.Later), ring => ring.Key == Defences.Bastions);
        Assert.All(new[] { Manners.Ancient, Manners.Modern }, epoch => Assert.DoesNotContain(Defences.Rings(epoch), ring => ring.Key == Defences.Bastions));
        Assert.Equal(["track", "lane", "street", "road", "chaussee", "highway", "railway"], Defences.Routes);
        Assert.Equal((Manners.Later, Manners.Modern, Manners.Modern), (Defences.RouteAge("chaussee"), Defences.RouteAge("highway"), Defences.RouteAge("railway")));
        Assert.Null(Defences.RouteAge("road"));
    }

    [Fact]
    public void A_star_fort_asked_for_more_bastions_than_can_be_laid_lays_the_most_there_can_be()
    {
        var fort = Defences.Lay(Site(Manners.Continental), "s", Defences.Star, Middle, 4, 2, int.MaxValue, 0, 0);
        Assert.Equal(Forts.MostLaidBastions, fort.Setting(Forts.Bastions, 0));
    }

    [Fact]
    public void A_war_age_place_is_defended_by_trenches_and_wire_and_never_by_bastions()
    {
        var defended = Town(Manners.Western, walls: 1);
        Assert.True(Count(defended, PartKinds.Line, "trench") >= 1 && Count(defended, PartKinds.Line, "wire") >= 1);
        Assert.Equal(0, Count(defended, PartKinds.Line, "rampart") + Count(defended, PartKinds.Line, "wall") + Count(defended, PartKinds.Area, Forts.Ground));
        var box = Defences.Lay(Site(Manners.Soviet), "s", Defences.Strongpoint, Middle, 4, 2, 5, 1, 0);
        Assert.True(Count(box, PartKinds.Line, "trench") >= 2 && Count(box, PartKinds.Line, "wire") >= 2, "two belts of each on the eastern front");
        Assert.True(Count(box, PartKinds.Block, "bunker") >= 1 && Count(box, PartKinds.Sign, "gunpit") >= 3);
        Assert.Equal(Defences.Strongpoint, Defences.KindOf(box));
        Assert.Equal(Defences.Strongpoint, Defences.KindOf(BuiltThings.Remade(Site(Manners.Soviet), box, 9)));
        var battery = Defences.Lay(Site(Manners.Western), "s", Defences.Battery, Middle, 2, 2, 0, 0, 0);
        Assert.True(Count(battery, PartKinds.Sign, "gunpit") >= 2 && Count(battery, PartKinds.Line, "trench") == 1);
    }

    [Fact]
    public void The_older_ages_have_their_hillfort_fortress_and_castle()
    {
        var hill = Defences.Lay(Site(Manners.Tribal), "s", Defences.Hillfort, Middle, 4, 2, 0, 0, 0);
        Assert.True(Count(hill, PartKinds.Line, "rampart") >= 2 && Count(hill, PartKinds.Line, "fosse") >= 2 && Count(hill, PartKinds.Block, "roundhouse") >= 3);
        Assert.Equal(Defences.Hillfort, Defences.KindOf(hill));
        var fortress = Defences.Lay(Site(Manners.Egyptian), "s", Defences.Fortress, Middle, 3, 2, 0, 0, 0);
        Assert.Equal(Defences.Fortress, Defences.KindOf(fortress));
        Assert.True(Count(fortress, PartKinds.Sign, "tower") + Count(fortress, PartKinds.Block, "watchtower") > Count(Defences.Lay(Site(Manners.Roman), "s", Defences.Camp, Middle, 3, 2, 0, 0, 0), PartKinds.Sign, "tower"),
            "towers stand closer on a fortress wall");
        var castle = Defences.Lay(Site(Manners.Japanese), "s", Defences.Castle, Middle, 3, 2, 0, 0, 0);
        Assert.True(Count(castle, PartKinds.Area, Forts.Platform) >= 3 && Count(castle, PartKinds.Area, Forts.Ditch) >= 2 && Count(castle, PartKinds.Block, "keep") == 1, "baileys nested in their moats round the keep");
        var town = Town(Manners.Japanese, walls: 0.5);
        Assert.True(Count(town, PartKinds.Area, Forts.Platform) >= 3 && Count(town, PartKinds.Area, Forts.Ditch) >= 2, "a castle town holds its castle");
        Assert.Equal(0, Count(town, PartKinds.Line, "rampart"));
    }

    [Fact]
    public void A_war_age_town_lives_by_its_works_and_an_older_one_by_its_land()
    {
        var modern = Town(Manners.Western, houses: 0.5);
        var older = Town(Manners.Continental, houses: 0.5);
        double Share(BuiltThing t) => Farming(t) / (double)t.Parts.Count(p => p.Kind == PartKinds.Block);
        Assert.True(Share(modern) * 3 < Share(older), $"{Share(modern):0.00} farm things to a building in the war-age town, {Share(older):0.00} in the older one");
        Assert.True(Count(modern, PartKinds.Line, "railway") >= 1 && Count(modern, PartKinds.Block, "station") == 1);
        Assert.True(Count(modern, PartKinds.Block, "factory") >= 1 && Count(modern, PartKinds.Block, "terrace") >= 1);
        Assert.Equal(0, Count(older, PartKinds.Line, "railway") + Count(older, PartKinds.Block, "factory") + Count(older, PartKinds.Block, "station"));
        var village = Town(Manners.Western, radius: 2.2, houses: 0.4);
        Assert.True(Farming(village) > 0, "a war-age village still farms");
        Assert.Equal(0, Count(village, PartKinds.Block, "factory"));
        Assert.True(Count(Town(Manners.Soviet), PartKinds.Block, "byre") >= 3, "a collective farm's byres");
        Assert.True(Ways.Of(Manners.Western).Farming(1) < 0.1 && Ways.Of(Manners.Continental).Farming(0) > 0.7);
        Assert.Equal(0, Ways.Town(100, 0.9));
        Assert.Equal(1, Ways.Town(400, 0.9));
        Assert.True(Ways.Town(300, 0.5, true) > Ways.Town(300, 0.5) * 1.5, "a war-age place is a town at a lower density");
    }

    [Fact]
    public void Each_culture_lays_its_own_plan()
    {
        foreach (var culture in Manners.All)
        {
            var town = Town(culture.Number);
            Assert.Equal(Plans.Offered(culture.Number)[0], Settlements.Form(town));
            Assert.True(town.Parts.Count(p => p.Kind == PartKinds.Block) > 10, culture.Id + " lays a town");
        }
        Assert.Equal(Plans.Grid, Plans.For(Manners.Roman, ""));
        Assert.Equal(Plans.Grid, Plans.For(Manners.Roman, Plans.Wards));
        Assert.Equal(Plans.Grown, Plans.For(Manners.Roman, Plans.Grown));
        Assert.Equal(1, Count(Town(Manners.Roman), PartKinds.Area, "forum"));
        Assert.True(Count(Town(Manners.Chinese), PartKinds.Line, "wardwall") >= 8, "walled wards");
        var tribal = Town(Manners.Tribal);
        Assert.True(Count(tribal, PartKinds.Line, "fence") + Count(tribal, PartKinds.Line, "palisade") >= 5 && Count(tribal, PartKinds.Block, "roundhouse") >= 5);
        Assert.Equal(0, Count(tribal, PartKinds.Line, "street") + Count(tribal, PartKinds.Line, "lane"));
        Assert.True(Count(Town(Manners.Ottoman), PartKinds.Block, "church") >= 2, "a mosque to each quarter");
        Assert.Equal(1, Count(Town(Manners.Ottoman), PartKinds.Block, "cloister"));
    }

    [Fact]
    public void A_fort_sets_its_ground_level_inside_high_on_the_rampart_and_low_in_the_ditch_on_a_slope()
    {
        const double Metres = 5;
        var grid = new ReliefGrid(61, 61);
        for (int i = 0; i < grid.H.Length; i++) grid.H[i] = (float)(0.02 + i % 61 / 60.0 * 0.03);
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / Metres, Grid = grid, HeightUnit = Side };
        var fort = Forts.Lay(site, "s", Middle, 14, 2, 5, 1, 0);
        var body = Assert.Single(Forts.Bodies(fort, 1 / Metres));
        var shaped = Earthworks.Shaped(grid, [fort], Side, Side, 1 / Metres, Side);
        double High(Spot p) => shaped.HeightAt(p.X / Side, p.Y / Side) * Side * Metres;
        var centre = Spot.Of(BuiltGeometry.Points(fort.Outline)[0]);
        var inside = new[] { centre, centre + new Spot(3, 1), centre - new Spot(2, 3) }.Select(High).ToList();
        Assert.True(inside.Max() - inside.Min() < 0.3, $"the parade is level: {inside.Min():0.00} to {inside.Max():0.00} m");
        var trace = Forts.Trace(centre, 14, 5, 0);
        Spot face = (trace[1] + trace[2]) * 0.5, along = (trace[2] - trace[1]).Unit, outward = along.Left.Dot(face - centre) > 0 ? along.Left : along.Left * -1;
        Assert.InRange(High(face) - inside[0], body.P.High - 1, body.P.High + 0.3);
        Assert.InRange(inside[0] - High(face + outward * (body.P.Foot + body.P.Wide / 2)), body.P.Deep - 1, body.P.Deep + 0.3);
        var ravelin = fort.Parts.First(p => p.Kind == PartKinds.Area && p.Class == Forts.Ravelin);
        Assert.True(High(BuiltSite.Round(ravelin).At) > inside[0] + 1, "a ravelin stands up out of the ditch");
        var gate = fort.Parts.First(p => p.Kind == PartKinds.Sign && p.Class == "bridge");
        Assert.InRange(High(BuiltSite.Round(gate).At) - inside[0], -0.6, 0.6);
        var far = new Spot(2, 2);
        Assert.Equal(grid.HeightAt(far.X / Side, far.Y / Side) * Side * Metres, High(far), 1);
        var foot = face + outward * (body.P.Foot + body.P.Out + 3 / Metres);
        Assert.Equal(grid.HeightAt(foot.X / Side, foot.Y / Side) * Side * Metres, High(foot), 0);
        Assert.Equal(Defences.Star, Defences.KindOf(fort));
        Assert.Equal(5, Forts.Of(fort)!.Tips.Count);
    }

    [Fact]
    public void A_named_mix_is_kept_in_the_scenario_under_its_name_and_comes_back_on_undo()
    {
        var g = new GameState();
        var kept = new SetCultures
        {
            Cultures =
            {
                new NamedMix { Name = "  Frontier  ", Code = "roman+tribal+roman++0" }, new NamedMix { Name = "Plain", Code = "roman" }, new NamedMix { Name = "", Code = "roman+egyptian+roman++0" },
                new NamedMix { Name = "frontier", Code = "roman+egyptian+roman++0" },
            },
        };
        Assert.Null(StateMachine.Apply(g, kept, out var inverse));
        var mix = Assert.Single(g.Cultures!);
        Assert.Equal(("Frontier", Manners.Tribal), (mix.Name, Manners.Parse(mix.Code).Builds));
        Assert.Single(GameState.FromScenario(g.ToScenario("scn", "Test")).Cultures!);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.Null(g.Cultures);
        Assert.Null(g.ToScenario("scn", "Test").Cultures);
    }

    [Fact]
    public void A_stroke_of_details_over_settled_country_takes_the_place_of_what_was_there()
    {
        var first = BuiltDetails.Lay(Site(Manners.Continental), "s", [new Spot(10, 30), new Spot(50, 30)], 8, 3, 0.8, 0.6);
        Assert.True(first.Parts.Count > 20, $"{first.Parts.Count} parts in the first stroke");
        Assert.Null(BuiltDetails.Cleared(first, [new Spot(10, 5), new Spot(50, 5)], 3));
        var cleared = BuiltDetails.Cleared(first, [new Spot(30, 20), new Spot(30, 40)], 6)!;
        Assert.True(cleared.Parts.Count < first.Parts.Count);
        Assert.All(cleared.Parts.Where(p => p.Kind != PartKinds.Line), p => Assert.True(Math.Abs(BuiltSite.Round(p).At.X - 30) > 6));
        Assert.All(cleared.Parts.Where(p => p.Kind == PartKinds.Line).SelectMany(p => BuiltGeometry.Points(p.At)), p => Assert.True(Math.Abs(Spot.Of(p).X - 30) > 6 - 1e-6));
        Assert.Empty(BuiltDetails.Cleared(first, [new Spot(10, 30), new Spot(50, 30)], 30)!.Parts);
        var keptPart = first.Clone();
        keptPart.Parts[0].Kept = true;
        Assert.Single(BuiltDetails.Cleared(keptPart, [new Spot(10, 30), new Spot(50, 30)], 30)!.Parts);
    }

    [Fact]
    public void The_country_is_divided_as_its_culture_divides_it()
    {
        BuiltThing Country(int manner) => BuiltDetails.Lay(Site(manner), "s", [new Spot(8, 30), new Spot(52, 30)], 14, 3, 0.8, 0.6);
        var american = Country(Manners.American);
        Assert.True(Count(american, PartKinds.Line, "fence") > Count(american, PartKinds.Line, "hedge"), "rail fences");
        var squared = american.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "fence" or "hedge").Select(p => BuiltGeometry.Points(p.At)).Where(p => p.Count == 2).ToList();
        Assert.All(squared, p => Assert.True(Math.Abs(p[0].X - p[1].X) < 60 * BuiltGeometry.PerUnit * 0.02 || Math.Abs(p[0].Y - p[1].Y) < 60 * BuiltGeometry.PerUnit * 0.02, "the survey runs to the compass"));
        Assert.True(Count(Country(Manners.Egyptian), PartKinds.Line, "ditch") > 5, "channels between the plots");
        Assert.True(Country(Manners.Soviet).Parts.Count(p => p.Kind == PartKinds.Line) * 3 < Country(Manners.Continental).Parts.Count(p => p.Kind == PartKinds.Line), "collective fields have few boundaries");
        Assert.True(Count(Country(Manners.Tribal), PartKinds.Block, "roundhouse") >= 1);
        Assert.Equal(0, Count(Country(Manners.Pacific), PartKinds.Block, "barn") + Count(Country(Manners.Pacific), PartKinds.Sign, "rick"));
    }

    [Fact]
    public void A_place_has_the_surroundings_of_its_culture()
    {
        var persian = Town(Manners.Persian);
        Assert.True(Count(persian, PartKinds.Sign, Hallmarks.Qanat) >= 4 && Count(persian, PartKinds.Sign, "fountain") >= 1, "a garden fed by a qanat");
        var ottoman = Town(Manners.Ottoman);
        Assert.True(Count(ottoman, PartKinds.Area, "churchyard") >= 2 && Count(ottoman, PartKinds.Sign, "fountain") >= 1, "a graveyard outside and fountains in the streets");
        Assert.True(Count(Town(Manners.NorthAfrican), PartKinds.Area, "plantation") == 1 && Count(Town(Manners.NorthAfrican), PartKinds.Sign, "cistern") >= 1);
        Assert.True(Count(Town(Manners.Chinese), PartKinds.Sign, "mound") >= 2 && Count(Town(Manners.Tribal), PartKinds.Sign, "mound") >= 2);
        Assert.All(new[] { Manners.Japanese, Manners.Nippon }, m => Assert.Equal(1, Count(Town(m), PartKinds.Sign, "torii")));
        Assert.True(Count(Town(Manners.Roman), PartKinds.Sign, "altar") + Count(Town(Manners.Roman), PartKinds.Sign, "column") >= 3, "tombs along the road out");
        Assert.Equal(0, Count(Town(Manners.Continental), PartKinds.Sign, "mound") + Count(Town(Manners.Continental), PartKinds.Sign, "torii"));
    }

    static List<Spot> Closed(IEnumerable<Spot> ring)
    {
        var closed = ring.ToList();
        closed.Add(closed[0]);
        return closed;
    }

    [Fact]
    public void A_bastioned_town_saved_before_the_set_section_still_gives_its_trace_and_takes_a_pulled_tip()
    {
        var trace = Forts.Trace(Middle, 5, 5, 0.3);
        var old = new BuiltThing
        {
            Sheet = "s", Kind = BuiltKinds.Settlement, Seed = 4, Settings = { [Settlements.Walls] = 1 }, Outline = BuiltGeometry.Flat([Middle.Stored, (Middle + new Spot(6, 0)).Stored]),
            Parts =
            {
                BuiltSite.Line("moat", Closed(trace.Select(p => Middle + (p - Middle) * 1.13))), BuiltSite.Line("rampart", Closed(trace)),
                BuiltSite.Line("glacis", Closed(trace.Select(p => Middle + (p - Middle) * 1.3))),
            },
        };
        var star = Forts.Of(old)!;
        Assert.Equal(5, star.Tips.Count);
        Assert.True((star.Centre - Middle).Length < 0.01, $"the centre is found again: {star.Centre}");
        Assert.All(star.Tips, tip => Assert.Equal(5, tip.Radius, 2));
        Assert.Equal(Forts.GlacisFrom, star.Outworks);
        Assert.Equal(5, Walls.Handles(old)!.Corners.Count);
        var pulled = Forts.TipMoved(Site(Manners.Continental), old, 0, Middle + Spot.Toward(star.Tips[0].Bearing) * 7)!;
        Assert.Equal(7, Forts.Of(pulled)!.Tips[0].Radius, 2);
        Assert.Equal(0, Count(pulled, PartKinds.Line, "moat"));
        Assert.Equal((1, 1), (Count(pulled, PartKinds.Area, Forts.Ground), Count(pulled, PartKinds.Area, Forts.Ditch)));
    }

    [Fact]
    public void Only_an_age_with_bastions_draws_a_town_in_behind_them()
    {
        static List<Spot> Ring(BuiltThing town) => town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == "rampart").MaxBy(p => p.At.Count) is { } rampart
            ? BuiltGeometry.Points(rampart.At).Select(Spot.Of).ToList() : [];
        double Farthest(BuiltThing town) => town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "house" && (Ring(town).Count == 0 || Enclosures.Inside(Ring(town), BuiltSite.Round(p).At)))
            .Max(p => (BuiltSite.Round(p).At - Middle).Length);
        Assert.All(new[] { Manners.Roman, Manners.Western, Manners.Japanese }, manner =>
            Assert.True(Farthest(Town(manner, walls: 1)) > 6 * 0.7, $"{Manners.Id(manner)} keeps its houses out to {Farthest(Town(manner, walls: 1)):0.0} of 6"));
        Assert.True(Farthest(Town(Manners.Continental, walls: 1)) < 6 * 0.62, "a bastioned town is drawn in");
    }

    [Fact]
    public void A_quarter_mosque_stands_square_in_its_yard()
    {
        foreach (int manner in new[] { Manners.Ottoman, Manners.NorthAfrican })
            foreach (int seed in new[] { 4, 5, 6 })
            {
                var yards = Town(manner, seed: seed).Parts.Where(p => p.Kind == PartKinds.Area && p.Class == "churchyard" && p.At.Count == 8).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList())
                    .ToList();
                Assert.True(yards.Count >= 2, $"{Manners.Id(manner)} {seed}: {yards.Count} yards");
                Assert.All(yards, yard => Assert.True(Math.Abs((yard[1] - yard[0]).Unit.Dot((yard[3] - yard[0]).Unit)) < 0.01, "a yard's sides meet square"));
            }
    }

    [Fact]
    public void No_street_of_a_castle_town_runs_through_its_moat()
    {
        foreach (string plan in new[] { Plans.Grid, Plans.Grown, Plans.Street })
        {
            var town = Settlements.Lay(Site(Manners.Japanese), "s", Middle, 6, 4, 0.75, 0.5, Plans.Value(plan));
            var moat = BuiltGeometry.Points(town.Parts.First(p => p.Kind == PartKinds.Area && p.Class == Forts.Ditch).At).Select(Spot.Of).ToList();
            foreach (var line in town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "street" or "lane" or "road" or "track"))
            {
                var points = BuiltGeometry.Points(line.At).Select(Spot.Of).ToList();
                for (int i = 0; i + 1 < points.Count; i++)
                    for (int k = 0; k <= 8; k++) Assert.False(Enclosures.Inside(moat, points[i] + (points[i + 1] - points[i]) * (k / 8.0)), $"{plan}: a {line.Class} runs through the moat");
            }
        }
    }

    [Fact]
    public async Task A_place_of_no_size_is_laid_empty_and_at_once()
    {
        var laid = Task.Run(() => Settlements.Lay(Site(Manners.Pacific), "s", Middle, 0, 4, 0.5, 0, Plans.Value(Plans.Shore)));
        Assert.Same(laid, await Task.WhenAny(laid, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.Empty((await laid).Parts);
    }

    [Fact]
    public void The_road_tool_lays_a_railway_and_a_trench_in_bays()
    {
        var rail = BuiltRoads.Lay(Site(Manners.Western), "s", [new Spot(10, 10), new Spot(50, 12)], "railway", false);
        Assert.Equal((BuiltKinds.Road, 1), (rail.Kind, Count(rail, PartKinds.Line, "railway")));
        var trench = BuiltRoads.Lay(Site(Manners.Western), "s", [new Spot(10, 20), new Spot(20, 20)], "trench", false);
        Assert.True(Assert.Single(trench.Parts).At.Count > 40, "a trench steps round its traverses");
        var town = Settlements.Lay(Site(Manners.Western, rail), "s", new Spot(30, 17), 5, 4, 0.75, 0, 0);
        var station = Assert.Single(town.Parts, p => p.Class == "station");
        Assert.True(BuiltSite.Round(station).At.Y < 13, "the station stands on the line that was there");
    }
}

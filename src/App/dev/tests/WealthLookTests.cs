using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;

namespace KTable.App;

static class WealthLookTests
{
    const double Side = 40, PerMetre = 0.5, At = 20;
    const float SideM = 9, LongM = 14;
    const int HandStoreys = 2;

    static readonly int[] Plain = [Manners.Tribal, Manners.Mongol, Manners.Pacific];
    static readonly int[] Marked = [Manners.Gothic, Manners.Continental, Manners.Saracen, Manners.Western];

    static string StyleOf(int culture) => MapStyles.All.First(s => Manners.Age(s.Id) == Manners.Named(culture).Epoch).Id;

    static BuiltThing Home(int culture, WealthLevel? wealth)
    {
        var part = BuiltSite.Block("house", new Spot(At, At), new Spot(1, 0), LongM * PerMetre, SideM * PerMetre);
        part.Wealth = wealth is { } level ? PlaceZones.Stored(level) : null;
        return new BuiltThing { Id = "h", Kind = BuiltKinds.Building, Settings = { [Manners.Setting] = culture, [BuiltThings.Scale] = PerMetre }, Parts = [part] };
    }

    static BuiltPainter.Shape ShapeOf(BuiltThing thing) => BuiltPainter.ShapesOf(thing, Side, Side, (float)PerMetre).Single();

    static RaisedLook Raised(BuiltThing thing, int culture) => RaisedLooks.Of(ShapeOf(thing), StyleOf(culture), SideM, LongM);

    static long Painted(BuiltThing thing)
    {
        var painter = new BuiltPainter();
        painter.Set([thing], Side, Side, PerMetre);
        return painter.Key;
    }

    [NodeTest]
    static Task A_rich_and_a_poor_house_are_painted_and_raised_as_their_culture_dresses_them(TestRunner r)
    {
        foreach (var culture in Manners.All.Select(c => c.Number))
        {
            string id = Manners.Id(culture);
            BuiltThing laid = Home(culture, null), rich = Home(culture, WealthLevel.Rich), poor = Home(culture, WealthLevel.Poor);
            var (plain, dear, cheap) = (Raised(laid, culture), Raised(rich, culture), Raised(poor, culture));
            Check.That(ShapeOf(laid).Class == "house", $"an untouched {id} house is drawn as laid");
            if (Plain.Contains(culture))
            {
                Check.That(dear == plain && cheap == plain && ShapeOf(rich).Class == "house", $"a {id} house has no rich or poor look of its own");
                continue;
            }
            Check.That(dear != cheap && ShapeOf(rich).Class != ShapeOf(poor).Class, $"a rich {id} {ShapeOf(rich).Class} and a poor {id} {ShapeOf(poor).Class} are raised differently");
            Check.That(Painted(rich) != Painted(poor) && Painted(rich) != Painted(laid), $"and the {id} map is painted again for each");
            if (Marked.Contains(culture))
                Check.That(dear.Storeys != cheap.Storeys || dear.Form != cheap.Form, $"a rich and a poor {id} house differ in height or roof: {dear.Storeys} {dear.Form}, {cheap.Storeys} {cheap.Form}");
        }
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Storeys_roof_and_covering_set_by_hand_win_over_what_wealth_gives(TestRunner r)
    {
        foreach (int culture in Marked)
        {
            BuiltThing rich = Home(culture, WealthLevel.Rich), poor = Home(culture, WealthLevel.Poor);
            foreach (var part in rich.Parts.Concat(poor.Parts)) (part.Storeys, part.Roof, part.Cover) = (HandStoreys, "hip", "slate");
            var (dear, cheap) = (Raised(rich, culture), Raised(poor, culture));
            Check.That(dear.Storeys == HandStoreys && cheap.Storeys == HandStoreys && dear.Form == cheap.Form && dear.Cover == Cover.Slate && cheap.Cover == Cover.Slate,
                $"a {Manners.Id(culture)} house keeps the storeys, roof and covering set on it");
        }
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Taking_the_wealth_back_paints_and_raises_the_house_as_it_was(TestRunner r)
    {
        var laid = Home(Manners.Gothic, null);
        var rich = ZoneOverrides.WithWealth(laid, [0], WealthLevel.Rich);
        Check.That(ShapeOf(rich).Class == "townhouse" && Painted(rich) != Painted(laid), "a rich Gothic house is a town house");
        var back = ZoneOverrides.WithWealth(rich, [0], WealthLevel.Middling);
        Check.That(back.Parts[0].Wealth == null && Painted(back) == Painted(laid) && Raised(back, Manners.Gothic) == Raised(laid, Manners.Gothic), "set back to middling it is the house it was");
        return Task.CompletedTask;
    }
}

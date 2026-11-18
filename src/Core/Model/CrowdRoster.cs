namespace KTable.Core.Model;

public static class CrowdRoster
{
    public const int Outfits = 6;
    public const int Coat = 0, Gown = 1, Apron = 2, Jacket = 3, Soldier = 4, Smock = 5;
    const int Hour = CalendarDef.SecondsInHour;
    const double LeastHouseholdM2 = 60, PerHouseholdM2 = 45, MostHousehold = 9;
    const double WalkM = 1.3, WalkPlay = 0.2, ChildM = 1.2, AdultM = 1.68, AdultPlay = 0.08;
    const double ChildShare = 0.35, OutdoorWork = 0.08, ChildOut = 0.4, ThirdOut = 0.4, SoldierShare = 0.03, MillReachM = 9, PlayReachM = 5;
    const double LeaveAt = 7, LeavePlay = 1.5, ShiftHours = 4, ShiftPlay = 2, OutHours = 0.6, OutPlay = 0.5;
    const double PlayFrom = 9, PlayPlay = 1.5, PlayHours = 2.5;
    static readonly string[] Yards = ["square", "green", "churchyard", "yard", "parade"];
    static readonly string[] Sheds = ["shed", "privy", "barn", "stable", "granary", "coop", "sty"];

    public static List<CrowdPerson> Of(BuiltThing place, SheetDef sheet, double unitsPerMetre)
    {
        var people = new List<CrowdPerson>();
        if (place.Kind != BuiltKinds.Settlement || unitsPerMetre <= 0) return people;
        Vec2 World(BuiltPart part) => BuiltGeometry.ToWorld(sheet, BuiltSite.Round(part).At.Stored);
        double Area(BuiltPart part) => BuiltSite.Round(part).Reach is var r && r > 0 ? Math.PI * r * r / (unitsPerMetre * unitsPerMetre) : LeastHouseholdM2;
        var blocks = place.Parts.Where(p => p.Kind == PartKinds.Block).ToList();
        var homes = blocks.Where(p => Brushed.Home(p.Class)).ToList();
        var works = blocks.Where(p => !Brushed.Home(p.Class) && !Sheds.Contains(p.Class)).Select(p => new CrowdSpot(World(p), CrowdStay.Indoors, 0)).ToList();
        var yards = place.Parts.Where(p => p.Kind == PartKinds.Area && Yards.Contains(p.Class)).Select(p => new CrowdSpot(World(p), CrowdStay.Milling, MillReachM)).ToList();
        if (homes.Count == 0) return people;
        if (works.Count == 0) works = homes.Select(h => new CrowdSpot(World(h), CrowdStay.Indoors, 0)).ToList();
        if (yards.Count == 0) yards = works.Select(w => w with { Stay = CrowdStay.Milling, ReachM = MillReachM }).ToList();
        var dice = new Dice(place.Seed, 7);
        foreach (var house in homes)
        {
            var home = new CrowdSpot(World(house), CrowdStay.Indoors, 0);
            int household = (int)Math.Clamp(Math.Round(Area(house) / PerHouseholdM2 * dice.Between(0.7, 1.3)), 1, MostHousehold);
            for (int i = 0; i < household; i++)
            {
                int seed = (int)(dice.Next() * int.MaxValue);
                bool child = i >= 2 && dice.Chance(ChildShare * 2);
                double speed = WalkM * dice.Between(1 - WalkPlay, 1 + WalkPlay);
                if (child)
                {
                    var play = dice.Chance(ChildOut) ? home with { Stay = CrowdStay.Milling, ReachM = PlayReachM } : works[dice.Below(works.Count)];
                    int from = Seconds(PlayFrom, PlayPlay, dice);
                    people.Add(new(seed, Smock, ChildM * dice.Between(0.75, 1.1), true, home, play, play, from, Seconds(PlayHours, 1, dice), 0, speed));
                    continue;
                }
                bool soldier = dice.Chance(SoldierShare);
                var work = works[dice.Below(works.Count)];
                if (dice.Chance(OutdoorWork)) work = work with { Stay = CrowdStay.Milling, ReachM = MillReachM * 0.5 };
                var third = dice.Chance(ThirdOut) ? yards[dice.Below(yards.Count)] : works[dice.Below(works.Count)];
                int outfit = soldier ? Soldier : i % 2 == 1 ? (dice.Chance(0.5) ? Apron : Gown) : dice.Chance(0.4) ? Jacket : Coat;
                people.Add(new(seed, outfit, AdultM * dice.Between(1 - AdultPlay, 1 + AdultPlay) * (i % 2 == 1 ? 0.94 : 1), false, home, work, third,
                    Seconds(LeaveAt, LeavePlay, dice), Seconds(ShiftHours, ShiftPlay, dice), Seconds(OutHours, OutPlay, dice), speed));
            }
        }
        return people;
    }

    static int Seconds(double hours, double play, Dice dice) => (int)(Math.Max(0, hours + dice.Between(-play, play)) * Hour);
}

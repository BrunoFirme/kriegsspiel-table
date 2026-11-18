namespace KTable.Core.Model;

public static class CrowdNight
{
    const string Lantern = "lantern", None = "none";
    const double UsualShare = 0.6;
    const int NightStream = 61;

    public static CrowdLook Carrying(CrowdLook look, Nationality people, int seed)
    {
        if (look.Item != null || look.Band == CrowdEntry.Child) return look;
        var dice = new Dice(seed, NightStream);
        if (!dice.Chance(people.Day?.NightShare ?? UsualShare)) return look;
        string light = CrowdDress.Pick(dice, people.Day?.NightLights ?? Lantern);
        if (light == None) return look;
        var lit = look.Clone();
        lit.Item = light;
        return lit;
    }

    public static string? Problem(DayRules day)
    {
        if (day.NightShare is < 0 or > 1) return "night share from 0 to 1";
        if (day.NightLights is { } lights && lights.Split('|').Select(o => o.Split(':')[0]).FirstOrDefault(o => o != None && CrowdBaseline.Items.All(i => i.Id != o)) is { } bad)
            return $"night lights: no hand item {bad}";
        return null;
    }
}

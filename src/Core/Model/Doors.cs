namespace KTable.Core.Model;

public static class Doors
{
    public const string Tag = "door", BuildingTag = "building";

    static List<PartDef> In(PieceDef d, int state, Func<string, PieceDef?> find) =>
        state >= 0 && state < d.States.Count && d.States[state].Parts is { } parts ? parts.Where(p => find(p.Def)?.Tags.Contains(Tag) == true).ToList() : [];

    public static int Count(PieceDef d, Func<string, PieceDef?> find) => In(d, 0, find).Count;

    public static string Name(PieceDef d, int door, Func<string, PieceDef?> find) => In(d, 0, find) is var shut && door < shut.Count ? find(shut[door].Def)?.Name ?? "" : "";

    public static bool Open(PieceDef d, int state, int door, Func<string, PieceDef?> find) =>
        In(d, state, find) is var now && In(d, 0, find) is var shut && door < now.Count && door < shut.Count && !Same(now[door], shut[door]);

    public static int? Toggled(PieceDef d, int state, int door, Func<string, PieceDef?> find)
    {
        var now = In(d, state, find);
        if (door < 0 || door >= now.Count) return null;
        for (int s = 0; s < d.States.Count; s++)
        {
            var there = In(d, s, find);
            if (there.Count != now.Count) continue;
            bool fits = true;
            for (int k = 0; k < now.Count && fits; k++) fits = Same(there[k], now[k]) != (k == door);
            if (fits) return s;
        }
        return null;
    }

    public static bool IsToggle(PieceDef d, int from, int to, Func<string, PieceDef?> find) =>
        Enumerable.Range(0, Count(d, find)).Any(k => Toggled(d, from, k, find) == to);

    static bool Same(PartDef a, PartDef b) => a.Def == b.Def && a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.Rot == b.Rot;
}

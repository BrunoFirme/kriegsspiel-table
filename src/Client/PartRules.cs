using KTable.Core.Model;

namespace KTable.Client;

public static class PartRules
{
    public const int Deepest = 4;

    public static bool PerLook(PieceDef d) => d.States.Any(s => s.Parts != null);

    public static List<PartDef>? Shown(PieceDef d, int look) =>
        !PerLook(d) ? d.Parts : look >= 0 && look < d.States.Count ? d.States[look].Parts : null;

    public static List<PartDef> Listed(PieceDef d, int look) =>
        PerLook(d) && look >= 0 && look < d.States.Count ? d.States[look].Parts ??= new() : d.Parts ??= new();

    public static void Tidy(PieceDef d)
    {
        if (d.Parts is { Count: 0 }) d.Parts = null;
        if (d.States.Any(s => s.Parts is { Count: > 0 })) return;
        foreach (var s in d.States) s.Parts = null;
    }

    public static bool Forget(IEnumerable<PieceDef> pieces, string id)
    {
        bool any = false;
        foreach (var d in pieces)
        {
            int gone = d.Parts?.RemoveAll(p => p.Def == id) ?? 0;
            foreach (var s in d.States) gone += s.Parts?.RemoveAll(p => p.Def == id) ?? 0;
            if (gone == 0) continue;
            Tidy(d);
            any = true;
        }
        return any;
    }

    public static int Below(PieceDef d, Func<string, PieceDef?> find) => Below(d, find, new HashSet<string>());

    static int Below(PieceDef d, Func<string, PieceDef?> find, HashSet<string> seen)
    {
        if (!seen.Add(d.Id)) return Deepest + 1;
        int most = 0;
        foreach (var id in d.AllParts.Select(p => p.Def).Distinct())
            if (find(id) is { } part) most = Math.Max(most, 1 + Below(part, find, seen));
        seen.Remove(d.Id);
        return most;
    }

    public static int Above(PieceDef d, IReadOnlyCollection<PieceDef> all) => Above(d, all, new HashSet<string>());

    static int Above(PieceDef d, IReadOnlyCollection<PieceDef> all, HashSet<string> seen)
    {
        if (!seen.Add(d.Id)) return Deepest + 1;
        int most = 0;
        foreach (var holder in all.Where(h => h.AllParts.Any(p => p.Def == d.Id))) most = Math.Max(most, 1 + Above(holder, all, seen));
        seen.Remove(d.Id);
        return most;
    }

    public static bool Holds(PieceDef d, string id, Func<string, PieceDef?> find) => Holds(d, id, find, new HashSet<string>());

    static bool Holds(PieceDef d, string id, Func<string, PieceDef?> find, HashSet<string> seen)
    {
        if (!seen.Add(d.Id)) return false;
        foreach (var part in d.AllParts.Select(p => p.Def).Distinct())
            if (part == id || find(part) is { } inner && Holds(inner, id, find, seen)) return true;
        return false;
    }

    public static string? Refusal(PieceDef piece, PieceDef part, IReadOnlyCollection<PieceDef> all, Func<string, PieceDef?> find)
    {
        if (part.Id == piece.Id) return $"{piece.Name} cannot be a part of itself.";
        if (PieceKinds.IsEffect(part)) return $"{part.Name} is an effect: place effects on the table.";
        if (PieceKinds.IsEffect(piece)) return $"{piece.Name} is an effect and carries no parts.";
        if (Holds(part, piece.Id, find)) return $"{part.Name} already holds {piece.Name}.";
        if (Above(piece, all) + 1 + Below(part, find) > Deepest) return $"{part.Name} would sit more than {Deepest} pieces deep, and the table draws {Deepest}.";
        return null;
    }

    public static List<PieceDef> Takes(PieceDef piece, IReadOnlyCollection<PieceDef> all, Func<string, PieceDef?> find)
    {
        if (PieceKinds.IsEffect(piece)) return new();
        int room = Deepest - Above(piece, all) - 1;
        return all.Where(k => k.Id != piece.Id && !k.Retired && !PieceKinds.IsEffect(k) && Below(k, find) <= room && !Holds(k, piece.Id, find)).ToList();
    }

    public static List<Vec2> InRanks(int count, int ranks, Size2 over)
    {
        count = Math.Max(1, count);
        ranks = Math.Clamp(ranks, 1, count);
        int files = (int)Math.Ceiling(count / (double)ranks);
        double across = over.W / files, deep = over.H / ranks;
        var places = new List<Vec2>(count);
        for (int k = 0; k < count; k++)
        {
            int rank = k / files, file = k % files;
            int inRank = Math.Min(files, count - rank * files);
            places.Add(new Vec2((file - (inRank - 1) / 2.0) * across, ((ranks - 1) / 2.0 - rank) * deep));
        }
        return places;
    }

    public static void Repeat(List<PartDef> list, PartDef like, int count, int ranks, Size2 over)
    {
        int at = Math.Max(0, list.FindIndex(p => p.Def == like.Def));
        list.RemoveAll(p => p.Def == like.Def);
        var places = InRanks(count, ranks, over);
        list.InsertRange(Math.Min(at, list.Count), places.Select(place => new PartDef
        {
            Def = like.Def, X = Math.Round(place.X, Digits), Z = Math.Round(-place.Z, Digits), Y = like.Y, Rot = like.Rot, TiltX = like.TiltX, TiltZ = like.TiltZ, FaceDown = like.FaceDown,
            Scale = like.Scale, Tint = like.Tint, State = like.State, Face = like.Face,
        }));
    }

    const int Digits = 4;
}

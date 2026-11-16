namespace KTable.Core.Model;

public readonly record struct Lamp(Spot At, double ReachM, double HighM, int Kind, double Strength, int Part = -1, int Order = 0, bool Dark = false);

public static class Lamps
{
    public const int Electric = 0, Lantern = 1, Torch = 2, Fire = 3, Window = 4, Flood = 5;

    const double ElectricApartM = 64, LaneApartM = 92, VillageApartM = 150, LanternApartM = 116, OffRoadM = 1.2, ModernTownFrom = 0.15, LanternTownFrom = 0.35, GateAsideM = 4;
    const double WindowsModern = 0.25, WindowsLater = 0.17, WindowsAncient = 0.11, HearthsTribal = 0.22, RowWindowM = 16, FrontOutM = 1, DoorOutM = 2, CornerInM = 3;
    const double TowerTorch = 0.18, WellLantern = 0.25, TentFire = 0.17, LeastPartM = 2;
    static readonly string[] Homes = ["house", "shop", "villa", "domus", "cabin", "stonehouse", "farm", "fisher", "hut", "roundhouse", "longhouse", "greathouse", "taberna"];
    static readonly string[] Rows = ["flats", "terrace", "barracks", "insula"];
    static readonly string[] Sacred = ["church", "public", "basilica", "cloister", "baths", "rotunda"];

    static double HalfM(string cls) => cls switch { "highway" => 5.5, "chaussee" => 4.75, "street" or "road" => 3.25, "lane" or "causeway" => 1.8, _ => 0 };

    static (double Reach, double High) Size(int kind) => kind switch
    {
        Electric => (9, 5), Lantern => (6, 3), Torch => (4.5, 2.2), Fire => (7, 0.6), Window => (3.2, 1.5), _ => (14, 8),
    };

    static Lamp Made(Spot at, int kind, double strength = 1) => new(at, Size(kind).Reach, Size(kind).High, kind, strength);

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static Spot Middle(List<Spot> points) => new(points.Average(p => p.X), points.Average(p => p.Y));

    public static int Street(int epoch) => epoch switch { Manners.Modern => Electric, Manners.Later => Lantern, _ => Torch };

    public static List<Lamp> Of(BuiltThing thing, int epoch, double unitsPerMetre)
    {
        var lamps = new List<Lamp>();
        if (unitsPerMetre <= 0 || !double.IsFinite(unitsPerMetre)) return lamps;
        int culture = Manners.For(epoch, Manners.Of(thing));
        double M(double metres) => metres * unitsPerMetre;
        bool Rolled(Spot at, int salt, double share) => BuiltSite.Roll(at, salt).Chance(share);
        var (_, reach) = BuiltThings.Round(thing);
        double town = thing.Kind == BuiltKinds.Settlement ? Ways.Town(reach / unitsPerMetre, thing.Setting(Settlements.Houses, 0.5), epoch == Manners.Modern) : 0;
        int street = Street(epoch);
        for (int index = 0; index < thing.Parts.Count; index++)
        {
            var part = thing.Parts[index];
            var points = Points(part);
            if (points.Count == 0) continue;
            int first = lamps.Count;
            switch (part.Kind)
            {
                case PartKinds.Line when thing.Kind == BuiltKinds.Settlement && HalfM(part.Class) > 0:
                    double apart = Apart(epoch, culture, town, part.Class);
                    if (apart > 0) Along(points, M(apart), M(HalfM(part.Class) + OffRoadM), at => lamps.Add(Made(at, street)));
                    break;
                case PartKinds.Block:
                    Block(part.Class, points, epoch, culture, town, M, Rolled, lamps);
                    break;
                case PartKinds.Area when part.Class is "square" or "forum" or "parade" || part.Class == "green" && town > LanternTownFrom:
                    Corners(points, epoch, town, M, lamps);
                    break;
                case PartKinds.Sign:
                    Sign(part, points[0], epoch, town, M, Rolled, lamps);
                    break;
            }
            for (int k = first; k < lamps.Count; k++)
                lamps[k] = lamps[k] with { Part = index, Order = k - first, Dark = part.Dark?.Contains(k - first) == true };
        }
        return lamps;
    }

    public static BuiltThing? Switched(BuiltThing thing, int epoch, double unitsPerMetre, IReadOnlyList<Spot> stroke, double radius, bool on)
    {
        if (stroke.Count == 0) return null;
        var touched = Of(thing, epoch, unitsPerMetre).Where(l => l.Dark == on && Under(l.At, stroke, radius)).ToList();
        if (touched.Count == 0) return null;
        var set = thing.Clone();
        foreach (var lamp in touched)
        {
            var part = set.Parts[lamp.Part];
            if (on) part.Dark!.Remove(lamp.Order);
            else (part.Dark ??= new()).Add(lamp.Order);
            if (part.Dark is { Count: 0 }) part.Dark = null;
            else part.Dark?.Sort();
        }
        return set;
    }

    static bool Under(Spot at, IReadOnlyList<Spot> stroke, double radius)
    {
        if (stroke.Count == 1) return (at - stroke[0]).Length <= radius;
        for (int i = 0; i + 1 < stroke.Count; i++)
            if (at.FromSegment(stroke[i], stroke[i + 1]) <= radius) return true;
        return false;
    }

    static double Apart(int epoch, int culture, double town, string cls) => epoch switch
    {
        Manners.Modern when culture == Manners.Pacific => 0,
        Manners.Modern when town < ModernTownFrom || culture == Manners.NorthAfrican => cls is "street" or "road" or "chaussee" ? VillageApartM : 0,
        Manners.Modern => cls is "lane" or "causeway" ? LaneApartM : ElectricApartM,
        Manners.Later when town >= LanternTownFrom => cls is "street" or "road" or "chaussee" ? LanternApartM : 0,
        _ => 0,
    };

    static void Along(List<Spot> line, double apart, double aside, Action<Spot> lay)
    {
        double carried = apart / 2;
        int side = 1;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var leg = line[i + 1] - line[i];
            double length = leg.Length;
            if (length < 1e-9) continue;
            for (double s = carried; s < length; s += apart)
            {
                lay(line[i] + leg * (s / length) + leg.Unit.Left * (aside * side));
                side = -side;
            }
            carried = (carried - length) % apart;
            if (carried < 0) carried += apart;
        }
    }

    static (Spot Front, Spot Out, Spot Along, double Long) Facing(List<Spot> corners)
    {
        var middle = Middle(corners);
        var front = (corners[0] + corners[1]) * 0.5;
        var along = corners[1] - corners[0];
        return (front, (front - middle).Unit, along.Unit, along.Length);
    }

    static void Block(string cls, List<Spot> corners, int epoch, int culture, double town, Func<double, double> M, Func<Spot, int, double, bool> rolled, List<Lamp> lamps)
    {
        if (corners.Count < 3) return;
        var (front, outward, along, length) = Facing(corners);
        if (length < M(LeastPartM)) return;
        var middle = Middle(corners);
        double windows = epoch switch { Manners.Modern => WindowsModern, Manners.Later => WindowsLater, _ => WindowsAncient };
        if (Array.IndexOf(Homes, cls) >= 0)
        {
            bool hearth = cls is "roundhouse" or "longhouse" or "greathouse";
            if (rolled(middle, 11, hearth ? HearthsTribal : windows)) lamps.Add(Made(front + outward * M(FrontOutM), Window, hearth ? 1.1 : 1));
            return;
        }
        if (Array.IndexOf(Rows, cls) >= 0)
        {
            int count = Math.Max(1, (int)(length / M(RowWindowM)));
            for (int k = 0; k < count; k++)
            {
                var at = front + along * ((k + 0.5) * length / count - length / 2) + outward * M(FrontOutM);
                if (rolled(at, 13, windows)) lamps.Add(Made(at, Window));
            }
            return;
        }
        if (Array.IndexOf(Sacred, cls) >= 0)
        {
            var door = front + outward * M(DoorOutM);
            if (epoch == Manners.Ancient)
                foreach (int side in new[] { -1, 1 }) lamps.Add(Made(door + along * (side * length * 0.3), Fire, 0.8));
            else lamps.Add(Made(door, Street(epoch)));
            return;
        }
        switch (cls)
        {
            case "inn" or "tavern":
                lamps.Add(Made(front + outward * M(DoorOutM), epoch == Manners.Ancient ? Torch : Lantern));
                return;
            case "gatehouse":
                foreach (int side in new[] { -1, 1 }) lamps.Add(Made(middle + along * (side * (length / 2 + M(GateAsideM / 2))), Street(epoch)));
                return;
            case "keep" or "watchtower" when epoch != Manners.Modern:
                lamps.Add(Made(middle, Fire));
                return;
            case "smithy" or "kiln":
                lamps.Add(Made(front + outward * M(FrontOutM), Fire, 0.7));
                return;
            case "factory" or "dockshed" or "goodsshed" or "hangar" when epoch == Manners.Modern:
                lamps.Add(Made(front + outward * M(DoorOutM), Flood));
                return;
            case "station" when epoch == Manners.Modern:
                Along([front - along * (length / 2), front + along * (length / 2)], M(RowWindowM * 1.5), M(DoorOutM), at => lamps.Add(Made(at, Electric, 0.8)));
                return;
        }
        if (cls == "barracks" || town > LanternTownFrom && cls is "warehouse" or "customs" or "markethall" && rolled(middle, 17, windows)) lamps.Add(Made(front + outward * M(FrontOutM), Window));
    }

    static void Corners(List<Spot> ring, int epoch, double town, Func<double, double> M, List<Lamp> lamps)
    {
        if (ring.Count < 3) return;
        var middle = Middle(ring);
        var inset = ring.Select(p => p + (middle - p).Unit * M(CornerInM)).ToList();
        switch (epoch)
        {
            case Manners.Modern:
                lamps.Add(Made(inset[0], Electric));
                lamps.Add(Made(inset[inset.Count / 2], Electric));
                break;
            case Manners.Later when town > LanternTownFrom:
                lamps.Add(Made(inset[0], Lantern));
                lamps.Add(Made(inset[inset.Count / 2], Lantern));
                break;
            case Manners.Ancient when town > 0:
                lamps.Add(Made(inset[0], Fire, 0.8));
                lamps.Add(Made(inset[inset.Count / 2], Fire, 0.8));
                break;
        }
    }

    static void Sign(BuiltPart part, Spot at, int epoch, double town, Func<double, double> M, Func<Spot, int, double, bool> rolled, List<Lamp> lamps)
    {
        var road = Spot.Toward(part.Turn * Math.PI / 1800);
        switch (part.Class)
        {
            case "bridge" when part.At.Count >= 2:
                foreach (int side in new[] { -1, 1 }) lamps.Add(Made(at + road.Left * (side * M(GateAsideM)), Street(epoch)));
                break;
            case "tower" when epoch != Manners.Modern && rolled(at, 19, TowerTorch):
                lamps.Add(Made(at, Torch));
                break;
            case "beacon":
                lamps.Add(Made(at, Fire, 1.4));
                break;
            case "clamp":
                lamps.Add(Made(at, Fire, 0.5));
                break;
            case "well" or "fountain" or "marketcross" when epoch == Manners.Later && town > LanternTownFrom && rolled(at, 23, WellLantern):
                lamps.Add(Made(at + road * M(DoorOutM), Lantern, 0.8));
                break;
            case "tent" when epoch != Manners.Modern && rolled(at, 29, TentFire):
                lamps.Add(Made(at + road * M(DoorOutM * 2), Fire, 0.8));
                break;
            case "ship" or "steamer":
                lamps.Add(Made(at, Lantern, 0.6));
                break;
        }
    }

    public static double Glow(Lighting l, double onFlat) => l.LampsOut ? 0 : Math.Clamp((DuskAt - onFlat) / (DuskAt - DarkAt), 0, 1);

    public const double DuskAt = 0.62, DarkAt = 0.38;
}

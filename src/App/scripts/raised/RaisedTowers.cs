using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedTowers
{
    const float SquareM = 6.5f, RoundM = 3.3f, HighM = 9.5f, LogM = 5.5f, LogHighM = 8, PlatformM = 3.2f, PlatformHighM = 6, PlatformRoofM = 1.8f, PostM = 0.3f, RoofOutM = 0.5f;
    const float TentPitch = 1.1f, ConePitch = 1.2f, TilePitch = 0.42f, PavilionPitch = 0.55f;

    enum Kind { Round, SquareTiled, SquareCrenellated, RoundLead, Log, Pavilion, Platform, Blockhouse }

    static Kind KindOf(int builders) => builders switch
    {
        Manners.Roman => Kind.SquareTiled,
        Manners.Byzantine or Manners.Saracen or Manners.NorthAfrican or Manners.Persian or Manners.Egyptian or Manners.Mongol => Kind.SquareCrenellated,
        Manners.Ottoman => Kind.RoundLead,
        Manners.Rus or Manners.Northern or Manners.Soviet => Kind.Log,
        Manners.Chinese or Manners.Japanese or Manners.Nippon => Kind.Pavilion,
        Manners.Tribal or Manners.Pacific => Kind.Platform,
        Manners.American => Kind.Blockhouse,
        _ => Kind.Round,
    };

    public static void Add(RaisedMesh m, int builders, Vector3 at, Vector3 across, float pick)
    {
        float upm = m.PerMetre;
        var x = (across with { Y = 0 }).Normalized();
        if (x.LengthSquared() < 1e-6f) x = Vector3.Right;
        var z = new Vector3(-x.Z, 0, x.X);
        Vector3[] Square(float halfM) => [at - (x + z) * (halfM * upm), at + (x - z) * (halfM * upm), at + (x + z) * (halfM * upm), at - (x - z) * (halfM * upm)];
        var kind = KindOf(builders);
        var stone = new RaisedMesh.Paint(RaisedColours.Wall(RoofBook.Flat(builders) ? Fabric.Mud : builders == Manners.Roman ? Fabric.Brick : Fabric.Stone, builders, pick),
            (int)(RoofBook.Flat(builders) ? Fabric.Mud : builders == Manners.Roman ? Fabric.Brick : Fabric.Stone) + 16 * (int)Glazing.Slit, Storeys: 2);
        var log = new RaisedMesh.Paint(RaisedColours.Wall(Fabric.Log, builders, pick), (int)Fabric.Log);
        float bottom = at.Y - 0.5f * upm, top = at.Y + HighM * upm;
        switch (kind)
        {
            case Kind.SquareTiled:
                Roofed(m, Square(SquareM / 2), bottom, top, stone, Cover.Tile, TilePitch, pick, false);
                break;
            case Kind.SquareCrenellated:
                var ring = Square(SquareM / 2);
                RaisedRoofs.Block(m, ring, bottom, top, stone);
                RaisedRoofs.Parapet(m, ring, top, RaisedRoofs.ParapetM * upm, stone with { Code = stone.Code % 16 }, true);
                break;
            case Kind.Log:
                Roofed(m, Square(LogM / 2), bottom, at.Y + LogHighM * upm, log, Cover.Plank, TentPitch, pick, false);
                break;
            case Kind.Pavilion:
                var white = new RaisedMesh.Paint(RaisedColours.Wall(Fabric.Plaster, builders, pick), (int)Fabric.Plaster);
                Roofed(m, Square(SquareM / 2), bottom, top, white, Cover.Kawara, PavilionPitch, pick, true);
                break;
            case Kind.Platform:
                Platform(m, at, x, z, pick);
                break;
            case Kind.Blockhouse:
                RaisedRoofs.Block(m, Square(LogM / 2), bottom, at.Y + 4 * upm, log);
                Roofed(m, Square(LogM / 2 + 0.8f), at.Y + 4 * upm, at.Y + 7 * upm, log, Cover.Shingle, TilePitch, pick, false);
                break;
            default:
                Round(m, at, top, bottom, stone, kind == Kind.RoundLead ? Cover.Lead : builders == Manners.Gothic ? Cover.Tile : Cover.Slate, pick);
                break;
        }
    }

    static void Roofed(RaisedMesh m, Vector3[] ring, float bottom, float eaves, RaisedMesh.Paint wall, Cover cover, float pitch, float pick, bool hip)
    {
        var roof = new RaisedMesh.Paint(RaisedColours.Roof(cover, pick), RaisedCodes.Roof + (int)cover);
        var frame = RaisedFrame.Of(ring);
        RaisedRoofs.Walls(new RaisedRoofs.Raise(m, frame, eaves, bottom, wall, roof, pitch), ring, eaves, wall);
        var raise = new RaisedRoofs.Raise(m, frame.Grown(RoofOutM * m.PerMetre), eaves, bottom, wall, roof, pitch);
        if (hip) RaisedRoofs.Hip(raise, 0.4f);
        else RaisedRoofs.Pyramid(raise);
    }

    static void Round(RaisedMesh m, Vector3 at, float top, float bottom, RaisedMesh.Paint wall, Cover cover, float pick)
    {
        float r = RoundM * m.PerMetre;
        RaisedRoofs.Turned(m, at with { Y = bottom }, Vector3.Right * r, Vector3.Back * r, top - bottom, [new(1, 0), new(1, 1), new(0, 1)], wall with { Code = wall.Code % 16 });
        var roof = new RaisedMesh.Paint(RaisedColours.Roof(cover, pick), RaisedCodes.Roof + (int)cover);
        float wide = (RoundM + RoofOutM) * m.PerMetre;
        RaisedRoofs.Turned(m, at with { Y = top }, Vector3.Right * wide, Vector3.Back * wide, wide * ConePitch * (cover == Cover.Lead ? 1.2f : 1), [new(1, 0), new(0, 1)], roof);
    }

    static void Platform(RaisedMesh m, Vector3 at, Vector3 x, Vector3 z, float pick)
    {
        float upm = m.PerMetre, half = PlatformM / 2;
        var wood = new RaisedMesh.Paint(RaisedColours.Timber, RaisedCodes.Timber);
        foreach (var (dx, dz) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
        {
            var foot = at + (x * dx + z * dz) * (half * 0.85f * upm);
            Vector3 px = x * (PostM / 2 * upm), pz = z * (PostM / 2 * upm);
            RaisedRoofs.Block(m, [foot - px - pz, foot + px - pz, foot + px + pz, foot - px + pz], at.Y - 0.5f * upm, at.Y + (PlatformHighM + PlatformRoofM) * upm, wood);
        }
        Vector3[] deck = [at - (x + z) * (half * upm), at + (x - z) * (half * upm), at + (x + z) * (half * upm), at - (x - z) * (half * upm)];
        RaisedRoofs.Block(m, deck, at.Y + (PlatformHighM - 0.3f) * upm, at.Y + PlatformHighM * upm, wood);
        var thatch = new RaisedMesh.Paint(RaisedColours.Roof(Cover.Thatch, pick), RaisedCodes.Roof + (int)Cover.Thatch);
        float wide = (half + RoofOutM) * upm;
        RaisedRoofs.Turned(m, at with { Y = at.Y + (PlatformHighM + PlatformRoofM) * upm }, x * wide, z * wide, wide * 1.1f, [new(1, 0), new(0, 1)], thatch, 8);
    }
}

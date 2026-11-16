using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedShapes
{
    const float GothicDormers = 0.3f, Dormered = 0.5f, JettyM = 0.55f, DormerWideM = 1.5f, DormerHighM = 1.3f, DormerInM = 1.1f, DormerPitch = 0.62f;
    const float DormerLongM = 14, DormerFromRiseM = 2.6f, DormerFromLongM = 7;

    public static bool Jettied(RaisedLook look, string cls, int builders) =>
        builders == Manners.Gothic && look.Storeys >= 2 && cls is "house" or "townhouse" or "inn" or "shop" or "bakery"
        && look.Form is not (Form.Flat or Form.Court or Form.Atrium or Form.Dome or Form.Cone or Form.Vault);

    public static RaisedRoofs.Raise Jetty(RaisedRoofs.Raise raise, RaisedLook look, Vector3? doorA, Vector3? doorB, float doorAt)
    {
        var m = raise.M;
        var f = raise.F;
        float floor = raise.Bottom + (RaisedLooks.PlinthM + RaisedLooks.StoreyM) * m.PerMetre, push = JettyM * m.PerMetre;
        var ring = f.Ring;
        int front = 0;
        for (int i = 0; i < 4; i++)
            if (f.FrontA.DistanceSquaredTo(ring[i]) < 1e-8f && (f.FrontB.DistanceSquaredTo(ring[(i + 1) % 4]) < 1e-8f || f.FrontB.DistanceSquaredTo(ring[(i + 3) % 4]) < 1e-8f))
                front = f.FrontB.DistanceSquaredTo(ring[(i + 1) % 4]) < 1e-8f ? i : (i + 3) % 4;
        Vector3 a = ring[front], b = ring[(front + 1) % 4];
        var outward = new Vector3(-(b - a).Z, 0, (b - a).X).Normalized();
        if (outward.Dot(a - f.Centre) < 0) outward = -outward;
        var upper = (Vector3[])ring.Clone();
        upper[front] += outward * push;
        upper[(front + 1) % 4] += outward * push;
        RaisedRoofs.Walls(raise, ring, floor, raise.Wall with { Storeys = 1 }, doorA, doorB, doorAt, look.Shop);
        var above = raise with { F = RaisedFrame.Of(upper), Bottom = floor };
        m.FootingM = 0;
        RaisedRoofs.Walls(above, upper, raise.Eaves, raise.Wall with { Storeys = look.Storeys - 1 });
        m.FootingM = RaisedLooks.PlinthM;
        var timber = new RaisedMesh.Paint(RaisedColours.Timber, RaisedCodes.Timber);
        m.Face([a with { Y = floor }, b with { Y = floor }, upper[(front + 1) % 4] with { Y = floor }, upper[front] with { Y = floor }], Vector3.Down, b - a, a with { Y = floor }, timber);
        return above;
    }

    public static void Dormers(RaisedRoofs.Raise r, RaisedLook look, int builders, float pick)
    {
        if (pick >= (builders == Manners.Gothic ? GothicDormers : Dormered)) return;
        var f = r.F;
        float rise = Mathf.Tan(r.Pitch) * f.Short / 2;
        bool fits = look.Form is Form.Gable or Form.HalfHip && look.Storeys >= 2 && rise >= DormerFromRiseM * r.M.PerMetre && f.Long >= DormerFromLongM * r.M.PerMetre;
        if (!fits || builders is not (Manners.Gothic or Manners.Continental or Manners.Western or Manners.Northern or Manners.American)) return;
        int count = f.Long / r.M.PerMetre >= DormerLongM ? 2 : 1;
        for (int k = 0; k < count; k++) Dormer(r, (k + 1f) / (count + 1), 0);
    }

    static void Dormer(RaisedRoofs.Raise r, float t, int side)
    {
        var f = r.F;
        float upm = r.M.PerMetre, tan = Mathf.Tan(r.Pitch);
        if (tan < 1e-3f) return;
        var inward = side == 0 ? f.Across : -f.Across;
        var wall = f.At(t, side);
        var w = f.Along * (DormerWideM / 2 * upm);
        float inset = DormerInM * upm, y0 = r.Eaves + tan * inset, y1 = y0 + DormerHighM * upm, y2 = y1 + Mathf.Tan(DormerPitch) * DormerWideM / 2 * upm;
        var front = wall + inward * inset;
        Vector3 back = wall + inward * ((y2 - r.Eaves) / tan), cheek = wall + inward * ((y1 - r.Eaves) / tan);
        var window = r.Wall with { Door = -2, Length = DormerWideM, Storeys = 1 };
        r.M.Face([(front - w) with { Y = y0 }, (front + w) with { Y = y0 }, (front + w) with { Y = y1 }, (front - w) with { Y = y1 }], -inward, w, (front - w) with { Y = y0 }, window);
        r.M.Face([(front - w) with { Y = y1 }, (front + w) with { Y = y1 }, front with { Y = y2 }], -inward, w, (front - w) with { Y = y1 }, r.Wall);
        foreach (int s in new[] { -1, 1 })
        {
            var edge = w * s;
            r.M.Face([(front + edge) with { Y = y0 }, (front + edge) with { Y = y1 }, (cheek + edge) with { Y = y1 }], edge, inward, (front + edge) with { Y = y0 }, r.Wall);
            var eave = (front + edge) with { Y = y1 };
            r.M.Face([eave, front with { Y = y2 }, back with { Y = y2 }, (cheek + edge) with { Y = y1 }], Vector3.Up + edge.Normalized(), inward, eave, r.Roof);
        }
    }
}

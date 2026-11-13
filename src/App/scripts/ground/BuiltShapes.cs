using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

readonly record struct Roofed(Vector2[] A, Vector2 From, Vector2 To, float LongPx, float ShortPx, Vector2 FrontA, Vector2 FrontB)
{
    public Vector2 Centre => (A[0] + A[2]) / 2;
    public Vector2 Along => (A[1] - A[0]) / Mathf.Max(LongPx, 0.001f);
    public Vector2 Across => (A[3] - A[0]) / Mathf.Max(ShortPx, 0.001f);
    public Vector2 At(float along, float across) => A[0] + (A[1] - A[0]) * along + (A[3] - A[0]) * across;
    public Vector2[] Part(float t0, float t1, float s0, float s1) => [At(t0, s0), At(t1, s0), At(t1, s1), At(t0, s1)];
    public Vector2[] Inset(float px) => Part(px / Mathf.Max(LongPx, 0.001f), 1 - px / Mathf.Max(LongPx, 0.001f), px / Mathf.Max(ShortPx, 0.001f), 1 - px / Mathf.Max(ShortPx, 0.001f));
}

static class BuiltShapes
{
    public static Vector2 Mid(Vector2[] of) => of.Aggregate(Vector2.Zero, (sum, p) => sum + p) / of.Length;

    public static Roofed Frame(Vector2[] px)
    {
        bool longFirst = (px[1] - px[0]).Length() >= (px[2] - px[1]).Length();
        Vector2[] a = longFirst ? px : [px[1], px[2], px[3], px[0]];
        return new Roofed(a, (a[0] + a[3]) / 2, (a[1] + a[2]) / 2, (a[1] - a[0]).Length(), (a[3] - a[0]).Length(), px[0], px[1]);
    }

    public static float AreaOf(Vector2[] ring)
    {
        float twice = 0;
        for (int i = 0; i < ring.Length; i++) twice += ring[i].Cross(ring[(i + 1) % ring.Length]);
        return Mathf.Abs(twice) / 2;
    }

    public static bool Smokeless(string cls) => cls is "barn" or "shed" or "privy" or "stable" or "byre" or "granary" or "cartshed" or "warehouse" or "steeple" or "keep" or "gatehouse" or "tower"
        or "mill" or "coop" or "pigsty" or "dovecote" or "kennel" or "magazine" or "arsenal" or "markethall" or "outshut" or "silo" or "kiln" or "watchtower" or "baths" or "basilica";

    public static Vector2[][] Wings(Vector2[] px)
    {
        if ((px[2] - px[1]).Length() > (px[5] - px[0]).Length())
        {
            var corner = px[1] + px[5] - px[0];
            return [[px[0], px[1], corner, px[5]], [px[4], corner, px[2], px[3]]];
        }
        var inner = px[0] + px[2] - px[1];
        return [[px[0], px[1], px[2], inner], [inner, px[3], px[4], px[5]]];
    }

    public static Vector2 Ridge(Roofed f, Form form, float t)
    {
        float far = Mathf.Min(0.5f, 0.5f * f.ShortPx / Mathf.Max(f.LongPx, 1));
        return form switch
        {
            Form.Hip => f.From.Lerp(f.To, far).Lerp(f.To.Lerp(f.From, far), t),
            Form.Pyramid => f.Centre,
            Form.Mansard => f.At(Mathf.Lerp(0.3f, 0.7f, t), 0.5f),
            Form.Valley => f.At(t, 0.25f),
            Form.Lean => f.At(t, 0.82f),
            _ => f.From.Lerp(f.To, t),
        };
    }

    public static IEnumerable<(Vector2 At, Vector2 Along)> Stepped(Vector2[] px, float every)
    {
        float carried = every / 2;
        for (int i = 0; i + 1 < px.Length; i++)
        {
            var leg = px[i + 1] - px[i];
            float length = leg.Length();
            if (length < 0.001f) continue;
            for (; carried < length; carried += every) yield return (px[i] + leg * (carried / length), leg / length);
            carried -= length;
        }
    }

    public static Vector2[] Hull(Vector2 at, Vector2 bow, Vector2 beam) =>
    [
        at + bow, at + bow * 0.62f + beam * 0.78f, at + bow * 0.1f + beam, at - bow * 0.7f + beam * 0.86f, at - bow + beam * 0.5f,
        at - bow - beam * 0.5f, at - bow * 0.7f - beam * 0.86f, at + bow * 0.1f - beam, at + bow * 0.62f - beam * 0.78f,
    ];
}

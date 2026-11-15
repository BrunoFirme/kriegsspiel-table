using System.Collections.Generic;
using Godot;

namespace KTable.App;

readonly record struct RaisedFrame(Vector3 A0, Vector3 A1, Vector3 A2, Vector3 A3, Vector3 FrontA, Vector3 FrontB)
{
    public float Long => (A1 - A0).Length();
    public float Short => (A3 - A0).Length();
    public Vector3 Along => (A1 - A0) / Mathf.Max(Long, 1e-6f);
    public Vector3 Across => (A3 - A0) / Mathf.Max(Short, 1e-6f);
    public Vector3 Centre => (A0 + A2) / 2;
    public Vector3 From => (A0 + A3) / 2;
    public Vector3 To => (A1 + A2) / 2;
    public Vector3[] Ring => [A0, A1, A2, A3];

    public Vector3 At(float along, float across) => A0 + (A1 - A0) * along + (A3 - A0) * across;

    public RaisedFrame Part(float t0, float t1, float s0, float s1) => Of([At(t0, s0), At(t1, s0), At(t1, s1), At(t0, s1)]);

    public RaisedFrame Grown(float units) =>
        new(A0 - Along * units - Across * units, A1 + Along * units - Across * units, A2 + Along * units + Across * units, A3 - Along * units + Across * units, FrontA, FrontB);

    public static RaisedFrame Of(Vector3[] p)
    {
        bool longFirst = (p[1] - p[0]).Length() >= (p[2] - p[1]).Length();
        Vector3[] a = longFirst ? p : [p[1], p[2], p[3], p[0]];
        return new RaisedFrame(a[0], a[1], a[2], a[3], p[0], p[1]);
    }

    public static Vector3[][] Wings(Vector3[] p)
    {
        if ((p[2] - p[1]).Length() > (p[5] - p[0]).Length())
        {
            var corner = p[1] + p[5] - p[0];
            return [[p[0], p[1], corner, p[5]], [p[4], corner, p[2], p[3]]];
        }
        var inner = p[0] + p[2] - p[1];
        return [[p[0], p[1], p[2], inner], [inner, p[3], p[4], p[5]]];
    }

    public static IEnumerable<(Vector3 A, Vector3 B)> Sides(Vector3[] ring)
    {
        for (int i = 0; i < ring.Length; i++) yield return (ring[i], ring[(i + 1) % ring.Length]);
    }

    public static Vector3 Middle(Vector3[] ring)
    {
        var sum = Vector3.Zero;
        foreach (var p in ring) sum += p;
        return sum / Mathf.Max(1, ring.Length);
    }
}

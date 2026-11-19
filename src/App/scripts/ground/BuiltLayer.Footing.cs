using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float FootM = 1.3f, FootBend = 0.3f, FootDark = 0.42f, FootMid = 0.2f, PropFootM = 0.8f, TreeFootM = 2.2f, TreeDark = 0.26f, LeastMitre = 0.35f, LeastFootPx = 1;
    const int DiscSides = 16;
    static readonly Color Shade = new(0.03f, 0.035f, 0.03f, 0);

    bool Footed => _raised.Footed && _raised.Bare(_m);

    static Color Dark(float a) => Shade with { A = a };

    void BlockFooting(Vector2[] px)
    {
        float reach = FootM * _m;
        if (px.Length < 3 || reach < LeastFootPx) return;
        var mid = Outset(px, reach * FootBend);
        var outer = Outset(px, reach);
        Bands(px, mid, Dark(FootDark), Dark(FootMid), true);
        Bands(mid, outer, Dark(FootMid), Dark(0), true);
        if (Mathf.Abs(BuiltShapes.AreaOf(px)) > LeastFootPx) DrawColoredPolygon(px, Dark(FootDark));
    }

    void LineFooting(BuiltPainter.Shape s, Vector2[] px)
    {
        string style = MapStyles.ShaderOrder[Mathf.Clamp(_style, 0, MapStyles.ShaderOrder.Count - 1)];
        if (px.Length < 2 || RaisedLines.HighM(s, style) <= 0) return;
        float half = RaisedLines.WideM(s, style) * 0.5f * _m, reach = FootM * _m;
        if (reach < LeastFootPx) return;
        for (int i = 0; i + 1 < px.Length; i++)
        {
            var along = px[i + 1] - px[i];
            if (along.LengthSquared() < 1e-6f) continue;
            var side = along.Normalized().Orthogonal();
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector2 Off(Vector2 p, float by) => p + side * sign * by;
                Quad(Off(px[i], half), Off(px[i + 1], half), Off(px[i + 1], half + reach * FootBend), Off(px[i], half + reach * FootBend), Dark(FootDark), Dark(FootMid));
                Quad(Off(px[i], half + reach * FootBend), Off(px[i + 1], half + reach * FootBend), Off(px[i + 1], half + reach), Off(px[i], half + reach), Dark(FootMid), Dark(0));
            }
        }
    }

    void SignFooting(BuiltPainter.Shape s, Vector2 at)
    {
        bool tree = s.Class == "tree";
        float reach = (tree ? TreeFootM : PropFootM) * _m, dark = tree ? TreeDark : FootDark;
        if (reach < LeastFootPx) return;
        var inner = new Vector2[DiscSides];
        var mid = new Vector2[DiscSides];
        var outer = new Vector2[DiscSides];
        for (int i = 0; i < DiscSides; i++)
        {
            var toward = Vector2.Right.Rotated(Mathf.Tau * i / DiscSides);
            inner[i] = at + toward * reach * FootBend * 0.5f;
            mid[i] = at + toward * reach * FootBend * 1.5f;
            outer[i] = at + toward * reach;
        }
        DrawColoredPolygon(inner, Dark(dark));
        Bands(inner, mid, Dark(dark), Dark(dark * FootMid / FootDark), true);
        Bands(mid, outer, Dark(dark * FootMid / FootDark), Dark(0), true);
    }

    void Bands(Vector2[] from, Vector2[] to, Color near, Color far, bool closed)
    {
        int n = closed ? from.Length : from.Length - 1;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % from.Length;
            Quad(from[i], from[j], to[j], to[i], near, far);
        }
    }

    void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color near, Color far) => Meshed([0, 1, 2, 0, 2, 3], [a, b, c, d], [near, near, far, far], new Vector2[4]);

    static Vector2[] Outset(Vector2[] ring, float by)
    {
        int n = ring.Length;
        float twice = 0;
        for (int i = 0; i < n; i++) twice += ring[i].Cross(ring[(i + 1) % n]);
        float outward = twice >= 0 ? 1 : -1;
        var normals = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            var e = ring[(i + 1) % n] - ring[i];
            normals[i] = e.LengthSquared() < 1e-9f ? Vector2.Zero : new Vector2(e.Y, -e.X).Normalized() * outward;
        }
        var made = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 before = normals[(i + n - 1) % n], after = normals[i];
            var bisector = (before + after).Normalized();
            float mitre = Mathf.Max(bisector.Dot(after), LeastMitre);
            made[i] = ring[i] + (bisector == Vector2.Zero ? after : bisector) * by / mitre;
        }
        return made;
    }
}

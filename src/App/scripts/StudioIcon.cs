using Godot;

namespace KTable.App;

public partial class StudioIcon : ToolIcon
{
    float _k, _w;
    Vector2 _mid;
    Color _c;

    public override void _Draw()
    {
        _k = Mathf.Clamp(Mathf.Min(Size.X, Size.Y) / 30f, 0.5f, 2.5f);
        _mid = Size / 2;
        _c = Color;
        _w = 1.6f * _k;
        if (!DrawToolGlyph() && !DrawLayerGlyph()) base._Draw();
    }

    Vector2 P(float x, float y) => _mid + new Vector2(x - 10, y - 10) * _k;
    void L(float x0, float y0, float x1, float y1, float width = -1) => DrawLine(P(x0, y0), P(x1, y1), _c, width < 0 ? _w : width * _k, true);

    void Poly(bool close, params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2 + (close ? 1 : 0)];
        for (int i = 0; i < xy.Length; i += 2) pts[i / 2] = P(xy[i], xy[i + 1]);
        if (close) pts[^1] = pts[0];
        DrawPolyline(pts, _c, _w, true);
    }

    void Fill(params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2];
        for (int i = 0; i < xy.Length; i += 2) pts[i / 2] = P(xy[i], xy[i + 1]);
        DrawColoredPolygon(pts, _c);
    }

    bool DrawToolGlyph()
    {
        float k = _k;
        var c = _c;
        switch (Kind)
        {
            case "move":
                L(10, 2.5f, 10, 17.5f);
                L(2.5f, 10, 17.5f, 10);
                Fill(10, 1.5f, 7.6f, 4.6f, 12.4f, 4.6f);
                Fill(10, 18.5f, 7.6f, 15.4f, 12.4f, 15.4f);
                Fill(1.5f, 10, 4.6f, 7.6f, 4.6f, 12.4f);
                Fill(18.5f, 10, 15.4f, 7.6f, 15.4f, 12.4f);
                break;
            case "brush":
                L(17.5f, 2.5f, 10.5f, 9.5f, 2.2f);
                Fill(10.8f, 7.6f, 12.4f, 9.2f, 9.6f, 12.0f, 8.0f, 10.4f);
                Fill(8.0f, 10.4f, 9.6f, 12.0f, 7.2f, 15.6f, 3.4f, 17.2f, 4.6f, 13.0f);
                DrawArc(P(8.5f, 19.5f), 5f * k, Mathf.DegToRad(200), Mathf.DegToRad(280), 10, c, 1.2f * k, true);
                break;
            case "text-tool":
                L(4, 4, 16, 4, 2);
                L(10, 4, 10, 17, 2);
                L(7.5f, 17, 12.5f, 17, 1.6f);
                L(4, 4, 4, 6.5f, 1.6f);
                L(16, 4, 16, 6.5f, 1.6f);
                break;
            case "fill":
                Poly(true, 4.5f, 8.5f, 10.5f, 2.5f, 16, 8, 10, 14);
                L(6.5f, 6.5f, 12.5f, 12.5f, 1.1f);
                Fill(16, 8, 18, 12.5f, 17.2f, 15, 15.6f, 15.2f, 15, 12.5f);
                DrawCircle(P(16.4f, 14.2f), 1.4f * k, c, true, -1, true);
                break;
            case "pick":
                Fill(13.4f, 2.6f, 17.4f, 6.6f, 15.6f, 8.4f, 11.6f, 4.4f);
                L(12, 6, 14, 8, 2.4f);
                Poly(false, 12.6f, 7.6f, 4.6f, 15.6f, 3.4f, 16.6f);
                Poly(false, 12.4f, 7.4f, 4.4f, 15.4f);
                DrawCircle(P(3.4f, 16.6f), 1.2f * k, c, true, -1, true);
                break;
            case "crop":
                Poly(false, 6, 2.5f, 6, 14, 17.5f, 14);
                Poly(false, 2.5f, 6, 14, 6, 14, 17.5f);
                break;
            case "outline":
                var pts = new[] { (4f, 4f), (16f, 4f), (16f, 11f), (10f, 17f), (4f, 11f) };
                for (int i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    L(a.Item1, a.Item2, b.Item1, b.Item2, 1.3f);
                }
                foreach (var (x, y) in pts) DrawRect(new Rect2(P(x - 1.5f, y - 1.5f), new Vector2(3, 3) * k), c);
                break;
            case "place":
                DrawDashedLine(P(2.5f, 5), P(17.5f, 5), c, 1.2f * k, 2.2f * k, true, true);
                DrawDashedLine(P(17.5f, 5), P(17.5f, 15), c, 1.2f * k, 2.2f * k, true, true);
                DrawDashedLine(P(17.5f, 15), P(2.5f, 15), c, 1.2f * k, 2.2f * k, true, true);
                DrawDashedLine(P(2.5f, 15), P(2.5f, 5), c, 1.2f * k, 2.2f * k, true, true);
                L(5.5f, 8.6f, 14.5f, 8.6f, 1.8f);
                L(7, 11.6f, 13, 11.6f, 1.2f);
                break;
            case "hand":
                DrawRect(new Rect2(P(5.5f, 9.5f), new Vector2(9.5f, 7.5f) * k), c);
                foreach (var (x, top) in new[] { (6.5f, 4.5f), (9f, 3f), (11.5f, 3.5f), (14f, 5.5f) }) L(x, top, x, 10.5f, 2.1f);
                L(5.6f, 13, 2.8f, 9.6f, 2.1f);
                break;
            case "grid":
                Poly(true, 3, 3, 17, 3, 17, 17, 3, 17);
                L(7.7f, 3, 7.7f, 17, 1.1f);
                L(12.3f, 3, 12.3f, 17, 1.1f);
                L(3, 7.7f, 17, 7.7f, 1.1f);
                L(3, 12.3f, 17, 12.3f, 1.1f);
                break;
            case "snap":
                DrawArc(P(10, 9), 5.5f * k, 0, Mathf.Pi, 20, c, 3.2f * k, true);
                L(4.5f, 9, 4.5f, 4, 3.2f);
                L(15.5f, 9, 15.5f, 4, 3.2f);
                DrawRect(new Rect2(P(3, 2.3f), new Vector2(3, 1.8f) * k), new Color(0, 0, 0, 0.55f));
                DrawRect(new Rect2(P(14, 2.3f), new Vector2(3, 1.8f) * k), new Color(0, 0, 0, 0.55f));
                break;
            case "sym":
                DrawDashedLine(P(10, 1.5f), P(10, 18.5f), c, 1.2f * k, 2 * k, true, true);
                Fill(8.5f, 5, 8.5f, 15, 2.5f, 15);
                Poly(true, 11.5f, 5, 11.5f, 15, 17.5f, 15);
                break;
            case "rulers":
                Poly(false, 2.5f, 17.5f, 2.5f, 2.5f, 17.5f, 2.5f);
                for (int i = 0; i < 4; i++) { L(2.5f, 5.5f + i * 3.5f, 5, 5.5f + i * 3.5f, 1.1f); L(5.5f + i * 3.5f, 2.5f, 5.5f + i * 3.5f, 5, 1.1f); }
                break;
            default: return false;
        }
        return true;
    }

    bool DrawLayerGlyph()
    {
        float k = _k;
        var c = _c;
        switch (Kind)
        {
            case "image":
                Poly(true, 2.5f, 4, 17.5f, 4, 17.5f, 16, 2.5f, 16);
                Fill(3.5f, 15, 8.5f, 8.5f, 12, 12.5f, 13.8f, 10.5f, 16.5f, 15);
                DrawCircle(P(13.5f, 7.5f), 1.6f * k, c, true, -1, true);
                break;
            case "paint-layer":
                DrawArc(P(7, 12), 4 * k, Mathf.Pi, Mathf.Tau, 12, c, 1.8f * k, true);
                DrawArc(P(13, 12), 2 * k, 0, Mathf.Pi, 10, c, 1.8f * k, true);
                L(3, 12, 3, 15, 1.8f);
                L(15, 12, 17, 5, 1.8f);
                break;
            case "text-layer":
                L(5, 5, 15, 5, 1.8f);
                L(10, 5, 10, 16, 1.8f);
                break;
            case "block-layer":
                Poly(true, 2.5f, 6, 17.5f, 6, 17.5f, 14, 2.5f, 14);
                L(2.5f, 6, 17.5f, 14, 1.2f);
                L(17.5f, 6, 2.5f, 14, 1.2f);
                break;
            case "lock":
            case "unlock":
                DrawArc(P(10, 8.5f), 3.6f * k, Kind == "lock" ? Mathf.Pi : Mathf.Pi * 1.15f, Kind == "lock" ? Mathf.Tau : Mathf.Tau * 1.02f, 16, c, 1.6f * k, true);
                if (Kind == "lock") { L(6.4f, 8.5f, 6.4f, 10, 1.6f); L(13.6f, 8.5f, 13.6f, 10, 1.6f); }
                DrawRect(new Rect2(P(5, 10), new Vector2(10, 7.5f) * k), Kind == "lock" ? c : new Color(c, 0.5f));
                break;
            case "orbit":
                DrawArc(P(10, 10), 7 * k, Mathf.DegToRad(200), Mathf.DegToRad(500), 36, c, 1.5f * k, true);
                Fill(15.2f, 3.6f, 17.6f, 7.2f, 13.4f, 7.6f);
                DrawCircle(P(10, 10), 2 * k, c, true, -1, true);
                break;
            case "part":
                DrawEllipseArc(P(10, 5), 6 * k, 2.2f * k, 0, Mathf.Tau, 24, c, 1.4f * k, true);
                L(4, 5, 4, 15, 1.4f);
                L(16, 5, 16, 15, 1.4f);
                DrawEllipseArc(P(10, 15), 6 * k, 2.2f * k, 0, Mathf.Pi, 14, c, 1.4f * k, true);
                break;
            case "flip-h":
                DrawDashedLine(P(10, 2), P(10, 18), c, 1 * k, 2 * k, true, true);
                Fill(8.5f, 5, 8.5f, 15, 2.5f, 10);
                Poly(true, 11.5f, 5, 11.5f, 15, 17.5f, 10);
                break;
            case "flip-v":
                DrawDashedLine(P(2, 10), P(18, 10), c, 1 * k, 2 * k, true, true);
                Fill(5, 8.5f, 15, 8.5f, 10, 2.5f);
                Poly(true, 5, 11.5f, 15, 11.5f, 10, 17.5f);
                break;
            case "up":
                Fill(10, 4, 16, 12, 4, 12);
                L(10, 11, 10, 17, 2);
                break;
            case "down":
                Fill(10, 16, 16, 8, 4, 8);
                L(10, 9, 10, 3, 2);
                break;
            case "plus":
                L(10, 3.5f, 10, 16.5f, 2);
                L(3.5f, 10, 16.5f, 10, 2);
                break;
            case "trash":
                L(4, 5.5f, 16, 5.5f, 1.6f);
                L(8, 5.5f, 8.5f, 3.2f, 1.4f);
                L(12, 5.5f, 11.5f, 3.2f, 1.4f);
                L(8.5f, 3.2f, 11.5f, 3.2f, 1.4f);
                Poly(false, 5.5f, 5.5f, 6.6f, 17, 13.4f, 17, 14.5f, 5.5f);
                L(8.6f, 8.5f, 8.8f, 14.5f, 1.1f);
                L(11.4f, 8.5f, 11.2f, 14.5f, 1.1f);
                break;
            case "dup":
                Poly(true, 3, 7, 13, 7, 13, 17, 3, 17);
                Poly(false, 7, 7, 7, 3, 17, 3, 17, 13, 13, 13);
                break;
            case "merge":
                Fill(10, 17, 15, 11, 5, 11);
                L(5, 4, 10, 9, 1.6f);
                L(15, 4, 10, 9, 1.6f);
                L(10, 9, 10, 12, 1.6f);
                break;
            case "wand":
                L(3, 17, 13, 7, 2.2f);
                foreach (var (x, y) in new[] { (15.5f, 4.5f), (12.5f, 2.5f), (17.5f, 7.5f) }) { L(x - 1.5f, y, x + 1.5f, y, 1.1f); L(x, y - 1.5f, x, y + 1.5f, 1.1f); }
                break;
            case "play":
                Fill(6, 4, 16, 10, 6, 16);
                break;
            default: return false;
        }
        return true;
    }
}

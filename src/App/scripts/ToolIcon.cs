using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

public partial class ToolIcon : Control
{
    public string Kind = "";
    public Color Color = new(0.9f, 0.9f, 0.88f);
    public bool Locked
    {
        get => _locked;
        set { if (_locked == value) return; _locked = value; QueueRedraw(); }
    }
    bool _locked;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (GetParent() is Control p) p.Resized += QueueRedraw;
        SetProcess(Kind.StartsWith("text:") || Kind == "heart");
        _wasLit = Lit;
    }

    bool Lit => GetParent() is not BaseButton b || !b.ToggleMode || b.ButtonPressed;
    bool _wasLit;

    public override void _Process(double delta)
    {
        if (_wasLit == Lit) return;
        _wasLit = Lit;
        QueueRedraw();
    }

    float _k, _w;
    Vector2 _mid;
    Color _c;

    Vector2 P(float x, float y) => _mid + new Vector2(x - 10, y - 10) * _k;
    void Line(float x0, float y0, float x1, float y1, float width = -1) => DrawLine(P(x0, y0), P(x1, y1), _c, width < 0 ? _w : width * _k, antialiased: true);
    void Poly(params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2 + 1];
        for (int i = 0; i < xy.Length; i += 2) pts[i / 2] = P(xy[i], xy[i + 1]);
        pts[^1] = pts[0];
        DrawPolyline(pts, _c, _w, antialiased: true);
    }

    void Eye()
    {
        const float Corner = 1.8f, Open = 5.4f, Iris = 2.9f;
        const int Steps = 20;
        float half = 10 - Corner, round = (half * half + Open * Open) / (2 * Open);
        foreach (var side in new[] { -1f, 1f })
        {
            var pts = new Vector2[Steps + 1];
            for (int i = 0; i <= Steps; i++)
            {
                float x = -half + 2 * half * i / Steps;
                float bulge = Mathf.Sqrt(Mathf.Max(0, round * round - x * x)) - (round - Open);
                pts[i] = P(10 + x, 10 + side * bulge);
            }
            DrawPolyline(pts, _c, _w, antialiased: true);
        }
        DrawArc(P(10, 10), Iris * _k, 0, Mathf.Tau, 28, _c, _w, antialiased: true);
    }

    public override void _Draw()
    {
        _k = Mathf.Clamp(Mathf.Min(Size.X, Size.Y) / 30f, 0.6f, 2.5f);
        _mid = Size / 2;
        _c = Color;
        _w = 1.6f * _k;
        if (Kind.StartsWith("text:")) { DrawWord(Kind[5..]); return; }
        if (_locked) _c = new Color(_c, 0.45f);
        DrawTool();
        DrawView();
        DrawPanel();
        if (_locked) DrawPadlock();
    }

    void DrawWord(string word)
    {
        var font = GetThemeDefaultFont();
        int fs = Mathf.RoundToInt(13 * _k);
        var sz = font.GetStringSize(word, HorizontalAlignment.Left, -1, fs);
        var at = new Vector2(_mid.X - sz.X / 2, _mid.Y - sz.Y / 2 + font.GetAscent(fs));
        DrawString(font, at, word, HorizontalAlignment.Left, -1, fs, Lit ? _c : new Color(_c, 0.4f));
    }

    void DrawPadlock()
    {
        var lk = Color with { A = 1 };
        DrawCircle(P(15.4f, 15.2f), 5.2f * _k, new Color(0.11f, 0.11f, 0.12f, 0.92f));
        DrawArc(P(15.4f, 14.1f), 2.0f * _k, Mathf.Pi, Mathf.Tau, 14, lk, 1.2f * _k, antialiased: true);
        DrawRect(new Rect2(P(12.8f, 14.1f), new Vector2(5.2f, 4.2f) * _k), lk);
    }

    void DrawTool()
    {
        switch (Kind)
        {
            case "select":
                DrawColoredPolygon(new[] { P(5, 2), P(5, 16), P(8.5f, 12.5f), P(11, 18), P(13.2f, 17), P(10.8f, 12), P(15.5f, 12) }, _c);
                break;
            case "formation":
                foreach (var (x, y) in new[] { (10f, 4.6f), (7.4f, 9.6f), (12.6f, 9.6f), (4.8f, 14.6f), (10f, 14.6f), (15.2f, 14.6f) })
                    DrawRect(new Rect2(P(x - 2.1f, y - 1.4f), new Vector2(4.2f, 2.8f) * _k), _c);
                break;
            case "ruler":
                Poly(2.5f, 14, 14, 2.5f, 17.5f, 6, 6, 17.5f);
                foreach (var (x, y) in new[] { (6.5f, 10f), (9f, 7.5f), (11.5f, 5f) }) DrawLine(P(x, y), P(x + 2.4f, y + 2.4f), _c, 1.3f * _k, antialiased: true);
                break;
            case "draw":
                DrawColoredPolygon(new[] { P(13.3f, 2.3f), P(16.8f, 5.8f), P(8.1f, 14.5f), P(4.6f, 11.0f) }, _c);
                DrawColoredPolygon(new[] { P(4.6f, 11.0f), P(8.1f, 14.5f), P(2.9f, 16.2f) }, _c);
                DrawColoredPolygon(new[] { P(2.9f, 16.2f), P(3.9f, 13.3f), P(5.8f, 15.2f) }, new Color(0, 0, 0, 0.6f));
                DrawLine(P(11.4f, 4.2f), P(14.9f, 7.7f), new Color(0, 0, 0, 0.45f), 1.2f * _k, antialiased: true);
                DrawLine(P(12.6f, 3.0f), P(16.1f, 6.5f), new Color(0, 0, 0, 0.3f), 1.0f * _k, antialiased: true);
                const float RuledLineY = 18.1f;
                Line(2.4f, RuledLineY, 17.6f, RuledLineY, 1.3f);
                break;
            case "shapes":
                DrawRect(new Rect2(P(2.4f, 7.4f), new Vector2(9.2f, 9.2f) * _k), _c, false, 1.5f * _k);
                DrawArc(P(13.2f, 7.2f), 4.8f * _k, 0, Mathf.Tau, 28, _c, 1.5f * _k, antialiased: true);
                break;
            case "erase":
                DrawColoredPolygon(new[] { P(10.8f, 2.6f), P(16.8f, 8.6f), P(9.6f, 15.8f), P(3.6f, 9.8f) }, _c);
                DrawLine(P(7.2f, 6.2f), P(13.2f, 12.2f), new Color(0, 0, 0, 0.5f), 1.3f * _k, antialiased: true);
                Line(3.5f, 17.4f, 12.5f, 17.4f, 1.5f);
                break;
            case "range":
                Compasses();
                break;
            case "sighting":
                DrawArc(P(10, 10), 7.6f * _k, 0, Mathf.Tau, 40, _c, 1.7f * _k, antialiased: true);
                DrawRect(new Rect2(P(8.9f, 9.2f), new Vector2(2.2f, 1.6f) * _k), _c);
                break;
            case "calibrate":
                ScaleBar();
                break;
            case "zones":
                for (int i = 0; i < 4; i++)
                {
                    float t0 = i * 3.4f;
                    DrawLine(P(4 + t0, 4.5f), P(4 + t0 + 2, 4.5f), _c, _w, antialiased: true);
                    DrawLine(P(4 + t0, 15.5f), P(4 + t0 + 2, 15.5f), _c, _w, antialiased: true);
                    DrawLine(P(4.5f, 4 + t0), P(4.5f, 4 + t0 + 2), _c, _w, antialiased: true);
                    DrawLine(P(15.5f, 4 + t0), P(15.5f, 4 + t0 + 2), _c, _w, antialiased: true);
                }
                break;
            case "capture":
                Poly(2.5f, 6, 7, 6, 8.5f, 4, 11.5f, 4, 13, 6, 17.5f, 6, 17.5f, 16, 2.5f, 16);
                DrawArc(P(10, 11), 3.2f * _k, 0, Mathf.Tau, 24, _c, _w, antialiased: true);
                break;
            case "dice":
                Poly(3.5f, 3.5f, 16.5f, 3.5f, 16.5f, 16.5f, 3.5f, 16.5f);
                foreach (var (px, py) in new[] { (7f, 7f), (13f, 7f), (10f, 10f), (7f, 13f), (13f, 13f) })
                    DrawCircle(P(px, py), 1.4f * _k, _c);
                break;
        }
    }

    void DrawView()
    {
        switch (Kind)
        {
            case "2d":
                Poly(3, 5, 17, 5, 17, 15, 3, 15);
                Line(3, 10, 17, 10, 1.2f); Line(10, 5, 10, 15, 1.2f);
                break;
            case "3d":
                Poly(3, 12, 10, 15.5f, 17, 12, 10, 8.5f);
                Line(3, 12, 3, 7); Line(17, 12, 17, 7); Line(10, 15.5f, 10, 10.5f);
                Poly(3, 7, 10, 10.5f, 17, 7, 10, 3.5f);
                break;
            case "undo":
            case "redo":
            {
                bool back = Kind == "undo";
                float s2 = back ? 1 : -1;
                Vector2 Q(float x, float y) => P(10 + s2 * (x - 10), y);
                DrawArc(P(10, 11.4f), 5.6f * _k, Mathf.Pi, Mathf.Tau, 28, _c, 1.8f * _k, antialiased: true);
                DrawColoredPolygon(new[] { Q(4.4f, 16.2f), Q(0.9f, 10.6f), Q(7.9f, 10.6f) }, _c);
                break;
            }
            case "frame":
                foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
                {
                    var cx = 10 - sx * 7f; var cy = 10 - sy * 7f;
                    DrawLine(P(cx, cy), P(cx + sx * 4, cy), _c, _w, antialiased: true);
                    DrawLine(P(cx, cy), P(cx, cy + sy * 4), _c, _w, antialiased: true);
                }
                DrawRect(new Rect2(P(8.5f, 8.5f), new Vector2(3, 3) * _k), _c);
                break;
            case "eye":
                Eye();
                break;
            case "eye-off":
                Eye();
                Line(4f, 16f, 16f, 4f, 1.8f);
                break;
            case "shape-line":
                Line(3.5f, 16.5f, 16.5f, 3.5f);
                break;
            case "shape-rect":
                Poly(3.5f, 5.5f, 16.5f, 5.5f, 16.5f, 14.5f, 3.5f, 14.5f);
                break;
            case "shape-ellipse":
                DrawArc(P(10, 10), 7f * _k, 0, Mathf.Tau, 36, _c, _w, antialiased: true);
                break;
            case "shape-arrow":
                Line(3.5f, 16.5f, 15.5f, 4.5f);
                Line(15.5f, 4.5f, 10.5f, 5.5f);
                Line(15.5f, 4.5f, 14.5f, 9.5f);
                break;
        }
    }

    void DrawPanel()
    {
        switch (Kind)
        {
            case "pieces":
                Poly(2.5f, 6, 17.5f, 6, 17.5f, 14, 2.5f, 14);
                Line(2.5f, 6, 17.5f, 14, 1.3f);
                Line(17.5f, 6, 2.5f, 14, 1.3f);
                break;
            case "map":
                Poly(2.5f, 5.5f, 7.5f, 3.5f, 12.5f, 6, 17.5f, 4, 17.5f, 14.5f, 12.5f, 16.5f, 7.5f, 14, 2.5f, 16);
                Line(7.5f, 3.5f, 7.5f, 14, 1.2f);
                Line(12.5f, 6, 12.5f, 16.5f, 1.2f);
                break;
            case "relief":
                DrawColoredPolygon(new[] { P(1.8f, 16.4f), P(7.2f, 5.6f), P(10.4f, 10.6f), P(13f, 7.4f), P(18.2f, 16.4f) }, _c);
                DrawColoredPolygon(new[] { P(5.9f, 8.2f), P(7.2f, 5.6f), P(8.6f, 7.8f), P(7.4f, 7.2f) }, new Color(0, 0, 0, 0.45f));
                Line(1.8f, 18.1f, 18.2f, 18.1f, 1.3f);
                break;
            case "stack":
                DrawPolyline(new[] { P(10, 2.2f), P(17.4f, 5.2f), P(10, 8.2f), P(2.6f, 5.2f), P(10, 2.2f) }, _c, 1.2f * _k, antialiased: true);
                foreach (float dy in new[] { 3.4f, 6.8f })
                    DrawPolyline(new[] { P(2.6f, 5.2f + dy), P(10, 8.2f + dy), P(17.4f, 5.2f + dy) }, _c, 1.2f * _k, antialiased: true);
                break;
            case "people":
                People();
                break;
            case "folder":
                DrawPolyline(new[] { P(2.5f, 5f), P(7.5f, 5f), P(9f, 6.8f), P(17.5f, 6.8f), P(17.5f, 15.5f), P(2.5f, 15.5f), P(2.5f, 5f) }, _c, 1.3f * _k, antialiased: true);
                break;
            case "log":
                foreach (var (y, x1) in new[] { (5.5f, 17f), (8.5f, 17f), (11.5f, 15f), (14.5f, 11f) })
                    DrawLine(P(3, y), P(x1, y), _c, 1.5f * _k, antialiased: true);
                break;
            case "menu":
                Line(4, 6, 16, 6); Line(4, 10, 16, 10); Line(4, 14, 16, 14);
                break;
            case "gizmo":
                DrawArc(P(10, 10.5f), 6.2f * _k, 0, Mathf.Tau, 48, _c, _w, antialiased: true);
                DrawCircle(P(10, 4.3f), 2.3f * _k, _c);
                DrawRect(new Rect2(P(14.4f, 8.7f), new Vector2(3.6f, 3.6f) * _k), _c);
                break;
            case "more":
                DrawCircle(P(4.5f, 10), 1.6f * _k, _c); DrawCircle(P(10, 10), 1.6f * _k, _c); DrawCircle(P(15.5f, 10), 1.6f * _k, _c);
                break;
            case "compass":
            {
                var o = P(10, 10);
                DrawArc(o, 8.6f * _k, 0, Mathf.Tau, 64, _c, _w * 0.75f, antialiased: true);
                var north = new[] { P(10, 4f), P(12.3f, 10), P(7.7f, 10) };
                DrawColoredPolygon(north, _c);
                DrawPolyline(north.Append(north[0]).ToArray(), _c, _w * 0.4f, antialiased: true);
                var south = new[] { P(7.7f, 10), P(10, 16f), P(12.3f, 10) };
                DrawColoredPolygon(south, new Color(_c, 0.35f));
                DrawPolyline(south.Append(south[0]).ToArray(), new Color(_c, 0.8f), _w * 0.4f, antialiased: true);
                break;
            }
            case "heart":
                Heart();
                break;
            case "sliders":
                foreach (var (y, knob) in new[] { (4.5f, 13f), (10f, 7f), (15.5f, 11.5f) })
                {
                    Line(3, y, 17, y);
                    DrawCircle(P(knob, y), 2.4f * _k, _c);
                }
                break;
        }
    }

    void Compasses()
    {
        DrawCircle(P(10, 3.6f), 2.0f * _k, _c, true, -1, antialiased: true);
        Line(10, 0.4f, 10, 2, 1.5f);
        Line(9, 5.2f, 3.6f, 17.8f, 1.8f);
        Line(11, 5.2f, 15.2f, 15, 1.8f);
        var lead = new[] { P(14.4f, 14.8f), P(16.2f, 14.2f), P(16.9f, 18.2f) };
        DrawColoredPolygon(lead, _c);
        DrawPolyline(new[] { lead[0], lead[1], lead[2], lead[0] }, _c, 0.6f * _k, antialiased: true);
        DrawArc(P(10, 3.6f), 9.6f * _k, Mathf.DegToRad(52), Mathf.DegToRad(128), 24, _c, 1.3f * _k, antialiased: true);
    }

    void ScaleBar()
    {
        DrawPolyline(new[] { P(2.4f, 12), P(17.6f, 12), P(17.6f, 16.6f), P(2.4f, 16.6f), P(2.4f, 12) }, _c, 1.0f * _k, antialiased: true);
        DrawRect(new Rect2(P(2.4f, 12), new Vector2(3.8f, 4.6f) * _k), _c);
        DrawRect(new Rect2(P(10, 12), new Vector2(3.8f, 4.6f) * _k), _c);
        Line(5.6f, 6.2f, 14.4f, 6.2f, 1.2f);
        foreach (var head in new[] { new[] { P(2.4f, 6.2f), P(6.2f, 4.6f), P(6.2f, 7.8f) }, new[] { P(17.6f, 6.2f), P(13.8f, 4.6f), P(13.8f, 7.8f) } })
        {
            DrawColoredPolygon(head, _c);
            DrawPolyline(new[] { head[0], head[1], head[2], head[0] }, _c, 0.6f * _k, antialiased: true);
        }
    }

    void People()
    {
        void Shoulders(float x0, float x1, float top, float r, float foot, bool near)
        {
            const int n = 8;
            var pts = new Vector2[(near ? n + 2 : 0) + n + 2];
            int at = 0;
            if (near)
            {
                pts[at++] = P(x0, foot);
                for (int i = 0; i <= n; i++) { float a = Mathf.Pi + i * Mathf.Pi / (2 * n); pts[at++] = P(x0 + r + r * Mathf.Cos(a), top + r + r * Mathf.Sin(a)); }
            }
            for (int i = 0; i <= n; i++) { float a = -Mathf.Pi / 2 + i * Mathf.Pi / (2 * n); pts[at++] = P(x1 - r + r * Mathf.Cos(a), top + r + r * Mathf.Sin(a)); }
            pts[at] = P(x1, foot);
            DrawPolyline(pts, _c, 1.5f * _k, antialiased: true);
        }
        DrawArc(P(7.6f, 6.4f), 3.1f * _k, 0, Mathf.Tau, 32, _c, 1.5f * _k, antialiased: true);
        Shoulders(1.8f, 13.4f, 12.5f, 3.6f, 17.6f, true);
        DrawArc(P(13.4f, 6.4f), 3.0f * _k, Mathf.DegToRad(-80), Mathf.DegToRad(80), 16, _c, 1.5f * _k, antialiased: true);
        Shoulders(15.0f, 18.2f, 12.6f, 3.2f, 17.6f, false);
    }

    void Heart()
    {
        var shape = new List<Vector2>();
        for (int i = 0; i < 48; i++)
        {
            float t = i / 48f * Mathf.Tau;
            float hx = 16 * Mathf.Pow(Mathf.Sin(t), 3), hy = 13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
            float y = -hy;
            if (y > 2) y = 2 + (y - 2) * 0.62f;
            shape.Add(new Vector2(hx * 1.08f, y));
        }
        for (int round = 0; round < 5; round++)
        {
            var next = new List<Vector2>(shape.Count * 2);
            for (int i = 0; i < shape.Count; i++)
            {
                var a = shape[i]; var b = shape[(i + 1) % shape.Count];
                next.Add(a.Lerp(b, 0.25f)); next.Add(a.Lerp(b, 0.75f));
            }
            shape = next;
        }
        var pts = shape.Select(v => P(10 + v.X * 0.47f, 9.4f + v.Y * 0.47f)).ToArray();
        bool on = Lit && GetParent() is BaseButton { ToggleMode: true };
        var red = new Color(0.87f, 0.32f, 0.34f);
        if (on) DrawColoredPolygon(pts, red);
        DrawPolyline(pts.Append(pts[0]).ToArray(), on ? red : _c, _w, antialiased: true);
    }
}

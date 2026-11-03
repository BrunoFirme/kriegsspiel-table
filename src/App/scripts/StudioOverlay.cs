using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

sealed class StudioOverlay
{
    readonly StudioCanvas _c;
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public StudioOverlay(StudioCanvas canvas) { _c = canvas; }

    static readonly Color Guide = new(0.35f, 0.85f, 0.95f, 0.85f);
    static readonly Color Gold = new(0.95f, 0.78f, 0.25f, 0.95f);

    public void Draw(Control o)
    {
        var font = _c.GetThemeDefaultFont();
        DrawPieceEdge(o);
        if (_c.P.Grid) DrawGrid(o);
        if (_c.P.Symmetry != "off") DrawSymmetry(o);
        DrawOutlineGuide(o);
        DrawPrints(o, font);
        if (_c.P.ShowWidgets) DrawWidgets(o, font);
        if (_c.Sel is { } s && _c.Tool is "move" or "crop" or "text") DrawSelection(o, s);
        if (_c.Gesture == StudioCanvas.Drag.DocCrop)
        {
            var r = new Rect2(_c.ToScreen(_c.DocCrop.Position), _c.DocCrop.Size * _c.Zoom).Abs();
            o.DrawRect(r, Gold, false, 2);
            o.DrawString(font, r.Position + new Vector2(2, -4), $"{Mathf.Abs(_c.DocCrop.Size.X):0} × {Mathf.Abs(_c.DocCrop.Size.Y):0} px", HorizontalAlignment.Left, -1, 12, Gold);
        }

        for (int i = 0; i + 3 < _c.Doc.Measures.Count; i += 4)
            DrawMeasure(o, font, new Vector2(_c.Doc.Measures[i], _c.Doc.Measures[i + 1]), new Vector2(_c.Doc.Measures[i + 2], _c.Doc.Measures[i + 3]));

        if (_c.Tool == "shape" && _c.P.ShapeKind == "poly" && _c.Poly.Count > 0)
        {
            var pts = _c.Poly.Select(_c.ToScreen).ToList();
            o.DrawPolyline(pts.Append(_c.ToScreen(_c.HoverDoc)).ToArray(), new Color(Guide, 0.8f), 1, true);
            o.DrawCircle(pts[0], 6, new Color(Guide, 0.9f), false, 1.5f, true);
        }

        if (_c.Gesture == StudioCanvas.Drag.Shape) DrawMeasure(o, font, _c.DownDoc, _c.Snap(_c.HoverDoc), faint: true);

        if (_c.HoverIn && _c.Tool is "brush" or "erase" && _c.Gesture != StudioCanvas.Drag.Pan)
        {
            float r = (_c.Tool == "erase" ? _c.P.Eraser : _c.P.Brush) / 2 * _c.Zoom;
            foreach (var t in _c.Sym())
            {
                var c = _c.ToScreen(t * _c.HoverDoc);
                o.DrawArc(c, Mathf.Max(1.5f, r), 0, Mathf.Tau, 40, new Color(0, 0, 0, 0.55f), 2.2f, true);
                o.DrawArc(c, Mathf.Max(1.5f, r), 0, Mathf.Tau, 40, new Color(1, 1, 1, 0.85f), 1, true);
            }
        }

        if (_c.P.Rulers) DrawRulers(o, font);
    }

    void DrawPieceEdge(Control o)
    {
        if (_c.Doc.Outline != null || _c.PieceShape == "alpha") return;
        var pts = ArtDraw.Outline(_c.PieceShape, _c.Doc.W, _c.Doc.H).Select(_c.ToScreen).ToList();
        pts.Add(pts[0]);
        for (int i = 0; i + 1 < pts.Count; i++) o.DrawDashedLine(pts[i], pts[i + 1], new Color(1, 1, 1, 0.35f), 1, 5, true, true);
    }

    void DrawGrid(Control o)
    {
        float step = _c.Doc.W / (float)Math.Max(1, _c.P.GridCells);
        int nx = (int)Mathf.Ceil(_c.Doc.W / step), ny = (int)Mathf.Ceil(_c.Doc.H / step);
        if (step * _c.Zoom < 3) return;
        for (int i = 0; i <= nx; i++)
        {
            bool major = _c.P.GridMajor > 0 && i % _c.P.GridMajor == 0;
            o.DrawLine(_c.ToScreen(new Vector2(i * step, 0)), _c.ToScreen(new Vector2(i * step, _c.Doc.H)), new Color(0.4f, 0.8f, 1f, major ? 0.42f : 0.18f), 1);
        }
        for (int i = 0; i <= ny; i++)
        {
            bool major = _c.P.GridMajor > 0 && i % _c.P.GridMajor == 0;
            o.DrawLine(_c.ToScreen(new Vector2(0, i * step)), _c.ToScreen(new Vector2(_c.Doc.W, i * step)), new Color(0.4f, 0.8f, 1f, major ? 0.42f : 0.18f), 1);
        }
    }

    void DrawSymmetry(Control o)
    {
        var dr = _c.DocRect;
        var c = _c.ToScreen(new Vector2(_c.Doc.W, _c.Doc.H) / 2);
        var col = new Color(1f, 0.45f, 0.75f, 0.8f);
        if (_c.P.Symmetry is "x" or "xy") o.DrawDashedLine(new Vector2(c.X, dr.Position.Y), new Vector2(c.X, dr.End.Y), col, 1.5f, 8, true, true);
        if (_c.P.Symmetry is "y" or "xy") o.DrawDashedLine(new Vector2(dr.Position.X, c.Y), new Vector2(dr.End.X, c.Y), col, 1.5f, 8, true, true);
        if (_c.P.Symmetry == "radial")
        {
            float rr = Mathf.Max(dr.Size.X, dr.Size.Y) * 0.75f;
            int n = Math.Clamp(_c.P.Radial, 2, 24);
            for (int i = 0; i < n; i++) o.DrawDashedLine(c, c + Vector2.FromAngle(-Mathf.Pi / 2 + Mathf.Tau * i / n) * rr, new Color(col, 0.55f), 1, 6, true, true);
        }
        o.DrawCircle(c, 4, col, true, -1, true);
    }

    void DrawOutlineGuide(Control o)
    {
        if (_c.Doc.Outline == null && _c.Tool != "outline") return;
        var pts = _c.OutlinePts().Select(_c.ToScreen).ToList();
        if (pts.Count >= 2) o.DrawPolyline(pts.Append(pts[0]).ToArray(), _c.Tool == "outline" ? Gold : new Color(Gold, 0.6f), _c.Tool == "outline" ? 2 : 1.4f, true);
        if (_c.Tool == "outline") foreach (var p in pts) { o.DrawCircle(p, 5, new Color(0, 0, 0, 0.7f), true, -1, true); o.DrawCircle(p, 3.5f, Gold, true, -1, true); }
    }

    void DrawPrints(Control o, Font font)
    {
        if (_c.Tool != "place") return;
        foreach (var p in _c.Prints.OrderBy(p => p.Key == _c.Chosen))
        {
            bool chosen = p.Key == _c.Chosen;
            var br = _c.BoxRect(p);
            var col = new Color(1, 0.9f, 0.2f, chosen ? 0.95f : 0.5f);
            o.DrawRect(br, col, false, chosen ? 2 : 1);
            if (chosen) foreach (var k in StudioCanvas.HandlePts(br)) Knob(o, k, col);
            o.DrawString(font, br.Position + new Vector2(2, -4), p.Label, HorizontalAlignment.Left, -1, 11, col);
        }
    }

    void DrawWidgets(Control o, Font font)
    {
        foreach (var w in _c.Widgets.Where(w => w.Face == _c.FaceSide))
        {
            var c = _c.ToScreen(_c.PieceToDoc(new Vector2((float)w.X, (float)w.Z)));
            var sz = new Vector2((float)w.W / _c.PieceUnits.X * _c.Doc.W, (float)w.H / _c.PieceUnits.Y * _c.Doc.H) * _c.Zoom;
            var col = new Color(0.55f, 1f, 0.6f, 0.85f);
            if (sz.X > 1 && sz.Y > 1) o.DrawRect(new Rect2(c - sz / 2, sz), col, false, 1);
            else o.DrawCircle(c, 3, col, true, -1, true);
            var label = w.Kind == "input" ? (w.Value.Length > 0 ? w.Value : w.Placeholder) : w.Locked && w.Value.Length > 0 ? w.Value : w.Label;
            if (label.Length > 0) o.DrawString(font, c + new Vector2(-40, -sz.Y / 2 - 3), label.Length > 24 ? label[..24] + "…" : label, HorizontalAlignment.Center, 80, 10, col);
        }
    }

    void DrawSelection(Control o, ArtLayer s)
    {
        var xf = s.Xf();
        var b = _c.Tool == "crop" && s.Kind == "image" ? ArtDraw.ImageBounds(s) : StudioCanvas.LocalBounds(s);
        var corners = new[] { b.Position, new Vector2(b.End.X, b.Position.Y), b.End, new Vector2(b.Position.X, b.End.Y) }.Select(p => _c.ToScreen(xf * p)).ToArray();
        var col = _c.Tool == "crop" ? Gold : Guide;
        o.DrawPolyline(corners.Append(corners[0]).ToArray(), col, 1.5f, true);
        if (!s.Locked && _c.Tool != "text")
        {
            foreach (var p in StudioCanvas.HandlePts(b)) Knob(o, _c.ToScreen(xf * p), col);
            if (_c.Tool == "move")
            {
                var knob = _c.RotateKnob(s);
                o.DrawLine(_c.ToScreen(xf * new Vector2(b.GetCenter().X, b.Position.Y)), knob, col, 1, true);
                o.DrawCircle(knob, 5.5f, new Color(0, 0, 0, 0.7f), true, -1, true);
                o.DrawArc(knob, 5.5f, 0, Mathf.Tau, 20, col, 1.5f, true);
            }
        }
        if (_c.Tool == "crop" && s.Kind == "image" && s.Crop != null)
        {
            var full = new Rect2(-new Vector2(s.SrcW, s.SrcH) / 2, new Vector2(s.SrcW, s.SrcH));
            var fc = new[] { full.Position, new Vector2(full.End.X, full.Position.Y), full.End, new Vector2(full.Position.X, full.End.Y) }.Select(p => _c.ToScreen(xf * p)).ToArray();
            o.DrawPolyline(fc.Append(fc[0]).ToArray(), new Color(1, 1, 1, 0.3f), 1, true);
        }
    }

    void Knob(Control o, Vector2 p, Color c)
    {
        o.DrawRect(new Rect2(p - new Vector2(4.5f, 4.5f), new Vector2(9, 9)), new Color(0, 0, 0, 0.75f));
        o.DrawRect(new Rect2(p - new Vector2(3.5f, 3.5f), new Vector2(7, 7)), c);
    }

    void DrawMeasure(Control o, Font font, Vector2 a, Vector2 b, bool faint = false)
    {
        var sa = _c.ToScreen(a); var sb = _c.ToScreen(b);
        var col = faint ? new Color(1, 1, 1, 0.75f) : new Color(1f, 0.6f, 0.2f, 0.95f);
        if (!faint)
        {
            o.DrawLine(sa, sb, new Color(0, 0, 0, 0.6f), 3, true);
            o.DrawLine(sa, sb, col, 1.5f, true);
            var n = (sb - sa).Normalized().Orthogonal() * 6;
            o.DrawLine(sa - n, sa + n, col, 1.5f, true); o.DrawLine(sb - n, sb + n, col, 1.5f, true);
        }
        float px = (b - a).Length();
        if (px < 0.5f) return;
        var du = new Vector2((b.X - a.X) / _c.Doc.W * _c.PieceUnits.X, (b.Y - a.Y) / _c.Doc.H * _c.PieceUnits.Y).Length();
        float ang = Mathf.PosMod(-Mathf.RadToDeg((b - a).Angle()), 360);
        if (ang >= 359.95f) ang = 0;
        var text = string.Format(Inv, "{0:0.#} px · {1:0.###} u · {2:0.#} m · {3:0.#}°", px, du, du * _c.P.MetresPerUnit, ang);
        var at = (sa + sb) / 2 + new Vector2(8, -8);
        o.DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, 12, 4, new Color(0, 0, 0, 0.85f));
        o.DrawString(font, at, text, HorizontalAlignment.Left, -1, 12, col);
    }

    void DrawRulers(Control o, Font font)
    {
        const float T = 18;
        var bg = new Color(0.09f, 0.095f, 0.105f, 0.94f);
        o.DrawRect(new Rect2(0, 0, _c.Size.X, T), bg);
        o.DrawRect(new Rect2(0, 0, T, _c.Size.Y), bg);
        float step = 1;
        foreach (var s in new[] { 1f, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000 }) { step = s; if (s * _c.Zoom >= 60) break; }
        var tick = new Color(1, 1, 1, 0.45f);
        float x0 = Mathf.Floor(_c.ToDoc(new Vector2(T, 0)).X / step) * step;
        for (float x = x0; _c.ToScreen(new Vector2(x, 0)).X < _c.Size.X; x += step)
        {
            float sx = _c.ToScreen(new Vector2(x, 0)).X;
            if (sx < T) continue;
            o.DrawLine(new Vector2(sx, T - 7), new Vector2(sx, T), tick, 1);
            o.DrawString(font, new Vector2(sx + 2, 11), $"{x:0}", HorizontalAlignment.Left, -1, 9, tick);
            for (int k = 1; k < 5; k++) { float sk = _c.ToScreen(new Vector2(x + step * k / 5, 0)).X; o.DrawLine(new Vector2(sk, T - 3), new Vector2(sk, T), new Color(tick, 0.25f), 1); }
        }
        float y0 = Mathf.Floor(_c.ToDoc(new Vector2(0, T)).Y / step) * step;
        for (float y = y0; _c.ToScreen(new Vector2(0, y)).Y < _c.Size.Y; y += step)
        {
            float sy = _c.ToScreen(new Vector2(0, y)).Y;
            if (sy < T) continue;
            o.DrawLine(new Vector2(T - 7, sy), new Vector2(T, sy), tick, 1);
            o.DrawString(font, new Vector2(2, sy + 10), $"{y:0}", HorizontalAlignment.Left, -1, 9, tick);
            for (int k = 1; k < 5; k++) { float sk = _c.ToScreen(new Vector2(0, y + step * k / 5)).Y; o.DrawLine(new Vector2(T - 3, sk), new Vector2(T, sk), new Color(tick, 0.25f), 1); }
        }
        if (_c.HoverIn)
        {
            var h = _c.ToScreen(_c.HoverDoc);
            o.DrawLine(new Vector2(h.X, 0), new Vector2(h.X, T), Gold, 1);
            o.DrawLine(new Vector2(0, h.Y), new Vector2(T, h.Y), Gold, 1);
        }
        var dr = _c.DocRect;
        o.DrawLine(new Vector2(Mathf.Max(T, dr.Position.X), T - 1), new Vector2(Mathf.Max(T, dr.End.X), T - 1), new Color(Gold, 0.6f), 2);
        o.DrawLine(new Vector2(T - 1, Mathf.Max(T, dr.Position.Y)), new Vector2(T - 1, Mathf.Max(T, dr.End.Y)), new Color(Gold, 0.6f), 2);
    }
}

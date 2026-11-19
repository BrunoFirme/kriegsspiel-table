using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Client;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

sealed class InkFill
{
    readonly TableContext _c;
    readonly DrawingLayer _drawings;

    public InkFill(TableContext c, DrawingLayer drawings) => (_c, _drawings) = (c, drawings);

    const int MostSide = 2048, LeastSide = 16;
    const double ThinPixels = 0.75, WholeInsetPixels = 2;
    const int LeastHolePixels = 150;

    public List<Vec2>? Region(Vec2 at)
    {
        var cfg = _c.Settings;
        var view = _c.Scene.GetViewport().GetVisibleRect().Size;
        double shrink = Math.Min(1, MostSide / Math.Max(1, Math.Max(view.X, view.Y)));
        int w = Math.Max(LeastSide, (int)(view.X * shrink)), h = Math.Max(LeastSide, (int)(view.Y * shrink));
        double perPx = 1 / (Math.Max(1e-6, _c.Rig.PixelsPerUnitAt(Coords.ToWorld(at))) * shrink);
        var corner = new Vec2(at.X - (w / 2 + 0.5) * perPx, at.Z + (h / 2 + 0.5) * perPx);
        (double X, double Y) ToPx(Vec2 p) => ((p.X - corner.X) / perPx, (corner.Z - p.Z) / perPx);
        Vec2 FromPx(double x, double y) => new(corner.X + x * perPx, corner.Z - y * perPx);
        var data = InkRaster.Paint(Shown(), w, h, ToPx, 1 / perPx);
        var grid = cfg.FillStopsAtGrid && GridSnap.On(_c.S.View.Grid) ? _c.S.View.Grid : null;
        var cell = grid != null ? GridCells.CellOf(grid, at) : default;
        var wall = grid != null ? Walls(grid, cell, w, h, ToPx, FromPx) : null;
        if (FloodFill.Region(data, w, h, w / 2, h / 2, cfg.FillReach, wall) is not { } m) return null;
        if (grid != null && Whole(grid, cell, m, w, h, ToPx, FromPx, perPx)) return GridCells.Corners(grid, cell);
        var loops = FloodFill.Loops(m.mask, m.w, m.h, LeastHolePixels).Select(l => Closed(l.Select(p => FromPx(p.X + m.x, p.Y + m.y)).ToList(), ThinPixels * perPx)).ToList();
        return loops.Count == 0 || loops[0].Count < 3 ? null : Polyline.Keyholed(loops);
    }

    IEnumerable<Stroke> Shown()
    {
        var view = _c.S.View;
        var sight = view.Boards.Where(b => b.Sight).Select(b => b.Id).ToHashSet();
        return view.Drawings.Where(l => _c.LayerShown(l.Key)).SelectMany(l => l.Value)
            .Where(st => st.Sight == null && !sight.Contains(st.Board) && _drawings.BoardShown(st.Board));
    }

    static List<Vec2> Closed(List<Vec2> loop, double tol)
    {
        var thin = Polyline.Thin(loop.Append(loop[0]).ToList(), tol);
        return thin.Take(thin.Count - 1).ToList();
    }

    static (int X0, int Y0, int X1, int Y1) Box(List<Vec2> ring, Func<Vec2, (double X, double Y)> toPx, int w, int h)
    {
        var px = ring.Select(toPx).ToList();
        return (Math.Max(0, (int)Math.Floor(px.Min(p => p.X))), Math.Max(0, (int)Math.Floor(px.Min(p => p.Y))),
            Math.Min(w - 1, (int)Math.Ceiling(px.Max(p => p.X))), Math.Min(h - 1, (int)Math.Ceiling(px.Max(p => p.Y))));
    }

    static bool[] Walls(GridDef grid, GridCell cell, int w, int h, Func<Vec2, (double X, double Y)> toPx, Func<double, double, Vec2> fromPx)
    {
        var wall = new bool[w * h];
        Array.Fill(wall, true);
        var (x0, y0, x1, y1) = Box(GridCells.Corners(grid, cell), toPx, w, h);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (GridCells.CellOf(grid, fromPx(x + 0.5, y + 0.5)) == cell) wall[y * w + x] = false;
        return wall;
    }

    static bool Whole(GridDef grid, GridCell cell, (byte[] mask, int w, int h, int x, int y) m, int w, int h, Func<Vec2, (double X, double Y)> toPx, Func<double, double, Vec2> fromPx, double perPx)
    {
        var ring = GridCells.Corners(grid, cell);
        var (x0, y0, x1, y1) = Box(ring, toPx, w, h);
        bool any = false;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = fromPx(x + 0.5, y + 0.5);
                if (!Polyline.Inside(ring, p) || SightingDraft.EdgeDistance(ring, p) <= WholeInsetPixels * perPx) continue;
                int mx = x - m.x, my = y - m.y;
                if (mx < 0 || my < 0 || mx >= m.w || my >= m.h || m.mask[(my * m.w + mx) * 4 + 3] == 0) return false;
                any = true;
            }
        return any;
    }
}

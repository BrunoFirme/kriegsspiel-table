using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed partial class BoundMarks : Control
{
    const float DotPx = 4.5f, LinePx = 2f;

    public TableContext? Table { get; init; }
    public BoundEditor? Edit { get; init; }
    bool _active;

    public void Tick(bool active)
    {
        if (!active && !_active) return;
        _active = active;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active || Table is not { } c || Edit is not { } edit) return;
        var ink = Ui.Accent;
        if (edit.Shape is { } shape && edit.Ring(shape) is { Count: >= 3 } ring && Seen(c, ring) is { } flat)
        {
            for (int i = 0; i < flat.Count; i++) DrawLine(flat[i], flat[(i + 1) % flat.Count], ink, Ui.Pxf(LinePx), true);
            foreach (var p in flat) DrawCircle(p, Ui.Pxf(DotPx), ink);
        }
        if (!edit.Drawing) return;
        var points = edit.Corners.ToList();
        if (edit.Cursor is { } cursor) points.Add(cursor);
        if (Seen(c, points) is not { } line) return;
        for (int i = 0; i + 1 < line.Count; i++) DrawLine(line[i], line[i + 1], ink, Ui.Pxf(LinePx), true);
        foreach (var p in line) DrawCircle(p, Ui.Pxf(DotPx), ink);
    }

    static List<Vector2>? Seen(TableContext c, List<Vector3> points) =>
        points.Any(p => c.Rig.Cam.IsPositionBehind(WorldShift.ToGlobal(p))) ? null : points.Select(c.Rig.Screen).ToList();
}

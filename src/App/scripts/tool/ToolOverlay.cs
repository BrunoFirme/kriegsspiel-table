using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class ToolOverlay
{
    readonly TableContext _c;
    public Node3D Root = null!;
    public ImmediateMesh Mesh { get; private set; } = null!;

    public ToolOverlay(TableContext c) { _c = c; }

    public void Build()
    {
        Mesh = new ImmediateMesh();
        var overlay = new MeshInstance3D { Mesh = Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer };
        Root.AddChild(overlay);
    }

    public event Action? Drawing;

    public void Redraw()
    {
        Mesh.ClearSurfaces();
        _c.Labels.RemoveWhere(RulerTool.IsLabel);
        Drawing?.Invoke();
    }

    public float Px(float pixels) => pixels / Math.Max(0.01f, _c.Rig.PixelsPerUnit());

    public void RibbonPx(ImmediateMesh im, StandardMaterial3D mat, List<Vec2> pts, float y, float pixels = Table.ToolPx) =>
        Ribbon.Draw(im, mat, pts, Px(pixels), _c.SheetsTop + y, _c.S.Ground);

    public void SegPx(ImmediateMesh im, StandardMaterial3D mat, Vec2 a, Vec2 b, float y, float pixels = Table.ToolPx) =>
        Ribbon.Draw(im, mat, new List<Vec2> { a, b }, Px(pixels), _c.SheetsTop + y, _c.S.Ground);
}

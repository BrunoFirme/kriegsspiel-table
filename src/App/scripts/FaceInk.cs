using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

abstract class FaceInk
{
    public const float Below = 0.004f;
    public static Vector3 Under(float x, float z, float below) => new(-x, -below, z);

    public enum Fill { Box, Band }

    public const int LetterPx = 64, EdgePx = 6;
    public static readonly Color TextColor = new(0.97f, 0.96f, 0.92f), ValueColor = new(1, 0.9f, 0.55f), DimColor = new(0.6f, 0.6f, 0.6f);
    static readonly Color BoxColor = new(1, 1, 1, 0.2f), BandColor = new(0.08f, 0.08f, 0.09f, 0.95f);
    public static Color FillColor(Fill f) => f == Fill.Band ? BandColor : BoxColor;

    internal static Color Edge(Color ink) => ink.Luminance < DarkInk ? new Color(1, 1, 1, 0.55f) : new Color(0, 0, 0, 0.8f);

    const float DarkInk = 0.5f;

    public abstract void Plane(bool back, float x, float z, float sw, float sh, Fill fill);

    protected abstract void Letters(bool back, string text, float x, float z, float pixel, Color color);

    public void Text(bool back, string text, float x, float z, float maxW, float lineH, Color color, HorizontalAlignment align)
    {
        var px = Fonts.Face.GetStringSize(text, HorizontalAlignment.Left, -1, LetterPx);
        float lineHeightPx = Mathf.Max(1, Fonts.Face.GetHeight(LetterPx));
        float pixel = Mathf.Max(0.00002f, Mathf.Min(lineH / lineHeightPx, maxW / Mathf.Max(1, px.X)));
        float half = px.X * pixel / 2;
        float cx = align == HorizontalAlignment.Left ? x + half : align == HorizontalAlignment.Right ? x - half : x;
        Letters(back, text, cx, z, pixel, color);
    }

    public readonly record struct Counter(string Name, int Value, int Min, int Max, bool ShowName, bool ShowButtons);

    public void CounterRow(bool back, Rect2 box, Counter c, List<(Rect2 rect, string name, int value)> buttons)
    {
        float left = box.Position.X, right = box.End.X, cw = box.Size.X, rowH = box.Size.Y, zc = box.GetCenter().Y;
        var value = c.Value.ToString();
        if (!c.ShowButtons)
        {
            if (!c.ShowName) { Text(back, value, box.GetCenter().X, zc, cw * 0.94f, rowH * 0.8f, ValueColor, HorizontalAlignment.Center); return; }
            float vw0 = Mathf.Min(cw * 0.4f, rowH * 1.5f);
            Text(back, c.Name, left, zc, cw - vw0 - rowH * 0.2f, rowH * 0.46f, TextColor, HorizontalAlignment.Left);
            Text(back, value, right - vw0 / 2, zc, vw0, rowH * 0.6f, ValueColor, HorizontalAlignment.Center);
            return;
        }
        float bs = c.ShowName ? Mathf.Min(Mathf.Max(rowH * 0.6f, 0.05f), cw * 0.12f) : Mathf.Min(rowH * 0.6f, cw / 4.1f);
        float gap = bs * 0.3f, vw = bs * 1.5f;
        float xPlus = c.ShowName ? right - bs / 2 : box.GetCenter().X + vw / 2 + gap + bs / 2;
        float xVal = xPlus - bs / 2 - gap - vw / 2, xMinus = xVal - vw / 2 - gap - bs / 2;
        if (c.ShowName) Text(back, c.Name, left, zc, (xMinus - bs / 2 - gap) - left, rowH * 0.46f, TextColor, HorizontalAlignment.Left);
        Text(back, value, xVal, zc, vw, rowH * 0.6f, ValueColor, HorizontalAlignment.Center);
        foreach (var (x, delta, glyph) in new[] { (xMinus, -1, "−"), (xPlus, 1, "+") })
        {
            int nv = Math.Clamp(c.Value + delta, c.Min, c.Max);
            bool can = nv != c.Value;
            Plane(back, x, zc, bs, bs, Fill.Box);
            Text(back, glyph, x, zc, bs * 0.8f, bs * 0.72f, can ? TextColor : DimColor, HorizontalAlignment.Center);
            if (can) buttons.Add((new Rect2(x - bs * 0.75f, zc - bs * 0.75f, bs * 1.5f, bs * 1.5f), c.Name, nv));
        }
    }
}

sealed class NodeInk : FaceInk
{
    readonly PieceNode _piece;
    readonly List<Node3D> _nodes;

    public NodeInk(PieceNode piece, List<Node3D> nodes) { _piece = piece; _nodes = nodes; }

    const float BandLift = 0.0005f, BoxLift = 0.001f, TextLift = 0.002f;

    static StandardMaterial3D? _boxMat, _bandMat;
    static StandardMaterial3D Flat(Color c, int priority) =>
        new() { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = priority };
    static StandardMaterial3D BoxMat => _boxMat ??= Flat(FillColor(Fill.Box), 2);
    static StandardMaterial3D BandMat => _bandMat ??= Flat(FillColor(Fill.Band), 1);

    float FaceY => _piece.Thickness + 0.006f;

    void Put(Node3D n, bool back, float x, float z, float lift)
    {
        n.Position = back ? Under(x, z, Below + lift) : new Vector3(x, FaceY + lift, z);
        _piece.AddChild(n);
        _nodes.Add(n);
    }

    public override void Plane(bool back, float x, float z, float sw, float sh, Fill fill)
    {
        var n =
            new MeshInstance3D { Mesh = PieceWidgets.UnitPlane, RotationDegrees = back ? new Vector3(180, 0, 0) : Vector3.Zero, Scale = new Vector3(Mathf.Max(1e-5f, sw), 1, Mathf.Max(1e-5f, sh)),
                MaterialOverride = fill == Fill.Band ? BandMat : BoxMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = _piece.LetteringRange() };
        Put(n, back, x, z, fill == Fill.Band ? BandLift : BoxLift);
    }

    protected override void Letters(bool back, string text, float x, float z, float pixel, Color color)
    {
        var l = new Label3D
        {
            Text = text, FontSize = LetterPx, OutlineSize = EdgePx, Modulate = color, OutlineModulate = Edge(color), RotationDegrees = back ? new Vector3(90, 180, 0)
                : new Vector3(-90, 0, 0), DoubleSided = false, VisibilityRangeEnd = _piece.LetteringRange(), PixelSize = pixel,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderPriority = 3, Font = Fonts.Face, Layers = Table.TextLayer,
        };
        Put(l, back, x, z, TextLift);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public sealed class SheetCovers : IEquatable<SheetCovers>
{
    public const int Most = 8;
    const float Inset = 0.001f;
    const string BoxesParameter = "cover_boxes", HalvesParameter = "cover_halves", CountParameter = "cover_count";
    public static readonly SheetCovers None = new([], []);

    readonly Vector4[] _boxes;
    readonly Vector2[] _halves;

    SheetCovers(Vector4[] boxes, Vector2[] halves) => (_boxes, _halves) = (boxes, halves);

    public static SheetCovers Of(IReadOnlyList<SheetDef> over)
    {
        if (over.Count == 0) return None;
        var boxes = new Vector4[Most];
        var halves = new Vector2[Most];
        int n = Math.Min(Most, over.Count);
        for (int i = 0; i < n; i++)
        {
            var s = over[i];
            var mid = Coords.ToWorld(s.Pos);
            float yaw = Coords.Yaw(s.RotDeg);
            boxes[i] = new Vector4(mid.X, mid.Z, Mathf.Cos(yaw), -Mathf.Sin(yaw));
            halves[i] = new Vector2((float)s.Size.W / 2 - Inset, (float)s.Size.H / 2 - Inset);
        }
        return new SheetCovers(boxes[..n], halves[..n]);
    }

    public void Lay(ShaderMaterial m)
    {
        var boxes = new Vector4[Most];
        var halves = new Vector2[Most];
        _boxes.CopyTo(boxes, 0);
        _halves.CopyTo(halves, 0);
        m.SetShaderParameter(BoxesParameter, boxes);
        m.SetShaderParameter(HalvesParameter, halves);
        m.SetShaderParameter(CountParameter, _boxes.Length);
    }

    public bool Equals(SheetCovers? o) => o != null && _boxes.SequenceEqual(o._boxes) && _halves.SequenceEqual(o._halves);
    public override bool Equals(object? o) => Equals(o as SheetCovers);
    public override int GetHashCode() => _boxes.Length;
    public static bool operator ==(SheetCovers? a, SheetCovers? b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(SheetCovers? a, SheetCovers? b) => !(a == b);
}

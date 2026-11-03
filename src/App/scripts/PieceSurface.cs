using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode : ITablePiece
{
    System.Numerics.Vector2 ITablePiece.Footprint => new(Footprint.X, Footprint.Y);
    bool ITablePiece.IsProp => Def?.Kind == "model";
    float? ITablePiece.SurfaceAt(Vec2 p) => SurfaceAt(Coords.ToWorld(p));

    public float? SurfaceAt(Vector3 world)
    {
        if (IsEffect || Ghost) return null;
        if (Def?.Kind == "model" && !IsDie && _model is { Mesh: { } mesh } model && IsInstanceValid(model) && model.IsInsideTree())
        {
            var grid = MeshTops.Of(mesh);
            if (grid != null)
            {
                var xf = model.GlobalTransform;
                var local = xf.AffineInverse() * new Vector3(world.X, xf.Origin.Y, world.Z);
                if (grid.At(local.X, local.Z) is not { } h) return null;
                return (xf * new Vector3(local.X, h, local.Z)).Y;
            }
        }
        return Contains(Coords.FromWorld(world)) ? TopAt(world) : null;
    }

    public Vector3[]? WorldFaces()
    {
        if (_model is not { Mesh: { } mesh } model || !IsInstanceValid(model) || !model.IsInsideTree()) return null;
        var faces = mesh.GetFaces();
        var xf = model.GlobalTransform;
        var o = new Vector3[faces.Length];
        for (int i = 0; i < faces.Length; i++) o[i] = xf * faces[i];
        return o;
    }
}

static class MeshTops
{
    static readonly ConditionalWeakTable<Mesh, HeightGrid?> Cache = new();

    public static HeightGrid? Of(Mesh mesh)
    {
        if (Cache.TryGetValue(mesh, out var g)) return g;
        HeightGrid? made = null;
        try
        {
            var box = mesh.GetAabb();
            made = HeightGrid.Of(new(box.Position.X, box.Position.Y, box.Position.Z), new(box.Size.X, box.Size.Y, box.Size.Z), Triangles(mesh));
        }
        catch (Exception e) { GD.PrintErr($"[surface] could not read a mesh's top: {e.Message}"); }
        Cache.AddOrUpdate(mesh, made);
        return made;
    }

    static IEnumerable<(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3)> Triangles(Mesh mesh)
    {
        static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idx = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.PackedInt32Array ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : null;
            int count = idx is { Length: > 0 } ? idx.Length : v.Length;
            for (int i = 0; i + 2 < count; i += 3)
                yield return (N(v[idx is { Length: > 0 } ? idx[i] : i]), N(v[idx is { Length: > 0 } ? idx[i + 1] : i + 1]), N(v[idx is { Length: > 0 } ? idx[i + 2] : i + 2]));
        }
    }
}

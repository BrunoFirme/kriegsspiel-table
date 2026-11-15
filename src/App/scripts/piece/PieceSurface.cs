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
    System.Numerics.Vector2 ITablePiece.GroundCentre => new(GroundCentre.X, GroundCentre.Y);
    public Vec2 MiddleAt(Vec2 pos, double rotDeg) => TablePieces.MiddleAt(this, pos, rotDeg);
    public Vec2 CentreOffset(double rotDeg) => TablePieces.CentreOffset(this, rotDeg);
    bool ITablePiece.IsProp => ShowsModel;
    float? ITablePiece.SurfaceAt(Vec2 p) => SurfaceAt(Coords.ToWorld(p));
    float ITablePiece.SurfaceReach => SurfaceReaches.Of(this, _model);

    public float? SurfaceAt(Vector3 world)
    {
        if (IsEffect || Ghost) return null;
        if (ShowsModel && !IsDie && _model is { Mesh: { } mesh } model && IsInstanceValid(model) && model.IsInsideTree())
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

    static readonly ConditionalWeakTable<Mesh, TriangleMesh> RayMeshes = new();

    public (bool Known, float? At) ModelAlong(Vector3 ro, Vector3 rd)
    {
        if (!ShowsModel || IsDie || _model is not { Mesh: { } mesh } model || !IsInstanceValid(model) || !model.IsInsideTree()) return (false, null);
        if (!RayMeshes.TryGetValue(mesh, out var tri))
        {
            if (mesh.GenerateTriangleMesh() is not { } made) return (false, null);
            RayMeshes.AddOrUpdate(mesh, made);
            tri = made;
        }
        var xf = model.GlobalTransform;
        var inv = xf.AffineInverse();
        var hit = tri.IntersectRay(inv * ro, inv.Basis * rd);
        if (hit.Count == 0 || !hit.TryGetValue("position", out var at)) return (true, null);
        return (true, (xf * at.AsVector3() - ro).Dot(rd));
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

static class SurfaceReaches
{
    const float SteepModel = 1e-4f;

    public static float Of(PieceNode n, MeshInstance3D? model)
    {
        if (n.IsEffect || n.Ghost) return 0;
        if (n.ShowsModel && !n.IsDie && model is { Mesh: { } mesh } && GodotObject.IsInstanceValid(model) && model.IsInsideTree() && MeshTops.Of(mesh) != null)
            return OfModel(model, mesh.GetAabb(), n.Piece.Pos);
        return Outline(n);
    }

    static float Outline(PieceNode n)
    {
        float reach = n.FootprintCentre.Length() + new Vector3(n.Footprint.X, n.Thickness, n.Footprint.Y).Length() / 2;
        foreach (var p in n.Parts) reach = Mathf.Max(reach, p.Position.Length() + Outline(p));
        return reach;
    }

    static float OfModel(MeshInstance3D model, Aabb box, Vec2 at)
    {
        var xf = model.GlobalTransform;
        var inv = xf.AffineInverse().Basis;
        var flat = new Transform2D(new Vector2(inv.X.X, inv.X.Z), new Vector2(inv.Z.X, inv.Z.Z), Vector2.Zero);
        if (Mathf.Abs(flat.Determinant()) < SteepModel) return float.PositiveInfinity;
        var back = flat.AffineInverse();
        float cellX = box.Size.X / (HeightGrid.Cells - 1), cellZ = box.Size.Z / (HeightGrid.Cells - 1);
        float far = 0;
        foreach (var x in new[] { box.Position.X - cellX, box.End.X + cellX })
            foreach (var z in new[] { box.Position.Z - cellZ, box.End.Z + cellZ })
                far = Mathf.Max(far, (back * new Vector2(x, z)).Length());
        var centre = Coords.ToWorld(at);
        return new Vector2(xf.Origin.X - centre.X, xf.Origin.Z - centre.Z).Length() + far;
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

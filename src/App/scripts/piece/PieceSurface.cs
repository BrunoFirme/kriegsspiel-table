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
                var xf = model.TableTransform();
                var local = xf.AffineInverse() * new Vector3(world.X, xf.Origin.Y, world.Z);
                if (grid.At(local.X, local.Z) is not { } h) return null;
                return (xf * new Vector3(local.X, h, local.Z)).Y;
            }
        }
        return Contains(Coords.FromWorld(world)) ? TopAt(world) : null;
    }

    public bool LifeSize => Def?.Tags.Contains(PieceDef.LifeSizeTag) == true;

    public Func<Vector3, bool>? Hidden { get; set; }

    bool ITablePiece.HasFloors => HasFloors;

    public bool HasFloors => ShowsModel && !IsDie && _model is { Mesh: not null } && Def?.Tags.Contains(WallCutaway.BuildingTag) == true;

    float? ITablePiece.FloorAt(Vec2 p, float below) => ModelAlong(WorldShift.ToGlobal(Coords.ToWorld(p, below)), Vector3.Down, seenOnly: false) is { At: { } down } ? below - down : null;

    public (bool Known, float? At) ModelAlong(Vector3 ro, Vector3 rd, bool seenOnly = true) =>
        ShowsModel && !IsDie && _model is { Mesh: { } mesh } model && IsInstanceValid(model) && model.IsInsideTree() ? ModelRays.Along(model, mesh, ro, rd, seenOnly ? Hidden : null) : (false, null);

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
        if (n.ShowsModel && !n.IsDie && model is { Mesh: { } mesh } && GodotObject.IsInstanceValid(model) && model.IsInsideTree() && MeshTops.Known(mesh) != null)
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
        var xf = model.TableTransform();
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
    readonly record struct Read(HeightGrid? Grid, float[] Floors);

    static readonly ConditionalWeakTable<Mesh, System.Threading.Tasks.Task<Read>> Cache = new();

    public static void Warm(Mesh mesh) => Reading(mesh);

    public static HeightGrid? Of(Mesh mesh) => Reading(mesh).Result.Grid;

    public static HeightGrid? Known(Mesh mesh) => Reading(mesh) is { IsCompleted: true } done ? done.Result.Grid : null;

    public static float[] Floors(Mesh mesh) => Reading(mesh).Result.Floors;

    static System.Threading.Tasks.Task<Read> Reading(Mesh mesh)
    {
        if (Cache.TryGetValue(mesh, out var had)) return had;
        var box = mesh.GetAabb();
        var corner = new System.Numerics.Vector3(box.Position.X, box.Position.Y, box.Position.Z);
        var size = new System.Numerics.Vector3(box.Size.X, box.Size.Y, box.Size.Z);
        var triangles = Triangles(mesh);
        var reading = System.Threading.Tasks.Task.Run(() => new Read(HeightGrid.Of(corner, size, triangles), Storeys.Of(corner, size, triangles)));
        Cache.AddOrUpdate(mesh, reading);
        return reading;
    }

    static List<(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3)> Triangles(Mesh mesh)
    {
        static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);
        var found = new List<(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3)>();
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idx = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.PackedInt32Array ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : [];
            int count = idx.Length > 0 ? idx.Length : v.Length;
            int At(int i) => idx.Length > 0 ? idx[i] : i;
            for (int i = 0; i + 2 < count; i += 3)
                if (At(i) < v.Length && At(i + 1) < v.Length && At(i + 2) < v.Length) found.Add((N(v[At(i)]), N(v[At(i + 1)]), N(v[At(i + 2)])));
        }
        return found;
    }
}

static class ModelRays
{
    static readonly ConditionalWeakTable<Mesh, TriangleMesh> RayMeshes = new();
    const int SeenThrough = 24;
    const float PastHit = 1e-4f;

    public static (bool Known, float? At) Along(MeshInstance3D model, Mesh mesh, Vector3 ro, Vector3 rd, Func<Vector3, bool>? hidden)
    {
        if (!RayMeshes.TryGetValue(mesh, out var tri))
        {
            if (mesh.GenerateTriangleMesh() is not { } made) return (false, null);
            RayMeshes.AddOrUpdate(mesh, made);
            tri = made;
        }
        var xf = model.GlobalTransform;
        var inv = xf.AffineInverse();
        Vector3 from = inv * ro, along = (inv.Basis * rd).Normalized();
        float past = mesh.GetAabb().GetLongestAxisSize() * PastHit;
        for (int i = 0; i < SeenThrough; i++)
        {
            var hit = tri.IntersectRay(from, along);
            if (hit.Count == 0 || !hit.TryGetValue("position", out var at)) return (true, null);
            var world = xf * at.AsVector3();
            if (hidden?.Invoke(world) != true) return (true, (world - ro).Dot(rd));
            from = at.AsVector3() + along * past;
        }
        return (true, null);
    }
}

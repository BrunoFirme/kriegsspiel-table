using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public readonly record struct Resting(float Y, Vector3 Up, bool OnGround = false);

public sealed class GroundFit
{
    public const float Clearance = 0.006f;

    readonly PieceNode _n;
    readonly Dictionary<Node3D, float> _effectHeights = new();
    ArrayMesh? _unbent;
    MeshInstance3D? _caster;
    (Transform3D At, Vector3 Scale)? _fittedFor;

    public GroundFit(PieceNode n) => _n = n;

    public float Low { get; private set; }
    public float High { get; private set; }
    public bool Fitted => _fittedFor != null;
    public bool CastsFromGround => _n.Model is { } m && GodotObject.IsInstanceValid(m) && m.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off;
    public int RaisedShadowFaces => _caster?.Mesh is ArrayMesh m ? GroundDrape.Faces(m) : 0;

    public void Forget()
    {
        _unbent = null;
        _caster = null;
        _fittedFor = null;
        _effectHeights.Clear();
        Low = High = 0;
    }

    public void Stale() => _fittedFor = null;

    public bool Lifted(bool held)
    {
        if (held) Fit(onGround: false);
        return held;
    }

    public void Fit(bool onGround)
    {
        if (_n.Model is not { } model || !GodotObject.IsInstanceValid(model)) return;
        if (!onGround || _n.GlideShown) { Release(model); return; }
        if ((_unbent ?? model.Mesh as ArrayMesh) is not { } source) return;
        var key = (_n.Transform, model.Scale);
        if (_fittedFor is { } was && was == key) return;
        var ground = _n.Session.Ground;
        double level = _n.Position.Y - Clearance - _n.PartsBelow;
        var drape = GroundDrape.Over(source, _n.Transform * model.Transform, ground, level);
        _unbent = source;
        _fittedFor = key;
        model.Mesh = drape.Mesh;
        (Low, High) = (drape.Low, drape.High);
        CastFromRaisedParts(model, drape);
        RaiseEffect(ground, level);
        if (_n.SelectionMesh is { } ring) ring.Position = ring.Position with { Y = drape.High };
    }

    void CastFromRaisedParts(MeshInstance3D model, GroundDrape.Draped drape)
    {
        if (_n.Def!.Shadowless) return;
        model.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        if (_caster == null || !GodotObject.IsInstanceValid(_caster))
        {
            _caster = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, Layers = model.Layers };
            _n.AddChild(_caster);
        }
        _caster.Transform = model.Transform;
        _caster.Mesh = drape.Caster;
        for (int s = 0; s < drape.CasterSurfaces.Count; s++) _caster.SetSurfaceOverrideMaterial(s, model.GetSurfaceOverrideMaterial(drape.CasterSurfaces[s]));
    }

    void Release(MeshInstance3D model)
    {
        if (_fittedFor == null) return;
        _fittedFor = null;
        model.Mesh = _unbent;
        model.CastShadow = _n.Def!.Shadowless ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On;
        if (_caster != null && GodotObject.IsInstanceValid(_caster)) _caster.QueueFree();
        _caster = null;
        Low = High = 0;
        RaiseEffect(null, 0);
        if (_n.SelectionMesh is { } ring) ring.Position = ring.Position with { Y = 0 };
    }

    void RaiseEffect(Ground? ground, double level)
    {
        foreach (var child in _n.GetChildren())
        {
            if (child is not (GpuParticles3D or Light3D) || child is not Node3D part) continue;
            if (!_effectHeights.TryGetValue(part, out float flat)) _effectHeights[part] = flat = part.Position.Y;
            float rise = ground == null ? 0 : (float)(ground.DrawnAt(Coords.FromWorld(_n.Transform * (part.Position with { Y = 0 }))) - level);
            part.Position = part.Position with { Y = flat + rise };
        }
    }
}

static class GroundDrape
{
    public const float CastsAbove = 0.12f;

    sealed record Surface(Mesh.PrimitiveType Kind, string Name, Godot.Collections.Array Arrays, Vector3[] Points, Vector3[]? Normals, int[] Corners);

    public sealed record Draped(ArrayMesh Mesh, ArrayMesh Caster, List<int> CasterSurfaces, float Low, float High);

    static readonly ConditionalWeakTable<ArrayMesh, Surface[]> Sources = new();

    public static Draped Over(ArrayMesh source, Transform3D toWorld, Ground ground, double level)
    {
        var made = new ArrayMesh();
        var caster = new ArrayMesh();
        var casterSurfaces = new List<int>();
        float low = 0, high = 0, perUnit = toWorld.Basis.Y.Y;
        Vector3 alongX = toWorld.Basis.X, alongZ = toWorld.Basis.Z;
        var surfaces = Sources.GetValue(source, Read);
        for (int s = 0; s < surfaces.Length; s++)
        {
            var surface = surfaces[s];
            var points = new Vector3[surface.Points.Length];
            var normals = surface.Normals == null ? null : new Vector3[surface.Normals.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var p = surface.Points[i];
                var under = ground.DrawnUnder(Coords.FromWorld(toWorld * p));
                float rise = (float)(under.Height - level);
                low = Math.Min(low, rise);
                high = Math.Max(high, rise);
                points[i] = p with { Y = p.Y + rise / perUnit };
                if (normals == null || i >= normals.Length) continue;
                float slopeX = (float)(under.SlopeX * alongX.X - under.SlopeZ * alongX.Z) / perUnit, slopeZ = (float)(under.SlopeX * alongZ.X - under.SlopeZ * alongZ.Z) / perUnit;
                var n = surface.Normals![i];
                normals[i] = new Vector3(n.X - slopeX * n.Y, n.Y, n.Z - slopeZ * n.Y).Normalized();
            }
            var arrays = surface.Arrays.Duplicate();
            arrays[(int)Mesh.ArrayType.Vertex] = points;
            if (normals != null) arrays[(int)Mesh.ArrayType.Normal] = normals;
            made.AddSurface(surface.Kind, arrays);
            made.SurfaceSetName(made.GetSurfaceCount() - 1, surface.Name);
            if (surface.Kind != Mesh.PrimitiveType.Triangles || Raised(surface, perUnit) is not { Length: > 0 } raised) continue;
            var cast = arrays.Duplicate();
            cast[(int)Mesh.ArrayType.Index] = raised;
            caster.AddSurface(Mesh.PrimitiveType.Triangles, cast);
            casterSurfaces.Add(s);
        }
        return new Draped(made, caster, casterSurfaces, low, high);
    }

    static int[] Raised(Surface surface, float perUnit)
    {
        var kept = new List<int>();
        var c = surface.Corners;
        for (int t = 0; t + 2 < c.Length; t += 3)
        {
            float top = Math.Max(surface.Points[c[t]].Y, Math.Max(surface.Points[c[t + 1]].Y, surface.Points[c[t + 2]].Y)) * perUnit;
            if (top >= CastsAbove) kept.AddRange([c[t], c[t + 1], c[t + 2]]);
        }
        return kept.ToArray();
    }

    public static int Faces(ArrayMesh mesh)
    {
        int faces = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var index = mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index];
            faces += (index.VariantType == Variant.Type.PackedInt32Array ? index.AsInt32Array().Length : mesh.SurfaceGetArrayLen(s)) / 3;
        }
        return faces;
    }

    static Surface[] Read(ArrayMesh mesh)
    {
        var surfaces = new Surface[mesh.GetSurfaceCount()];
        for (int s = 0; s < surfaces.Length; s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var normals = arrays[(int)Mesh.ArrayType.Normal];
            var index = arrays[(int)Mesh.ArrayType.Index];
            var points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var corners = index.VariantType == Variant.Type.PackedInt32Array && index.AsInt32Array() is { Length: > 0 } listed ? listed : Every(points.Length);
            surfaces[s] = new Surface(mesh.SurfaceGetPrimitiveType(s), mesh.SurfaceGetName(s), arrays, points,
                normals.VariantType == Variant.Type.PackedVector3Array ? normals.AsVector3Array() : null, corners);
        }
        return surfaces;
    }

    static int[] Every(int count)
    {
        var all = new int[count];
        for (int i = 0; i < count; i++) all[i] = i;
        return all;
    }
}

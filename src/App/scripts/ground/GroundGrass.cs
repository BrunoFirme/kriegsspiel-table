using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class GroundGrass
{
    public const float SpacingM = 0.2f, FirstRingM = 6f, BandShare = 0.15f;
    public const int MostRings = 4;
    const int Blades = 7, RelayFrames = 30, Seed = 7331;
    const float ClumpM = 0.09f, BaseM = 0.016f, WaistShare = 0.62f, WaistAt = 0.55f, LeanM = 0.07f, LeanPlay = 0.6f, AboveM = 30f;
    static readonly string[] Taken = ["heights", "built_near"];

    readonly Node _parent;
    readonly ArrayMesh _clump;
    readonly ShaderMaterial _draw;
    readonly List<(GpuParticles3D Node, ShaderMaterial Place)> _rings = new();
    float _reach = -1;
    GroundTiles.Sheet? _on;
    int _changes, _frames;
    Vector3 _wind;
    bool _showing;

    public GroundGrass(Node parent)
    {
        _parent = parent;
        _draw = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass.gdshader") };
        _clump = Clump();
        _clump.SurfaceSetMaterial(0, _draw);
    }

    public bool Showing => _showing;

    public int Rings => _rings.Count;

    public int Clumps
    {
        get
        {
            int all = 0;
            foreach (var (node, _) in _rings) all += node.Amount;
            return all;
        }
    }

    public static float InnerM(int ring) => ring == 0 ? 0 : FirstRingM * (1 << (ring - 1));

    public static float OuterM(int ring) => FirstRingM * (1 << ring);

    public static float SpacingOf(int ring) => SpacingM * (1 << ring);

    public static int RingsFor(float reach)
    {
        int rings = 0;
        while (rings < MostRings && InnerM(rings) < reach) rings++;
        return rings;
    }

    public static int AcrossOf(int ring, float reach) => (int)Math.Ceiling(2 * Math.Min(OuterM(ring) * (1 + BandShare), reach) / SpacingOf(ring));

    void Build(float reach)
    {
        Drop();
        _reach = reach;
        _on = null;
        int rings = RingsFor(reach);
        for (int i = 0; i < rings; i++)
        {
            int across = AcrossOf(i, reach);
            var place = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass_place.gdshader") };
            place.SetShaderParameter("across", across);
            place.SetShaderParameter("spacing_m", SpacingOf(i));
            place.SetShaderParameter("inner_m", InnerM(i));
            place.SetShaderParameter("outer_m", i == rings - 1 ? reach : OuterM(i));
            place.SetShaderParameter("last", i == rings - 1);
            place.SetShaderParameter("ring_scale", (float)(1 << i));
            var node = new GpuParticles3D
            {
                Name = "Grass" + i, TopLevel = true, Visible = false, Emitting = false, Amount = across * across, Lifetime = 600, Explosiveness = 1, OneShot = false,
                LocalCoords = false, FixedFps = 0, Interpolate = false, ProcessMaterial = place, DrawPass1 = _clump, DrawOrder = GpuParticles3D.DrawOrderEnum.Index,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer,
            };
            _parent.AddChild(node);
            _rings.Add((node, place));
        }
    }

    public void Show(Camera3D cam, float unitsPerMetre, float reach)
    {
        if (!Mathf.IsEqualApprox(reach, _reach)) Build(reach);
        var eye = WorldShift.ToTable(cam.GlobalPosition);
        if (Under(eye) is not { } sheet)
        {
            Hide();
            return;
        }
        if (sheet != _on || sheet.Changes != _changes || ++_frames >= RelayFrames) Lay(sheet);
        float box = (reach + AboveM) * unitsPerMetre;
        if (EffectFx.WindNow != _wind)
        {
            _wind = EffectFx.WindNow;
            var flat = new Vector2(_wind.X, _wind.Z);
            _draw.SetShaderParameter("wind", flat.Length() > 1e-4f ? flat.Normalized() * Mathf.Clamp(flat.Length(), 0.3f, 1.5f) : new Vector2(0.3f, 0.1f));
        }
        foreach (var (node, _) in _rings)
        {
            node.VisibilityAabb = new Aabb(-Vector3.One * box, Vector3.One * box * 2);
            node.GlobalPosition = cam.GlobalPosition;
            if (node.Visible) continue;
            node.Visible = true;
            node.Emitting = true;
        }
        _showing = true;
    }

    public void Hide()
    {
        if (!_showing) return;
        foreach (var (node, _) in _rings)
        {
            node.Visible = false;
            node.Emitting = false;
        }
        _showing = false;
        _on = null;
    }

    public void Drop()
    {
        foreach (var (node, _) in _rings) node.QueueFree();
        _rings.Clear();
        _showing = false;
        _reach = -1;
    }

    static GroundTiles.Sheet? Under(Vector3 eye)
    {
        foreach (var s in GroundTiles.Showing)
            if (s.UvOf(eye) is { } uv && uv.X >= 0 && uv.Y >= 0 && uv.X <= 1 && uv.Y <= 1) return s;
        return null;
    }

    void Lay(GroundTiles.Sheet sheet)
    {
        _on = sheet;
        _changes = sheet.Changes;
        _frames = 0;
        var rows = sheet.UvRows ?? (Vector2.Right, Vector2.Down);
        foreach (var (_, place) in _rings)
        {
            foreach (var name in Taken) place.SetShaderParameter(name, sheet.Bake.GetShaderParameter(name));
            sheet.Lay(place);
            place.SetShaderParameter("sheet_origin", sheet.Origin);
            place.SetShaderParameter("sheet_u", sheet.AlongU);
            place.SetShaderParameter("sheet_v", sheet.AlongV);
            place.SetShaderParameter("sheet_up", sheet.Rising);
            place.SetShaderParameter("uv_across", rows.Across);
            place.SetShaderParameter("uv_down", rows.Down);
        }
    }

    static ArrayMesh Clump()
    {
        var rng = new Random(Seed);
        var points = new Vector3[Blades * 5];
        var normals = new Vector3[Blades * 5];
        var uvs = new Vector2[Blades * 5];
        var index = new int[Blades * 9];
        for (int b = 0; b < Blades; b++)
        {
            float around = b * 2.39996f, off = ClumpM * MathF.Sqrt((b + 0.5f) / Blades);
            var foot = new Vector3(MathF.Cos(around) * off, 0, MathF.Sin(around) * off);
            float facing = (float)rng.NextDouble() * MathF.Tau, wide = BaseM * (0.7f + 0.6f * (float)rng.NextDouble());
            var across = new Vector3(MathF.Cos(facing), 0, MathF.Sin(facing)) * wide * 0.5f;
            var lean = (foot.LengthSquared() > 1e-8f ? foot.Normalized() : Vector3.Right) * LeanM * (1 - LeanPlay + LeanPlay * 2 * (float)rng.NextDouble());
            var face = across.Cross(Vector3.Up).Normalized();
            int v = b * 5;
            points[v] = foot - across;
            points[v + 1] = foot + across;
            points[v + 2] = foot - across * WaistShare + lean * WaistAt * WaistAt + Vector3.Up * WaistAt;
            points[v + 3] = foot + across * WaistShare + lean * WaistAt * WaistAt + Vector3.Up * WaistAt;
            points[v + 4] = foot + lean + Vector3.Up;
            for (int k = 0; k < 5; k++)
            {
                normals[v + k] = face;
                uvs[v + k] = new Vector2(0, points[v + k].Y);
            }
            int t = b * 9;
            (index[t], index[t + 1], index[t + 2]) = (v, v + 1, v + 3);
            (index[t + 3], index[t + 4], index[t + 5]) = (v, v + 3, v + 2);
            (index[t + 6], index[t + 7], index[t + 8]) = (v + 2, v + 3, v + 4);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = index;
        var mesh = new ArrayMesh();
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}

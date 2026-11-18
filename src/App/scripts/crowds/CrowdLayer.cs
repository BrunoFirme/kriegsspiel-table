using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CrowdLayer
{
    const float FigureM = 600, LiftM = 0.25f, SquareM = 1.4f, SquarePx = 3, ChildSquare = 0.7f;
    const int Stride = 16, Least = 64;

    static readonly int[] Undyed = [0x8a7b66, 0x6f6553, 0x9c9283, 0x5b5145, 0xb3a68f, 0x7a6e5c, 0x4e463d, 0xa59a86];
    static readonly int[] Dyed = [0x7a2a24, 0x2d4a6e, 0x3f5a2e, 0x8a6a1e, 0x4b2f55];
    static readonly int[] Linen = [0xd8d0bd, 0xc9c0aa, 0xbfb59c, 0x9a917d];
    static readonly int[] Hairs = [0x2a1d14, 0x4a3524, 0x6b4c2e, 0x9b7a4e, 0x1a1512, 0x7d7a74];
    const int ArmyCoat = 0x2c3e6b;
    const double DyedShare = 0.12;

    readonly TableContext _c;
    readonly MultiMeshInstance3D[] _figures = new MultiMeshInstance3D[CrowdRoster.Outfits];
    readonly MultiMeshInstance3D _squares;
    readonly float[][] _figureData = new float[CrowdRoster.Outfits][];
    float[] _squareData = new float[Least * Stride];
    readonly int[] _counts = new int[CrowdRoster.Outfits];
    readonly ShaderMaterial _squareMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_square.gdshader") };
    List<CrowdPerson> _people = new();
    float[] _looks = [];
    float[] _heights = [];
    CrowdTimes[] _times = [];

    public readonly Node3D Root = new() { Name = "Crowds" };

    public int People => _people.Count;

    public bool On
    {
        get => Root.Visible;
        set => Root.Visible = value;
    }
    public int Out { get; private set; }

    public CrowdLayer(TableContext c)
    {
        _c = c;
        var figure = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crowd_figure.gdshader") };
        for (int o = 0; o < _figures.Length; o++)
        {
            _figureData[o] = new float[Least * Stride];
            Root.AddChild(_figures[o] = Instance(CrowdMeshes.Figure(o), figure, "Figures" + o));
        }
        Root.AddChild(_squares = Instance(CrowdMeshes.Square, _squareMaterial, "Squares"));
    }

    static MultiMeshInstance3D Instance(Mesh mesh, Material material, string name) => new()
    {
        Name = name, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = Least, VisibleInstanceCount = 0 },
        CustomAabb = new Aabb(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f)),
    };

    public void Lay()
    {
        var view = _c.S.View;
        double upm = view.Calibration.UnitsPerMeter ?? 0;
        _people = view.Built.Where(b => b.Kind == BuiltKinds.Settlement)
            .SelectMany(b => view.Sheets.FirstOrDefault(s => s.Id == b.Sheet) is { } sheet ? CrowdRoster.Of(b, sheet, upm) : []).ToList();
        _looks = new float[_people.Count * 4];
        _heights = new float[_people.Count * 3];
        _times = _people.Select(p => CrowdDay.Times(p, upm)).ToArray();
        float Ground(Vec2 at) => _c.SheetsTop + (float)_c.S.Ground.DrawnAt(at);
        for (int i = 0; i < _people.Count; i++)
        {
            var p = _people[i];
            (_heights[i * 3], _heights[i * 3 + 1], _heights[i * 3 + 2]) = (Ground(p.Home.At), Ground(p.Work.At), Ground(p.Third.At));
            var dice = new KTable.Core.Model.Dice(p.Seed, 3);
            int outer = p.Outfit == CrowdRoster.Soldier ? ArmyCoat : dice.Chance(DyedShare) ? Pick(Dyed, dice) : Pick(Undyed, dice);
            _looks[i * 4] = outer;
            _looks[i * 4 + 1] = Pick(p.Outfit is CrowdRoster.Gown or CrowdRoster.Apron or CrowdRoster.Smock ? Linen : Undyed, dice);
            _looks[i * 4 + 2] = Pick(Hairs, dice);
            _looks[i * 4 + 3] = dice.Below(3);
        }
    }

    static int Pick(int[] from, KTable.Core.Model.Dice dice) => from[dice.Below(from.Length)];

    public void Tick()
    {
        var view = _c.S.View;
        float upm = (float)(view.Calibration.UnitsPerMeter ?? 0);
        if (upm <= 0 || _people.Count == 0 || !On) return;
        long clock = view.Clock.Seconds;
        var cam = _c.Rig.Cam;
        var eye = cam.GlobalPosition;
        bool flat = cam.Projection == Camera3D.ProjectionType.Orthogonal;
        float viewH = Math.Max(1, cam.GetViewport().GetVisibleRect().Size.Y);
        _squareMaterial.SetShaderParameter("pixel_size", (flat ? 2 / viewH : 2 * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2) / viewH) * SquarePx);
        Array.Clear(_counts);
        int squares = 0, outside = 0;
        float near = FigureM * upm, nearSq = near * near;
        for (int i = 0; i < _people.Count; i++)
        {
            var p = _people[i];
            if (!CrowdDay.Where(p, _times[i], clock, upm, out var w)) continue;
            outside++;
            var at = Coords.ToWorld(w.At, Height(i, w));
            float yaw = (float)(Math.PI - w.Heading);
            float h = (float)p.HeightM * upm;
            float stride = (float)(w.StrideM / Math.Max(p.HeightM * upm * 1.1, 1e-6));
            float custom = (w.Moving ? 16 : 0) + 2 * (int)_looks[i * 4 + 3] + (stride - MathF.Floor(stride));
            if (!flat && at.DistanceSquaredTo(eye) < nearSq)
            {
                int o = p.Outfit;
                Write(ref _figureData[o], _counts[o]++, at, yaw, h, i, custom);
            }
            else
            {
                float size = SquareM * upm * (p.Child ? ChildSquare : 1);
                Write(ref _squareData, squares++, at + new Vector3(0, LiftM * upm, 0), 0, size, i, 0);
            }
        }
        Out = outside;
        for (int o = 0; o < _figures.Length; o++) Push(_figures[o], _figureData[o], _counts[o]);
        Push(_squares, _squareData, squares);
    }

    float Height(int person, CrowdWhere w)
    {
        int k = person * 3;
        float along = (float)w.Along;
        return w.Leg switch
        {
            CrowdLeg.ToWork => Mathf.Lerp(_heights[k], _heights[k + 1], along),
            CrowdLeg.AtWork => _heights[k + 1],
            CrowdLeg.ToThird => Mathf.Lerp(_heights[k + 1], _heights[k + 2], along),
            CrowdLeg.AtThird => _heights[k + 2],
            _ => Mathf.Lerp(_heights[k + 2], _heights[k], along),
        };
    }

    void Write(ref float[] data, int n, Vector3 at, float yaw, float scale, int person, float phase)
    {
        if ((n + 1) * Stride > data.Length) Array.Resize(ref data, data.Length * 2);
        float c = MathF.Cos(yaw) * scale, s = MathF.Sin(yaw) * scale;
        int k = n * Stride;
        data[k] = c; data[k + 1] = 0; data[k + 2] = s; data[k + 3] = at.X;
        data[k + 4] = 0; data[k + 5] = scale; data[k + 6] = 0; data[k + 7] = at.Y;
        data[k + 8] = -s; data[k + 9] = 0; data[k + 10] = c; data[k + 11] = at.Z;
        data[k + 12] = _looks[person * 4];
        data[k + 13] = _looks[person * 4 + 1];
        data[k + 14] = _looks[person * 4 + 2];
        data[k + 15] = phase;
    }

    static void Push(MultiMeshInstance3D node, float[] data, int count)
    {
        var many = node.Multimesh;
        int room = data.Length / Stride;
        if (many.InstanceCount != room) many.InstanceCount = room;
        many.VisibleInstanceCount = count;
        if (count > 0) RenderingServer.MultimeshSetBuffer(many.GetRid(), data);
    }
}

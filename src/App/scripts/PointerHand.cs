using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

public partial class PointerHand : Node3D
{
    static readonly Vector3 PalmSize = new(0.78f, 0.2f, 0.86f);
    const float PalmSquareness = 0.45f;
    const float KnucklesAt = -0.4f;
    const float FingerRise = 0.02f;
    static readonly (float At, float HalfWidth, float HalfHeight)[] SleeveStations =
        [(0.12f, 0.33f, 0.08f), (0.38f, 0.31f, 0.105f), (0.52f, 0.28f, 0.125f), (0.76f, 0.305f, 0.145f)];
    const float SleeveSquareness = 0.7f;
    const float SleeveEndDepth = 0.05f;
    const float HandPitchDeg = 24;
    const float PoseRate = 7f;
    const float FollowStiffness = 420f;
    const float TipPerSpeed = 0.012f;
    const float MostTip = 0.2f;
    const float TipStiffness = 160f;
    const float GloveDeeper = 0.12f;
    const float Taper = 0.88f;

    sealed record Joint(float Length, float Point, float Pinch);
    sealed record FingerShape(Vector3 Base, Vector3 Turn, float Radius, Joint[] Joints);

    static readonly FingerShape Thumb = new(new(-0.35f, -0.03f, -0.02f), new(18, -48, -62), 0.09f,
        [new(0.21f, 20, 34), new(0.17f, 18, 26), new(0.13f, 14, 20)]);
    static readonly FingerShape Index = new(new(-0.26f, FingerRise, KnucklesAt), new(0, -3, 0), 0.08f,
        [new(0.26f, 2, 36), new(0.16f, 2, 40), new(0.12f, 0, 22)]);
    static readonly FingerShape[] Others =
    [
        new(new(-0.09f, FingerRise, KnucklesAt - 0.02f), new(0, 0, 0), 0.082f, [new(0.29f, 72, 46), new(0.18f, 94, 56), new(0.13f, 52, 30)]),
        new(new(0.08f, FingerRise, KnucklesAt), new(0, 2, 0), 0.078f, [new(0.27f, 76, 54), new(0.17f, 96, 62), new(0.12f, 54, 34)]),
        new(new(0.24f, FingerRise, KnucklesAt + 0.05f), new(0, 6, 0), 0.07f, [new(0.21f, 80, 62), new(0.13f, 98, 70), new(0.1f, 56, 38)]),
    ];

    sealed record Finger(FingerShape Shape, Node3D[] Joints);

    readonly Node3D _tip = new();
    readonly Node3D _hand = new();
    readonly Node3D _endOfIndex = new();
    readonly List<Finger> _fingers = new();
    readonly StandardMaterial3D _glove = new() { Roughness = 0.4f, Metallic = 0, RimEnabled = true, Rim = 0.18f, RimTint = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
    float _pinch;
    Vector3 _velocity;
    Vector2 _tilt, _tiltVelocity;

    public PointerHand(Color hue)
    {
        AddChild(_tip);
        _tip.AddChild(_hand);
        Recolor(hue);
        var leather = _glove;
        _hand.AddChild(new MeshInstance3D { Mesh = RoundedBlock(PalmSize, PalmSquareness), MaterialOverride = leather });
        foreach (var knuckle in new[] { -0.26f, -0.09f, 0.08f, 0.24f })
            _hand.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 16, Rings = 8 }, MaterialOverride = leather,
                Position = new Vector3(knuckle, FingerRise + 0.02f, KnucklesAt + 0.02f), Scale = new Vector3(1, 0.5f, 1),
            });
        _hand.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.2f, Height = 0.4f, RadialSegments = 20, Rings = 10 }, MaterialOverride = leather,
            Position = new Vector3(-0.24f, -0.05f, 0.08f), Scale = new Vector3(0.9f, 0.55f, 1.3f),
        });
        _hand.AddChild(new MeshInstance3D { Mesh = Sleeve(), MaterialOverride = leather });
        _fingers.Add(MakeFinger(Thumb, leather));
        var index = MakeFinger(Index, leather);
        _fingers.Add(index);
        index.Joints[^1].AddChild(_endOfIndex);
        _endOfIndex.Position = new Vector3(0, 0, -(Index.Joints[^1].Length + Index.Radius * Taper * Taper));
        foreach (var f in Others) _fingers.Add(MakeFinger(f, leather));
        Pose();
    }

    public void Recolor(Color hue) => _glove.AlbedoColor = hue.Darkened(GloveDeeper);

    Finger MakeFinger(FingerShape s, Material leather)
    {
        var root = new Node3D { Position = s.Base, RotationDegrees = s.Turn };
        _hand.AddChild(root);
        var joints = new Node3D[s.Joints.Length];
        Node3D parent = root;
        var radius = s.Radius;
        for (var i = 0; i < s.Joints.Length; i++)
        {
            var joint = new Node3D { Position = i == 0 ? Vector3.Zero : new Vector3(0, 0, -s.Joints[i - 1].Length) };
            parent.AddChild(joint);
            joint.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = radius, Height = s.Joints[i].Length + radius * 2, RadialSegments = 16, Rings = 6 },
                MaterialOverride = leather, Position = new Vector3(0, 0, -s.Joints[i].Length / 2), RotationDegrees = new Vector3(90, 0, 0),
                Scale = new Vector3(1, 1, 0.86f),
            });
            joints[i] = joint;
            parent = joint;
            radius *= Taper;
        }
        return new Finger(s, joints);
    }

    static ArrayMesh RoundedBlock(Vector3 size, float squareness)
    {
        const int Rings = 18, Segments = 28;
        static float Power(float x, float e) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), e);
        Vector3 Point(int i, int j, out Vector3 normal)
        {
            float v = -Mathf.Pi / 2 + Mathf.Pi * i / Rings, u = -Mathf.Pi + Mathf.Tau * j / Segments;
            float cv = Mathf.Cos(v), sv = Mathf.Sin(v), cu = Mathf.Cos(u), su = Mathf.Sin(u);
            normal = new Vector3(Power(cv, 2 - squareness) * Power(cu, 2 - squareness) / size.X, Power(sv, 2 - squareness) / size.Y, Power(cv, 2 - squareness) * Power(su, 2 - squareness) / size.Z)
                .Normalized();
            return new Vector3(Power(cv, squareness) * Power(cu, squareness) * size.X / 2, Power(sv, squareness) * size.Y / 2, Power(cv, squareness) * Power(su, squareness) * size.Z / 2);
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Add(int i, int j)
        {
            var p = Point(i, j, out var n);
            st.SetNormal(n);
            st.AddVertex(p);
        }
        for (var i = 0; i < Rings; i++)
            for (var j = 0; j < Segments; j++)
            {
                Add(i, j);
                Add(i + 1, j);
                Add(i + 1, j + 1);
                Add(i, j);
                Add(i + 1, j + 1);
                Add(i, j + 1);
            }
        return st.Commit();
    }

    static ArrayMesh Sleeve()
    {
        const int Rings = 24, EndRings = 6, Segments = 32;
        const float Step = 1e-3f;
        static float Power(float x, float e) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), e);
        var from = SleeveStations[0].At;
        var to = SleeveStations[^1].At;
        var middle = new Vector3(0, 0, (from + to) / 2);
        (float z, float w, float h) Section(float s)
        {
            if (s > Rings)
            {
                var a = Mathf.Min(s - Rings, EndRings) / EndRings * Mathf.Pi / 2;
                var (_, ew, eh) = Section(Rings);
                return (to + SleeveEndDepth * Mathf.Sin(a), ew * Mathf.Cos(a), eh * Mathf.Cos(a));
            }
            var z = Mathf.Lerp(from, to, s / Rings);
            var i = 1;
            while (i < SleeveStations.Length - 1 && SleeveStations[i].At < z) i++;
            var (za, wa, ha) = SleeveStations[i - 1];
            var (zb, wb, hb) = SleeveStations[i];
            var k = Mathf.SmoothStep(0, 1, (z - za) / (zb - za));
            return (z, Mathf.Lerp(wa, wb, k), Mathf.Lerp(ha, hb, k));
        }
        Vector3 Point(float u, float s)
        {
            var (z, w, h) = Section(s);
            return new Vector3(Power(Mathf.Cos(u), SleeveSquareness) * w, Power(Mathf.Sin(u), SleeveSquareness) * h, z);
        }
        Vector3 Normal(float u, float s)
        {
            var n = (Point(u + Step, s) - Point(u - Step, s)).Cross(Point(u, s + Step) - Point(u, s - Step));
            if (n.LengthSquared() < 1e-12f) return Vector3.Back;
            n = n.Normalized();
            return n.Dot(Point(u, s) - middle) < 0 ? -n : n;
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(params (float u, float s)[] corners)
        {
            var p = Array.ConvertAll(corners, c => Point(c.u, c.s));
            var facing = (p[1] - p[0]).Cross(p[2] - p[0]).Dot(Normal(corners[0].u, corners[0].s)) > 0;
            foreach (var c in facing ? new[] { corners[0], corners[2], corners[1] } : corners) { st.SetNormal(Normal(c.u, c.s)); st.AddVertex(Point(c.u, c.s)); }
        }
        for (var r = 0; r < Rings + EndRings; r++)
            for (var j = 0; j < Segments; j++)
            {
                float u0 = Mathf.Tau * j / Segments, u1 = Mathf.Tau * (j + 1) / Segments;
                Triangle((u0, r), (u1, r), (u1, r + 1));
                Triangle((u0, r), (u1, r + 1), (u0, r + 1));
            }
        return st.Commit();
    }

    public Vector3 Above =>_hand.GlobalTransform * new Vector3(0, PalmSize.Y + 0.25f, 0.1f);

    public void Jump(Vector3 to)
    {
        Position = to;
        _velocity = Vector3.Zero;
        _tilt = _tiltVelocity = Vector2.Zero;
    }

    public void Step(Vector3 target, float yaw, bool pinching, float size, double delta)
    {
        var dt = (float)Math.Min(delta, 1 / 30.0);
        _velocity += ((target - Position) * FollowStiffness - _velocity * 2 * Mathf.Sqrt(FollowStiffness)) * dt;
        Position += _velocity * dt;
        var want = new Vector2(_velocity.X, _velocity.Z) * (-TipPerSpeed / Mathf.Max(size, 1e-3f));
        if (want.Length() > MostTip) want = want.Normalized() * MostTip;
        _tiltVelocity += ((want - _tilt) * TipStiffness - _tiltVelocity * 2 * Mathf.Sqrt(TipStiffness)) * dt;
        _tilt += _tiltVelocity * dt;
        var axis = Vector3.Up.Cross(new Vector3(_tilt.X, 0, _tilt.Y));
        _tip.Basis = axis.LengthSquared() > 1e-8f ? new Basis(axis.Normalized(), _tilt.Length()) : Basis.Identity;
        _pinch = Mathf.MoveToward(_pinch, pinching ? 1 : 0, PoseRate * dt);
        Pose();
        _hand.Basis = new Basis(Vector3.Up, yaw) * Basis.FromEuler(new Vector3(Mathf.DegToRad(-HandPitchDeg), 0, 0));
        _hand.Position = Vector3.Zero;
        _hand.Position = -_tip.ToLocal(_endOfIndex.GlobalPosition);
        Scale = Vector3.One * size;
    }

    void Pose()
    {
        var k = _pinch * _pinch * (3 - 2 * _pinch);
        foreach (var f in _fingers)
            for (var i = 0; i < f.Joints.Length; i++)
                f.Joints[i].RotationDegrees = new Vector3(-Mathf.Lerp(f.Shape.Joints[i].Point, f.Shape.Joints[i].Pinch, k), 0, 0);
    }
}

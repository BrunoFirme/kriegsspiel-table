using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ThrownDice(TableContext c)
{
    const float DieShare = 0.12f, SpreadShare = 1.4f, SpinLeast = 9f, SpinMost = 18f, SpinFade = 1.6f, SettleShare = 0.35f, FallHeights = 5.4f;
    const double RestSeconds = 4, FadeSeconds = 0.35;
    const int Sparks = 18;

    sealed record Flight(Node3D Pivot, PieceDef Def, List<(Vector3 At, float T)> Stops, float Size, float Low, Vector3 Axis, float Spin, Quaternion Start, Quaternion Rest, float Fall, Color Colour);

    readonly List<(Flight Flight, ulong Born)> _flying = new();

    public double Throw(RollDice rd, Color colour)
    {
        if (rd.Thrown is not { Count: > 5 } data || !rd.ThrowIsSound || Kind(rd.Sides) is not { } kind) return 0;
        float h = (float)data[0];
        var stops = new List<(Vector3 At, float T)>();
        for (int i = 1; i + 3 < data.Count; i += 4) stops.Add((new Vector3((float)data[i], (float)data[i + 1], (float)data[i + 2]), (float)data[i + 3]));
        var along = (stops[^1].At - stops[0].At) with { Y = 0 };
        var side = along.LengthSquared() > 1e-12f ? along.Normalized().Cross(Vector3.Up) : Vector3.Right;
        var rng = new Random((int)(rd.Seq % int.MaxValue));
        float size = h * DieShare;
        for (int k = 0; k < rd.Results.Count; k++)
        {
            if (Faced(kind.Def, rd.Results[k]) is not { } up) continue;
            var shift = side * ((k - (rd.Results.Count - 1) / 2f) * size * SpreadShare) + along.Normalized() * (float)(rng.NextDouble() * size);
            var pivot = Made(kind, size, out var corners);
            c.Scene.AddChild(pivot);
            var axis = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).Normalized();
            float spin = SpinLeast + (float)rng.NextDouble() * (SpinMost - SpinLeast);
            var start = PieceNode.AnyTurn(rng);
            var spunAtEnd = new Quaternion(axis, spin / SpinFade * (1 - Mathf.Exp(-SpinFade * stops[^1].T))) * start;
            var rest = ArcTo(spunAtEnd * up, Vector3.Up) * spunAtEnd;
            float low = corners.Length > 0 ? -corners.Min(v => (rest * v).Y) : size / 2;
            var flight = new Flight(pivot, kind.Def, stops.Select(s => (s.At + shift, s.T)).ToList(), size, low, axis, spin, start, rest, FallHeights * h, colour);
            _flying.Add((flight, Time.GetTicksMsec()));
            Place(flight, 0);
        }
        return stops[^1].T;
    }

    public int Flying => _flying.Count;

    public List<string> Showing() =>
        _flying.Select(f => f.Flight.Def.Faces.Where(face => face.Up is { Length: 3 })
            .OrderByDescending(face => (f.Flight.Pivot.Quaternion * new Vector3((float)face.Up[0], (float)face.Up[1], (float)face.Up[2]).Normalized()).Dot(Vector3.Up)).First().Value).ToList();

    public void Tick()
    {
        ulong now = Time.GetTicksMsec();
        foreach (var (flight, born) in _flying.ToList())
        {
            if (!GodotObject.IsInstanceValid(flight.Pivot))
            {
                _flying.Remove((flight, born));
                continue;
            }
            float t = (now - born) / 1000f, end = flight.Stops[^1].T;
            Place(flight, Mathf.Min(t, end));
            if (t < end + RestSeconds) continue;
            _flying.Remove((flight, born));
            Vanish(flight);
        }
    }

    void Place(Flight f, float t)
    {
        int k = 0;
        while (k < f.Stops.Count - 2 && t > f.Stops[k + 1].T) k++;
        var (from, t0) = f.Stops[k];
        var (to, t1) = f.Stops[k + 1];
        float span = Mathf.Max(t1 - t0, 1e-3f), tau = Mathf.Clamp(t - t0, 0, span);
        float rise = (to.Y - from.Y + 0.5f * f.Fall * span * span) / span;
        var at = from.Lerp(to, tau / span) with { Y = from.Y + rise * tau - 0.5f * f.Fall * tau * tau };
        float end = f.Stops[^1].T;
        var spun = new Quaternion(f.Axis, f.Spin / SpinFade * (1 - Mathf.Exp(-SpinFade * t))) * f.Start;
        float settle = Mathf.SmoothStep(end * (1 - SettleShare), end, t);
        f.Pivot.Quaternion = spun.Slerp(f.Rest, settle);
        float lift = k == 0 ? f.Size / 2 * (tau / span) : Mathf.Lerp(f.Size / 2, f.Low, settle);
        f.Pivot.Position = at + Vector3.Up * lift;
    }

    static void Vanish(Flight f)
    {
        var parent = f.Pivot.GetParent<Node3D>();
        var burst = FlightFx.Motes(f.Colour, f.Size * 4, Sparks, true);
        parent.AddChild(burst);
        burst.Position = f.Pivot.Position;
        burst.Finished += burst.QueueFree;
        burst.Emitting = true;
        var tween = f.Pivot.CreateTween();
        tween.TweenProperty(f.Pivot, Node3D.PropertyName.Scale.ToString(), Vector3.One * 0.01f, FadeSeconds).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Back);
        tween.TweenCallback(Callable.From(f.Pivot.QueueFree));
    }

    sealed record DieKind(PieceDef Def, ArrayMesh Mesh, Texture2D? Picture);

    DieKind? Kind(int sides)
    {
        if (c.S.Store.Pack(StandardPacks.Dice) is not { } pack) return null;
        var def = pack.Pieces.FirstOrDefault(d => d.Faces.Count == sides && d.Tags.Contains("d" + sides) && !d.Tags.Contains("picture"));
        if (def?.Mesh is not { } meshKey || !pack.Assets.TryGetValue(meshKey, out var meshFile)) return null;
        if (ModelCache.Get(c.S.Assets.Abs(pack.Id, meshFile.File)) is not { } mesh) return null;
        var picture = def.Texture is { } texKey && pack.Assets.TryGetValue(texKey, out var tex) ? c.S.Assets.Texture(pack.Id, tex.File, null, maxSize: 1024) : null;
        return new DieKind(def, mesh, picture);
    }

    static Vector3? Faced(PieceDef def, int result)
    {
        var face = def.Faces.FirstOrDefault(f => f.Value == result.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return face?.Up is { Length: 3 } up ? new Vector3((float)up[0], (float)up[1], (float)up[2]).Normalized() : null;
    }

    static Node3D Made(DieKind kind, float size, out Vector3[] corners)
    {
        var box = kind.Mesh.GetAabb();
        float scale = size / Mathf.Max(box.Size[(int)box.Size.MaxAxisIndex()], 1e-6f);
        var model = new MeshInstance3D
        {
            Mesh = kind.Mesh, Position = -box.GetCenter() * scale, Scale = Vector3.One * scale,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = kind.Picture, Roughness = 0.32f, MetallicSpecular = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Back,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            },
        };
        var pivot = new Node3D();
        pivot.AddChild(model);
        corners = kind.Mesh.GetSurfaceCount() > 0 ? kind.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(v => (v - box.GetCenter()) * scale).ToArray() : [];
        return pivot;
    }

    static Quaternion ArcTo(Vector3 from, Vector3 to)
    {
        var d = from.Dot(to);
        if (d > 0.9999f) return Quaternion.Identity;
        if (d < -0.9999f) return new Quaternion(Vector3.Right, Mathf.Pi);
        return new Quaternion(from.Cross(to).Normalized(), Mathf.Acos(Mathf.Clamp(d, -1, 1)));
    }
}

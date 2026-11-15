using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

public enum WallView { Up, Cutaway, Down }

sealed class WallCutaway
{
    public const string BuildingTag = KTable.Core.Model.Doors.BuildingTag;
    const float FeetShare = 0.01f;
    const float CutMetres = 1.1f, CutShare = 0.35f, UnderNextMetres = 0.05f, UnderNextShare = 0.02f;
    const double LookEvery = 0.5;

    readonly PieceLayer _pieces;
    readonly Dictionary<PieceNode, PieceCut> _buildings = new();
    readonly List<PieceNode> _inside = new();
    readonly HashSet<PieceNode> _hidden = new();

    public Func<PieceNode, bool> Driven { get; set; } = _ => false;
    double _lookIn;

    public WallCutaway(PieceLayer pieces) { _pieces = pieces; }

    public WallView Mode { get; private set; }
    public int Storey { get; private set; }
    public int Storeys { get; private set; } = 1;
    public event Action? Changed;

    public void Set(WallView mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        _lookIn = 0;
        if (mode == WallView.Up) Clear();
        Show();
        Changed?.Invoke();
    }

    public void Next() => Set(Mode switch { WallView.Up => WallView.Cutaway, WallView.Cutaway => WallView.Down, _ => WallView.Up });

    public void Step(int by)
    {
        if (Mode == WallView.Up) Mode = WallView.Cutaway;
        Storey = Math.Clamp(Storey + by, 0, Math.Max(0, Storeys - 1));
        _lookIn = 0;
        Changed?.Invoke();
    }

    void Show()
    {
        foreach (var n in _hidden.Where(GodotObject.IsInstanceValid)) n.Visible = true;
        _hidden.Clear();
    }

    public bool Hides(Vector3 global) =>
        Mode != WallView.Up && _buildings.Keys.Any(b => GodotObject.IsInstanceValid(b) && b.Hidden is { } hides && Within(b, global) && hides(global + Vector3.Up * Lift(b)));

    static float Lift(PieceNode b) => b.Model?.Mesh is { } mesh ? mesh.GetAabb().Size.Y * b.Model.Scale.Y * FeetShare : 0;

    static bool Within(PieceNode b, Vector3 global)
    {
        if (b.Model is not { Mesh: { } mesh } model || !model.IsInsideTree()) return false;
        var local = model.GlobalTransform.AffineInverse() * global;
        var box = mesh.GetAabb();
        return local.X >= box.Position.X && local.X <= box.End.X && local.Z >= box.Position.Z && local.Z <= box.End.Z;
    }

    void Clear()
    {
        foreach (var cut in _buildings.Values.Where(c => GodotObject.IsInstanceValid(c.Piece))) cut.Set(null);
        _buildings.Clear();
    }

    public void Tick(double delta, Vector3 eye)
    {
        if (Mode == WallView.Up) return;
        _lookIn -= delta;
        if (_lookIn <= 0)
        {
            _lookIn = LookEvery;
            Look();
        }
        foreach (var (b, cut) in _buildings)
            if (GodotObject.IsInstanceValid(b) && b.IsInsideTree() && b.Model is { Mesh: { } mesh } model) cut.Set(CutOf(model, mesh, Floors(b), eye));
        foreach (var n in _inside)
        {
            if (!GodotObject.IsInstanceValid(n) || Driven(n)) continue;
            bool hide = Hides(n.GlobalPosition);
            if (hide == _hidden.Contains(n)) continue;
            n.Visible = !hide;
            if (hide) _hidden.Add(n);
            else _hidden.Remove(n);
        }
    }

    const float UnderGroundShare = 0.01f;

    public static float[] Floors(PieceNode b)
    {
        if (b.Model is not { Mesh: { } mesh } model || !GodotObject.IsInstanceValid(model)) return [];
        float ground = -model.Position.Y / Mathf.Max(model.Scale.Y, 1e-6f), slack = mesh.GetAabb().Size.Y * UnderGroundShare;
        var kept = Array.FindAll(MeshTops.Floors(mesh), f => f >= ground - slack);
        return kept.Length > 0 ? kept : [ground];
    }

    public float? FloorAt(Vector3 table)
    {
        if (Mode == WallView.Up) return null;
        foreach (var b in _buildings.Keys)
        {
            if (!GodotObject.IsInstanceValid(b) || !b.IsInsideTree() || b.Model is not { Mesh: { } mesh } model) continue;
            var xf = model.TableTransform();
            var local = xf.AffineInverse() * table;
            var box = mesh.GetAabb();
            if (local.X < box.Position.X || local.X > box.End.X || local.Z < box.Position.Z || local.Z > box.End.Z) continue;
            var floors = Floors(b);
            if (floors.Length == 0) continue;
            return (xf * new Vector3(local.X, floors[Math.Min(Storey, floors.Length - 1)], local.Z)).Y;
        }
        return null;
    }

    void Look()
    {
        var now = _pieces.Nodes.Where(n => GodotObject.IsInstanceValid(n) && n.HasFloors).ToHashSet();
        foreach (var gone in _buildings.Keys.Where(b => !now.Contains(b)).ToList()) _buildings.Remove(gone);
        foreach (var b in now) _buildings.TryAdd(b, new PieceCut(b));
        _inside.Clear();
        if (now.Count > 0) _inside.AddRange(_pieces.Nodes.Where(n => GodotObject.IsInstanceValid(n) && !n.HasFloors && !n.IsPart && now.Any(b => Within(b, n.GlobalPosition))));
        foreach (var gone in _hidden.Where(n => !_inside.Contains(n)).ToList())
        {
            if (GodotObject.IsInstanceValid(gone)) gone.Visible = true;
            _hidden.Remove(gone);
        }
        int most = now.Select(b => Math.Max(1, Floors(b).Length)).DefaultIfEmpty(1).Max();
        if (most == Storeys) return;
        Storeys = most;
        Storey = Math.Min(Storey, most - 1);
        Changed?.Invoke();
    }

    WallCut CutOf(MeshInstance3D model, Mesh mesh, float[] floors, Vector3 eye)
    {
        var box = mesh.GetAabb();
        var xf = model.GlobalTransform;
        var middle = xf * box.GetCenter();
        float At(float local) => (xf * new Vector3(0, local, 0)).Y;
        int k = Math.Min(Storey, floors.Length - 1);
        float floor = floors[k], next = k + 1 < floors.Length ? floors[k + 1] : box.End.Y;
        float low = At(floor + Math.Min(CutMetres, CutShare * (next - floor)));
        float top = k + 1 < floors.Length ? At(next - Math.Min(UnderNextMetres, UnderNextShare * (next - floor))) : float.MaxValue;
        return new WallCut(low, top, Mode == WallView.Cutaway, new Vector2(middle.X, middle.Z), new Vector2(eye.X, eye.Z));
    }
}

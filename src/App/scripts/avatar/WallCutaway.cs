using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class WallCutaway
{
    public const string BuildingTag = "building";
    const float CutMetres = 1.1f;
    const double LookEvery = 0.5;

    readonly PieceLayer _pieces;
    readonly Dictionary<PieceNode, PieceCut> _buildings = new();
    double _lookIn;

    public WallCutaway(PieceLayer pieces) { _pieces = pieces; }

    public bool On { get; private set; }

    public void Set(bool on)
    {
        if (on == On) return;
        On = on;
        _lookIn = 0;
        if (on) return;
        foreach (var cut in _buildings.Values.Where(c => GodotObject.IsInstanceValid(c.Piece))) cut.Set(null);
        _buildings.Clear();
    }

    public void Tick(double delta)
    {
        if (!On) return;
        _lookIn -= delta;
        if (_lookIn <= 0)
        {
            _lookIn = LookEvery;
            var now = _pieces.Nodes.Where(n => GodotObject.IsInstanceValid(n) && n.ShowsModel && n.Def?.Tags.Contains(BuildingTag) == true).ToHashSet();
            foreach (var gone in _buildings.Keys.Where(b => !now.Contains(b)).ToList()) _buildings.Remove(gone);
            foreach (var b in now) _buildings.TryAdd(b, new PieceCut(b));
        }
        foreach (var (b, cut) in _buildings)
            if (GodotObject.IsInstanceValid(b) && b.IsInsideTree()) cut.Set(b.GlobalPosition.Y + CutMetres * (float)b.Piece.Scale);
    }
}

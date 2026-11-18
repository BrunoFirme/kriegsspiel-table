using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class ActedBodies(TableContext c, PieceLayer pieces, Func<PieceNode, bool> taken)
{
    const double LookEvery = 0.25;

    readonly Dictionary<PieceNode, Rigged> _posed = new();
    double _lookIn;

    public int Count => _posed.Count;

    public void Tick(double delta)
    {
        if ((_lookIn -= delta) <= 0)
        {
            _lookIn = LookEvery;
            Look();
        }
        foreach (var (node, rigged) in _posed.ToList())
        {
            if (!GodotObject.IsInstanceValid(node) || node.Piece.Act == null || taken(node))
            {
                Drop(node);
                continue;
            }
            rigged.Act = node.Piece.Act;
            rigged.Step(node.GlobalPosition, HeightOf(node), (float)delta);
        }
    }

    void Look()
    {
        var wanted = pieces.Nodes.Where(n => GodotObject.IsInstanceValid(n) && n.Piece.Act != null && Rigged.Wants(n.Def) && !taken(n)).ToHashSet();
        foreach (var gone in _posed.Keys.Where(n => !wanted.Contains(n)).ToList()) Drop(gone);
        foreach (var n in wanted.Where(n => !_posed.ContainsKey(n))) _posed[n] = new Rigged(n, c);
    }

    void Drop(PieceNode node)
    {
        if (_posed.Remove(node, out var rigged)) rigged.Stop();
    }

    static float HeightOf(PieceNode node) =>
        node.Model is { } model && GodotObject.IsInstanceValid(model) ? Mathf.Max(model.GetAabb().Size.Y * model.GlobalBasis.Scale.Y, 1e-4f) : 1;
}

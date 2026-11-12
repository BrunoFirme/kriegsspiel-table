using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

public partial class ModelStudio
{
    const string PieceKeyHead = "piece:";
    const double BesideTheLast = 0.3, LeastTop = 0.02, MostTop = 2;

    Node3D? _pieceStand;
    PieceNode? _carrier;

    internal static string PieceKey(int index) => PieceKeyHead + index;

    internal int PieceLook => _flat ? Math.Max(0, _flatState) : 0;

    internal IReadOnlyList<PartDef> PieceRows => _def != null && PartRules.Shown(_def, PieceLook) is { } rows ? rows : Array.Empty<PartDef>();

    internal IReadOnlyList<PieceNode> PieceNodes => _carrier != null && IsInstanceValid(_carrier) ? _carrier.Parts : Array.Empty<PieceNode>();

    internal int SelPiece =>
        _selPart is { } key && key.StartsWith(PieceKeyHead, StringComparison.Ordinal) && int.TryParse(key.AsSpan(PieceKeyHead.Length), out int at) && at >= 0 && at < PieceRows.Count ? at : -1;

    internal Size2 PieceGround => _def == null ? new Size2(1, 1) : (_flat && _flatState >= 0 && _flatState < _def.States.Count ? _def.States[_flatState].Size : null) ?? _def.Size;

    double PieceTop => _def == null ? 0 : Math.Clamp((_flat && _flatState >= 0 && _flatState < _def.States.Count ? _def.States[_flatState].Thickness : null) ?? _def.Thickness, LeastTop, MostTop);

    internal void ShowPieces()
    {
        if (_pieceStand != null && IsInstanceValid(_pieceStand)) _pieceStand.QueueFree();
        _pieceStand = null;
        _carrier = null;
        if (_def == null || _studio.S == null || PieceRows.Count == 0) return;
        var p = new PieceInstance { Id = "studio-parts", Def = _def.Id, State = PieceLook };
        try { _carrier = PieceNode.Create(_studio.S, Layers.Umpire, p, ghost: true, lettered: false, partsOnly: true); }
        catch (Exception e)
        {
            GD.PrintErr($"[studio] the pieces on {_def.Name}: {e.Message}");
            return;
        }
        _pieceStand = new Node3D { Name = "Pieces" };
        _pieceStand.AddChild(_carrier);
        _root.AddChild(_pieceStand);
        PlacePieces();
        RedrawOverlay();
    }

    void PlacePieces()
    {
        if (_pieceStand == null || !IsInstanceValid(_pieceStand)) return;
        if (_flat || DrawnBounds() is not { } b)
        {
            _pieceStand.Transform = Transform3D.Identity;
            return;
        }
        float toTable = 1 / Mathf.Max(1e-4f, _doc.Scale);
        _pieceStand.Transform = new Transform3D(Basis.FromScale(Vector3.One * toTable), new Vector3(b.GetCenter().X, b.Position.Y, b.GetCenter().Z));
    }

    PieceNode? ChosenPieceNode()
    {
        int chosen = SelPiece, drawn = 0;
        var rows = PieceRows;
        var nodes = PieceNodes;
        for (int i = 0; i <= chosen && drawn < nodes.Count; i++)
        {
            if (_store.Def(rows[i].Def) == null) continue;
            if (i == chosen) return nodes[drawn];
            drawn++;
        }
        return null;
    }

    const float RingClear = 6, RingWidth = 2, RingCasing = 4.4f;
    const int RingSteps = 48;
    static readonly Color RingShade = new(0, 0, 0, 0.55f);

    void RingChosenPiece(Control o)
    {
        if (ChosenPieceNode() is not { } n) return;
        var middle = n.GlobalTransform * new Vector3(0, n.Thickness / 2, 0);
        if (_cam.IsPositionBehind(middle)) return;
        float reach = Mathf.Max(Mathf.Max(n.Footprint.X, n.Footprint.Y), n.Thickness) / 2;
        var at = _cam.UnprojectPosition(middle);
        float r = at.DistanceTo(_cam.UnprojectPosition(middle + _cam.GlobalBasis.X * reach)) + RingClear;
        o.DrawArc(at, r, 0, Mathf.Tau, RingSteps, RingShade, RingCasing, true);
        o.DrawArc(at, r, 0, Mathf.Tau, RingSteps, Ui.Accent, RingWidth, true);
    }

    Aabb? PiecesBounds()
    {
        if (_pieceStand == null || !IsInstanceValid(_pieceStand)) return null;
        Aabb? all = null;
        foreach (var n in PieceNodes)
        {
            float reach = Mathf.Max(n.Footprint.X, n.Footprint.Y) / 2;
            var box = _pieceStand.Transform * new Aabb(n.Position - new Vector3(reach, 0, reach), new Vector3(reach * 2, n.Thickness, reach * 2));
            all = all is { } a ? a.Merge(box) : box;
        }
        return all;
    }

    Aabb? WholeBounds() => (DrawnBounds(), PiecesBounds()) switch
    {
        ({ } shapes, { } pieces) => shapes.Merge(pieces),
        ({ } shapes, null) => shapes,
        var (_, pieces) => pieces,
    };

    void ChangePieces(string what, Action<List<PartDef>> change, int select)
    {
        if (_def is not { } d || _studio.Sel != d) return;
        int look = PieceLook;
        _studio.Apply(x =>
        {
            change(PartRules.Listed(x, look));
            PartRules.Tidy(x);
        }, what: what);
        if (select >= 0 && select < PieceRows.Count) _selPart = PieceKey(select);
        else if (SelPiece < 0) _selPart = _doc.Parts.FirstOrDefault()?.Id ?? (_flat ? _views.Values.FirstOrDefault(v => v.Face != null)?.Part.Id : null);
        ShowPieces();
        RefreshSelection();
    }

    internal void AddPiece(PieceDef kind)
    {
        int at = PieceRows.Count;
        double top = PieceTop;
        ChangePieces("a piece added", list => list.Add(new PartDef { Def = kind.Id, Y = top }), at);
        Frame();
        _studio.RebuildModelPanels();
    }

    internal void SetPiece(int at, Action<PartDef> set)
    {
        if (at < 0 || at >= PieceRows.Count) return;
        ChangePieces("a piece's place", list => set(list[at]), at);
    }

    internal void DuplicatePiece(int at)
    {
        if (at < 0 || at >= PieceRows.Count) return;
        ChangePieces("a piece duplicated", list =>
        {
            var copy = KTable.Core.Json.Clone(list[at]);
            copy.X = Math.Round(copy.X + BesideTheLast, 4);
            list.Insert(at + 1, copy);
        }, at + 1);
        _studio.RebuildModelPanels();
    }

    internal void RemovePiece(int at)
    {
        if (at < 0 || at >= PieceRows.Count) return;
        ChangePieces("a piece taken off", list => list.RemoveAt(at), Math.Min(at, PieceRows.Count - 2));
        _studio.RebuildModelPanels();
    }

    internal void RepeatPiece(int at, int count, int ranks)
    {
        if (at < 0 || at >= PieceRows.Count) return;
        var like = PieceRows[at];
        var over = PieceGround;
        int first = Enumerable.Range(0, PieceRows.Count).First(i => PieceRows[i].Def == like.Def);
        ChangePieces("pieces in ranks", list => PartRules.Repeat(list, like, count, ranks, over), first);
        Frame();
        _studio.RebuildModelPanels();
    }
}

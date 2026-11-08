using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    const int DeepestPart = 4;
    const double HalfTurnDeg = 180;

    readonly List<PieceNode> _parts = new();
    int _partDepth;
    public bool IsPart { get; private set; }

    public IReadOnlyList<PieceNode> Parts => _parts;

    List<PartDef>? ShownParts => Def == null ? null : Def.States.Any(s => s.Parts != null) ? CurState?.Parts : Def.Parts;

    double? ShownThickness(PieceDef? worn) => worn == null ? CurState?.Thickness : null;

    public float PartsBelow => IsPart || Piece.FaceDown || ShownParts is not { Count: > 0 } shown ? 0 : (float)(Mathf.Max(0, -shown.Min(p => p.Y)) * Piece.Scale);

    void BuildParts()
    {
        _parts.Clear();
        if (PrintingOnly || IsEffect || _partDepth >= DeepestPart || ShownParts is not { Count: > 0 } shown) return;
        foreach (var part in shown)
        {
            if (_s.Def(part.Def) == null) continue;
            var n = new PieceNode
            {
                _s = _s, Layer = Layer, Piece = PartInstance(part), Ghost = Ghost, _layerTint = _layerTint, Lettered = false,
                IsPart = true, _partDepth = _partDepth + 1,
            };
            n.Build();
            AddChild(n);
            _parts.Add(n);
        }
    }

    PieceInstance PartInstance(PartDef part) => new()
    {
        Id = $"{Piece.Id}/part{_parts.Count}", Def = part.Def, Pos = new Vec2(part.X * Piece.Scale, -part.Z * Piece.Scale), Y = part.Y * Piece.Scale,
        RotDeg = HalfTurnDeg + part.Rot, TiltX = part.TiltX, TiltZ = part.TiltZ, FaceDown = part.FaceDown, Scale = Piece.Scale * part.Scale,
        TintOverride = part.Tint, State = part.Face >= 0 ? part.Face : part.State, Face = part.Face,
        Decals = part.Decals?.ToList() ?? new(), Lines = part.Lines?.ToList() ?? new(),
    };

    Transform3D PartRest()
    {
        var b = PoseBasis(Pose(Piece.RotDeg));
        var at = Coords.ToWorld(Piece.Pos, (float)Piece.Y);
        if (!Piece.HasTilt) return new Transform3D(b, at + Vector3.Up * LiftY);
        _tiltHalf = HalfRise(b, Slab);
        return new Transform3D(b, at + Vector3.Up * _tiltHalf - b * SlabMiddle);
    }

    Vector3 LocalTop => Piece.HasTilt ? Transform * SlabMiddle + Vector3.Up * _tiltHalf : Transform * new Vector3(0, Piece.FaceDown ? 0 : _thickness, 0);

    float PartsTop(float own)
    {
        foreach (var p in _parts) own = Mathf.Max(own, (Transform * p.LocalTop).Y);
        return own;
    }

    bool PartsContain(Vector2 local)
    {
        var flip = Basis.FromEuler(new Vector3(0, 0, _flipA));
        foreach (var p in _parts)
        {
            var at = (new Transform3D(flip, Vector3.Zero) * new Transform3D(Basis.FromEuler(new Vector3(0, p._poseYaw, 0)), p.Position)).AffineInverse() * new Vector3(local.X, 0, local.Y);
            var l = new Vector2(at.X, at.Z);
            if (p.ContainsLocal(l) || p.PartsContain(l)) return true;
        }
        return false;
    }
}

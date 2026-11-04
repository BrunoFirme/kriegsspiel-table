using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    bool _shownFaceDown, _flipping;
    public bool Held;
    int _settling;
    public void BeginSettle() => _settling++;
    public void EndSettle() { if (_settling > 0) _settling--; }
    public bool Placed => Held || _settling > 0;
    int _flipGen;
    StandardMaterial3D? _backMat;

    public string CardTitle => Piece.Text.GetValueOrDefault("Name") is { Length: > 0 } nm ? nm : Piece.Label is { Length: > 0 } lb ? lb : Def?.Name ?? Piece.Def;

    public void SetTitleText(string text) => _back.SetTitleText(text);

    public float LiftY => Piece.FaceDown ? _thickness : 0;
    float _poseYaw, _flipA;
    Vector3 Pose(double rotDeg) => PoseFromYaw(Coords.Yaw(rotDeg));
    public Vector3 PoseFromYaw(float yaw)
    {
        _poseYaw = yaw;
        _tilt = Basis.Identity;
        if (!_flipping) _flipA = Piece.FaceDown ? Mathf.Pi : 0;
        return new Vector3(0, _poseYaw, _flipA);
    }
    public Vector3 SlabCentre => Position - Vector3.Up * LiftY + Vector3.Up * _thickness / 2;

    StandardMaterial3D BackMat(bool scissor)
    {
        if (_backMat != null) return _backMat;
        if (CurrentBack() is { } back && _s.Store.PackOfDef(Piece.Def) is { } pack && pack.Assets.TryGetValue(back, out var a))
            return _backMat = Art.Print(pack.Id, a.File, _texSize > 0 ? _texSize : 512, PrintTint(), scissor);
        var (pkg, rel, size) = TexKey();
        return _backMat = Art.Print(pkg, rel, size, PrintTint(), scissor);
    }

    void SyncSide()
    {
        if (Piece.FaceDown == _shownFaceDown)
        {
            if (Piece.FaceDown && !_flipping && _back.Stale) _back.Build();
            return;
        }
        _shownFaceDown = Piece.FaceDown;
        Sounds?.Flip(this);
        _ = FlipAnim(++_flipGen, Piece.FaceDown);
    }

    async Task FlipAnim(int gen, bool toDown)
    {
        _flipping = true;
        _back.Build();
        const double dur = 0.36;
        double t = 0;
        float depth = Footprint.X, th = _thickness, from = toDown ? 0 : Mathf.Pi;
        while (t < dur)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(this) || gen != _flipGen) return;
            t += GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / dur), 0, 1);
            float a = from + Mathf.Pi * (1 - Mathf.Cos(Mathf.Pi * k)) / 2;
            var p = Piece;
            _flipA = a;
            if (Placed) { Rotation = new Vector3(0, _poseYaw, _flipA); continue; }
            float s = Mathf.Abs(Mathf.Sin(a));
            float lift = depth / 2 * s + th * (1 - Mathf.Cos(a)) / 2 + Mathf.Min(0.35f, depth * 0.15f) * s;
            var (baseY, up) = RestAt(p.Pos, p.RotDeg, p.Y, p.Stack);
            _tilt = TiltTo(up);
            Transform = new Transform3D(_tilt * Basis.FromEuler(new Vector3(0, _poseYaw, _flipA)), Coords.ToWorld(p.Pos, baseY) + Up * lift);
        }
        _flipping = false;
        _flipA = Piece.FaceDown ? Mathf.Pi : 0;
        if (!Placed) Rest(Piece.Pos, Piece.RotDeg, Piece.Y, Piece.Stack);
        else Rotation = new Vector3(0, _poseYaw, _flipA);
        if (!Piece.FaceDown) _back.Clear();
        SyncWidgets(force: true);
    }

    public void TakenInHand() => StopDieAnim();

    public bool CardTitleAt(Vec2 point) =>
        Piece.FaceDown && !_flipping && _back.TitleAt(Coords.ToLocal(point, Piece.Pos, Piece.RotDeg));

    public (string name, int value)? CardButtonAt(Vec2 point)
    {
        if (_flipping) return null;
        var local = Coords.ToLocal(point, Piece.Pos, Piece.RotDeg);
        if (_prints.ButtonAt(local, Piece.FaceDown) is { } placed) return placed;
        if (!Piece.FaceDown || !_back.HasButtons) return null;
        return _back.ButtonAt(local);
    }
}

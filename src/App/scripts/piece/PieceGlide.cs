using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public partial class PieceNode
{
    const double GlideHoldSeconds = 1;

    Glide? _glide;
    double _glideTime, _glideHeld;

    public bool Gliding => _glide is { Done: false };

    public bool GlideShown => _glide != null;

    public void StartGlide(GlidePiece g)
    {
        if (IsDie || Placed) return;
        if (_glide is not { Done: false }) (_glide, _glideTime, _glideHeld) = (new Glide(), 0, 0);
        _glide.Fast = g.Fast;
        if (g.To is { } to) _glide.MoveTo(Piece.Pos, to);
        if (g.Rot is { } rot) _glide.TurnTo(Piece.RotDeg, rot);
        if (_glide.Done && _glide.Target == null && _glide.TurnedTo == null) { _glide = null; return; }
        SetProcess(true);
    }

    public void StopGlide() => _glide = null;

    bool StepGlide(double delta)
    {
        if (_glide is not { } g) return false;
        if (Placed) { _glide = null; return false; }
        _glideTime += delta;
        while (_glideTime >= Glide.Step && !g.Done && g.Steps < Glide.MostSteps)
        {
            g.Advance();
            _glideTime -= Glide.Step;
        }
        if (g.Done || g.Steps >= Glide.MostSteps) _glideHeld += delta;
        if (_glideHeld > GlideHoldSeconds)
        {
            _glide = null;
            Resettle();
            return false;
        }
        Rest(g.At ?? g.Target ?? Piece.Pos, g.Heading ?? g.TurnedTo ?? Piece.RotDeg, Piece.Y, Piece.Stack);
        return true;
    }
}

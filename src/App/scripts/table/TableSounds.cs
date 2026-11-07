using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

enum Matter { Wood, Card, Plastic, Dice, Silent }

sealed class TableSounds
{
    readonly TableContext _c;
    readonly Dictionary<Sound, double> _last = new();
    static readonly Random Rng = new();

    public TableSounds(TableContext c) { _c = c; }

    public readonly record struct Heard(Sound Sound, Vector3 At, float Gain, double After);

    public List<Heard>? Kept { get; set; }

    public static Matter Of(PieceNode n)
    {
        if (n.IsDie) return Matter.Dice;
        if (n.IsEffect) return Matter.Silent;
        if (PieceFinish.MatterOf(n.Def) is { } finish) return finish;
        if (n.Def?.Kind == "model") return Matter.Plastic;
        float side = Mathf.Max(0.05f, Mathf.Min(n.Footprint.X, n.Footprint.Y));
        return n.Thickness / side >= 0.08f ? Matter.Wood : Matter.Card;
    }

    static Sound Hit(Matter m, bool heavy) => m switch
    {
        Matter.Wood => heavy ? Sound.WoodHeavy : Sound.Wood,
        Matter.Card => Sound.Card,
        Matter.Plastic => Sound.Plastic,
        _ => Sound.Die,
    };

    public void Lift(IReadOnlyCollection<PieceNode> nodes)
    {
        var heard = nodes.Where(Shown).ToList();
        if (heard.Count == 0) return;
        var at = Middle(heard);
        if (heard.All(n => n.IsDie)) Emit(Sound.DiceGrab, at, 0.4f);
        else Emit(Sound.Lift, at, 0.3f);
    }

    public void Land(PieceNode n, float strength, bool onPiece, double after = 0)
    {
        if (!Shown(n)) return;
        var m = Of(n);
        if (m == Matter.Silent) return;
        Emit(Hit(m, heavy: false), n.GlobalPosition, Gain(m) * Mathf.Clamp(strength, 0.2f, 1) * (onPiece ? 1f : 0.85f), onPiece ? 1.05f : 0.92f, after);
    }

    public void LandMany(IReadOnlyList<(PieceNode n, bool onPiece, double after)> landed, float strength)
    {
        var heard = landed.Where(x => Shown(x.n) && Of(x.n) != Matter.Silent).ToList();
        if (heard.Count == 0) return;
        if (heard.Count == 1) { Land(heard[0].n, strength, heard[0].onPiece, heard[0].after); return; }
        var m = heard.GroupBy(x => Of(x.n)).OrderByDescending(g => g.Count()).First().Key;
        double first = heard.Min(x => x.after);
        float crowd = Mathf.Clamp(0.7f + 0.1f * Mathf.Log(heard.Count) / Mathf.Log(2), 0.7f, 1.2f);
        Emit(Hit(m, heavy: true), Middle(heard.Select(x => x.n)), Gain(m) * Mathf.Clamp(strength, 0.3f, 1) * crowd, 0.9f, first);
        foreach (var x in heard.OrderBy(_ => Rng.Next()).Take(3))
            Emit(Hit(Of(x.n), heavy: false), x.n.GlobalPosition, Gain(Of(x.n)) * 0.5f, x.onPiece ? 1.05f : 0.95f, x.after + 0.02 + 0.06 * Rng.NextDouble());
    }

    public void Knock(PieceNode n, Vector3 at, float speed, bool onDie = false)
    {
        if (!Shown(n)) return;
        var m = Of(n);
        if (m == Matter.Silent) return;
        float k = Mathf.Clamp(speed / 10f, 0, 1);
        if (k < 0.08f) return;
        if (m == Matter.Dice) Emit(onDie ? Sound.Dice : Sound.Die, at, (onDie ? 0.45f : 0.6f) * k, 0.95f + 0.1f * k);
        else Emit(Hit(m, heavy: k > 0.7f), at, Gain(m) * k, 1f);
    }

    public void Tumble(PieceNode n, float strength)
    {
        if (!Shown(n)) return;
        Emit(Sound.Die, n.ModelWorldCenter, 0.6f * Mathf.Clamp(strength, 0, 1), 1f);
    }

    public void Turn(IEnumerable<PieceNode> nodes)
    {
        var heard = nodes.Where(Shown).ToList();
        if (heard.Count > 0) Emit(Sound.Tick, Middle(heard), 0.22f, 1.1f);
    }

    public void Shaking(IReadOnlyCollection<PieceNode> nodes)
    {
        var heard = nodes.Where(Shown).ToList();
        if (heard.Count == 0) return;
        if (heard.Any(n => n.IsDie)) Emit(Sound.Shake, Middle(heard), 0.45f);
        else Emit(Sound.Shuffle, Middle(heard), 0.35f);
    }

    public void Gathered(IReadOnlyCollection<PieceNode> nodes)
    {
        var heard = nodes.Where(Shown).ToList();
        if (heard.Count == 0) return;
        var at = Middle(heard);
        if (heard.All(n => n.IsDie)) Emit(Sound.Dice, at, 0.6f);
        else Emit(Hit(heard.Select(Of).FirstOrDefault(m => m is not Matter.Dice and not Matter.Silent, Matter.Wood), heavy: true), at, 0.5f, 1.05f);
    }

    public void Flip(PieceNode n)
    {
        if (Shown(n) && Of(n) != Matter.Silent) Emit(Sound.Flip, n.GlobalPosition, 0.4f, 1.05f);
    }

    static float Gain(Matter m) => m switch { Matter.Wood => 0.55f, Matter.Card => 0.5f, Matter.Plastic => 0.45f, _ => 0.6f };

    static bool Shown(PieceNode n) => GodotObject.IsInstanceValid(n) && n.IsInsideTree() && n.IsVisibleInTree() && !n.Ghost;

    static Vector3 Middle(IEnumerable<PieceNode> nodes)
    {
        var sum = Vector3.Zero;
        int k = 0;
        foreach (var n in nodes) { sum += n.GlobalPosition; k++; }
        return k == 0 ? Vector3.Zero : sum / k;
    }

    static double SecondsBeforeNextTake(Sound s) => s switch
    {
        Sound.Shake => 0.95,
        Sound.Shuffle => 0.4,
        Sound.Lift or Sound.Tick or Sound.Flip or Sound.DiceGrab => 0.05,
        _ => 0.025,
    };

    void Emit(Sound s, Vector3 at, float gain, float pitch = 1, double after = 0)
    {
        try
        {
            gain *= Hearing(at);
            if (gain < 0.02f) return;
            double when = Time.GetTicksMsec() / 1000.0 + Math.Max(0, after);
            if (_last.TryGetValue(s, out var last) && Math.Abs(when - last) < SecondsBeforeNextTake(s)) return;
            _last[s] = when;
            Kept?.Add(new Heard(s, at, gain, after));
            Sfx.Current?.PlayAfter(after, s, at, gain, pitch);
        }
        catch (Exception e) { GD.PushError($"[sound] {s}: {e.Message}"); }
    }

    float Hearing(Vector3 at)
    {
        var rig = _c.Rig;
        var cam = rig?.Cam;
        if (cam == null || !cam.IsInsideTree()) return 1;
        if (cam.IsPositionBehind(at)) return 0;
        var size = cam.GetViewport().GetVisibleRect().Size;
        if (size.X < 1 || size.Y < 1) return 1;
        var p = cam.UnprojectPosition(at);
        float dx = Mathf.Max(0, Mathf.Max(-p.X, p.X - size.X)), dy = Mathf.Max(0, Mathf.Max(-p.Y, p.Y - size.Y));
        float off = Mathf.Sqrt(dx * dx + dy * dy) / size.X;
        float far = Mathf.Clamp(1.1f - 0.12f * Mathf.Log(Mathf.Max(1, rig!.Distance) / 40f) / Mathf.Log(2), 0.55f, 1);
        return Mathf.Clamp(1 - off / 2, 0, 1) * far;
    }
}

sealed class Knocks
{
    readonly Dictionary<RigidBody3D, (Vector3 v, double quietUntil)> _was = new();

    public void Frame(RigidBody3D b, float delta, Action<float> struck)
    {
        if (!GodotObject.IsInstanceValid(b)) return;
        var v = b.LinearVelocity;
        double now = Time.GetTicksMsec() / 1000.0;
        if (_was.TryGetValue(b, out var was))
        {
            var fall = Vector3.Down * (b.GravityScale * 9.8f * delta);
            float jolt = (v - was.v - fall).Length();
            if (jolt > 1.5f && now >= was.quietUntil && b.GetCollidingBodies().Count > 0)
            {
                _was[b] = (v, now + 0.06);
                struck(jolt);
                return;
            }
            _was[b] = (v, was.quietUntil);
        }
        else _was[b] = (v, 0);
    }
}

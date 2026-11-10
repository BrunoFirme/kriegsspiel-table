using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    public bool IsEffect => KTable.Client.PieceKinds.IsEffect(Def);

    readonly List<EffectFx.Emitter> _emitters = new();

    void ResetEffect() => _emitters.Clear();

    public EffectDef EffectSpec => Def?.Effect ?? EffectKinds.FromName(Def?.Name ?? "");
    public EffectDef PlacedEffect => EffectKinds.WithDensity(EffectSpec, Piece.Smoke);

    public bool CarriesEffect => IsEffect || _emitters.Count > 0;

    public int Emitters => _emitters.Count;

    void BuildEffect()
    {
        if (IsEffect) _thickness = 0.02f;
        var e = SpotsAtSize(PlacedEffect);
        Texture2D? own = null;
        if (e.OwnPicture && Def?.Image is { } sha && _s.Store.PackOfDef(Piece.Def) is { } pack && pack.Assets.TryGetValue(sha, out var a))
        {
            var key = Def;
            own = _s.Assets.Texture(pack.Id, a.File, _ => { if (IsInstanceValid(this) && Def == key && !_built) Rebuild(); }, maxSize: 512);
            _built = own != null;
        }
        float phase = (Piece.Id.Aggregate(17u, (h, c) => h * 31 + c) % 1000) / 1000f;
        _smokeAirBuilt = EffectFx.SmokeAirNow;
        _builtEffectOff = Piece.EffectOff;
        _builtSmoke = Piece.Smoke;
        if (Piece.EffectOff && _onceGen == 0) return;
        _emitters.AddRange(EffectFx.Build(this, e, Footprint.X, Footprint.Y, phase, own));
        SteerEffect();
    }

    EffectDef SpotsAtSize(EffectDef e)
    {
        if (e.At is not { Count: >= 2 } at || Def?.Size is not { W: > 0, H: > 0 } size) return e;
        double kx = Footprint.X / size.W, kz = Footprint.Y / size.H;
        var placed = e.Clone();
        placed.At = at.Select((v, i) => v * (i % 2 == 0 ? kx : kz)).ToList();
        return placed;
    }

    const float OnceSeconds = 3f;
    int _onceGen, _onceCount;

    public bool PlayingOnce => _onceGen != 0;

    public void PlayOnce()
    {
        if (!IsEffect || !IsInsideTree()) return;
        int gen = ++_onceCount;
        _onceGen = gen;
        Rebuild();
        float seconds = EffectSpec.Every > 0 ? (float)EffectSpec.Every : OnceSeconds;
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (!IsInstanceValid(this) || _onceGen != gen) return;
            _onceGen = 0;
            Rebuild();
        };
    }

    bool _built, _builtEffectOff;
    double? _builtSmoke;
    SmokeAir _smokeAirBuilt;

    bool EffectSwitched => IsEffect && _builtEffectOff != Piece.EffectOff || (IsEffect || Def?.Effect != null) && _builtSmoke != Piece.Smoke;

    public void ShadeEffect() => EffectFx.Shade(_emitters);

    public void SteerEffect()
    {
        if (!IsInstanceValid(this)) return;
        if (_smokeAirBuilt != EffectFx.SmokeAirNow && _emitters.Any(m => m.Heat != SmokeHeat.None)) { Callable.From(Rebuild).CallDeferred(); return; }
        EffectFx.Steer(this, _emitters);
    }
}

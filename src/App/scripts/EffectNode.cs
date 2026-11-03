using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    public bool IsEffect => Def is { Kind: not "model" } d && d.Tags.Contains("effect");

    readonly List<EffectFx.Emitter> _emitters = new();

    void ResetEffect() => _emitters.Clear();

    public EffectDef EffectSpec => Def?.Effect ?? EffectKinds.FromName(Def?.Name ?? "");

    public bool CarriesEffect => IsEffect || _emitters.Count > 0;

    void BuildEffect()
    {
        if (IsEffect) _thickness = 0.02f;
        var e = EffectSpec;
        Texture2D? own = null;
        if (e.OwnPicture && Def?.Image is { } sha && _s.Store.PackOfDef(Piece.Def) is { } pack && pack.Assets.TryGetValue(sha, out var a))
        {
            var key = Def;
            own = _s.Assets.Texture(pack.Id, a.File, _ => { if (IsInstanceValid(this) && Def == key && !_built) Rebuild(); }, maxSize: 512);
            _built = own != null;
        }
        float phase = (Piece.Id.Aggregate(17u, (h, c) => h * 31 + c) % 1000) / 1000f;
        _emitters.AddRange(EffectFx.Build(this, e, Footprint.X, Footprint.Y, phase, own));
        SteerEffect();
    }

    bool _built;

    public void HoldForPicture(bool on) => EffectFx.Hold(this, _emitters, on);

    public void ShadeEffect() => EffectFx.Shade(_emitters);

    public void SteerEffect()
    {
        if (IsInstanceValid(this)) EffectFx.Steer(this, _emitters);
    }
}

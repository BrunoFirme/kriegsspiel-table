using Godot;
using KTable.Client.Effects;

namespace KTable.App;

static class EffectTint
{
    public static bool TakesTint(this PieceNode n) => !n.IsEffect || EffectKinds.HasTint(n.EffectSpec);

    public static Color TintShown(this PieceNode n) =>
        n.IsEffect ? EffectFx.ColorOf(n.PlacedEffect)
        : n.Spoken && n.Piece.Lettering is { } lt ? ModuleText.InkOf(lt, n.Piece.TintOverride)
        : Coords.ParseColor(n.Piece.TintOverride ?? n.Def?.Tint, Colors.White);
}

using Godot;

namespace KTable.App;

sealed class PieceAura(PieceNode piece)
{
    bool _cutout;

    public MeshInstance3D? Cover { get; private set; }

    public Texture2D? CoverTexture => piece.PictureMask?.HoleCoverTexture();

    public void CutOut() => _cutout = true;

    public void Forget()
    {
        _cutout = false;
        Cover = null;
    }

    public void CoverHoles(float thickness, Mesh plane)
    {
        if (!_cutout || Cover != null || CoverTexture is not { } holes) return;
        piece.AddChild(Cover = new MeshInstance3D
        {
            Mesh = plane, Position = new Vector3(0, thickness / 2, 0), MaterialOverride = SelectionRing.HoleCover(holes),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = 0, Visible = false,
        });
        if (piece.Ringed) Set(SelectionAura.MaskLayer, true);
        if (piece.Hovered) Set(SelectionAura.HoverLayer, true);
    }

    public void Set(uint layer, bool on)
    {
        if (piece.IsEffect) on = false;
        foreach (var vi in Visuals.Under(piece))
            if (vi != piece.SelectionMesh) vi.Layers = on && !InsideOutline(vi) && !LandingMark.Drawn(vi) ? vi.Layers | layer : vi.Layers & ~layer;
        if (Cover != null) Cover.Visible = (Cover.Layers & (SelectionAura.MaskLayer | SelectionAura.HoverLayer)) != 0;
    }

    bool InsideOutline(VisualInstance3D vi)
    {
        if (vi is not Label3D || vi.GetParent() != piece) return false;
        var drawn = vi.GetAabb();
        if (drawn.Size.X <= 0) return false;
        var box = vi.Transform * drawn;
        box.Position -= new Vector3(piece.FootprintCentre.X, 0, piece.FootprintCentre.Y);
        var half = piece.Footprint / 2;
        return box.Position.X >= -half.X && box.End.X <= half.X && box.Position.Z >= -half.Y && box.End.Z <= half.Y;
    }
}

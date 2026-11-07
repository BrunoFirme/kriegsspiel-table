using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    const float TextAboveFace = 0.004f, TextEmbolden = 0.6f;
    const int LargestTextPixels = 512;

    string? _shownFacePicture;
    KTable.Core.Model.Lettering? _shownLettering;

    bool Spoken => Piece.Lettering != null && Def?.Kind != "model";

    void BuildLiveText()
    {
        _shownLettering = Piece.Lettering;
        _shownFacePicture = Piece.FacePicture;
        if (!Spoken || Piece.Lettering is not { } lt) return;
        var font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = TextEmbolden };
        AddChild(new Label3D
        {
            Text = lt.Text, Font = font, FontSize = Mathf.Clamp((int)Mathf.Round((float)lt.Size), 1, LargestTextPixels), OutlineSize = 0,
            Modulate = Coords.ParseColor(lt.Color, Colors.White) * _layerTint, PixelSize = (float)(lt.UnitsPerPixel * Piece.Scale),
            RotationDegrees = new Vector3(-90, 0, 0), Position = new Vector3(0, _thickness + TextAboveFace, 0), DoubleSided = true,
            HorizontalAlignment = HorizontalAlignment.Center, VisibilityRangeEnd = LetteringRange(),
        });
    }

    bool LooksChanged(PieceInstance p) => !ReferenceEquals(p.Lettering, _shownLettering) || p.FacePicture != _shownFacePicture;
}

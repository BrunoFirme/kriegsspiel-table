using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    const float TextAboveFace = 0.004f;

    string? _shownFacePicture, _shownWebMesh, _shownWebTexture;
    KTable.Core.Model.Lettering? _shownLettering;

    bool Spoken => Piece.Lettering != null && Def?.Kind != "model";

    void BuildLiveText()
    {
        _shownLettering = Piece.Lettering;
        _shownFacePicture = Piece.FacePicture;
        (_shownWebMesh, _shownWebTexture) = (Piece.WebMesh, Piece.WebTexture);
        if (!Spoken || Piece.Lettering is not { } lt) return;
        var label = ModuleText.Label(lt, Piece.Scale, _layerTint);
        label.Position = new Vector3(0, _thickness + TextAboveFace, 0);
        label.VisibilityRangeEnd = LetteringRange();
        AddChild(label);
    }

    bool LooksChanged(PieceInstance p) =>
        !ReferenceEquals(p.Lettering, _shownLettering) || p.FacePicture != _shownFacePicture || p.WebMesh != _shownWebMesh || p.WebTexture != _shownWebTexture;
}

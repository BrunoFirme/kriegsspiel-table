using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    const float TextAboveFace = 0.004f;

    string? _shownFacePicture, _shownWebMesh, _shownWebTexture;
    KTable.Core.Model.CrowdLook? _shownLook;
    KTable.Core.Model.Lettering? _shownLettering;

    internal bool Spoken =>Piece.Lettering != null && Def?.Kind != "model";

    void BuildLiveText()
    {
        _shownLettering = Piece.Lettering;
        _shownFacePicture = Piece.FacePicture;
        (_shownWebMesh, _shownWebTexture, _shownLook) = (Piece.WebMesh, Piece.WebTexture, Piece.Look);
        if (!Spoken || Piece.Lettering is not { } lt) return;
        var text = ModuleText.Drawn(lt, Piece.Scale, _layerTint, Piece.TintOverride);
        text.Position = new Vector3(0, _thickness + TextAboveFace, 0);
        foreach (var label in text is Label3D one ? [one] : text.GetChildren().OfType<Label3D>().ToArray()) label.VisibilityRangeEnd = LetteringRange();
        AddChild(text);
    }

    bool LooksChanged(PieceInstance p) =>
        !ReferenceEquals(p.Lettering, _shownLettering) || p.FacePicture != _shownFacePicture || p.WebMesh != _shownWebMesh || p.WebTexture != _shownWebTexture || !ReferenceEquals(p.Look, _shownLook);
}

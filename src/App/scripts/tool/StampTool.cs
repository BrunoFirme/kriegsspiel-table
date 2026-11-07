using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class StampTool
{
    public const double TurnStep = 15, Grow = 1.1, SmallestGrowth = 0.05, LargestGrowth = 40;
    const float PreviewAlpha = 0.75f;
    const double HalfTurn = 180;

    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly PieceLayer _pieces;
    readonly DrawingLayer _drawings;

    public StampTool(TableContext c, ToolOverlay overlay, PieceLayer pieces, DrawingLayer drawings)
    {
        _c = c;
        _overlay = overlay;
        _pieces = pieces;
        _drawings = drawings;
    }

    public PaletteDecal? Armed { get; private set; }
    public double Turn { get; private set; }
    public double Growth { get; private set; } = 1;

    public event Action? Changed;

    public void Arm(PaletteDecal? decal)
    {
        Armed = decal;
        Turn = 0;
        Growth = 1;
        Changed?.Invoke();
        if (_c.Tool == Tool.Decal) _c.Say(Hint());
    }

    public void Wheel(bool up, bool size)
    {
        if (size) Growth = Math.Clamp(Growth * (up ? Grow : 1 / Grow), SmallestGrowth, LargestGrowth);
        else Turn = ((Turn + (up ? -TurnStep : TurnStep)) % 360 + 360) % 360;
    }

    public string Hint() => Armed == null
        ? "Pick a decal in the panel · click a stamped decal to lift it off"
        : $"Click to stamp {(Armed.Name.Length > 0 ? Armed.Name : "the decal")} on a piece or the table · the wheel turns it, Shift+wheel sizes it · click a stamped decal to lift it off";

    readonly Dictionary<string, float> _aspects = new();

    float Aspect(string picture)
    {
        if (_aspects.TryGetValue(picture, out var known)) return known;
        if (KTable.Client.PieceFiles.DecalFile(_c.S.View, _c.S.Store, picture) is not { } file) return 1;
        var tex = _c.S.Assets.Texture(file.Package, file.Path, t => _aspects[picture] = t.GetHeight() > 0 ? t.GetWidth() / (float)t.GetHeight() : 1);
        if (tex == null || tex.GetHeight() <= 0) return 1;
        return _aspects[picture] = tex.GetWidth() / (float)tex.GetHeight();
    }

    public DecalDef Shape(Vec2 at)
    {
        var a = Armed!;
        double h = a.Size * Growth;
        return new DecalDef { Picture = a.Picture, Name = a.Name, X = Math.Round(at.X, 4), Z = Math.Round(at.Z, 4), H = Math.Round(h, 4), W = Math.Round(h * Aspect(a.Picture), 4), Rot = Turn };
    }

    StandardMaterial3D? _previewMat;
    string? _previewPicture;

    public void Draw()
    {
        if (_c.Tool != Tool.Decal || Armed == null || _c.Ground(Pointer.In(_c.Scene.GetViewport())) is not { } at) return;
        if (_previewPicture != Armed.Picture || _previewMat == null)
        {
            _previewPicture = Armed.Picture;
            _previewMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
                AlbedoColor = new Color(1, 1, 1, PreviewAlpha), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var mat = _previewMat;
            if (KTable.Client.PieceFiles.DecalFile(_c.S.View, _c.S.Store, Armed.Picture) is { } file
                && _c.S.Assets.Texture(file.Package, file.Path, t => mat.AlbedoTexture = t) is { } tex)
                mat.AlbedoTexture = tex;
        }
        DrawingLayer.DecalMesh(Shape(at), _c.SheetsTop + DrawingLayer.DecalLift, _c.S.Ground, _overlay.Mesh, _previewMat);
    }

    public void Press(Vec2 at, Vector2 screen)
    {
        if (_drawings.TableDecalAt(at) is { Id: { } id } stamped && _drawings.LayerOf(stamped) is { } layer)
        {
            _c.S.Do(new RemoveDecal { Layer = layer, DecalId = id });
            return;
        }
        var piece = _pieces.Pick(at, screen: screen);
        if (piece != null && OnPiece(piece, at) is { } onPiece)
        {
            _c.S.Do(new SetDecals { Layer = piece.Layer, Id = piece.Piece.Id, Decals = piece.Piece.Decals.Where(d => d != onPiece).Select(d => d.Clone()).ToList() });
            return;
        }
        if (Armed == null)
        {
            _c.Say(Hint());
            return;
        }
        if (piece != null && Stampable(piece) && _c.S.CanTouch(piece.Layer, piece.Piece))
        {
            _c.S.Do(new SetDecals { Layer = piece.Layer, Id = piece.Piece.Id, Decals = piece.Piece.Decals.Select(d => d.Clone()).Append(Local(piece, at)).ToList() });
            return;
        }
        _c.S.Do(new StampDecal { Layer = Layers.Umpire, Decal = Shape(at) });
    }

    static bool Stampable(PieceNode n) => n.Def?.Kind != "model" && !n.IsDie && !n.IsEffect && n.Piece.Decals.Count < DecalDef.MostOnAPiece;

    DecalDef Local(PieceNode n, Vec2 at)
    {
        var d = Shape(at);
        double k = Math.Max(1e-6, n.Piece.Scale);
        var local = Coords.ToLocal(at, n.Piece.Pos, n.Piece.RotDeg);
        d.X = Math.Round(local.X / k, 4);
        d.Z = Math.Round(local.Y / k, 4);
        d.W = Math.Round(d.W / k, 4);
        d.H = Math.Round(d.H / k, 4);
        d.Rot = ((Turn + HalfTurn - n.Piece.RotDeg) % 360 + 360) % 360;
        d.Face = n.Piece.FaceDown ? "back" : "front";
        return d;
    }

    static DecalDef? OnPiece(PieceNode n, Vec2 at)
    {
        double k = Math.Max(1e-6, n.Piece.Scale);
        var local = Coords.ToLocal(at, n.Piece.Pos, n.Piece.RotDeg);
        var face = n.Piece.FaceDown ? "back" : "front";
        return n.Piece.Decals.LastOrDefault(d => d.Face == face && new Vector2((float)(d.X * k) - local.X, (float)(d.Z * k) - local.Y).Length() <= Math.Max(d.W, d.H) * k / 2);
    }
}

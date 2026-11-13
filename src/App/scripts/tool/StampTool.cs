using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class StampTool
{
    public const double TurnStep = 15, FineTurn = 1, Grow = 1.1, SmallestGrowth = 0.05, LargestGrowth = 40;
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
    public double Turn => _c.Settings.DecalTurn;
    public double Growth => Math.Clamp(_c.Settings.DecalGrowth, SmallestGrowth, LargestGrowth);

    public event Action? Changed;

    public void Arm(PaletteDecal? decal)
    {
        Armed = decal;
        var scenario = _c.S.View.ScenarioId;
        if (decal != null) _c.Settings.Decal[scenario] = decal.Picture;
        else _c.Settings.Decal.Remove(scenario);
        _c.Settings.Save();
        Changed?.Invoke();
        if (_c.Tool == Tool.Decal) _c.Say(Hint());
    }

    public void ArmAsBefore()
    {
        if (Armed != null || !_c.Settings.Decal.TryGetValue(_c.S.View.ScenarioId, out var picture)) return;
        if (_c.S.View.DecalPalette.FirstOrDefault(d => d.Picture == picture) is { } decal) Arm(decal);
    }

    public void Size(int notches)
    {
        _c.Settings.DecalGrowth = Math.Clamp(Growth * Math.Pow(Grow, notches), SmallestGrowth, LargestGrowth);
        _c.Settings.Save();
        _c.Say(Hint());
    }

    public void TurnBy(double deg)
    {
        _c.Settings.DecalTurn = Math.Round(((Turn + deg) % 360 + 360) % 360, 6);
        _c.Settings.Save();
        _c.Say(Hint());
    }

    public bool Key(InputEventKey k, KTable.Client.Prefs.Keymap km)
    {
        if (Armed == null) return false;
        if (km.Is(k, "turnLeft")) TurnBy(k.ShiftPressed ? -FineTurn : -TurnStep);
        else if (km.Is(k, "turnRight")) TurnBy(k.ShiftPressed ? FineTurn : TurnStep);
        else if (km.Is(k, "larger")) Size(1);
        else if (km.Is(k, "smaller")) Size(-1);
        else return false;
        return true;
    }

    public string Hint()
    {
        if (Armed == null) return "Pick a decal in the panel · click a stamped decal to lift it off";
        var keys = _c.Settings.Keys;
        return $"Click to stamp {(Armed.Name.Length > 0 ? Armed.Name : "the decal")} on a piece or the table · turned {Turn:0.#}°, {Growth * 100:0}% · "
            + $"{keys.Label("turnLeft")} {keys.Label("turnRight")} turn it, Shift by 1° · {keys.Label("smaller")} {keys.Label("larger")} or Ctrl+wheel size it · click a stamped decal to lift it off";
    }

    readonly Dictionary<string, float> _aspects = new();

    float Aspect(string picture)
    {
        if (_aspects.TryGetValue(picture, out var known)) return known;
        if (KTable.Client.PieceFiles.DecalFile(_c.S.View, _c.S.Store, picture) is not { } file) return 1;
        var tex = _c.S.Assets.Texture(file.Package, file.Path, t => _aspects[picture] = t.GetHeight() > 0 ? t.GetWidth() / (float)t.GetHeight() : 1);
        if (tex == null || tex.GetHeight() <= 0) return 1;
        return _aspects[picture] = tex.GetWidth() / (float)tex.GetHeight();
    }

    public bool Sized(string picture)
    {
        Aspect(picture);
        return _aspects.ContainsKey(picture);
    }

    public DecalDef Shape(Vec2 at) => ShapeOf(Armed!, at, Growth, Turn);

    public DecalDef ShapeOf(PaletteDecal a, Vec2 at, double growth, double turn)
    {
        double h = a.Size * growth;
        return new DecalDef { Picture = a.Picture, Name = a.Name, X = Math.Round(at.X, 4), Z = Math.Round(at.Z, 4), H = Math.Round(h, 4), W = Math.Round(h * Aspect(a.Picture), 4), Rot = turn };
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
        DrawingLayer.DecalMesh(Shape(at), _c.SheetsTop + _drawings.Lifted(DrawingLayer.DecalLift), _c.S.Ground, _overlay.Mesh, _previewMat);
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

    public static bool Stampable(PieceNode n) => n.Def?.Kind != "model" && !n.IsDie && !n.IsEffect && n.Piece.Decals.Count < DecalDef.MostOnAPiece;

    DecalDef Local(PieceNode n, Vec2 at) => OnPieceAt(n, Shape(at), Turn);

    public static DecalDef OnPieceAt(PieceNode n, DecalDef d, double turn)
    {
        double k = Math.Max(1e-6, n.Piece.Scale);
        var local = Coords.ToLocal(new Vec2(d.X, d.Z), n.Piece.Pos, n.Piece.RotDeg);
        d.X = Math.Round(local.X / k, 4);
        d.Z = Math.Round(local.Y / k, 4);
        d.W = Math.Round(d.W / k, 4);
        d.H = Math.Round(d.H / k, 4);
        d.Rot = ((turn + HalfTurn - n.Piece.RotDeg) % 360 + 360) % 360;
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

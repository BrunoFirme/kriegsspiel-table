using System.Text.Json.Serialization;
using KTable.Core.Model;

namespace KTable.Core.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SpawnPiece), "SpawnPiece")]
[JsonDerivedType(typeof(RestorePiece), "RestorePiece")]
[JsonDerivedType(typeof(MovePiece), "MovePiece")]
[JsonDerivedType(typeof(DeletePiece), "DeletePiece")]
[JsonDerivedType(typeof(Stow), "Stow")]
[JsonDerivedType(typeof(Unstow), "Unstow")]
[JsonDerivedType(typeof(SetReady), "SetReady")]
[JsonDerivedType(typeof(ReleaseReady), "ReleaseReady")]
[JsonDerivedType(typeof(RecallReady), "RecallReady")]
[JsonDerivedType(typeof(SetState), "SetState")]
[JsonDerivedType(typeof(TurnDie), "TurnDie")]
[JsonDerivedType(typeof(SetWorth), "SetWorth")]
[JsonDerivedType(typeof(SetFacePicture), "SetFacePicture")]
[JsonDerivedType(typeof(SetLettering), "SetLettering")]
[JsonDerivedType(typeof(SetDieFaces), "SetDieFaces")]
[JsonDerivedType(typeof(SetCounter), "SetCounter")]
[JsonDerivedType(typeof(SetText), "SetText")]
[JsonDerivedType(typeof(SetLabel), "SetLabel")]
[JsonDerivedType(typeof(SetNote), "SetNote")]
[JsonDerivedType(typeof(RestoreTurn), "RestoreTurn")]
[JsonDerivedType(typeof(LoadSave), "LoadSave")]
[JsonDerivedType(typeof(SetTint), "SetTint")]
[JsonDerivedType(typeof(SetLocked), "SetLocked")]
[JsonDerivedType(typeof(SetHighlight), "SetHighlight")]
[JsonDerivedType(typeof(SetBookHighlight), "SetBookHighlight")]
[JsonDerivedType(typeof(PlayEffect), "PlayEffect")]
[JsonDerivedType(typeof(GlidePiece), "GlidePiece")]
[JsonDerivedType(typeof(ShowCast), "ShowCast")]
[JsonDerivedType(typeof(ReportSelection), "ReportSelection")]
[JsonDerivedType(typeof(SetWebModel), "SetWebModel")]
[JsonDerivedType(typeof(SetPieceOption), "SetPieceOption")]
[JsonDerivedType(typeof(SetPointerRule), "SetPointerRule")]
[JsonDerivedType(typeof(SetBriefing), "SetBriefing")]
[JsonDerivedType(typeof(SetScale), "SetScale")]
[JsonDerivedType(typeof(Attach), "Attach")]
[JsonDerivedType(typeof(Detach), "Detach")]
[JsonDerivedType(typeof(AddSheet), "AddSheet")]
[JsonDerivedType(typeof(MoveSheet), "MoveSheet")]
[JsonDerivedType(typeof(LockSheet), "LockSheet")]
[JsonDerivedType(typeof(RenameSheet), "RenameSheet")]
[JsonDerivedType(typeof(RemoveSheet), "RemoveSheet")]
[JsonDerivedType(typeof(CropSheet), "CropSheet")]
[JsonDerivedType(typeof(SetSheetRelief), "SetSheetRelief")]
[JsonDerivedType(typeof(SetSheetOpacity), "SetSheetOpacity")]
[JsonDerivedType(typeof(SetSheetPicture), "SetSheetPicture")]
[JsonDerivedType(typeof(FlipPiece), "FlipPiece")]
[JsonDerivedType(typeof(TiltPiece), "TiltPiece")]
[JsonDerivedType(typeof(SetLighting), "SetLighting")]
[JsonDerivedType(typeof(SetWind), "SetWind")]
[JsonDerivedType(typeof(SetVisionRadius), "SetVisionRadius")]
[JsonDerivedType(typeof(AddBoard), "AddBoard")]
[JsonDerivedType(typeof(RenameBoard), "RenameBoard")]
[JsonDerivedType(typeof(RemoveBoard), "RemoveBoard")]
[JsonDerivedType(typeof(Draw), "Draw")]
[JsonDerivedType(typeof(EraseStroke), "EraseStroke")]
[JsonDerivedType(typeof(StampDecal), "StampDecal")]
[JsonDerivedType(typeof(RemoveDecal), "RemoveDecal")]
[JsonDerivedType(typeof(SetDecalPalette), "SetDecalPalette")]
[JsonDerivedType(typeof(RollDice), "RollDice")]
[JsonDerivedType(typeof(SetClock), "SetClock")]
[JsonDerivedType(typeof(AdvanceClock), "AdvanceClock")]
[JsonDerivedType(typeof(SetCalibration), "SetCalibration")]
[JsonDerivedType(typeof(SetGrid), "SetGrid")]
[JsonDerivedType(typeof(SetPicture), "SetPicture")]
[JsonDerivedType(typeof(PublishSighting), "PublishSighting")]
[JsonDerivedType(typeof(SetZone), "SetZone")]
[JsonDerivedType(typeof(RemoveZone), "RemoveZone")]
[JsonDerivedType(typeof(AddPack), "AddPack")]
[JsonDerivedType(typeof(Undo), "Undo")]
[JsonDerivedType(typeof(Batch), "Batch")]
[JsonDerivedType(typeof(SetScript), "SetScript")]
[JsonDerivedType(typeof(SetGlobalScript), "SetGlobalScript")]
[JsonDerivedType(typeof(SetScriptState), "SetScriptState")]
[JsonDerivedType(typeof(SetGlobalScriptState), "SetGlobalScriptState")]
[JsonDerivedType(typeof(SetTags), "SetTags")]
[JsonDerivedType(typeof(SetMemo), "SetMemo")]
[JsonDerivedType(typeof(SetWidgets), "SetWidgets")]
[JsonDerivedType(typeof(SetMenuItems), "SetMenuItems")]
[JsonDerivedType(typeof(ClickWidget), "ClickWidget")]
[JsonDerivedType(typeof(SetHotkeys), "SetHotkeys")]
[JsonDerivedType(typeof(SetGlobalMenuItems), "SetGlobalMenuItems")]
[JsonDerivedType(typeof(Hotkey), "Hotkey")]
[JsonDerivedType(typeof(PickGlobalMenuItem), "PickGlobalMenuItem")]
[JsonDerivedType(typeof(NumberTyped), "NumberTyped")]
[JsonDerivedType(typeof(SelectPiece), "SelectPiece")]
[JsonDerivedType(typeof(ShowContents), "ShowContents")]
[JsonDerivedType(typeof(EditWidget), "EditWidget")]
[JsonDerivedType(typeof(PickMenuItem), "PickMenuItem")]
[JsonDerivedType(typeof(HoldPiece), "HoldPiece")]
[JsonDerivedType(typeof(Say), "Say")]
[JsonDerivedType(typeof(SetDef), "SetDef")]
[JsonDerivedType(typeof(SetLines), "SetLines")]
[JsonDerivedType(typeof(SetDecals), "SetDecals")]
[JsonDerivedType(typeof(SetUi), "SetUi")]
[JsonDerivedType(typeof(SetUiAttributes), "SetUiAttributes")]
[JsonDerivedType(typeof(SetUiPictures), "SetUiPictures")]
[JsonDerivedType(typeof(UiEvent), "UiEvent")]
[JsonDerivedType(typeof(ShowDialog), "ShowDialog")]
[JsonDerivedType(typeof(DialogResult), "DialogResult")]
public abstract class Command
{
    public long Seq { get; set; }
    public long Actor { get; set; }
    public long At { get; set; }
    public long UndoOf { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long UndoFrom { get; set; }

    public abstract Sender SentBy();

    public bool MakesUndoStep() => this is Batch b ? b.Items.Any(i => i != null && i.MakesUndoStep()) : OnUndoStack() && SentBy() != Sender.Runtime;

    public abstract bool OnUndoStack();

    public abstract Shown ShownToObservers();

    public abstract IEnumerable<(string Layer, string Id)> ZonePieces();

    public abstract IEnumerable<(string Layer, Vec2 Pos)> ZonePoints();

    public abstract IEnumerable<string> ScriptsToReload();

    public const string GlobalScript = "(global script)";
}

public enum Sender
{
    Umpire,
    UmpireAlone,
    Lead,
    LeadAlone,
    Runtime,
}

public enum Shown
{
    Table,
    UmpiresOnly,
    Addressee,
    DiceOnBoard,
    WholeTable,
    Parts,
}

public abstract class LayerCommand : Command
{
    public string Layer { get; set; } = Layers.Umpire;
}

public sealed class SpawnPiece : LayerCommand
{
    public string Def { get; set; } = "";
    public Vec2 Pos { get; set; }
    public double RotDeg { get; set; }
    public string? Id { get; set; }
    public string? Label { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Y { get; set; }
    public string? Guid { get; set; }
    public string? ScriptState { get; set; }
    public List<string>? Tags { get; set; }
    public string? Memo { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [(Layer, Pos)];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class RestorePiece : LayerCommand
{
    public PieceInstance Piece { get; set; } = new();
    public int? Index { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [(Layer, Piece.Pos)];
    public override IEnumerable<string> ScriptsToReload() => [Piece.Id];
}

public sealed class MovePiece : LayerCommand
{
    public string Id { get; set; } = "";
    public Vec2 Pos { get; set; }
    public double RotDeg { get; set; }
    public int Stack { get; set; }
    public double Y { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [(Layer, Pos)];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class DeletePiece : LayerCommand
{
    public string Id { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetState : LayerCommand
{
    public string Id { get; set; } = "";
    public int State { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Leaving { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class TurnDie : LayerCommand
{
    public string Id { get; set; } = "";
    public int Face { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetFacePicture : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Picture { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetLettering : LayerCommand
{
    public string Id { get; set; } = "";
    public Lettering? Lettering { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetWorth : LayerCommand
{
    public string Id { get; set; } = "";
    public int Worth { get; set; }
    public int Groups { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetDieFaces : LayerCommand
{
    public string Id { get; set; } = "";
    public List<DieFace>? Faces { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetCounter : LayerCommand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public bool Clear { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetText : LayerCommand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Clear { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetLabel : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Value { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetNote : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Value { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetTint : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Value { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetPieceOption : LayerCommand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Value { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Inherit { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public static class PieceOptions
{
    public const string NoCollision = "no-collision";
    public const string PhysicalRoll = "physical-roll";
    public const string Holds = "holds";
    public const string NotSticky = "not-sticky";
    public const string NotBoxSelected = "not-box-selected";
    public const string MeasuresMoves = "measures-moves";
    public const string EffectOff = "effect-off";
    public const string NotInteractable = "not-interactable";
}

public sealed class SetHighlight : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Color { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetBookHighlight : LayerCommand
{
    public const int Corners = 4;
    public string Id { get; set; } = "";
    public double[]? Box { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class PlayEffect : LayerCommand
{
    public string Id { get; set; } = "";
    public int Index { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class GlidePiece : LayerCommand
{
    public string Id { get; set; } = "";
    public Vec2? To { get; set; }
    public double? Rot { get; set; }
    public bool Fast { get; set; }
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class ShowCast : Command
{
    public const int Axes = 3;
    public const double MostSeconds = 60;
    public static readonly string[] Shapes = ["ray", "sphere", "box"];
    public string Shape { get; set; } = "ray";
    public double[] From { get; set; } = [0, 0, 0];
    public double[] To { get; set; } = [0, 0, 0];
    public double[] Size { get; set; } = [0, 0, 0];
    public double[] Turn { get; set; } = [0, 0, 0];
    public double Seconds { get; set; } = 1;
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];

    public string? Problem()
    {
        if (!Shapes.Contains(Shape)) return "a cast is a ray, a sphere or a box";
        if (new[] { From, To, Size, Turn }.Any(v => v is not { Length: Axes } || v.Any(x => !double.IsFinite(x)))) return "a cast's points are three numbers each";
        return double.IsFinite(Seconds) && Seconds > 0 && Seconds <= MostSeconds ? null : "a cast is shown for a few seconds";
    }
}

public sealed class SetWebModel : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Mesh { get; set; }
    public string? Texture { get; set; }
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetLocked : LayerCommand
{
    public string Id { get; set; } = "";
    public bool Value { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class RestoreTurn : Command
{
    public int Index { get; set; }
    public GameState? State { get; set; }
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.WholeTable;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class LoadSave : Command
{
    public string Name { get; set; } = "";
    public GameState? State { get; set; }
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.WholeTable;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetScale : LayerCommand
{
    public string Id { get; set; } = "";
    public double Scale { get; set; } = 1;
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class TiltPiece : LayerCommand
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Z { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class FlipPiece : LayerCommand
{
    public string Id { get; set; } = "";
    public bool FaceDown { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class Attach : LayerCommand
{
    public string Parent { get; set; } = "";
    public string Child { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Parent), (Layer, Child)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class Detach : LayerCommand
{
    public string Child { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Child)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class Stow : LayerCommand
{
    public string Holder { get; set; } = "";
    public string Id { get; set; } = "";
    public int? Slot { get; set; }
    public string? Guid { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}

public sealed class Unstow : LayerCommand
{
    public string Holder { get; set; } = "";
    public string Id { get; set; } = "";
    public Vec2? Pos { get; set; }
    public double? RotDeg { get; set; }
    public int? Index { get; set; }
    public bool KeepGuid { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => Pos is { } p ? [(Layer, p)] : [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}

public sealed class SetReady : LayerCommand
{
    public string Holder { get; set; } = "";
    public ReadyPiece? Ready { get; set; }
    public string? Move { get; set; }
    public int Slot { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Holder)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class ReleaseReady : LayerCommand
{
    public string Holder { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Holder)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class RecallReady : LayerCommand
{
    public string Holder { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Holder)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class AddSheet : Command
{
    public SheetDef Sheet { get; set; } = new();
    public int? Index { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class MoveSheet : Command
{
    public string Id { get; set; } = "";
    public Vec2 Pos { get; set; }
    public double RotDeg { get; set; }
    public int Z { get; set; }
    public Size2? Size { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class LockSheet : Command
{
    public string Id { get; set; } = "";
    public bool Locked { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class RenameSheet : Command
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class RemoveSheet : Command
{
    public string Id { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class CropSheet : Command
{
    public string Id { get; set; } = "";
    public FaceBox? Crop { get; set; }
    public Vec2? Pos { get; set; }
    public Size2? Size { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetSheetRelief : Command
{
    public string Id { get; set; } = "";
    public string? Relief { get; set; }
    public double Height { get; set; } = 1;
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetSheetOpacity : Command
{
    public string Id { get; set; } = "";
    public double Opacity { get; set; } = 1;
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetSheetPicture : Command
{
    public string Id { get; set; } = "";
    public string Image { get; set; } = "";
    public string Package { get; set; } = "";
    public Size2? Size { get; set; }
    public FaceBox? Crop { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class Draw : LayerCommand
{
    public Stroke Stroke { get; set; } = new();
    public int? Index { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class EraseStroke : LayerCommand
{
    public string StrokeId { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class StampDecal : LayerCommand
{
    public DecalDef Decal { get; set; } = new();
    public int? Index { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class RemoveDecal : LayerCommand
{
    public string DecalId { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetDecalPalette : Command
{
    public List<PaletteDecal> Entries { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class AddBoard : Command
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Index { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class RenameBoard : Command
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class RemoveBoard : Command
{
    public string Id { get; set; } = "";
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class RollDice : Command
{
    public int Sides { get; set; } = 6;
    public int Count { get; set; } = 1;
    public string Label { get; set; } = "";
    public List<int> Results { get; set; } = new();
    public bool Physical { get; set; }
    public string ActorName { get; set; } = "";
    public string? Layer { get; set; }
    public List<string> PieceIds { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.DiceOnBoard;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetClock : Command
{
    public int Turn { get; set; }
    public string GameTime { get; set; } = "";
    public int IncrementMinutes { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class AdvanceClock : Command
{
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetLighting : Command
{
    public Lighting Lighting { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetWind : Command
{
    public Wind Wind { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetVisionRadius : Command
{
    public double Radius { get; set; } = 40;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Sharp { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Blur { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetPointerRule : Command
{
    public bool AlwaysShown { get; set; }
    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetBriefing : Command
{
    public string? Text { get; set; }
    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetPicture : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Picture { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetGrid : Command
{
    public GridDef Grid { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetCalibration : Command
{
    public double? UnitsPerMeter { get; set; }
    public string? Units { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class PublishSighting : Command
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Old { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetZone : Command
{
    public Zone Zone { get; set; } = new();
    public int? Index { get; set; }
    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class RemoveZone : Command
{
    public string Id { get; set; } = "";
    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class AddPack : Command
{
    public string Id { get; set; } = "";
    public string Hash { get; set; } = "";
    public override Sender SentBy() => Sender.UmpireAlone;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class Undo : Command
{
    public long TargetSeq { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long FromSeq { get; set; }
    public override Sender SentBy() => Sender.UmpireAlone;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class Batch : Command
{
    public List<Command> Items { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Parts;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => Present().SelectMany(i => i.ZonePieces());
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => Present().SelectMany(i => i.ZonePoints());
    public override IEnumerable<string> ScriptsToReload() => Present().SelectMany(i => i.ScriptsToReload());
    IEnumerable<Command> Present() => Items.Where(i => i != null);
}

public sealed class SetScript : LayerCommand
{
    public string Id { get; set; } = "";
    public string? Text { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetGlobalScript : Command
{
    public string? Text { get; set; }
    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [GlobalScript];
}
public sealed class SetScriptState : LayerCommand
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetGlobalScriptState : Command
{
    public string State { get; set; } = "";
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [GlobalScript];
}
public sealed class SetTags : LayerCommand
{
    public string Id { get; set; } = "";
    public List<string>? Tags { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetMemo : LayerCommand
{
    public string Id { get; set; } = "";
    public string Memo { get; set; } = "";
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetWidgets : LayerCommand
{
    public string Id { get; set; } = "";
    public List<Widget> Widgets { get; set; } = new();
    public int OnState { get; set; } = -1;
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetMenuItems : LayerCommand
{
    public string Id { get; set; } = "";
    public List<MenuItemDef> Items { get; set; } = new();
    public int OnState { get; set; } = -1;
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class ClickWidget : LayerCommand
{
    public string Id { get; set; } = "";
    public int Index { get; set; }
    public bool Alt { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetHotkeys : Command
{
    public List<HotkeyDef> Items { get; set; } = new();
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetGlobalMenuItems : Command
{
    public List<MenuItemDef> Items { get; set; } = new();
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class ReportSelection : Command
{
    public const int MostPieces = 10000;
    public List<string> Selected { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class Hotkey : Command
{
    public int Index { get; set; }
    public bool Up { get; set; }
    public string? Layer { get; set; }
    public string? Piece { get; set; }
    public Vec2? Pos { get; set; }
    public List<string> Selected { get; set; } = new();
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class PickGlobalMenuItem : Command
{
    public int Index { get; set; }
    public Vec2? Pos { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class NumberTyped : LayerCommand
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class ShowContents : Command
{
    public string Layer { get; set; } = "";
    public string Id { get; set; } = "";
    public string To { get; set; } = "";
    public int Most { get; set; } = -1;
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Addressee;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SelectPiece : Command
{
    public string Layer { get; set; } = "";
    public string Id { get; set; } = "";
    public string To { get; set; } = "";
    public bool Selected { get; set; } = true;
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Addressee;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class EditWidget : LayerCommand
{
    public string Id { get; set; } = "";
    public int Index { get; set; }
    public string Value { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Typing { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class PickMenuItem : LayerCommand
{
    public string Id { get; set; } = "";
    public int Index { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class HoldPiece : LayerCommand
{
    public string Id { get; set; } = "";
    public bool Held { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetDef : LayerCommand
{
    public string Id { get; set; } = "";
    public string Def { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool SwapsScript { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Script { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [Id];
}
public sealed class SetDecals : LayerCommand
{
    public string Id { get; set; } = "";
    public List<DecalDef> Decals { get; set; } = new();
    public int OnState { get; set; } = -1;
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetLines : LayerCommand
{
    public string Id { get; set; } = "";
    public List<LineDef> Lines { get; set; } = new();
    public int OnState { get; set; } = -1;
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetUi : Command
{
    public List<UiNode> Tree { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Xml { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KTable.Core.Panels.UiLayout? Layout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<KTable.Core.Panels.UiAsset>? Assets { get; set; }
    public string? Layer { get; set; }
    public string? Piece { get; set; }
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetUiPictures : Command
{
    public Dictionary<string, string> Pictures { get; set; } = new();
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class SetUiAttributes : Command
{
    public string Id { get; set; } = "";
    public Dictionary<string, string> Attributes { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Value { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public UiNode? Data { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? DataPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KTable.Core.Panels.UiElement? Live { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? LivePath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<int>>? Ids { get; set; }
    public string? Layer { get; set; }
    public string? Piece { get; set; }
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class UiEvent : Command
{
    public string Id { get; set; } = "";
    public string Event { get; set; } = "click";
    public string Value { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? Path { get; set; }
    public string? Layer { get; set; }
    public string? Piece { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class ShowDialog : Command
{
    public string? To { get; set; }
    public string Kind { get; set; } = "memo";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public string Token { get; set; } = "";
    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class DialogResult : Command
{
    public string Token { get; set; } = "";
    public string? Value { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
public sealed class Say : Command
{
    public string Text { get; set; } = "";
    public string? To { get; set; }
    public string? Color { get; set; }
    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Addressee;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

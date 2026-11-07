using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public static class CoreSamples
{
    public static PieceInstance Piece(string id, string def = "def-a", double x = 0, double z = 0) => new()
    {
        Id = id, Def = def, Pos = new Vec2(x, z), RotDeg = 30, Stack = 0, State = 1,
        Counters = new() { ["str"] = 4, ["morale"] = 2 },
        Text = new() { ["name"] = "1st Bn", ["note"] = "tired" },
        TintOverride = "#FF0000", Label = "label-" + id, Scale = 1.25,
        Guid = "a1b2c3", Script = "function on_load(s) end", ScriptState = "{\"n\":1}",
        Widgets =
            new() { new Widget { Kind = "input", Face = "back", X = 0.1, Z = -0.2, W = 0.3, H = 0.05, Rot = 180, Label = "L", Value = "v", Placeholder = "p", Font = 0.04, Color = "#112233", TextColor = "#FFFFFF", Align = "left", Lines = 2,
                Tooltip = "t", OnClick = "c", OnChange = "e" } },
        MenuItems = new() { new MenuItemDef { Label = "Rally", OnSelect = "rally" } },
        Lines = new() { new LineDef { Points = { new(0, 2), new(2, 0) }, Color = "#00FF00", Width = 0.15, Loop = false } },
    };

    public static SheetDef Sheet(string id) => new()
    {
        Id = id, Name = "Sheet " + id, Image = "abc123", Package = "pack-x", Pos = new Vec2(1.5, -2.5),
        RotDeg = 90, Size = new Size2(20, 30), Locked = false, Z = 2, Kind = "reference",
    };

    public static Stroke Stroke(string id) => new()
    {
        Id = id, Points = new() { new Vec2(0, 0), new Vec2(1, 2), new Vec2(3.25, -4) }, Width = 0.5, Color = "#123456",
    };

    public static Scenario Scenario() => new()
    {
        Id = "scn-1", Name = "Aspern", Packs = new() { "pack-x" },
        Sheets = new() { Sheet("s-000001") },
        Calibration = new Calibration { UnitsPerMeter = 0.01 },
        Clock = new GameClock { Turn = 2, GameTime = "1809-05-21T10:00", IncrementMinutes = 20 },
        UmpireBoard = new() { Piece("p-000001"), Piece("p-000002", "def-b", 5, 5), Piece("p-000003") },
        Drawings = new() { ["umpire"] = new() { Stroke("d-000004"), Stroke("d-000005") } },
    };

    public static GameState State()
    {
        var g = GameState.FromScenario(Scenario());
        g.Zones.Add(new Zone { Id = "z-000010", Name = "North", Owner = "Ump2", Polygon = { new(0, 0), new(10, 0), new(10, 10) } });
        g.Dice.Add(new DiceRoll { Seq = 4, Actor = "Bob", Sides = 6, Results = new() { 1, 6 }, Label = "fire" });
        g.LastSeq = 7;
        return g;
    }

    public static PackManifest Pack() => new()
    {
        Id = "pack-x", Name = "Pack X",
        Source = new PackSource { WorkshopId = "12345", Authors = new() { "A", "B" }, Url = "https://example.invalid/x" },
        Pieces = new()
        {
            new PieceDef
            {
                Id = "def-a", Name = "Infantry", Kind = "model", Image = "h1", Back = "h2", Mesh = "h3", Texture = "h4", Script = "function on_load(s) end", ScriptHash = "0123abcd", ScriptState = "{\"n\":0}",
                Size = new Size2(1, 0.5), Thickness = 0.1, Tint = "#AABBCC", Tags = new() { "inf" },
                States = new() { new PieceStateDef { Name = "routed", Image = "h5", Back = "h6", Size = new Size2(2, 2) }, new PieceStateDef { Name = "plain" } },
                Counters = new() { new CounterDef { Name = "str", Min = 0, Max = 12, Default = 8 } },
                TextLines = new() { new TextLineDef { Name = "name", Default = "Bn" } },
                Attachments = new() { new AttachmentDef { Piece = "def-b", Offset = new Vec2(0.5, 0) } },
            },
        },
        Assets = new() { ["h1"] = new AssetEntry { File = "img/h1.png", Kind = "image", W = 64, H = 32 } },
    };

    public static IEnumerable<Command> AllCommands()
    {
        const string L = Layers.Umpire;
        yield return new SpawnPiece { Layer = L, Def = "def-a", Pos = new(1, 2), RotDeg = 15, Id = "p-000100", Label = "x", Scale = 2, Guid = "a1b2c3", ScriptState = "{\"n\":1}" };
        yield return new RestorePiece { Layer = "scratch:red", Piece = Piece("p-000101") };
        yield return new MovePiece { Layer = L, Id = "p-000001", Pos = new(3, 4), RotDeg = 45, Stack = 1 };
        yield return new DeletePiece { Layer = L, Id = "p-000001" };
        yield return new SetState { Layer = L, Id = "p-000001", State = 2 };
        yield return new SetCounter { Layer = L, Id = "p-000001", Name = "str", Value = 3 };
        yield return new SetText { Layer = L, Id = "p-000001", Name = "name", Value = "2nd Bn" };
        yield return new SetLabel { Layer = L, Id = "p-000001", Value = "new" };
        yield return new SetNote { Layer = L, Id = "p-000001", Value = "umpires' notes" };
        yield return new RestoreTurn { Index = 1, State = new GameState { ScenarioId = "s", Name = "restored", Clock = new GameClock { Turn = 1 } } };
        yield return new LoadSave { Name = "Turn 1", State = new GameState { ScenarioId = "s", Name = "loaded", Clock = new GameClock { Turn = 1 } } };
        yield return new SetTint { Layer = L, Id = "p-000001", Value = "#00FF00" };
        yield return new SetLocked { Layer = L, Id = "p-000001", Value = true };
        yield return new SetPieceOption { Layer = L, Id = "p-000001", Name = PieceOptions.NoCollision, Value = true };
        yield return new FlipPiece { Layer = L, Id = "p-000001", FaceDown = true };
        yield return new SetScale { Layer = L, Id = "p-000001", Scale = 1.5 };
        yield return new SetZone { Zone = new Zone { Id = "z-000001", Name = "North", Owner = "Ump2", Polygon = { new(0, 0), new(10, 0), new(10, 10) } } };
        yield return new AddPack { Id = "user.my-pieces", Hash = "abc" };
        yield return new RemoveZone { Id = "z-000001" };
        yield return new Attach { Layer = L, Parent = "p-000001", Child = "p-000002" };
        yield return new Detach { Layer = L, Child = "p-000002" };
        yield return new Stow { Layer = L, Holder = "p-000001", Id = "p-000002", Slot = 0 };
        yield return new Unstow { Layer = L, Holder = "p-000001", Id = "p-000002", Pos = new Vec2(3, 4), RotDeg = 90, Index = 1 };
        yield return new AddSheet { Sheet = Sheet("s-000200") };
        yield return new MoveSheet { Id = "s-000001", Pos = new(7, 8), RotDeg = 180, Z = 3 };
        yield return new LockSheet { Id = "s-000001", Locked = true };
        yield return new RenameSheet { Id = "s-000001", Name = "North sheet" };
        yield return new RemoveSheet { Id = "s-000001" };
        yield return new CropSheet { Id = "s-000001", Crop = new FaceBox { X = 0.1, Y = 0.05, W = 0.8, H = 0.9 } };
        yield return new SetSheetRelief { Id = "s-000001", Relief = new string('d', 64), Height = 1.5 };
        yield return new SetSheetPicture { Id = "s-000001", Image = "pic2", Package = "sheets", Size = new(20, 10) };
        yield return new SetLighting { Lighting = new Lighting { Warmth = 0.4, SunEnergy = 0.6, Ambient = 0.5, Shadows = false } };
        yield return new SetWind { Wind = new Wind { From = 225, Strength = 3.5 } };
        yield return new SetVisionRadius { Radius = 30 };
        yield return new SetPointerRule { AlwaysShown = true };
        yield return new SetBriefing { Text = "18:00. Plancenoit holds." };
        yield return new AddBoard { Name = "Plan" };
        yield return new RenameBoard { Id = "b-000001", Name = "Ground" };
        yield return new RemoveBoard { Id = "b-000001" };
        yield return new Draw { Layer = "scratch:red", Stroke = Stroke("d-000300") };
        yield return new EraseStroke { Layer = L, StrokeId = "d-000004" };
        yield return new StampDecal { Layer = L, Decal = new DecalDef { Id = "dc-000001", Picture = "blood", Name = "Blood", X = 1, Z = 2, W = 1.5, H = 1, Rot = 30 } };
        yield return new RemoveDecal { Layer = L, DecalId = "dc-000001" };
        yield return new SetDecalPalette { Entries = new() { new PaletteDecal { Name = "Guts", Picture = "guts", Size = 0.5 } } };
        yield return new RollDice { Sides = 10, Count = 3, Label = "melee", Results = new() { 1, 5, 10 }, ActorName = "Ann" };
        yield return new SetClock { Turn = 5, GameTime = "1809-05-21T12:00", IncrementMinutes = 30 };
        yield return new AdvanceClock();
        yield return new SetCalibration { UnitsPerMeter = 0.02, Units = "yd" };
        yield return new SetGrid { Grid = new GridDef { Kind = "hex", Pitch = 12.5, Subdivisions = 2, Color = "#1A1A2080" } };
        yield return new SetPicture { Layer = L, Id = "p-000001", Picture = "pack-x.marker" };
        yield return new PublishSighting();
        yield return new Undo { TargetSeq = 3 };
        yield return new SetScript { Layer = L, Id = "p-000001", Text = "function on_load(s) end" };
        yield return new SetGlobalScript { Text = "function on_turn(t) end" };
        yield return new SetScriptState { Layer = L, Id = "p-000001", State = "{\"ctr1\":9}" };
        yield return new SetTags { Layer = L, Id = "p-000001", Tags = new() { "Source", "France", "Brigade" } };
        yield return new SetMemo { Layer = L, Id = "p-000001", Memo = "{\"Unit\":{\"StateID\":2}}" };
        yield return new SetGlobalScriptState { State = "{}" };
        yield return new SetWidgets { Layer = L, Id = "p-000001", Widgets = { new Widget { Label = "Ammo 9", X = 0.1, Z = 0.2, OnClick = "up" } } };
        yield return new SetMenuItems { Layer = L, Id = "p-000001", Items = { new MenuItemDef { Label = "Rally", OnSelect = "rally" } } };
        yield return new ClickWidget { Layer = L, Id = "p-000001", Index = 1, Alt = true };
        yield return new SetHotkeys { Items = { new HotkeyDef { Label = "Magic Hotkey #3" }, new HotkeyDef { Label = "Toggle lines", OnUp = true } } };
        yield return new SetGlobalMenuItems { Items = { new MenuItemDef { Label = "Casualty Report" } } };
        yield return new Hotkey { Index = 1, Up = false, Layer = L, Piece = "p-000001", Pos = new Vec2(4, 5), Selected = { "p-000001", "p-000002" } };
        yield return new PickGlobalMenuItem { Index = 1, Pos = new Vec2(1, 2) };
        yield return new NumberTyped { Layer = L, Id = "p-000001", Number = 3 };
        yield return new SelectPiece { Layer = L, Id = "p-000001", To = "p:7", Selected = false };
        yield return new EditWidget { Layer = L, Id = "p-000001", Index = 1, Value = "Regiment" };
        yield return new PickMenuItem { Layer = L, Id = "p-000001", Index = 1 };
        yield return new HoldPiece { Layer = L, Id = "p-000001", Held = true };
        yield return new Say { Text = "Rally!", To = "p:7", Color = "#FFAA00" };
        yield return new SetDef { Layer = L, Id = "p-000001", Def = "def-b" };
        yield return new SetLines { Layer = L, Id = "p-000001", Lines = { new LineDef { Points = { new(0, 1), new(1, 0), new(0, -1) }, Color = "#FF000080", Width = 0.2, Loop = true } } };
        yield return new SetDecals { Layer = L, Id = "p-000001", Decals = { new DecalDef { Picture = "aa11", Name = "Arms", Face = "back", X = 0.2, Z = -0.1, W = 0.3, H = 0.25, Rot = 30 } } };
        yield return new SetUi { Tree = { new UiNode { Tag = "Panel", Attributes = { ["id"] = "p1", ["width"] = "200" }, Children = { new UiNode { Tag = "Button", Attributes = { ["id"] = "b1", ["text"] = "Go", ["onClick"] = "go" } } } } } };
        yield return new SetUiAttributes { Layer = L, Piece = "p-000001", Id = "b1", Attributes = { ["text"] = "Stop", ["active"] = "false" } };
        yield return new SetUiPictures { Pictures = { ["crest"] = "pic-crest" } };
        yield return new UiEvent { Id = "b1", Event = "click", Value = "-1" };
        yield return new ShowDialog { To = "p:7", Kind = "memo", Title = "Edit Unit", Text = "strength: 9", Options = { "a", "b" }, Token = "d-000001" };
        yield return new DialogResult { Token = "d-000001", Value = "strength: 8" };
        yield return new Batch
        {
            Items = new()
            {
                new MovePiece { Layer = L, Id = "p-000001", Pos = new(1, 1) },
                new Batch { Items = new() { new SetState { Layer = L, Id = "p-000002", State = 1 } } },
            },
        };
    }

    public static IEnumerable<Command> WithHeader(this IEnumerable<Command> cs)
    {
        long i = 1;
        foreach (var c in cs)
        {
            c.Seq = i;
            c.Actor = 100 + i;
            c.At = 1_700_000_000_000 + i;
            i++;
            yield return c;
        }
    }
}

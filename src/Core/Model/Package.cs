using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public record struct Vec2(double X, double Z)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    [System.Text.Json.Serialization.JsonIgnore]
    public double Length => Math.Sqrt(X * X + Z * Z);
}

public record struct Size2(double W, double H);

public sealed class PackSource
{
    public string? WorkshopId { get; set; }
    public List<string> Authors { get; set; } = new();
    public string Url { get; set; } = "";
    public string License { get; set; } = "free, IKS community";
}

public sealed class AssetEntry
{
    public string File { get; set; } = "";
    public string Kind { get; set; } = "image";
    public int W { get; set; }
    public int H { get; set; }
    public string? Url { get; set; }
}

public sealed class PackManifest : IJsonOnDeserialized, IJsonOnSerializing
{
    public const string UserPrefix = "user.";
    public static bool IsUserPackId(string id) =>
        id.StartsWith(UserPrefix) && id.Length > UserPrefix.Length && id.Length <= 60 &&
        id[UserPrefix.Length..].All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static string? NamedBy(IEnumerable<string> packs, string def) =>
        packs.Where(p => def.StartsWith(p + ".", StringComparison.Ordinal)).OrderByDescending(p => p.Length).FirstOrDefault();

    public const int CurrentFormat = 2;
    const int PrintsPlaced = 2;
    public int Format { get; set; } = CurrentFormat;
    public int ApiVersion { get; set; } = ScriptApi.Version;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public PackSource Source { get; set; } = new();
    public string? Description { get; set; }
    public List<PieceDef> Pieces { get; set; } = new();
    public Dictionary<string, AssetEntry> Assets { get; set; } = new();
    public bool Partial { get; set; }

    public const double MarkerBeside = 0.8;

    void IJsonOnDeserialized.OnDeserialized()
    {
        ContentVersions.Check("pack", Name is { Length: > 0 } ? Name : Id ?? "", Format, CurrentFormat, ApiVersion);
        if (Format >= PrintsPlaced) return;
        foreach (var d in Pieces) d.PlaceWhatPrintedByItself();
    }

    public bool MoveCounterMarkers()
    {
        bool moved = false;
        foreach (var d in Pieces)
        {
            if (!d.Counters.Any(c => c.SpawnsMarker != null)) continue;
            d.Attachments = d.Markers().ToList();
            foreach (var c in d.Counters) c.SpawnsMarker = null;
            moved = true;
        }
        return moved;
    }
    void IJsonOnSerializing.OnSerializing() { Format = CurrentFormat; ApiVersion = ScriptApi.Version; }

    public static string ShapeOf(string moduleName, int tileType) => moduleName switch
    {
        "Custom_Token" => "alpha",
        "Custom_Tile" => tileType switch { 1 => "hex", 2 => "circle", 3 => "rounded", _ => "box" },
        _ => "box",
    };
}

public sealed class PieceStateDef
{
    public string Name { get; set; } = "";
    public string? Shape { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Tint { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<List<Vec2>>? Outline { get; set; }
    public string? Image { get; set; }
    public string? Back { get; set; }
    public string? Mesh { get; set; }
    public string? Texture { get; set; }
    public Size2? Size { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? Up { get; set; }
    public string? Script { get; set; }
    public List<DecalDef>? Decals { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PartDef>? Parts { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Thickness { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ModuleStateData? Module { get; set; }
}

public sealed class ModuleStateData
{
    public string Type { get; set; } = "";
    public string Guid { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GmNotes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Memo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ScriptState { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Tags { get; set; }
}

public sealed class CounterDef
{
    public string Name { get; set; } = "";
    public int Min { get; set; }
    public int Max { get; set; } = 9;
    public int Default { get; set; }
    public string? SpawnsMarker { get; set; }
    public PrintPlace? Place { get; set; }
    public bool ShowName { get; set; } = true;
    public bool ShowButtons { get; set; } = true;
}

public sealed class TextLineDef
{
    public string Name { get; set; } = "";
    public string Default { get; set; } = "";
    public PrintPlace? Place { get; set; }
}

public sealed class PrintPlace
{
    public const string Front = "front", Back = "back";

    public string Side { get; set; } = Front;
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 1;
    public double H { get; set; } = 0.2;

    public static PrintPlace On(string side, FaceBox b) => new() { Side = side, X = b.X, Y = b.Y, W = b.W, H = b.H };
    public FaceBox Box() => new() { X = X, Y = Y, W = W, H = H };
    public PrintPlace Turned() => new() { Side = Side == Back ? Front : Back, X = X, Y = Y, W = W, H = H };
}

public sealed class PartDef
{
    public string Def { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Rot { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double TiltX { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double TiltZ { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool FaceDown { get; set; }
    public double Scale { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Tint { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int State { get; set; }
    public int Face { get; set; } = -1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<DecalDef>? Decals { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<LineDef>? Lines { get; set; }
}

public sealed class AttachmentDef
{
    public string Piece { get; set; } = "";
    public Vec2 Offset { get; set; }
}

public sealed class FaceBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 1;
    public double H { get; set; } = 0.2;
}

public sealed class ShineDef
{
    public string Color { get; set; } = "#E6E6E6";
    public double Strength { get; set; } = 0.1;
    public double Sharpness { get; set; } = 3;
    public double Rim { get; set; } = 0.1;
    public ShineDef Clone() => new() { Color = Color, Strength = Strength, Sharpness = Sharpness, Rim = Rim };
}

public static class Finishes
{
    public const string Plastic = "plastic", Wood = "wood", Metal = "metal", Cardboard = "cardboard", Glass = "glass";
    public static readonly string[] All = [Plastic, Wood, Metal, Cardboard, Glass];
    public static string? Of(int index) => index >= 0 && index < All.Length ? All[index] : null;
}

public sealed class Lettering
{
    public string Text { get; set; } = "";
    public double Size { get; set; } = 64;
    public string Color { get; set; } = "#FFFFFF";
    public double UnitsPerPixel { get; set; } = 1.0 / 80;
    public Lettering Clone() => (Lettering)MemberwiseClone();
}

public sealed class DieFace
{
    public string Value { get; set; } = "";
    public double[] Up { get; set; } = [0, 1, 0];
    public DieFace Clone() => new() { Value = Value, Up = Up.ToArray() };
    [JsonIgnore] public double? Number => double.TryParse(Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
}

public sealed class PieceDef : IJsonOnDeserialized
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "flat";
    public string? Image { get; set; }
    public string? Back { get; set; }
    public string? Mesh { get; set; }
    public string? Texture { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Normal { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<List<Vec2>>? Outline { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ModuleType { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? ModuleTags { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Sources { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ModuleTop { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ModuleOrigin { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ModuleRise { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ModuleObjectTag { get; set; }
    public string? AddOnMesh { get; set; }
    public string? AddOnTexture { get; set; }
    public Size2 Size { get; set; } = new(1, 1);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Size2? LocalUnit { get; set; }
    public string Shape { get; set; } = "box";
    public double Thickness { get; set; } = 0.2;
    public string Tint { get; set; } = "#FFFFFF";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool TintsPictures { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Shadowless { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Holds { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<PieceStateDef> States { get; set; } = new();
    public List<DieFace> Faces { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Finish { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShineDef? Shine { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int PageOffset { get; set; }
    public List<CounterDef> Counters { get; set; } = new();
    public List<TextLineDef> TextLines { get; set; } = new();
    public List<AttachmentDef> Attachments { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PartDef>? Parts { get; set; }
    [JsonIgnore] public IEnumerable<PartDef> AllParts => (Parts ?? []).Concat(States.SelectMany(s => s.Parts ?? []));
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FaceBox? NameBox { get; set; }
    public string? Script { get; set; }
    public string? ScriptHash { get; set; }
    public string? ScriptState { get; set; }
    public EffectDef? Effect { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (Faces.Count > 0 || States.Count < 2) return;
        if (!States.All(s => s.Up is { Length: 3 }) && !(Kind == "model" && Tags.Contains("die"))) return;
        Faces = States.Select(s => new DieFace { Value = s.Name, Up = s.Up is { Length: 3 } up ? up : [0, 1, 0] }).ToList();
        States = new();
    }

    public void PlaceWhatPrintedByItself()
    {
        if (!NoteCard.Is(this))
        {
            bool boxy = Shape is "box" or "rounded" && Kind != "model";
            PiecePrinting.PinDefaults(this, NameBox ?? PiecePrinting.Band(boxy), boxy);
        }
        NameBox = null;
    }

    public IEnumerable<AttachmentDef> Markers()
    {
        foreach (var a in Attachments) yield return a;
        foreach (var marker in Counters.Select(c => c.SpawnsMarker).OfType<string>().Distinct())
            if (!Attachments.Any(a => a.Piece == marker)) yield return new AttachmentDef { Piece = marker, Offset = new Vec2(Size.W * PackManifest.MarkerBeside, 0) };
    }
}

public sealed class EffectDef
{
    public string Kind { get; set; } = "smoke";
    public string? Volley { get; set; }
    public double Amount { get; set; } = 1;
    public double Height { get; set; } = 1;
    public double Puff { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public double Wind { get; set; } = 1;
    public double Every { get; set; }
    public string? Color { get; set; }
    public bool Light { get; set; } = true;
    public bool Smoke { get; set; } = true;
    public bool Lingers { get; set; }
    public bool OwnPicture { get; set; }
    public List<double>? At { get; set; }

    public EffectDef Clone() => (EffectDef)MemberwiseClone();
}

public sealed class SheetDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string Package { get; set; } = "";
    public Vec2 Pos { get; set; }
    public double RotDeg { get; set; }
    public Size2 Size { get; set; } = new(10, 10);
    public bool Locked { get; set; } = true;
    public int Z { get; set; }
    public FaceBox? Crop { get; set; }
    public string Kind { get; set; } = "map";
    public string? Url { get; set; }
    public string? Relief { get; set; }
    public double Height { get; set; } = 1;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPicture => Image.Length > 0;
}

public sealed class Calibration
{
    public double? UnitsPerMeter { get; set; }
    public string Units { get; set; } = "m";

    public const double PacesPerMetre = 1.0 / 0.762;
    public const double YardsPerMetre = 1.0 / 0.9144;
    const double YardsPerMile = 1760;

    public double PerMetre => Units switch { "pace" => PacesPerMetre, "yd" => YardsPerMetre, _ => 1 };

    public string UnitName => UnitsPerMeter is { } u && u > 0 ? Units switch { "pace" => "paces", "yd" => "yd", _ => "m" } : "u";

    [System.Text.Json.Serialization.JsonIgnore]
    public (double Length, string Words) ScaleBar => Units switch
    {
        "pace" => (1000.0, "a thousand paces"),
        "yd" => (YardsPerMile, "a mile"),
        _ => (1000.0, "a kilometre"),
    };

    public double ToTableUnits(double x) => UnitsPerMeter is { } u && u > 0
        ? Units switch { "pace" => x / PacesPerMetre * u, "yd" => x / YardsPerMetre * u, _ => x * u }
        : x;

    public double FromTableUnits(double x) => UnitsPerMeter is { } u && u > 0
        ? Units switch { "pace" => x / u * PacesPerMetre, "yd" => x / u * YardsPerMetre, _ => x / u }
        : x;

    public string Describe(double tableUnits)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        if (UnitsPerMeter is not { } upm || upm <= 0) return string.Format(c, "{0:0.00} u", tableUnits);
        double m = tableUnits / upm, paces = m * PacesPerMetre;
        switch (Units)
        {
            case "pace": return string.Format(c, "{0:#,0} paces", paces);
            case "yd":
            {
                double yd = m * YardsPerMetre;
                return yd >= YardsPerMile
                    ? string.Format(c, "{0:0.00} mi ({1:#,0} paces)", yd / YardsPerMile, paces)
                    : string.Format(c, "{0:#,0} yd ({1:#,0} paces)", yd, paces);
            }
            default: return m >= 1000
                ? string.Format(c, "{0:0.00} km ({1:#,0} paces)", m / 1000, paces)
                : string.Format(c, "{0:#,0} m ({1:#,0} paces)", m, paces);
        }
    }

    public string DescribeSize(double w, double h)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        if (UnitsPerMeter is not { } upm || upm <= 0) return string.Format(c, "{0:0.#} × {1:0.#} u", w, h);
        double mw = w / upm, mh = h / upm;
        return Units switch
        {
            "pace" => string.Format(c, "{0:#,0} × {1:#,0} paces", mw * PacesPerMetre, mh * PacesPerMetre),
            "yd" => string.Format(c, "{0:0.0} × {1:0.0} mi", mw * YardsPerMetre / YardsPerMile, mh * YardsPerMetre / YardsPerMile),
            _ => string.Format(c, "{0:0.0} × {1:0.0} km", mw / 1000, mh / 1000),
        };
    }
}

public sealed class GameClock
{
    public int Turn { get; set; }
    public string GameTime { get; set; } = "1800-01-01T06:00";
    public int IncrementMinutes { get; set; } = 15;

    public const int MinutesInHour = 60, MinutesInDay = 1440, MostMinutes = 100000;

    public static string StepText(int minutes)
    {
        if (minutes >= MinutesInDay && minutes % MinutesInDay == 0) return minutes == MinutesInDay ? "1 day" : $"{minutes / MinutesInDay} days";
        if (minutes < MinutesInHour) return $"{minutes}′";
        return minutes % MinutesInHour == 0 ? $"{minutes / MinutesInHour} h" : $"{minutes / MinutesInHour} h {minutes % MinutesInHour}′";
    }

    public bool CanAdvance() =>
        !DateTime.TryParse(GameTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t)
        || (DateTime.MaxValue - t).TotalMinutes >= IncrementMinutes;

    public void Advance()
    {
        Turn++;
        if (DateTime.TryParse(GameTime, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var t))
            GameTime = t.AddMinutes(IncrementMinutes).ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class PieceInstance
{
    public string Id { get; set; } = "";
    public string Def { get; set; } = "";
    public Vec2 Pos { get; set; }
    public double RotDeg { get; set; }
    public int Stack { get; set; }
    public int State { get; set; }
    public int Face { get; set; } = -1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FacePicture { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool EffectOff { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Lettering? Lettering { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? StateGuids { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? StateScripts { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Worth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int WorthGroups { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<DieFace>? Faces { get; set; }
    public Dictionary<string, int> Counters { get; set; } = new();
    public Dictionary<string, string> Text { get; set; } = new();
    public List<string> Attached { get; set; } = new();
    public string? TintOverride { get; set; }
    public string? Label { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Y { get; set; }
    public bool FaceDown { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double TiltX { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double TiltZ { get; set; }
    [JsonIgnore] public bool HasTilt => TiltX != 0 || TiltZ != 0;
    public string? Note { get; set; }
    public string? Picture { get; set; }
    public string? Script { get; set; }
    public string ScriptState { get; set; } = "";
    public string? Guid { get; set; }
    public List<string>? Tags { get; set; }
    public bool Locked { get; set; }
    public bool NoCollision { get; set; }
    public bool PhysicalRoll { get; set; }
    public bool NotSticky { get; set; }
    public bool NotBoxSelected { get; set; }
    public bool MeasuresMoves { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool NotInteractable { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Highlight { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? BookHighlight { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WebMesh { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WebTexture { get; set; }
    public string Memo { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GmNotes { get; set; }
    public List<Widget> Widgets { get; set; } = new();
    public int DrawnOnState { get; set; }
    public List<MenuItemDef> MenuItems { get; set; } = new();
    public List<LineDef> Lines { get; set; } = new();
    public List<DecalDef> Decals { get; set; } = new();
    public int? DecalsOnState { get; set; }
    public List<UiNode> Ui { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KTable.Core.Panels.UiLayout? UiLayout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<KTable.Core.Panels.UiAsset>? UiAssets { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PieceInstance>? Stowed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Holds { get; set; }

    public bool HoldsPieces(PieceDef? def) => Holds ?? (def?.Holds == true || Stowed is { Count: > 0 });
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TakeOrder { get; set; }

    public PieceInstance Clone() => new()
    {
        Id = Id, Def = Def, Pos = Pos, RotDeg = RotDeg, Stack = Stack, State = State, Face = Face, Faces = Faces?.Select(f => f.Clone()).ToList(),
        Worth = Worth, WorthGroups = WorthGroups, FacePicture = FacePicture, EffectOff = EffectOff, Lettering = Lettering?.Clone(), StateGuids = StateGuids?.ToList(),
        StateScripts = StateScripts?.ToList(),
        Counters = new(Counters), Text = new(Text), Attached = new(Attached),
        TintOverride = TintOverride, Label = Label, Scale = Scale, Y = Y, FaceDown = FaceDown, TiltX = TiltX, TiltZ = TiltZ, Note = Note, Picture = Picture, Locked = Locked, Holds = Holds,
        NoCollision = NoCollision, PhysicalRoll = PhysicalRoll, NotSticky = NotSticky, NotBoxSelected = NotBoxSelected, MeasuresMoves = MeasuresMoves,
        NotInteractable = NotInteractable, Highlight = Highlight, BookHighlight = BookHighlight?.ToArray(), WebMesh = WebMesh, WebTexture = WebTexture,
        Script = Script, ScriptState = ScriptState, Guid = Guid, Tags = Tags == null ? null : new(Tags), Memo = Memo, GmNotes = GmNotes,
        Widgets = Widgets.Select(w => w.Clone()).ToList(), DrawnOnState = DrawnOnState, MenuItems = MenuItems.Select(m => m.Clone()).ToList(),
        Lines = Lines.Select(l => l.Clone()).ToList(), Decals = Decals.Select(d => d.Clone()).ToList(), DecalsOnState = DecalsOnState,
        Ui = Ui.Select(n => n.Clone()).ToList(), UiLayout = UiLayout?.Clone(), UiAssets = UiAssets?.Select(a => a.Clone()).ToList(),
        Stowed = Stowed?.Select(s => s.Clone()).ToList(), TakeOrder = TakeOrder,
    };

    [JsonIgnore] public int ShownFace => Face >= 0 ? Face : State;

    public static List<DieFace> FacesOf(PieceDef? def, PieceInstance p) => p.Faces ?? def?.Faces ?? [];

    public PieceInstance? NextOut(Random rng)
    {
        if (Stowed is not { Count: > 0 } all) return null;
        var loose = all.Where(s => !all.Any(o => o.Attached.Contains(s.Id))).ToList();
        if (loose.Count == 0) return null;
        return TakeOrder switch
        {
            TakeOrders.Last => loose[^1],
            TakeOrders.Random => loose[rng.Next(loose.Count)],
            _ => loose[0],
        };
    }

    public IEnumerable<PieceInstance> AllStowed()
    {
        foreach (var s in Stowed ?? [])
        {
            yield return s;
            foreach (var deeper in s.AllStowed()) yield return deeper;
        }
    }
}

public static class TakeOrders
{
    public const string First = "first", Last = "last", Random = "random";
}

public sealed class UiNode
{
    Dictionary<string, string> _attributes = UiAttributes.Fresh();
    public string Tag { get; set; } = "Panel";
    public Dictionary<string, string> Attributes { get => _attributes; set => _attributes = UiAttributes.Folded(value); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Value { get; set; }
    public List<UiNode> Children { get; set; } = new();
    public string? Id => Attributes.GetValueOrDefault("id");
    public UiNode Clone() => new() { Tag = Tag, Attributes = new(Attributes, StringComparer.OrdinalIgnoreCase), Value = Value, Children = Children.Select(c => c.Clone()).ToList() };
    public UiNode? Find(string id)
    {
        if (Id == id) return this;
        foreach (var c in Children) if (c.Find(id) is { } f) return f;
        return null;
    }
}

public static class UiAttributes
{
    public static Dictionary<string, string> Fresh() => new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> Folded(Dictionary<string, string>? from)
    {
        var d = Fresh();
        if (from == null) return d;
        foreach (var (k, v) in from) d[k] = v;
        return d;
    }
}

public sealed class LineDef
{
    public List<Vec2> Points { get; set; } = new();
    public string Color { get; set; } = "#FFFFFF";
    public double Width { get; set; } = 0.1;
    public bool Loop { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Round { get; set; }
    public LineDef Clone() => new() { Points = new(Points), Color = Color, Width = Width, Loop = Loop, Round = Round };
}

public sealed class DecalDef
{
    static readonly List<DecalDef> None = new();

    public static List<DecalDef> ShownOn(PieceInstance p, PieceDef? def)
    {
        if (p.DecalsOnState == p.State) return p.Decals;
        var looks = def?.States;
        if (looks == null || !looks.Any(s => s.Decals is { Count: > 0 })) return p.Decals;
        return (p.State >= 0 && p.State < looks.Count ? looks[p.State].Decals : null) ?? None;
    }

    public const int MostOnAPiece = 64;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Id { get; set; }
    public string Picture { get; set; } = "";
    public string Name { get; set; } = "";
    public string Face { get; set; } = "front";
    public double X { get; set; }
    public double Z { get; set; }
    public double W { get; set; } = 0.2;
    public double H { get; set; } = 0.2;
    public double Rot { get; set; }
    public DecalDef Clone() => (DecalDef)MemberwiseClone();
}

public sealed class PaletteDecal
{
    public const int MostInAPalette = 64, MostOnTheTable = 1024;
    public const double SmallestSize = 0.01, LargestSize = 1000;
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public double Size { get; set; } = 1;
    public PaletteDecal Clone() => (PaletteDecal)MemberwiseClone();
}

public sealed class Widget
{
    public string Kind { get; set; } = "button";
    public string Face { get; set; } = "front";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Y { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Rot { get; set; }
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public double Font { get; set; } = 0.05;
    public string? Color { get; set; }
    public string? TextColor { get; set; }
    public string Align { get; set; } = "center";
    public int Lines { get; set; } = 1;
    public string? Tooltip { get; set; }
    public string? OnClick { get; set; }
    public string? OnChange { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? HoverColor { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? PressColor { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Validation { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Tab { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool Locked => Kind == "label";
    public Widget Clone() => (Widget)MemberwiseClone();
}

public sealed class MenuItemDef
{
    public string Label { get; set; } = "";
    public string? OnSelect { get; set; }
    public MenuItemDef Clone() => (MenuItemDef)MemberwiseClone();
}

public sealed class HotkeyDef
{
    public string Label { get; set; } = "";
    public bool OnUp { get; set; }
    public HotkeyDef Clone() => (HotkeyDef)MemberwiseClone();
}

public sealed class Stroke
{
    public string Id { get; set; } = "";
    public List<Vec2> Points { get; set; } = new();
    public double Width { get; set; } = 0.3;
    public string Color { get; set; } = "#000000";
    public string Board { get; set; } = "";
    public string? Text { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Round { get; set; }
}

public sealed class BoardDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public BoardDef Clone() => (BoardDef)MemberwiseClone();
}

public sealed class Lighting
{
    public double SunEnergy { get; set; } = 0.54;
    public double Ambient { get; set; } = 0.9;
    public double Warmth { get; set; } = 0.35;
    public bool Shadows { get; set; } = true;
    public bool FromClock { get; set; }
    public double TableShade { get; set; }
    public string Backdrop { get; set; } = "";

    public const double Black = 1, SeeThrough = 1.05;
    public static bool IsSeeThrough(double tableShade) => tableShade > Black;
    public string MapStyle { get; set; } = MapStyles.Survey;
    public bool Contours { get; set; }
    public double SnowLine { get; set; }
    public bool Streams { get; set; }
}

public sealed class Wind
{
    public double From { get; set; } = 270;
    public double Strength { get; set; } = 1;
    public Wind Clone() => (Wind)MemberwiseClone();

    static readonly string[] Points = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
    public static string Point(double deg) => Points[(int)Math.Round(((deg % 360) + 360) % 360 / 22.5) % 16];
    public static string Words(double s) => s < 0.25 ? "calm" : s < 1.25 ? "a light breeze" : s < 2.25 ? "a moderate breeze" : s < 3.25 ? "a fresh wind" : s < 4.25 ? "a strong wind" : "a gale";
}

public sealed class GridDef
{
    public string Kind { get; set; } = "none";
    public double Pitch { get; set; } = 10;
    public int Subdivisions { get; set; } = 1;
    public string Color { get; set; } = "#1A1A2080";
}

public sealed class Scenario : IJsonOnDeserialized, IJsonOnSerializing
{
    public const int CurrentFormat = 1;
    public int Format { get; set; } = CurrentFormat;
    public int ApiVersion { get; set; } = ScriptApi.Version;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Packs { get; set; } = new();
    public List<SheetDef> Sheets { get; set; } = new();
    public GridDef Grid { get; set; } = new();
    public Calibration Calibration { get; set; } = new();
    public GameClock Clock { get; set; } = new();
    public Lighting Lighting { get; set; } = new();
    public Wind Wind { get; set; } = new();
    public double VisionRadius { get; set; } = 40;
    public bool PointersAlwaysShown { get; set; }
    public string? Briefing { get; set; }
    public List<BoardDef> Boards { get; set; } = new();
    public List<PieceInstance> UmpireBoard { get; set; } = new();
    public Dictionary<string, List<Stroke>> Drawings { get; set; } = new();
    public Dictionary<string, List<DecalDef>> Decals { get; set; } = new();
    public List<PaletteDecal> DecalPalette { get; set; } = new();
    public string? Script { get; set; }
    public string ScriptState { get; set; } = "";
    public List<UiNode> Ui { get; set; } = new();
    public Dictionary<string, string> UiPictures { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KTable.Core.Panels.UiLayout? UiLayout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<KTable.Core.Panels.UiAsset>? UiAssets { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? UiLinks { get; set; }

    void IJsonOnDeserialized.OnDeserialized() => ContentVersions.Check("scenario", Name is { Length: > 0 } ? Name : Id ?? "", Format, CurrentFormat, ApiVersion);
    void IJsonOnSerializing.OnSerializing() { Format = CurrentFormat; ApiVersion = ScriptApi.Version; }
}

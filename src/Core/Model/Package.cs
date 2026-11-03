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

    public const int CurrentFormat = 1;
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

    void IJsonOnDeserialized.OnDeserialized() => ContentVersions.Check("pack", Name is { Length: > 0 } ? Name : Id ?? "", Format, CurrentFormat, ApiVersion);

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

    public static string ShapeOf(string ttsName, int tileType) => ttsName switch
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
    public string? Image { get; set; }
    public string? Back { get; set; }
    public string? Mesh { get; set; }
    public string? Texture { get; set; }
    public Size2? Size { get; set; }
    public double[]? Up { get; set; }
    public string? Script { get; set; }
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

public sealed class PieceDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "flat";
    public string? Image { get; set; }
    public string? Back { get; set; }
    public string? Mesh { get; set; }
    public string? Texture { get; set; }
    public string? AddOnMesh { get; set; }
    public string? AddOnTexture { get; set; }
    public Size2 Size { get; set; } = new(1, 1);
    public string Shape { get; set; } = "box";
    public double Thickness { get; set; } = 0.2;
    public string Tint { get; set; } = "#FFFFFF";
    public List<string> Tags { get; set; } = new();
    public List<PieceStateDef> States { get; set; } = new();
    public List<CounterDef> Counters { get; set; } = new();
    public List<TextLineDef> TextLines { get; set; } = new();
    public List<AttachmentDef> Attachments { get; set; } = new();
    public FaceBox? NameBox { get; set; }
    public string? Script { get; set; }
    public string? ScriptHash { get; set; }
    public string? ScriptState { get; set; }
    public EffectDef? Effect { get; set; }

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
    public Dictionary<string, int> Counters { get; set; } = new();
    public Dictionary<string, string> Text { get; set; } = new();
    public List<string> Attached { get; set; } = new();
    public string? TintOverride { get; set; }
    public string? Label { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Y { get; set; }
    public bool FaceDown { get; set; }
    public string? Note { get; set; }
    public string? Picture { get; set; }
    public string? Script { get; set; }
    public string ScriptState { get; set; } = "";
    public string? Guid { get; set; }
    public List<string>? Tags { get; set; }
    public bool Locked { get; set; }
    public bool NoCollision { get; set; }
    public bool PhysicalRoll { get; set; }
    public string Memo { get; set; } = "";
    public List<Widget> Widgets { get; set; } = new();
    public int DrawnOnState { get; set; }
    public List<MenuItemDef> MenuItems { get; set; } = new();
    public List<LineDef> Lines { get; set; } = new();
    public List<DecalDef> Decals { get; set; } = new();
    public List<UiNode> Ui { get; set; } = new();

    public PieceInstance Clone() => new()
    {
        Id = Id, Def = Def, Pos = Pos, RotDeg = RotDeg, Stack = Stack, State = State,
        Counters = new(Counters), Text = new(Text), Attached = new(Attached),
        TintOverride = TintOverride, Label = Label, Scale = Scale, Y = Y, FaceDown = FaceDown, Note = Note, Locked = Locked,
        Script = Script, ScriptState = ScriptState, Guid = Guid, Tags = Tags == null ? null : new(Tags), Memo = Memo,
        Widgets = Widgets.Select(w => w.Clone()).ToList(), DrawnOnState = DrawnOnState, MenuItems = MenuItems.Select(m => m.Clone()).ToList(),
        Lines = Lines.Select(l => l.Clone()).ToList(), Decals = Decals.Select(d => d.Clone()).ToList(), Ui = Ui.Select(n => n.Clone()).ToList(),
    };
}

public sealed class UiNode
{
    public string Tag { get; set; } = "Panel";
    public Dictionary<string, string> Attributes { get; set; } = new();
    public List<UiNode> Children { get; set; } = new();
    public string? Id => Attributes.GetValueOrDefault("id");
    public UiNode Clone() => new() { Tag = Tag, Attributes = new(Attributes), Children = Children.Select(c => c.Clone()).ToList() };
    public UiNode? Find(string id)
    {
        if (Id == id) return this;
        foreach (var c in Children) if (c.Find(id) is { } f) return f;
        return null;
    }
}

public sealed class LineDef
{
    public List<Vec2> Points { get; set; } = new();
    public string Color { get; set; } = "#FFFFFF";
    public double Width { get; set; } = 0.1;
    public bool Loop { get; set; }
    public LineDef Clone() => new() { Points = new(Points), Color = Color, Width = Width, Loop = Loop };
}

public sealed class DecalDef
{
    public const int MostOnAPiece = 64;
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

public sealed class Widget
{
    public string Kind { get; set; } = "button";
    public string Face { get; set; } = "front";
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
}

public sealed class BoardDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public BoardDef Clone() => (BoardDef)MemberwiseClone();
}

public sealed class Lighting
{
    public double SunEnergy { get; set; } = 0.35;
    public double Ambient { get; set; } = 0.72;
    public double Warmth { get; set; } = 0;
    public bool Shadows { get; set; } = true;
    public bool FromClock { get; set; }
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
    public string? Script { get; set; }
    public string ScriptState { get; set; } = "";

    void IJsonOnDeserialized.OnDeserialized() => ContentVersions.Check("scenario", Name is { Length: > 0 } ? Name : Id ?? "", Format, CurrentFormat, ApiVersion);
    void IJsonOnSerializing.OnSerializing() { Format = CurrentFormat; ApiVersion = ScriptApi.Version; }
}

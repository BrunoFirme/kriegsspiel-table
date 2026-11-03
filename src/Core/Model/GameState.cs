namespace KTable.Core.Model;

public enum Role { Observer, Umpire, LeadUmpire, HostUmpire }

public sealed class PeerInfo
{
    public long PeerId { get; set; }
    public string Name { get; set; } = "";
    public Role Role { get; set; }
}

public static class Layers
{
    public const string Umpire = "umpire";
}

public sealed class GameState
{
    public int Format { get; set; } = 1;
    public string ScenarioId { get; set; } = "";
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
    public Dictionary<string, List<PieceInstance>> Layers { get; set; } = new();
    public Dictionary<string, List<Stroke>> Drawings { get; set; } = new();
    public List<BoardDef> Boards { get; set; } = new();
    public List<DiceRoll> Dice { get; set; } = new();
    public long NextId { get; set; } = 1;
    public long LastSeq { get; set; }
    public List<Zone> Zones { get; set; } = new();
    public string? Script { get; set; }
    public string ScriptState { get; set; } = "";
    public List<UiNode> Ui { get; set; } = new();
    public List<HotkeyDef> Hotkeys { get; set; } = new();
    public List<MenuItemDef> MenuItems { get; set; } = new();

    public string? PackNamedBy(string def) =>
        Packs.Where(p => def.StartsWith(p + ".", StringComparison.Ordinal)).OrderByDescending(p => p.Length).FirstOrDefault();

    public UiNode? UiNode(string id)
    {
        foreach (var n in Ui) if (n.Find(id) is { } f) return f;
        return null;
    }

    public List<UiNode>? UiRoot(string? layer, string? piece) => piece == null ? Ui : Find(layer ?? KTable.Core.Model.Layers.Umpire, piece)?.Ui;

    public UiNode? UiNode(string? layer, string? piece, string id)
    {
        var root = UiRoot(layer, piece);
        if (root == null) return null;
        foreach (var n in root) if (n.Find(id) is { } f) return f;
        return null;
    }

    public Scenario ToScenario(string id, string name)
    {
        var s = new Scenario
        {
            Id = id, Name = name, Packs = Packs.ToList(),
            Sheets = Sheets.Select(Json.Clone).ToList(), Grid = Json.Clone(Grid),
            Calibration = new Calibration { UnitsPerMeter = Calibration.UnitsPerMeter },
            Clock = new GameClock { Turn = Clock.Turn, GameTime = Clock.GameTime, IncrementMinutes = Clock.IncrementMinutes },
            Lighting = Json.Clone(Lighting), Wind = Wind.Clone(),
            VisionRadius = VisionRadius, PointersAlwaysShown = PointersAlwaysShown, Briefing = Briefing,
            UmpireBoard = Layer(Model.Layers.Umpire).Select(p => p.Clone()).ToList(),
            Script = Script, ScriptState = ScriptState,
        };
        s.Drawings["umpire"] = DrawLayer(Model.Layers.Umpire).Select(Json.Clone).ToList();
        s.Boards = Boards.Select(b => b.Clone()).ToList();
        return s;
    }

    public static GameState FromScenario(Scenario s)
    {
        var g = new GameState
        {
            ScenarioId = s.Id, Name = s.Name, Packs = new(s.Packs),
            Sheets = s.Sheets.Select(Json.Clone).ToList(), Grid = Json.Clone(s.Grid ?? new GridDef()), Calibration = new() { UnitsPerMeter = s.Calibration.UnitsPerMeter },
            Lighting = Json.Clone(s.Lighting ?? new Lighting()), Wind = (s.Wind ?? new Wind()).Clone(),
            VisionRadius = s.VisionRadius > 0 ? s.VisionRadius : 40, PointersAlwaysShown = s.PointersAlwaysShown, Briefing = s.Briefing,
            Clock = new() { Turn = s.Clock.Turn, GameTime = s.Clock.GameTime, IncrementMinutes = s.Clock.IncrementMinutes },
            Script = s.Script, ScriptState = s.ScriptState ?? "",
        };
        g.Layers[Model.Layers.Umpire] = s.UmpireBoard.Select(p => p.Clone()).ToList();
        g.Drawings[Model.Layers.Umpire] = s.Drawings.TryGetValue("umpire", out var u) ? u.Select(Json.Clone).ToList() : new();
        g.Boards = s.Boards.Select(b => b.Clone()).ToList();
        g.NextId = 1;
        g.EnsureNextId();
        g.EnsureBoard();
        return g;
    }

    public void EnsureBoard()
    {
        if (Boards.Count == 0) Boards.Add(new BoardDef { Id = NewId("b"), Name = "Layer 1" });
        var first = Boards[0].Id;
        foreach (var l in Drawings.Values)
            foreach (var s in l)
                if (string.IsNullOrEmpty(s.Board)) s.Board = first;
    }

    public void EnsureNextId()
    {
        long max = 0;
        var ids = Layers.Values.SelectMany(x => x).Select(p => p.Id)
            .Concat(Drawings.Values.SelectMany(x => x).Select(s => s.Id))
            .Concat(Sheets.Select(s => s.Id))
            .Concat(Zones.Select(z => z.Id))
            .Concat(Boards.Select(b => b.Id));
        foreach (var id in ids)
        {
            if (id == null) continue;
            var dash = id.IndexOf('-');
            if (dash > 0 && long.TryParse(id[(dash + 1)..], out var n)) max = Math.Max(max, n);
        }
        NextId = Math.Max(NextId, max + 1);
    }

    public void CopyFrom(GameState src)
    {
        var s = Json.Clone(src);
        var packs = Packs.Union(s.Packs ?? []).ToList();
        Format = s.Format; ScenarioId = s.ScenarioId; Name = s.Name;
        Packs = packs;
        Sheets = s.Sheets; Grid = s.Grid; Calibration = s.Calibration; Clock = s.Clock; Lighting = s.Lighting; Wind = s.Wind ?? new Wind();
        VisionRadius = s.VisionRadius; PointersAlwaysShown = s.PointersAlwaysShown; Briefing = s.Briefing; Layers = s.Layers; Drawings = s.Drawings; Dice = s.Dice; Zones = s.Zones; Boards = s.Boards;
        Script = s.Script; ScriptState = s.ScriptState; Ui = s.Ui; Hotkeys = s.Hotkeys; MenuItems = s.MenuItems;
        NextId = Math.Max(NextId, s.NextId);
        EnsureNextId();
    }

    public List<PieceInstance> Layer(string layer)
    {
        if (!Layers.TryGetValue(layer, out var l)) Layers[layer] = l = new();
        return l;
    }

    public List<Stroke> DrawLayer(string layer)
    {
        if (!Drawings.TryGetValue(layer, out var l)) Drawings[layer] = l = new();
        return l;
    }

    public PieceInstance? Find(string layer, string id)
    {
        if (!Layers.TryGetValue(layer, out var l)) return null;
        const int ScanIsFasterBelow = 64;
        if (l.Count < ScanIsFasterBelow) return l.FirstOrDefault(p => p.Id == id);
        _index ??= new();
        if (!_index.TryGetValue(layer, out var ix) || ix.List != l) _index[layer] = ix = new Index(l);
        if (ix.At.TryGetValue(id, out var at) && at < l.Count && l[at].Id == id) return l[at];
        int found = l.FindIndex(p => p.Id == id);
        if (++ix.Misses > 16) ix.Refile();
        else if (found >= 0) ix.At[id] = found;
        return found >= 0 ? l[found] : null;
    }

    Dictionary<string, Index>? _index;

    sealed class Index
    {
        public readonly List<PieceInstance> List;
        public readonly Dictionary<string, int> At = new(StringComparer.Ordinal);
        public int Misses;
        public Index(List<PieceInstance> list) { List = list; Refile(); }
        public void Refile()
        {
            At.Clear(); Misses = 0;
            for (int i = 0; i < List.Count; i++) At.TryAdd(List[i].Id, i);
        }
    }

    public string NewId(string prefix = "p") => $"{prefix}-{NextId++:D6}";

    public GameState DeepClone() => Json.Clone(this);
}

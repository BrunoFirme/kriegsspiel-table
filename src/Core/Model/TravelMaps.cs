using KTable.Core.Protocol;

namespace KTable.Core.Model;

public sealed class TravelMaps
{
    readonly Dictionary<string, (ReliefGrid? Relief, TravelMap? Map)> _maps = new();
    GameState? _of;

    public int Built { get; private set; }

    public void Forget() => _maps.Clear();

    public void After(Command c)
    {
        if (Shapes(c)) Forget();
    }

    public static bool Shapes(Command c) => c switch
    {
        Batch b => b.Items.Any(Shapes),
        LayBuilt or SetBuilt or RemoveBuilt or AddSheet or MoveSheet or RemoveSheet or CropSheet or SetSheetRelief or SetSheetPicture or SetCalibration => true,
        Undo or RestoreTurn or LoadSave => true,
        _ => false,
    };

    public static IReadOnlyDictionary<string, Traveller> Travellers(GameState state, Func<string, PackManifest?> pack) =>
        Model.Travellers.Merged(Model.Travellers.AgeOf(state), state.Packs.Select(pack).OfType<PackManifest>());

    public static string? Unknown(IReadOnlyDictionary<string, Traveller> all, string? by, out Traveller? who)
    {
        string name = (by ?? Model.Travellers.Foot).Trim().ToLowerInvariant();
        who = all.GetValueOrDefault(name);
        return who != null ? null : $"no traveller \"{by}\": there are {string.Join(", ", all.Keys.Order(StringComparer.Ordinal))}";
    }

    public static SheetDef? SheetOf(GameState state, Vec2 from, Vec2 to) =>
        PlaceTree.SheetUnder(state, from) is { } id && state.Sheets.FirstOrDefault(s => s.Id == id) is { } sheet && Ground.Uv(sheet, to) != null ? sheet : null;

    public TravelMap? Map(GameState state, SheetDef sheet, Func<SheetDef, ReliefGrid?> relief)
    {
        if (!ReferenceEquals(state, _of))
        {
            _maps.Clear();
            _of = state;
        }
        var grid = relief(sheet);
        if (_maps.TryGetValue(sheet.Id, out var kept) && ReferenceEquals(kept.Relief, grid)) return kept.Map;
        var map = TravelMap.For(state, sheet.Id, grid);
        Built++;
        _maps[sheet.Id] = (grid, map);
        return map;
    }

    public Route? Find(GameState state, Vec2 from, Vec2 to, Traveller who, Func<SheetDef, ReliefGrid?> relief) =>
        SheetOf(state, from, to) is { } sheet ? Map(state, sheet, relief)?.Find(from, to, who) : null;
}

using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server;

public sealed class HostTravel(GameServer server)
{
    readonly TravelMaps _maps = new();
    readonly Dictionary<string, ReliefGrid?> _grids = new();

    public int MapsBuilt => _maps.Built;

    public void After(Command c)
    {
        if (!TravelMaps.Shapes(c)) return;
        _maps.Forget();
        var standing = server.State.Sheets.Select(s => s.Relief).OfType<string>().ToHashSet();
        foreach (var sha in _grids.Keys.Where(k => !standing.Contains(k)).ToList()) _grids.Remove(sha);
    }

    public IReadOnlyDictionary<string, Traveller> Travellers() => TravelMaps.Travellers(server.State, id => server.Store?.Pack(id));

    public TravelMap? MapFor(Vec2 from, Vec2 to) => TravelMaps.SheetOf(server.State, from, to) is { } sheet ? _maps.Map(server.State, sheet, Relief) : null;

    ReliefGrid? Relief(SheetDef sheet)
    {
        if (sheet.Relief is not { Length: > 0 } sha || server.Store is not { } store) return null;
        if (_grids.TryGetValue(sha, out var kept)) return kept;
        string? Found(string rel) =>
            new[] { sheet.Package, UploadAsset.ReliefPackage }.Where(p => p.Length > 0).Select(p => Path.Combine(store.PackageDir(p), rel)).FirstOrDefault(File.Exists)
            ?? (store.FindPackageWith(rel) is { } found ? Path.Combine(store.PackageDir(found), rel) : null);
        ReliefGrid? grid = null;
        try
        {
            if (Found(ReliefFiles.HeaderRel(sha)) is { } header) grid = ReliefFiles.Load(ReliefFiles.LoadIndex(header), Found);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            grid = null;
        }
        return _grids[sha] = grid;
    }
}

using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed class TableHooks
{
    public Func<Vec2, string, double?, List<Vec2>>? PlaceName { get; init; }
    public Func<string, bool, Task<string?>>? KeepPicture { get; init; }
    public Action<IEnumerable<string>, Command>? SendWithFiles { get; init; }
    public Func<string, bool?, bool>? Favourite { get; init; }
    public Func<IReadOnlyList<PlaceBody>>? Bodies { get; init; }
    public Func<SheetDef, ReliefGrid?>? Relief { get; init; }
    public Func<string?, string?>? Crowd { get; init; }
    public Func<Lighting, double?, Task<(Command? Change, string? Refused)>>? Rescaled { get; init; }
}

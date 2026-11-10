using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed class TableHooks
{
    public Func<Vec2, string, List<Vec2>>? PlaceName { get; init; }
    public Func<string, bool, Task<string?>>? KeepPicture { get; init; }
    public Action<IEnumerable<string>, Command>? SendWithFiles { get; init; }
    public Func<string, bool?, bool>? Favourite { get; init; }
}

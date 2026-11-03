using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public sealed record SaveCard(string? Name, string? ScenarioId, GameClock? Clock, string? MirrorOf, string? Error);

public static class CardReader
{
    static readonly Dictionary<string, (DateTime at, long len, Scenario? s, int pieces)> _cards = new();

    static readonly Dictionary<string, (DateTime at, long len, SaveCard card)> _saves = new();

    public static SaveCard Save(string path)
    {
        DateTime at; long len;
        try { var fi = new FileInfo(path); if (!fi.Exists) return new(null, null, null, null, "the file is missing"); at = fi.LastWriteTimeUtc; len = fi.Length; }
        catch (Exception e) { return new(null, null, null, null, e.Message); }
        lock (_saves) if (_saves.TryGetValue(path, out var c) && c.at == at && c.len == len) return c.card;
        SaveCard got;
        try
        {
            var (name, scenarioId, clock, mirrorOf) = Json.SaveCard(path);
            got = new(name ?? "", scenarioId ?? "", clock, mirrorOf, null);
        }
        catch (Exception e) { got = new(null, null, null, null, e.Message); }
        lock (_saves) _saves[path] = (at, len, got);
        return got;
    }

    public static (Scenario? s, int pieces) Card(string path)
    {
        DateTime at; long len;
        try { var fi = new FileInfo(path); if (!fi.Exists) return (null, 0); at = fi.LastWriteTimeUtc; len = fi.Length; }
        catch { return (null, 0); }
        lock (_cards) if (_cards.TryGetValue(path, out var c) && c.at == at && c.len == len) return (c.s, c.pieces);
        (Scenario? s, int pieces) got;
        try
        {
            var (head, n) = Json.ScenarioCard(path);
            got = head is { Name: not null, Packs: not null, Sheets: not null } && n >= 0 ? (head, n) : (null, 0);
        }
        catch { got = (null, 0); }
        lock (_cards) _cards[path] = (at, len, got.s, got.pieces);
        return got;
    }
}

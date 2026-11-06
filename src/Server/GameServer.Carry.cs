using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server;

public sealed record CarriedPiece(string Layer, string Id, float Dx, float Dz, float RotDeg, float Lift);

public sealed partial class GameServer
{
    readonly Dictionary<long, HashSet<long>> _carryWatchers = new();

    public List<(long To, List<T> Seen)> MotionSeenBy<T>(long from, IReadOnlyList<T> pieces, Func<T, (string Layer, string Id)> key)
    {
        var res = new List<(long, List<T>)>();
        if (!_peers.TryGetValue(from, out var mover)) return res;
        var moved = pieces.Where(p => MayMove(mover, key(p))).ToList();
        foreach (var peer in _peers.Values)
        {
            if (peer.PeerId == from) continue;
            var seen = moved.Where(p => Visibility.ForPeer(MoveOf(key(p)), peer, State) != null).ToList();
            if (seen.Count > 0) res.Add((peer.PeerId, seen));
        }
        return res;
    }

    public List<(long To, List<CarriedPiece> Pieces)> CarryLayout(long from, IReadOnlyList<CarriedPiece> pieces)
    {
        var res = MotionSeenBy(from, pieces, p => (p.Layer, p.Id));
        var watchers = res.Select(r => r.To).ToHashSet();
        if (_carryWatchers.Remove(from, out var before))
            foreach (var gone in before.Where(w => !watchers.Contains(w) && _peers.ContainsKey(w))) res.Add((gone, new()));
        if (watchers.Count > 0) _carryWatchers[from] = watchers;
        return res;
    }

    public IEnumerable<long> CarryWatchers(long from) =>
        _carryWatchers.TryGetValue(from, out var watchers) ? watchers.Where(_peers.ContainsKey).ToList() : [];

    bool MayMove(PeerInfo mover, (string Layer, string Id) piece)
    {
        if (State.Find(piece.Layer, piece.Id) == null) return false;
        var move = MoveOf(piece);
        return Allowed(mover, move) && ZoneDenies(mover, move) == null;
    }

    MovePiece MoveOf((string Layer, string Id) piece) => new() { Layer = piece.Layer, Id = piece.Id, Pos = State.Find(piece.Layer, piece.Id)?.Pos ?? default };

    void ForgetCarrier(long peerId)
    {
        _carryWatchers.Remove(peerId);
        foreach (var watchers in _carryWatchers.Values) watchers.Remove(peerId);
    }
}

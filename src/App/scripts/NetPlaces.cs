using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server;

namespace KTable.App;

public partial class Net
{
    readonly HashSet<long> _placed = new();
    readonly Dictionary<long, string> _given = new();
    (Vector3 Feet, float Yaw, string? Piece)? _placement;

    public event Action? Placed;
    public event Action<long, string, Vector3, float>? PlayerPlaced;

    public (Vector3 Feet, float Yaw, string? Piece)? TakePlacement()
    {
        var placement = _placement;
        _placement = null;
        return placement;
    }

    public void Place(string player, Vector3 feet, float yaw, string? piece)
    {
        if (Server != null) PlaceFrom(GameServer.HostPeer, player, feet, yaw, piece ?? "");
        else if (AtATable) RpcId(1, nameof(PlaceUp), player, feet.X, feet.Y, feet.Z, yaw, piece ?? "");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
    void PlaceUp(string player, float x, float y, float z, float yaw, string piece)
    {
        if (Server != null) PlaceFrom(Multiplayer.GetRemoteSenderId(), player, new Vector3(x, y, z), yaw, piece);
    }

    void PlaceFrom(long by, string player, Vector3 feet, float yaw, string piece)
    {
        if (Server!.Peer(by) is not { } umpire || !Visibility.IsUmpire(umpire.Role) || !feet.IsFinite() || !float.IsFinite(yaw)) return;
        if (Server.Peers.FirstOrDefault(p => p.Role == Role.Player && p.Name == player) is not { } who) return;
        if (piece.Length > 0 && Server.State.Find(Layers.Umpire, piece) == null) return;
        _placed.Add(who.PeerId);
        if (piece.Length > 0) _given[who.PeerId] = piece;
        PlayerPlaced?.Invoke(who.PeerId, who.Name, feet, yaw);
        RpcId(who.PeerId, nameof(PlacedDown), feet.X, feet.Y, feet.Z, yaw, piece);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
    void PlacedDown(float x, float y, float z, float yaw, string piece)
    {
        var feet = new Vector3(x, y, z);
        if (Multiplayer.GetRemoteSenderId() != 1 || !feet.IsFinite() || !float.IsFinite(yaw) || piece.Length > AvatarPose.LongestRide) return;
        _placement = (feet, yaw, piece.Length > 0 ? piece : null);
        Placed?.Invoke();
    }

    bool Held(PeerInfo who) => who.Role == Role.Player && !_placed.Contains(who.PeerId);

    string? Granted(PeerInfo who, string spec)
    {
        if (who.Role != Role.Player) return spec;
        var rights = Server!.State.RightsOf(who.Name);
        if (AvatarBody.Taking(spec) is { } piece) return rights.Take || _given.GetValueOrDefault(who.PeerId) == piece ? spec : null;
        return null;
    }

    void DropRoleChanges()
    {
        foreach (var id in _placed.ToList())
            if (Server!.Peer(id) is { Role: not Role.Player } changed)
            {
                _placed.Remove(id);
                Bodies.Forget(changed.Name);
            }
    }

    bool Unplace(long peer)
    {
        _placed.Remove(peer);
        _given.Remove(peer);
        if (Server?.Peer(peer) is not { Role: Role.Player } gone || Bodies.Now().All(b => b.Name != gone.Name)) return false;
        Bodies.Forget(gone.Name);
        return true;
    }
}

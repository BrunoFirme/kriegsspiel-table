using System;
using System.Collections.Generic;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server;

namespace KTable.App;

public partial class Net
{
    const ulong PingEveryMs = 600;
    readonly Dictionary<long, ulong> _pingedAt = new();

    public event Action<long, string, Vector3, int>? PingReceived;

    public void Ping(Vector3 at)
    {
        if (Server != null) PingFrom(GameServer.HostPeer, at);
        else if (AtATable) RpcId(1, nameof(PingUp), at.X, at.Y, at.Z);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2)]
    void PingUp(float x, float y, float z)
    {
        if (Server != null) PingFrom(Multiplayer.GetRemoteSenderId(), new Vector3(x, y, z));
    }

    void PingFrom(long from, Vector3 at)
    {
        if (Server!.Peer(from) is not { } who || !at.IsFinite()) return;
        if (who.Role == Role.Player ? !Server.State.RightsOf(who.Name).Ping : !Visibility.IsUmpire(who.Role)) return;
        ulong now = Time.GetTicksMsec();
        if (_pingedAt.TryGetValue(from, out var was) && now - was < PingEveryMs) return;
        _pingedAt[from] = now;
        int slot = KTable.Client.UmpireColors.Slot(who.Name, Server.Peers, Server.Access) ?? AvatarPose.NoColour;
        foreach (var other in Server.Peers)
            if (other.PeerId == GameServer.HostPeer) PingReceived?.Invoke(from, who.Name, at, slot);
            else RpcId(other.PeerId, nameof(PingDown), from, who.Name, at.X, at.Y, at.Z, slot);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2)]
    void PingDown(long from, string name, float x, float y, float z, int slot)
    {
        var at = new Vector3(x, y, z);
        if (Multiplayer.GetRemoteSenderId() != 1 || name.Length > AvatarPose.LongestName || !at.IsFinite()) return;
        PingReceived?.Invoke(from, name, at, slot);
    }
}

using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;
using Json = KTable.Core.Json;

namespace KTable.App;

public partial class Net
{
    const int MostLook = 8000;

    public void Dress(string pieceId, CrowdLook look)
    {
        var json = Json.Serialize(look);
        if (Server != null)
        {
            if (Server.Peer(GameServer.HostPeer) is { } me) DressFrom(me, pieceId, json);
        }
        else if (AtATable) RpcId(1, nameof(DressUp), pieceId, json);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
    void DressUp(string pieceId, string json)
    {
        if (Server?.Peer(Multiplayer.GetRemoteSenderId()) is { } who && !Held(who)) DressFrom(who, pieceId, json);
    }

    void DressFrom(PeerInfo who, string pieceId, string json)
    {
        if (Server is not { } s || json.Length > MostLook || BestEffort.Read(() => Json.Deserialize<CrowdLook>(json), null) is not { } look) return;
        var rights = s.State.RightsOf(who.Name);
        var offer = CrowdWardrobe.Offer(Allowed(rights.DressAs), rights.DressWealth);
        if (!DressGranted(who.Role, rights, s.Bodies.GetValueOrDefault(pieceId) == who.Name, look, offer)) return;
        Dispatch(s.HandleForNoOne(new SetLook { Layer = Layers.Umpire, Id = pieceId, Look = look }));
    }

    Nationality? Allowed(string? id) =>
        id == null ? null : CrowdDress.Find(id, Nationalities.Of(Session.View.Packs.Select(p => Session.Store.Pack(p)).OfType<PackManifest>())) is { } n ? CrowdDress.Flat(n) : null;

    internal static bool DressGranted(Role role, PlayerRights rights, bool ownBody, CrowdLook look, LookOffer offer) =>
        role != Role.Observer && CrowdWardrobe.Problem(look) == null && (role != Role.Player || rights.Dress && ownBody && CrowdWardrobe.Fits(look, offer));
}

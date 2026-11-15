using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;
using KTable.Client;

namespace KTable.App;

sealed class BodyMaker(Func<GameServer?> server, PackageStore store, Action<List<Outgoing>> dispatch)
{
    readonly HashSet<string> _made = new();

    public string? Take(PeerInfo who, string spec, Vector3 feet, float yaw, Vector2 shape)
    {
        if (server() is not { } s) return null;
        var had = s.Bodies.FirstOrDefault(b => b.Value == who.Name).Key;
        string? id;
        if (AvatarBody.Taking(spec) is { } wanted)
        {
            var kind = s.State.Find(Layers.Umpire, wanted) is { } piece ? store.Def(piece.Def) : null;
            if (kind?.Tags.Contains(WallCutaway.BuildingTag) == true) return Refused(s, who, "buildings can't be taken over");
            if (!AvatarBody.Takeable(kind)) return Refused(s, who, "that piece can't be taken over");
            if (s.Bodies.TryGetValue(wanted, out var owner) && owner != who.Name) return Refused(s, who, $"{owner} is in that piece");
            id = wanted;
        }
        else if (had != null && s.State.Find(Layers.Umpire, had) is { } old && Wears(old, spec)) id = had;
        else id = Made(s, spec, feet, yaw, shape);
        if (id == null) return Refused(s, who, "no body could be made from that");
        if (had != null && had != id) Let(s, had);
        s.Bodies[id] = who.Name;
        if (AvatarBody.Model(spec) is var (mesh, picture) && s.State.Find(Layers.Umpire, id) is { } carrier && carrier.WebMesh != mesh)
            dispatch(s.Handle(GameServer.HostPeer, new SetWebModel { Layer = Layers.Umpire, Id = id, Mesh = mesh, Texture = picture }));
        return id;
    }

    public void Forget(string playerName)
    {
        if (server() is not { } s) return;
        foreach (var id in s.Bodies.Where(b => b.Value == playerName).Select(b => b.Key).ToList()) s.Bodies.Remove(id);
    }

    public IReadOnlyList<BodyOf> Now()
    {
        if (server() is not { } s) return [];
        var gone = s.Bodies.Keys.Where(id => s.State.Find(Layers.Umpire, id) == null).ToList();
        foreach (var id in gone) s.Bodies.Remove(id);
        return s.Bodies.Select(b => s.Peers.FirstOrDefault(p => p.Name == b.Value) is { } p
            ? new BodyOf(b.Key, p.PeerId, b.Value, p.Role == Role.Player ? UmpireColors.Slot(p.Name, s.Peers, s.Access) ?? AvatarPose.NoColour : AvatarPose.NoColour)
            : new BodyOf(b.Key, 0, b.Value, AvatarPose.NoColour)).ToList();
    }

    static bool Wears(PieceInstance piece, string spec) =>
        AvatarBody.Piece(spec) is { } def ? piece.Def == def : AvatarBody.Model(spec) is var (mesh, _) && piece.WebMesh == mesh;

    string? Made(GameServer s, string spec, Vector3 feet, float yaw, Vector2 shape)
    {
        var defId = AvatarBody.Piece(spec) ?? (AvatarBody.Model(spec) != null ? AvatarBody.OwnModel : null);
        if (defId == null || store.Def(defId) is not { } def || !AvatarBody.Takeable(def)) return null;
        var pack = defId[..defId.LastIndexOf('.')];
        if (!s.State.Packs.Contains(pack) && store.Pack(pack) != null) dispatch(s.Handle(GameServer.HostPeer, new AddPack { Id = pack, Hash = UserPacks.Hash(store, pack) }));
        double perMetre = AvatarPose.UnitsPerMetre(s.State);
        bool lifeSize = def.Tags.Contains(PieceDef.LifeSizeTag);
        double tallest = Math.Max(def.Thickness, Math.Max(def.Size.W, def.Size.H));
        double scale = lifeSize ? 1 : AvatarPose.PersonMetres * perMetre / Math.Max(tallest, 1e-6);
        var before = s.State.Layer(Layers.Umpire).Select(p => p.Id).ToHashSet();
        dispatch(s.Handle(GameServer.HostPeer, new SpawnPiece
        {
            Layer = Layers.Umpire, Def = defId, Pos = Coords.FromWorld(feet), RotDeg = AvatarBody.RotOf(yaw), Scale = scale, Y = feet.Y,
        }));
        if (s.State.Layer(Layers.Umpire).FirstOrDefault(p => !before.Contains(p.Id)) is not { } made) return null;
        _made.Add(made.Id);
        if (defId == AvatarBody.OwnModel && shape.X > 0 && shape.Y > 0)
            dispatch(s.Handle(GameServer.HostPeer, new SetScale
            {
                Layer = Layers.Umpire, Id = made.Id, Scale = made.Scale, Wide = shape.X * AvatarPose.PersonMetres, Deep = shape.Y * AvatarPose.PersonMetres,
            }));
        return made.Id;
    }

    void Let(GameServer s, string id)
    {
        s.Bodies.Remove(id);
        if (_made.Remove(id) && s.State.Find(Layers.Umpire, id) != null) dispatch(s.Handle(GameServer.HostPeer, new DeletePiece { Layer = Layers.Umpire, Id = id }));
    }

    string? Refused(GameServer s, PeerInfo who, string why)
    {
        dispatch([new Outgoing(who.PeerId, new ErrorMessage { Text = why })]);
        return null;
    }
}

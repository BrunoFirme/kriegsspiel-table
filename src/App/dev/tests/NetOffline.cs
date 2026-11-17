using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.App;

public partial class Net
{
    internal void HostOffline(GameServer server, string name)
    {
        Server = server;
        server.BodiesDropped = DropBodies;
        server.AppVersion = AppVersion;
        Session.SetName(name);
        Dispatch(Server.Scripts.Load());
        Dispatch(Server.Join(1, new JoinRequest { Name = name }));
        SetStatus("hosting offline");
    }

    internal void LoadOffline(GameState state, string name) => Dispatch(Server!.LoadSave(state, [], name));
}

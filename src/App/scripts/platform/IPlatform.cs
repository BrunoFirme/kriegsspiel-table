using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

public enum WorkshopVisibility { Everyone, FriendsOnly, Unlisted, OnlyMe }

public interface IPlatform
{
    static IPlatform ForThisBuild() =>
#if KTABLE_STEAM
        new SteamPlatform();
#else
        new NullPlatform();
#endif

    bool HasWorkshop { get; }
    bool Available { get; }
    string? Status { get; }
    string UserName { get; }
    event Action<string>? Changed;

    void Start(Node root);
    IEnumerable<string> SelfTest();

    ulong? PublishedId(string kind, string id);
    List<string> UnpublishedPacks(string scenarioId);
    void Publish(string kind, string id, string title, string description, WorkshopVisibility visibility, string changeNote,
        Action<string, float> progress, Action<bool, ulong, string> done, WorkshopContent.PackMode packs);
    void Query(string search, string tag, uint page, Action<List<WorkshopEntry>, uint> done);
    void Subscribe(ulong id, Action<bool> done);
    void Unsubscribe(ulong id, Action<bool> done);
    int SyncInstalled();
    void OpenItemPage(ulong id);
}

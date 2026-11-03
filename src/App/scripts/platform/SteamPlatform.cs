#if KTABLE_STEAM
using System;
using System.Collections.Generic;
using Godot;
using KTable.Core;
using Steamworks;

namespace KTable.App;

public sealed class SteamPlatform : IPlatform
{
    Workshop? _workshop;
    Workshop Ws => _workshop ??= new Workshop(new PackageStore(PackageStore.DefaultRoot), SteamService.Instance!);

    public bool HasWorkshop => true;
    public bool Available => SteamService.Instance is { Running: true };
    public string? Status => SteamService.Instance?.Status;
    public string UserName => SteamService.Instance?.UserName ?? "";
    public event Action<string>? Changed { add => Ws.Changed += value; remove => Ws.Changed -= value; }

    public void Start(Node root) => root.AddChild(new SteamService { Name = "Steam" });

    public IEnumerable<string> SelfTest()
    {
        yield return $"[workshop] native library: {SteamService.NativePath() ?? "missing"}";
        yield return $"[workshop] Steam client running: {SteamAPI.IsSteamRunning()}; app id configured: {SteamService.Instance?.AppId}; status: {SteamService.Instance?.Status}";
    }

    public ulong? PublishedId(string kind, string id) => Ws.PublishedId(kind, id);
    public List<string> UnpublishedPacks(string scenarioId) => Ws.UnpublishedPacks(scenarioId);

    public void Publish(string kind, string id, string title, string description, WorkshopVisibility visibility, string changeNote,
        Action<string, float> progress, Action<bool, ulong, string> done, WorkshopContent.PackMode packs) =>
        Ws.Publish(kind, id, title, description, visibility switch
        {
            WorkshopVisibility.FriendsOnly => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
            WorkshopVisibility.Unlisted => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
            WorkshopVisibility.OnlyMe => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
            _ => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
        }, changeNote, progress, done, packs);

    public void Query(string search, string tag, uint page, Action<List<WorkshopEntry>, uint> done) => Ws.Query(search, tag, page, done);
    public void Subscribe(ulong id, Action<bool> done) => Ws.Subscribe(id, done);
    public void Unsubscribe(ulong id, Action<bool> done) => Ws.Unsubscribe(id, done);
    public int SyncInstalled() => Ws.SyncInstalled();
    public void OpenItemPage(ulong id) => SteamService.Instance?.OpenUrl(SteamService.ItemUrl(id));
}
#endif

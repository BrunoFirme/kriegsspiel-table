#if KTABLE_STEAM
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text.Json;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using Steamworks;
using Json = KTable.Core.Json;

namespace KTable.App;

public sealed class Workshop
{
    public sealed class Registry
    {
        public Dictionary<string, ulong> Published { get; set; } = new();
        public Dictionary<string, uint> Installed { get; set; } = new();
    }

    static string RegistryPath => Path.Combine(DataPaths.Local, "workshop.json");
    public Registry Reg { get; private set; } = Load();
    readonly PackageStore _store;
    readonly SteamService _steam;
    readonly List<object> _pending = new();
    Callback<ItemInstalled_t>? _installed;
    public event Action<string>? Changed;

    public Workshop(PackageStore store, SteamService steam)
    {
        _store = store;
        _steam = steam;
        if (steam.Running) _installed = Callback<ItemInstalled_t>.Create(r => { if (r.m_unAppID == steam.App) SyncInstalled(); });
    }

    public bool Available => _steam.Running;

    static Registry Load()
    {
        try { if (File.Exists(RegistryPath)) return JsonSerializer.Deserialize<Registry>(File.ReadAllText(RegistryPath), WorkshopContent.Opts) ?? new(); }
        catch (Exception e) { Problems.Tell($"The record of what you have from the Workshop could not be read ({e.Message}): subscribed items install again, and what you published shows as not yet published."); }
        return new();
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        File.WriteAllText(RegistryPath, JsonSerializer.Serialize(Reg, WorkshopContent.Opts));
    }

    public ulong? PublishedId(string kind, string id) => Reg.Published.TryGetValue($"{kind}:{id}", out var v) ? v : null;

    CallResult<T> Call<T>(SteamAPICall_t call, Action<T, bool> handler) where T : struct
    {
        CallResult<T>? cr = null;
        cr = CallResult<T>.Create((r, fail) => { _pending.Remove(cr!); handler(r, fail); });
        cr.Set(call);
        _pending.Add(cr);
        return cr;
    }

    public void Publish(string kind, string id, string title, string description, ERemoteStoragePublishedFileVisibility visibility,
        string changeNote, Action<string, float> progress, Action<bool, ulong, string> done, WorkshopContent.PackMode packs = WorkshopContent.PackMode.None)
    {
        if (!Available) { done(false, 0, _steam.Status); return; }
        string folder, preview;
        try { folder = WorkshopContent.Stage(_store, kind, id, packs); preview = WorkshopContent.Preview(_store, kind, id); }
        catch (Exception e) { done(false, 0, e.Message); return; }

        void Upload(PublishedFileId_t item)
        {
            var h = SteamUGC.StartItemUpdate(_steam.App, item);
            SteamUGC.SetItemTitle(h, title.Length > 128 ? title[..128] : title);
            SteamUGC.SetItemDescription(h, description.Length > 7900 ? description[..7900] : description);
            SteamUGC.SetItemVisibility(h, visibility);
            SteamUGC.SetItemTags(h, new List<string> { kind == "pack" ? "Pack" : "Scenario" });
            var itemMeta = JsonSerializer.Deserialize<WorkshopItemMeta>(File.ReadAllText(Path.Combine(folder, WorkshopContent.MetaFile)), WorkshopContent.Opts)!;
            itemMeta.Name = title;
            SteamUGC.SetItemMetadata(h, JsonSerializer.Serialize(itemMeta, WorkshopContent.Opts));
            SteamUGC.SetItemContent(h, folder);
            SteamUGC.SetItemPreview(h, preview);
            var submitted = false;
            Call<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(h, changeNote), (r, fail) =>
            {
                submitted = true;
                if (fail || r.m_eResult != EResult.k_EResultOK) { done(false, item.m_PublishedFileId, $"upload failed: {r.m_eResult}"); return; }
                Reg.Published[$"{kind}:{id}"] = item.m_PublishedFileId;
                Save();
                if (kind == "scenario" && packs == WorkshopContent.PackMode.None) LinkPacks(item, id);
                if (r.m_bUserNeedsToAcceptWorkshopLegalAgreement) _steam.OpenUrl("steam://url/CommunityFilePage/" + item.m_PublishedFileId);
                done(true, item.m_PublishedFileId, r.m_bUserNeedsToAcceptWorkshopLegalAgreement ? "Published. Accept the Steam Workshop agreement on the page that opened so others can see it." : "Published.");
            });
            void Poll()
            {
                if (submitted) return;
                var st = SteamUGC.GetItemUpdateProgress(h, out ulong sent, out ulong total);
                progress(st.ToString().Replace("k_EItemUpdateStatus", ""), total > 0 ? sent / (float)total : 0);
                _steam.GetTree().CreateTimer(0.25).Timeout += Poll;
            }
            Poll();
        }

        if (PublishedId(kind, id) is { } existing) { Upload(new PublishedFileId_t(existing)); return; }
        progress("Creating the Workshop item", 0);
        Call<CreateItemResult_t>(SteamUGC.CreateItem(_steam.App, EWorkshopFileType.k_EWorkshopFileTypeCommunity), (r, fail) =>
        {
            if (fail || r.m_eResult != EResult.k_EResultOK) { done(false, 0, $"could not create the item: {r.m_eResult}"); return; }
            Reg.Published[$"{kind}:{id}"] = r.m_nPublishedFileId.m_PublishedFileId;
            Save();
            Upload(r.m_nPublishedFileId);
        });
    }

    void LinkPacks(PublishedFileId_t scenarioItem, string scenarioId)
    {
        var sc = Json.Load<Scenario>(Path.Combine(_store.Root, "scenarios", scenarioId, "scenario.json"));
        foreach (var p in sc.Packs)
            if (PublishedId("pack", p) is { } child)
                Call<AddUGCDependencyResult_t>(SteamUGC.AddDependency(scenarioItem, new PublishedFileId_t(child)), (_, _) => { });
    }

    public List<string> UnpublishedPacks(string scenarioId)
    {
        var sc = Json.Load<Scenario>(Path.Combine(_store.Root, "scenarios", scenarioId, "scenario.json"));
        return sc.Packs.Where(p => PublishedId("pack", p) == null).ToList();
    }

    public void Query(string search, string tag, uint page, Action<List<WorkshopEntry>, uint> done)
    {
        if (!Available) { done(new(), 0); return; }
        var q = SteamUGC.CreateQueryAllUGCRequest(search.Length > 0 ? EUGCQuery.k_EUGCQuery_RankedByTextSearch : EUGCQuery.k_EUGCQuery_RankedByTrend,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse, _steam.App, _steam.App, page);
        if (search.Length > 0) SteamUGC.SetSearchText(q, search);
        if (tag.Length > 0) SteamUGC.AddRequiredTag(q, tag);
        SteamUGC.SetReturnMetadata(q, true);
        SteamUGC.SetReturnLongDescription(q, true);
        SteamUGC.SetRankedByTrendDays(q, 365);
        Call<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(q), (r, fail) =>
        {
            var list = new List<WorkshopEntry>();
            if (!fail && r.m_eResult == EResult.k_EResultOK)
            {
                var subscribed = Subscribed().ToHashSet();
                for (uint i = 0; i < r.m_unNumResultsReturned; i++)
                {
                    if (!SteamUGC.GetQueryUGCResult(r.m_handle, i, out var d)) continue;
                    SteamUGC.GetQueryUGCPreviewURL(r.m_handle, i, out var url, 1024);
                    SteamUGC.GetQueryUGCMetadata(r.m_handle, i, out var md, Constants.k_cchDeveloperMetadataMax);
                    SteamUGC.GetQueryUGCStatistic(r.m_handle, i, EItemStatistic.k_EItemStatistic_NumSubscriptions, out ulong subs);
                    var meta = BestEffort.Read<WorkshopItemMeta?>(() => JsonSerializer.Deserialize<WorkshopItemMeta>(md, WorkshopContent.Opts), fallback: null);
                    list.Add(new WorkshopEntry
                    {
                        Id = d.m_nPublishedFileId.m_PublishedFileId, Title = d.m_rgchTitle, Description = d.m_rgchDescription, Tags = d.m_rgchTags,
                        PreviewUrl = url, Meta = meta, Subscriptions = (uint)subs, Subscribed = subscribed.Contains(d.m_nPublishedFileId.m_PublishedFileId),
                        Owner = d.m_ulSteamIDOwner, Updated = d.m_rtimeUpdated,
                    });
                }
            }
            SteamUGC.ReleaseQueryUGCRequest(r.m_handle);
            done(list, r.m_unTotalMatchingResults);
        });
    }

    public IEnumerable<ulong> Subscribed()
    {
        if (!Available) return Array.Empty<ulong>();
        var n = SteamUGC.GetNumSubscribedItems();
        var ids = new PublishedFileId_t[n];
        SteamUGC.GetSubscribedItems(ids, n);
        return ids.Select(i => i.m_PublishedFileId);
    }

    public void Subscribe(ulong id, Action<bool> done)
    {
        if (!Available) { done(false); return; }
        Call<RemoteStorageSubscribePublishedFileResult_t>(SteamUGC.SubscribeItem(new PublishedFileId_t(id)), (r, fail) =>
        {
            bool ok = !fail && r.m_eResult == EResult.k_EResultOK;
            if (ok)
            {
                SteamUGC.DownloadItem(new PublishedFileId_t(id), true);
                SubscribeChildren(id);
            }
            done(ok);
        });
    }

    void SubscribeChildren(ulong id)
    {
        var q = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t(id) }, 1);
        SteamUGC.SetReturnChildren(q, true);
        Call<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(q), (r, fail) =>
        {
            if (!fail && SteamUGC.GetQueryUGCResult(r.m_handle, 0, out var d) && d.m_unNumChildren > 0)
            {
                var kids = new PublishedFileId_t[d.m_unNumChildren];
                SteamUGC.GetQueryUGCChildren(r.m_handle, 0, kids, d.m_unNumChildren);
                foreach (var k in kids) Call<RemoteStorageSubscribePublishedFileResult_t>(SteamUGC.SubscribeItem(k), (_, _) => SteamUGC.DownloadItem(k, true));
            }
            SteamUGC.ReleaseQueryUGCRequest(r.m_handle);
        });
    }

    public void Unsubscribe(ulong id, Action<bool> done)
    {
        if (!Available) { done(false); return; }
        Call<RemoteStorageUnsubscribePublishedFileResult_t>(SteamUGC.UnsubscribeItem(new PublishedFileId_t(id)), (r, fail) => done(!fail && r.m_eResult == EResult.k_EResultOK));
    }

    public int SyncInstalled()
    {
        if (!Available) return 0;
        int n = 0;
        var mine = Reg.Published.Values.ToHashSet();
        foreach (var id in Subscribed())
        {
            if (mine.Contains(id)) continue;
            var state = (EItemState)SteamUGC.GetItemState(new PublishedFileId_t(id));
            if ((state & EItemState.k_EItemStateInstalled) == 0) { SteamUGC.DownloadItem(new PublishedFileId_t(id), false); continue; }
            if (!SteamUGC.GetItemInstallInfo(new PublishedFileId_t(id), out _, out var folder, 1024, out var stamp)) continue;
            if (Reg.Installed.TryGetValue(id.ToString(), out var had) && had == stamp) continue;
            try
            {
                var meta = WorkshopContent.Install(_store, folder);
                Reg.Installed[id.ToString()] = stamp;
                n++;
                Changed?.Invoke($"Installed from the Workshop: {meta.Name}" + (meta.Trashed.Count > 0 ? $". What you had under the same name went to the trash first ({string.Join(", ", meta.Trashed)})." : "")
                    + string.Concat(meta.Warnings.Select(w => " " + w)));
            }
            catch (InUseException) { continue; }
            catch (Exception e) { Changed?.Invoke($"Workshop item {id} could not be installed: {e.Message}"); }
        }
        if (n > 0) Save();
        return n;
    }
}
#endif

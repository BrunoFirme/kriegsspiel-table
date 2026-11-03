using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

public sealed class NullPlatform : IPlatform
{
    public bool HasWorkshop => false;
    public bool Available => false;
    public string? Status => null;
    public string UserName => "";
    public event Action<string>? Changed { add { } remove { } }

    public void Start(Node root) { }
    public IEnumerable<string> SelfTest() => Array.Empty<string>();

    public ulong? PublishedId(string kind, string id) => null;
    public List<string> UnpublishedPacks(string scenarioId) => new();
    public void Publish(string kind, string id, string title, string description, WorkshopVisibility visibility, string changeNote,
        Action<string, float> progress, Action<bool, ulong, string> done, WorkshopContent.PackMode packs) => done(false, 0, "There is no Workshop in this build.");
    public void Query(string search, string tag, uint page, Action<List<WorkshopEntry>, uint> done) => done(new(), 0);
    public void Subscribe(ulong id, Action<bool> done) => done(false);
    public void Unsubscribe(ulong id, Action<bool> done) => done(false);
    public int SyncInstalled() => 0;
    public void OpenItemPage(ulong id) { }
}

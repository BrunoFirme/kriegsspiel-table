using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

sealed class MutedFaces
{
    readonly Assets _assets;
    readonly Dictionary<(string pkg, string sha), Texture2D?> _done = new();
    readonly Dictionary<(string pkg, string sha), List<Action>> _waiting = new();
    readonly Queue<(string pkg, string sha)> _queue = new();
    const int AtOnce = 6;
    int _rendering;

    public MutedFaces(Assets assets, Node host) { _assets = assets; Host = host; }

    public Node Host { get; set; }

    public bool Pending => _waiting.Count > 0;

    public Texture2D? Texture(string pkg, string rel, Action onReady)
    {
        var key = (pkg, Path.GetFileNameWithoutExtension(rel));
        if (_done.TryGetValue(key, out var t)) return t;
        if (_waiting.TryGetValue(key, out var list)) { list.Add(onReady); return null; }
        _waiting[key] = new() { onReady };
        _queue.Enqueue(key);
        Pump();
        return null;
    }

    void Pump()
    {
        while (_rendering < AtOnce && _queue.TryDequeue(out var key)) _ = Render(key);
    }

    async Task Render((string pkg, string sha) key)
    {
        _rendering++;
        Texture2D? tex = null;
        try { tex = await Mute(key.pkg, key.sha); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            GD.PushWarning($"the face {key.sha} could not be drawn without its text: {e.Message}");
        }
        _rendering--;
        _done[key] = tex;
        if (_waiting.Remove(key, out var list)) foreach (var a in list) a();
        Pump();
    }

    async Task<Texture2D?> Mute(string pkg, string sha)
    {
        var store = _assets.Store;
        var doc = ArtStore.Load(store, pkg, sha);
        if (doc == null || !Wordy(doc)) return null;
        var r = new ArtRenderer { Doc = Strip(doc), LoadSrc = name => ArtStore.LoadSrc(store, pkg, name) };
        Host.AddChild(r);
        try
        {
            r.RebuildAll();
            var img = await r.Flatten();
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }
        finally { r.QueueFree(); }
    }

    public static bool Wordy(ArtDoc doc) =>
        doc.Layers.Any(l => l.Visible && (l.Kind == "text" && !string.IsNullOrWhiteSpace(l.Text) || l.Block is { } b && (b.Label.Trim().Length > 0 || b.Symbol == "text")));

    public static ArtDoc Strip(ArtDoc doc)
    {
        var copy = ArtStore.Deserialize(ArtStore.Serialize(doc))!;
        copy.Layers.RemoveAll(l => l.Kind == "text");
        foreach (var plate in copy.Layers.Select(l => l.Block).OfType<BlockSpec>())
        {
            plate.Label = "";
            if (plate.Symbol == "text") plate.Symbol = "none";
        }
        return copy;
    }
}

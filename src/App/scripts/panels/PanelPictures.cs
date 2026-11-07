using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;
using KTable.Core.Panels;
using KTable.Core.Protocol;

namespace KTable.App;

static class PanelPictures
{
    public static Texture2D? Texture(Session s, string name, Action<Texture2D> ready)
    {
        var id = PanelSeats.Picture(s.View, name.Trim()) ?? name;
        return AssetTexture(s, id, ready);
    }

    public static Texture2D? AssetTexture(Session s, string id, Action<Texture2D> onReady)
    {
        if (id.Length == 0) return null;
        if (s.View.UiPictures.TryGetValue(id.Trim(), out var named)) id = named;
        foreach (var pk in s.Store.LoadedPacks)
            if (pk.Assets.TryGetValue(id, out var a) && a.Kind == "image") return s.Assets.Texture(pk.Id, a.File, onReady, maxSize: 512);
        return null;
    }

    public static PanelMeter Meter(Session s) => new(name =>
    {
        var id = PanelSeats.Picture(s.View, name.Trim()) ?? name;
        foreach (var pk in s.Store.LoadedPacks)
            if (pk.Assets.TryGetValue(id, out var a) && a.Kind == "image" && a.W > 0 && a.H > 0) return new Vector2(a.W, a.H);
        return null;
    });
}

static class PanelChanges
{
    public static IEnumerable<Command> Flat(Command c) => c is Batch b ? b.Items.SelectMany(Flat) : new[] { c };

    public static void Forget(Dictionary<string, string> local, UiLayout? layout, string id)
    {
        if (layout != null && layout.Ids.TryGetValue(id, out var path)) local.Remove(PanelScene.Key(path));
    }
}

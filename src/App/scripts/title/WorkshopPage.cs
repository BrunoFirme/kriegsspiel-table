using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class WorkshopPage
{
    readonly TitleScreen _t;
    readonly Main _main;
    readonly IPlatform _platform;

    public WorkshopPage(TitleScreen t, Main main, IPlatform platform)
    {
        _t = t;
        _main = main;
        _platform = platform;
    }

    public void Show()
    {
        var v = _t.NewPage("Steam Workshop", _platform.Available
            ? "Packs and scenarios made by the community. Subscribing installs them; Steam keeps them up to date."
            : (_platform.Status ?? "") + " The Workshop opens up in the Steam release of Kriegsspiel Table.");
        var guide = new Button { Text = "Sharing packs and scenarios", Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        guide.AddThemeColorOverride("font_color", Muted);
        guide.AddThemeColorOverride("font_hover_color", Ink);
        guide.Pressed += () => GuideReader.Show(_main, "community-guide.md", "share-packs-and-scenarios");
        v.AddChild(guide);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", Ui.Gap.Block);
        scroll.AddChild(col);
        if (_platform.Available) Browse(col);
        Mine(col);
    }

    void Browse(VBoxContainer col)
    {
        var bar = new HBoxContainer();
        var search = new LineEdit { PlaceholderText = "search the Workshop", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var kind = new OptionButton();
        kind.AddItem("Everything");
        kind.AddItem("Packs");
        kind.AddItem("Scenarios");
        bar.AddChild(search);
        bar.AddChild(kind);
        col.AddChild(bar);
        var results = new HFlowContainer();
        results.AddThemeConstantOverride("h_separation", Ui.Gap.Block);
        results.AddThemeConstantOverride("v_separation", Ui.Gap.Block);
        col.AddChild(results);
        void Run()
        {
            foreach (var c in results.GetChildren()) c.QueueFree();
            results.AddChild(new Label { Text = "Searching…", Modulate = Muted });
            _platform.Query(search.Text.Trim(), kind.Selected switch { 1 => "Pack", 2 => "Scenario", _ => "" }, 1, (list, total) =>
            {
                foreach (var c in results.GetChildren()) c.QueueFree();
                if (list.Count == 0) results.AddChild(new Label { Text = "Nothing found.", Modulate = Muted });
                foreach (var e in list) results.AddChild(ItemCard(e));
            });
        }
        search.TextSubmitted += _ => Run();
        kind.ItemSelected += _ => Run();
        Run();
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
    }

    void Mine(VBoxContainer col)
    {
        col.AddChild(Caps("Your packs and scenarios"));
        var store = new PackageStore(PackageStore.DefaultRoot);
        var mine = store.ListPackIds().Where(PackManifest.IsUserPackId).Select(id => ("pack", id, store.Pack(id)?.Name))
            .Concat(store.ListScenarios().Select(sc => ("scenario", sc.id, ScenarioFiles.TryLoad(sc.path)?.Name)))
            .Concat(store.ListPackIds().Where(id => !PackManifest.IsUserPackId(id)).Select(id => ("pack", id, store.Pack(id)?.Name is { } n ? n + "  (converted)" : null)))
            .ToList();
        foreach (var (k, id, readName) in mine)
        {
            var damaged = readName == null;
            var name = readName ?? id + "  (damaged: its file cannot be read)";
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", CardStyle(false));
            var h = new HBoxContainer();
            row.AddChild(h);
            var nm = new Label { Text = name, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            nm.AddThemeFontOverride("font", Serif());
            h.AddChild(nm);
            var pub = _platform.PublishedId(k, id);
            h.AddChild(new Label { Text = (k == "pack" ? "pack" : "scenario") + (pub != null ? " · on the Workshop" : ""), Modulate = Muted });
            var b = new Button { Text = pub == null ? "Publish…" : "Update…", Disabled = !_platform.Available || damaged, FocusMode = Control.FocusModeEnum.None };
            var (kk, ii) = (k, id);
            b.Pressed += () =>
            {
                var dlg = new PublishDialog(_platform, store, kk, ii);
                dlg.Finished += m => _t.Status.Text = m;
                Ui.Open(_main, dlg);
            };
            h.AddChild(b);
            col.AddChild(row);
        }
    }

    Control ItemCard(WorkshopEntry e)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(230, 0) };
        card.AddThemeStyleboxOverride("panel", CardStyle(false));
        var cv = new VBoxContainer();
        card.AddChild(cv);
        var pic = new TextureRect { CustomMinimumSize = new Vector2(206, 206), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
        cv.AddChild(pic);
        if (e.PreviewUrl.Length > 0)
        {
            var http = new HttpRequest();
            card.AddChild(http);
            http.RequestCompleted += (result, code, headers, body) =>
            {
                var img = new Image();
                if (ContentLimits.PictureAllowed(body) && (img.LoadPngFromBuffer(body) == Error.Ok || img.LoadJpgFromBuffer(body) == Error.Ok || img.LoadWebpFromBuffer(body) == Error.Ok))
                    pic.Texture = ImageTexture.CreateFromImage(img);
                http.QueueFree();
            };
            Callable.From(() => http.Request(e.PreviewUrl)).CallDeferred();
        }
        var t = new Label { Text = e.Title, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(206, 0) };
        t.AddThemeFontOverride("font", Serif());
        t.AddThemeFontSizeOverride("font_size", 17);
        cv.AddChild(t);
        cv.AddChild(new Label { Text = $"{e.Meta?.Kind ?? e.Tags} · {e.Subscriptions} subscribers", Modulate = Muted });
        var row = new HBoxContainer();
        var sub = new Button { Text = e.Subscribed ? "Unsubscribe" : "Subscribe", FocusMode = Control.FocusModeEnum.None };
        sub.Pressed += () =>
        {
            sub.Disabled = true;
            if (e.Subscribed) _platform.Unsubscribe(e.Id, ok =>
            {
                sub.Disabled = false;
                if (ok) { e.Subscribed = false; sub.Text = "Subscribe"; }
            });
            else _platform.Subscribe(e.Id, ok =>
            {
                sub.Disabled = false;
                if (ok)
                {
                    e.Subscribed = true;
                    sub.Text = "Unsubscribe";
                    _t.Status.Text = $"Subscribed to {e.Title}; it installs when Steam finishes downloading.";
                }
            });
        };
        row.AddChild(sub);
        var open = new Button { Text = "Page", FocusMode = Control.FocusModeEnum.None };
        open.Pressed += () => _platform.OpenItemPage(e.Id);
        row.AddChild(open);
        cv.AddChild(row);
        return card;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public partial class PublishDialog : AcceptDialog
{
    readonly IPlatform _platform;
    readonly PackageStore _store;
    readonly string _kind, _id;
    public event Action<string>? Finished;

    public PublishDialog(IPlatform platform, PackageStore store, string kind, string id)
    {
        _platform = platform;
        _store = store;
        _kind = kind;
        _id = id;
        Title = "Publish to the Steam Workshop";
        OkButtonText = "Close";
        Size = new Vector2I(760, 560);
    }

    public override void _Ready()
    {
        var (name, about, warnings) = Describe();
        var existing = _platform.PublishedId(_kind, _id);

        var root = new HBoxContainer();
        root.AddThemeConstantOverride("separation", Ui.Gap.Section);
        AddChild(root);
        var previewBox = new VBoxContainer();
        root.AddChild(previewBox);
        var preview = new TextureRect { CustomMinimumSize = new Vector2(220, 220), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
        preview.Texture = BestEffort.Read<Texture2D?>(() => ImageTexture.CreateFromImage(Image.LoadFromFile(WorkshopContent.Preview(_store, _kind, _id))), fallback: null);
        previewBox.AddChild(preview);
        previewBox.AddChild(new Label { Text = existing is { } e ? $"Updates item {e}" : "New Workshop item", Modulate = Ui.Muted });

        var form = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddChild(form);
        form.AddChild(new Label { Text = "Title" });
        var title = new LineEdit { Text = name };
        form.AddChild(title);
        form.AddChild(new Label { Text = "Description" });
        var desc = new TextEdit { Text = about, CustomMinimumSize = new Vector2(0, 120), WrapMode = TextEdit.LineWrappingMode.Boundary };
        form.AddChild(desc);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Visible to" });
        var vis = new OptionButton();
        vis.AddItem("Everyone");
        vis.AddItem("Friends only");
        vis.AddItem("Unlisted (link only)");
        vis.AddItem("Only me");
        row.AddChild(vis);
        form.AddChild(row);
        OptionButton? packMode = null;
        if (_kind == "scenario")
        {
            form.AddChild(new Label { Text = "Packs" });
            packMode = ExportDialog.PackModePicker(_store, _id, workshop: true);
            form.AddChild(packMode);
        }
        form.AddChild(new Label { Text = "What changed (shown on the item's change notes)" });
        var note = new LineEdit { Text = existing == null ? "First release" : "" };
        form.AddChild(note);
        foreach (var w in warnings)
            form.AddChild(new Label { Text = w, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = Ui.AccentInk });
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, Visible = false };
        form.AddChild(bar);
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        form.AddChild(status);
        var go = new Button { Text = existing == null ? "Publish" : "Publish update" };
        form.AddChild(go);
        if (!_platform.Available) { go.Disabled = true; status.Text = _platform.Status ?? "Steam is not available."; }

        go.Pressed += () =>
        {
            go.Disabled = true;
            bar.Visible = true;
            var visibility = vis.Selected switch { 1 => WorkshopVisibility.FriendsOnly, 2 => WorkshopVisibility.Unlisted, 3 => WorkshopVisibility.OnlyMe, _ => WorkshopVisibility.Everyone };
            _platform.Publish(_kind, _id, title.Text.Trim(), desc.Text, visibility, note.Text.Trim(),
                (step, frac) => { status.Text = step; bar.Value = frac; },
                (ok, item, msg) =>
                {
                    bar.Visible = false;
                    go.Disabled = false;
                    go.Text = "Publish update";
                    status.Text = msg;
                    if (ok)
                    {
                        var open = new Button { Text = "Open the Workshop page" };
                        open.Pressed += () => _platform.OpenItemPage(item);
                        form.AddChild(open);
                        Finished?.Invoke($"Published '{title.Text.Trim()}' to the Steam Workshop.");
                    }
                }, packMode == null ? WorkshopContent.PackMode.None : (WorkshopContent.PackMode)packMode.GetSelectedId());
        };
    }

    (string name, string about, List<string> warnings) Describe()
    {
        var warnings = new List<string>();
        if (_kind == "pack")
        {
            var pack = _store.Pack(_id)!;
            if (!PackManifest.IsUserPackId(_id))
                warnings.Add("This pack was converted from someone else's TTS module. Publish it only with its authors' permission, and credit them in the description.");
            return (pack.Name, $"{pack.Pieces.Count} pieces" + (pack.Source.Authors.Count > 0 ? $" by {string.Join(", ", pack.Source.Authors)}" : "") + ".", warnings);
        }
        var sc = Json.Load<Scenario>(Path.Combine(_store.Root, "scenarios", _id, "scenario.json"));
        var missing = _platform.UnpublishedPacks(_id);
        if (missing.Count > 0)
            warnings.Add($"If you choose not to include packs, publish these first so subscribers get them as required items: {string.Join(", ", missing.Select(p => _store.Pack(p)?.Name ?? p))}.");
        return (sc.Name, $"{sc.Sheets.Count} map sheet{(sc.Sheets.Count == 1 ? "" : "s")}, {sc.UmpireBoard.Count} pieces.", warnings);
    }
}

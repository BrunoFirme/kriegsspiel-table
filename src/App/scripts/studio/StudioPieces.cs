using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class PieceList
{
    readonly PieceStudio _s;
    readonly Dictionary<string, Texture2D> _thumbs = new();
    LineEdit _search = null!;
    Label _count = null!;
    ItemList _list = null!;
    VSplitContainer _split = null!;
    bool _sizedByHand;
    int _listGen;
    const int SearchFrom = 9, TileWidth = 74, TileHeight = 96, ListPad = 12, ListMax = 320;
    const string RetiredNote = "Gone from the module at its last import: kept so pieces already placed still show, not offered in Pieces";

    public PieceList(PieceStudio s) => _s = s;

    PackManifest Pack => _s.Pack;

    public VSplitContainer Build(VBoxContainer col)
    {
        var head = new HBoxContainer();
        var title = new Label { Text = "PIECES", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        Ui.HeadStyle(title);
        head.AddChild(title);
        _count = new Label { Modulate = Ui.Faint, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _count.AddThemeFontSizeOverride("font_size", 11);
        head.AddChild(_count);
        head.AddChild(Ui.Btn("＋ New piece", _s.NewPieces.Show, "A block, a token, pieces from pictures, a 3D model…"));
        col.AddChild(head);
        _search = new LineEdit { PlaceholderText = "find a piece by name or tag", ClearButtonEnabled = true };
        _search.TextChanged += _ => _s.RebuildList();
        col.AddChild(_search);
        var split = _split = new VSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SplitOffsets = new[] { TileHeight + ListPad } };
        split.Dragged += _ => _sizedByHand = true;
        col.AddChild(split);
        head.Visible = _search.Visible = _s.InPlace == null;
        _list = new ItemList
        {
            IconMode = ItemList.IconModeEnum.Top, MaxColumns = 0,
            FixedIconSize = new Vector2I(60, 60), FixedColumnWidth = 74, SameColumnWidth = true, MaxTextLines = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, AllowReselect = true, AllowRmbSelect = true,
            CustomMinimumSize = new Vector2(0, TileHeight + ListPad),
        };
        _list.AddThemeFontSizeOverride("font_size", 11);
        _list.ItemSelected += i => { if (DefAt((int)i) is { } d) _s.Select(d, frame: true); };
        _list.ItemClicked += (i, at, button) => { if (button == (long)MouseButton.Right && DefAt((int)i) is { } d) ShowMenu(_list.GetGlobalTransform() * at, d); };
        _list.Visible = _s.InPlace == null;
        split.AddChild(_list);
        return split;
    }

    PieceDef? DefAt(int i) => i >= 0 && i < _list.ItemCount ? Pack.Pieces.FirstOrDefault(p => p.Id == _list.GetItemMetadata(i).AsString()) : null;

    public void Show(PieceDef? d)
    {
        if (d == null) { _list.DeselectAll(); return; }
        for (int i = 0; i < _list.ItemCount; i++) if (_list.GetItemMetadata(i).AsString() == d.Id)
        {
            _list.Select(i);
            _list.EnsureCurrentIsVisible();
            break;
        }
    }

    public void Rebuild(PieceDef? sel)
    {
        int gen = ++_listGen;
        _list.Clear();
        var q = _search.Text.Trim();
        foreach (var d in Pack.Pieces.Where(d => q.Length == 0 || d.Name.Contains(q,
            StringComparison.OrdinalIgnoreCase) || d.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))).OrderBy(d => d.Name,
            StringComparer.OrdinalIgnoreCase))
        {
            int idx = _list.AddItem(d.Name, Ui.LoadingIcon);
            _list.SetItemMetadata(idx, d.Id);
            _list.SetItemTooltip(idx, $"{d.Name}\n{d.Size.W:0.##} × {d.Size.H:0.##} u · {ShapeWord(d)}{(d.Script != null ? " · scripted" : "")}{(d.Retired ? "\n" + RetiredNote : "")}");
            if (d.Retired) _list.SetItemCustomFgColor(idx, Ui.Muted);
            if (sel == d) _list.Select(idx);
            if (d.Kind == "model" && d.Mesh != null && ModelThumb(d.Mesh) is { } mt) { _list.SetItemIcon(idx, mt); continue; }
            if (EffectFx.ListIcon(d) is { } fxIcon) { _list.SetItemIcon(idx, fxIcon); continue; }
            int i2 = idx;
            void Icon(Texture2D t)
            {
                if (gen == _listGen && i2 < _list.ItemCount) _list.SetItemIcon(i2, t);
            }
            if (d.Kind == "model") ModelPictures.Show(_s.S.Assets, Pack, d, Icon, () => Flat(d, Icon));
            else Flat(d, Icon);
        }
        _count.Text = Pack.Pieces.Count.ToString();
        _search.Visible = Pack.Pieces.Count >= SearchFrom || q.Length > 0;
        Fit();
    }

    void Fit()
    {
        if (_sizedByHand) return;
        int columns = Math.Max(1, (int)((_list.Size.X > 0 ? _list.Size.X : 340) / (TileWidth + 6)));
        int rows = Math.Max(1, (_list.ItemCount + columns - 1) / columns);
        _split.SplitOffsets = new[] { Math.Min(ListMax, rows * TileHeight + ListPad) };
    }

    void Flat(PieceDef d, Action<Texture2D> icon)
    {
        var img = d.Image ?? d.Texture ?? d.States.Select(st => st.Image ?? st.Texture).FirstOrDefault(h => h != null);
        if (img == null || !Pack.Assets.TryGetValue(img, out var a)) return;
        if (_s.S.Assets.Texture(Pack.Id, a.File, icon, mipmaps: false, maxSize: 128) is { } tex) icon(tex);
    }

    public void ForgetThumb(string meshSha) => _thumbs.Remove(meshSha);

    Texture2D? ModelThumb(string meshSha)
    {
        if (_thumbs.TryGetValue(meshSha, out var t)) return t;
        var p = ModelBake.ThumbPath(_s.Store, Pack.Id, meshSha);
        if (!File.Exists(p) || Image.LoadFromFile(p) is not { } img || img.IsEmpty()) return null;
        return _thumbs[meshSha] = ImageTexture.CreateFromImage(img);
    }

    static string ShapeWord(PieceDef d) => d.Kind == "model" ? (d.Tags.Contains("die") ? "die" : "3D model") : d.Tags.Contains("effect") ? "effect" : d.Shape switch
    {
        "alpha" => "cut-out", "circle" => "round", "hex" => "hex", "rounded" => "rounded tile", _ => "tile",
    };

    void ShowMenu(Vector2 at, PieceDef d)
    {
        var pm = new PopupMenu();
        pm.AddItem("Paint", 1);
        pm.AddItem("Show on the table", 2);
        pm.AddSeparator();
        pm.AddItem("Duplicate", 3);
        pm.AddItem("Delete", 4);
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 1:
                    _s.SetView(d.Kind == "model" ? "model" : "paint");
                    break;
                case 2:
                    _s.SetView("table");
                    if (_s.Paper.Instance(d) is { } p) _s.T.Rig.Frame(Coords.ToWorld(p.Pos), (float)Math.Max(6, Math.Max(d.Size.W, d.Size.H) * 4));
                    break;
                case 3:
                    _s.DuplicateSel(d);
                    break;
                case 4:
                    _s.DeleteSel(d);
                    break;
            }
        };
        pm.PopupHide += () => pm.QueueFree();
        _s.Root.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }
}

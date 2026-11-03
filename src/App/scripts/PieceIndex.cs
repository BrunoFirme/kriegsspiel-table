using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceIndex : HudPart
{
    const int MostRows = 1000;
    const string Missing = "⚠ ";

    sealed record Row(string Layer, PieceInstance Piece, string Name, string Kind, string Pack, bool PackMissing, string Find);

    public readonly VBoxContainer Root = new() { Name = "On the table" };
    readonly LineEdit _search;
    readonly ItemList _list;
    readonly Label _count, _warning;
    readonly Button _selectMissing;
    readonly Button _delete;
    List<Row> _shown = new();
    bool _stale = true;

    public PieceIndex(HudContext ctx) : base(ctx)
    {
        Root.AddThemeConstantOverride("separation", 4);
        _search = new LineEdit { PlaceholderText = "search name, kind, pack, tag, text, counter…", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _search.TextChanged += _ => Rebuild();
        Root.AddChild(_search);
        _warning = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = TitleStyle.Warning, Visible = false, CustomMinimumSize = new Vector2(240, 0) };
        Root.AddChild(_warning);
        _selectMissing = Btn("Select the missing pieces", SelectMissing, "Selects every row whose pack is not installed here, ready to delete");
        Ui.MakeQuiet(_selectMissing);
        _selectMissing.Visible = false;
        Root.AddChild(Ui.Actions(_selectMissing));
        _list = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Multi,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, AllowReselect = true, CustomMinimumSize = new Vector2(0, 80),
        };
        _list.AddThemeFontSizeOverride("font_size", 12);
        _list.ItemActivated += i => Locate(new[] { (int)i });
        _list.MultiSelected += (_, _) => SyncButtons();
        Root.AddChild(_list);
        _count = new Label { Modulate = Ui.Muted };
        _count.AddThemeFontSizeOverride("font_size", 11);
        var show = Btn("Show", () => Locate(_list.GetSelectedItems()), "Selects them on the table and brings the camera to them · double-click a row does the same");
        _delete = Btn("Delete", Delete, "Deletes the selected pieces; Ctrl+Z brings them back");
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        actions.AddChild(show);
        actions.AddChild(_count);
        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        actions.AddChild(_delete);
        Root.AddChild(actions);
        S.Applied += MarkStale;
        S.Reset += MarkStale;
        Root.TreeExiting += () => { S.Applied -= MarkStale; S.Reset -= MarkStale; };
        Root.VisibilityChanged += () => { if (Root.IsVisibleInTree() && _stale) Rebuild(); };
    }

    void MarkStale(KTable.Core.Protocol.Command _) => MarkStale();

    void MarkStale()
    {
        if (_stale) return;
        _stale = true;
        Callable.From(() => { if (GodotObject.IsInstanceValid(Root) && Root.IsVisibleInTree() && _stale) Rebuild(); }).CallDeferred();
    }

    List<Row> Rows()
    {
        var rows = new List<Row>();
        foreach (var (layer, pieces) in S.View.Layers)
            foreach (var p in pieces)
            {
                var def = S.Store.Def(p.Def);
                var pack = S.Store.PackOfDef(p.Def);
                var packId = pack?.Id ?? S.View.PackNamedBy(p.Def);
                var packName = pack?.Name ?? packId ?? "a pack not installed here";
                var kind = def?.Name ?? (packId != null ? p.Def[(packId.Length + 1)..] : p.Def);
                var name = p.Label is { Length: > 0 } label ? label : kind;
                var words = new[] { name, kind, p.Def, packName, packId ?? "", p.Note ?? "", p.Memo }
                    .Concat(p.Tags ?? new List<string>()).Concat(def?.Tags ?? new List<string>())
                    .Concat(p.Text.Values).Concat(p.Counters.Select(c => $"{c.Key} {c.Value}"));
                rows.Add(new Row(layer, p, name, kind, packName, def == null, string.Join('\n', words)));
            }
        return rows.OrderByDescending(r => r.PackMissing).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Piece.Id, StringComparer.Ordinal).ToList();
    }

    public void Rebuild()
    {
        _stale = false;
        var all = Rows();
        var q = _search.Text.Trim();
        _shown = (q.Length == 0 ? all : all.Where(r => r.Find.Contains(q, StringComparison.OrdinalIgnoreCase))).Take(MostRows).ToList();
        _list.Clear();
        foreach (var r in _shown)
        {
            var parts = new[] { r.Name, r.Name == r.Kind ? "" : r.Kind, r.Pack, string.Join("  ", r.Piece.Counters.Select(c => $"{c.Key} {c.Value}")) };
            int i = _list.AddItem((r.PackMissing ? Missing : "") + string.Join("  ·  ", parts.Where(x => x.Length > 0)));
            _list.SetItemTooltip(i, r.PackMissing
                ? $"{r.Name}\nIts pack is not installed here ({r.Pack}), so it is not drawn. Install the pack under Piece studio, or delete it here."
                : $"{r.Name}\n{r.Kind} · {r.Pack}\nat {r.Piece.Pos.X:0.#}, {r.Piece.Pos.Z:0.#}");
            if (r.PackMissing) _list.SetItemCustomFgColor(i, TitleStyle.Warning);
        }
        var missing = all.Where(r => r.PackMissing).ToList();
        _warning.Visible = _selectMissing.Visible = missing.Count > 0;
        if (missing.Count > 0)
            _warning.Text = $"{missing.Count} piece{(missing.Count == 1 ? "" : "s")} from {string.Join(", ", missing.Select(r => r.Pack).Distinct())} {(missing.Count == 1 ? "is" : "are")} not drawn: the pack is not installed here. Install it, or delete them here and the game stops asking for it.";
        _count.Text = _shown.Count < all.Count ? $"{_shown.Count} of {all.Count}" : $"{all.Count} piece{(all.Count == 1 ? "" : "s")}";
        SyncButtons();
    }

    void SyncButtons() => _delete.Disabled = _list.GetSelectedItems().Length == 0;

    List<Row> Picked(IEnumerable<int> items) => items.Where(i => i < _shown.Count).Select(i => _shown[i]).ToList();

    void Locate(IEnumerable<int> items)
    {
        var rows = Picked(items);
        if (rows.Count > 0) T.Locate(rows.Select(r => r.Piece).ToList());
    }

    void SelectMissing()
    {
        _list.DeselectAll();
        for (int i = 0; i < _shown.Count; i++) if (_shown[i].PackMissing) _list.Select(i, single: false);
        SyncButtons();
    }

    void Delete()
    {
        var rows = Picked(_list.GetSelectedItems());
        var allowed = rows.Where(r => S.CanTouch(r.Layer, r.Piece)).ToList();
        if (allowed.Count > 0) T.DeletePieces(allowed.Select(r => (r.Layer, r.Piece)));
        var refused = rows.Count - allowed.Count;
        Log(refused == 0
            ? $"Deleted {allowed.Count} piece{(allowed.Count == 1 ? "" : "s")}."
            : $"Deleted {allowed.Count}; {refused} {(refused == 1 ? "is" : "are")} outside what your seat may change.");
    }
}

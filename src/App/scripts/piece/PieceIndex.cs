using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PieceIndex : HudPart
{
    const int MostRows = 1000;
    const string Missing = "⚠ ";
    const char Apart = '\n', LayerFromId = '\t';

    sealed record Row(string Layer, PieceInstance Piece, string Name, string Kind, string Pack, bool PackMissing, string Find);

    public readonly VBoxContainer Root = new() { Name = "On the table" };
    readonly LineEdit _search;
    readonly Tree _tree;
    readonly Label _count, _warning, _nestHint;
    readonly Button _selectMissing;
    readonly Button _delete;
    readonly List<Row> _shown = new();
    readonly HashSet<(string, string)> _folded = new();
    bool _stale = true;
    bool _filling;

    public PieceIndex(HudContext ctx) : base(ctx)
    {
        Root.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        _search = new LineEdit { PlaceholderText = "search name, kind, pack, tag, text, counter…", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _search.TextChanged += _ => Rebuild();
        Root.AddChild(_search);
        _warning = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = TitleStyle.Warning, Visible = false, CustomMinimumSize = new Vector2(240, 0) };
        Root.AddChild(_warning);
        _selectMissing = Btn("Select the missing pieces", SelectMissing, "Selects every row whose pack is not installed here, ready to delete");
        Ui.MakeQuiet(_selectMissing);
        _selectMissing.Visible = false;
        Root.AddChild(Ui.Actions(_selectMissing));
        _tree = new Tree
        {
            HideRoot = true, SelectMode = Tree.SelectModeEnum.Multi, Columns = 1, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 80), DropModeFlags = (int)(Tree.DropModeFlagsEnum.OnItem | Tree.DropModeFlagsEnum.Inbetween),
        };
        _tree.AddThemeFontSizeOverride("font_size", 12);
        _tree.AddThemeStyleboxOverride("cursor", new StyleBoxEmpty());
        _tree.AddThemeStyleboxOverride("cursor_unfocused", new StyleBoxEmpty());
        _tree.AddThemeConstantOverride("draw_guides", 0);
        _tree.AddThemeConstantOverride("draw_relationship_lines", 0);
        _tree.ItemActivated += () => Locate(Picked());
        _tree.MultiSelected += (_, _, _) => SyncButtons();
        _tree.ItemCollapsed += Folded;
        _tree.SetDragForwarding(Callable.From<Vector2, Variant>(DragFrom), Callable.From<Vector2, Variant, bool>((at, data) => DropAt(at, data).Count > 0),
            Callable.From<Vector2, Variant>((at, data) => Nest(DropAt(at, data))));
        Root.AddChild(_tree);
        _nestHint = Hint("Drag a row onto another piece to attach it there: moving that piece carries it. Drag it below the list to free it.");
        _nestHint.CustomMinimumSize = new Vector2(240, 0);
        Root.AddChild(_nestHint);
        _count = new Label { Modulate = Ui.Muted };
        _count.AddThemeFontSizeOverride("font_size", 11);
        var show = Btn("Show", () => Locate(Picked()), "Selects them on the table and brings the camera to them · double-click a row does the same");
        _delete = Btn("Delete", Delete, "Deletes the selected pieces; Ctrl+Z brings them back");
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", Ui.Gap.Group);
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

    internal Tree Rows => _tree;

    void MarkStale(Command _) => MarkStale();

    void MarkStale()
    {
        if (_stale) return;
        _stale = true;
        Callable.From(() => { if (GodotObject.IsInstanceValid(Root) && Root.IsVisibleInTree() && _stale) Rebuild(); }).CallDeferred();
    }

    List<Row> AllRows()
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
        var all = AllRows();
        var q = _search.Text.Trim();
        var matches = (q.Length == 0 ? all : all.Where(r => r.Find.Contains(q, StringComparison.OrdinalIgnoreCase))).Take(MostRows).ToList();
        var parents = S.View.Layers.ToDictionary(kv => kv.Key, kv => PieceTree.Parents(kv.Value));
        var matched = matches.Select(r => (r.Layer, r.Piece.Id)).ToHashSet();
        var kept = matched.ToHashSet();
        foreach (var (layer, id) in matched)
            for (var at = id; parents[layer].TryGetValue(at, out var up) && kept.Add((layer, up.Id)); at = up.Id) { }
        var picked = Picked().Select(r => (r.Layer, r.Piece.Id)).ToHashSet();
        _filling = true;
        try { Fill(all.Where(r => kept.Contains((r.Layer, r.Piece.Id))).ToList(), parents, matched, picked); }
        finally { _filling = false; }
        var missing = all.Where(r => r.PackMissing).ToList();
        _warning.Visible = _selectMissing.Visible = missing.Count > 0;
        if (missing.Count > 0)
            _warning.Text = $"{missing.Count} piece{(missing.Count == 1 ? "" : "s")} from {string.Join(", ", missing.Select(r => r.Pack).Distinct())} {(missing.Count == 1 ? "is" : "are")} not drawn: the pack is not installed here. Install it, or delete them here and the game stops asking for it.";
        _count.Text = matches.Count < all.Count ? $"{matches.Count} of {all.Count}" : $"{all.Count} piece{(all.Count == 1 ? "" : "s")}";
        _nestHint.Visible = S.IsUmpire && all.Count > 1;
        SyncButtons();
    }

    void Fill(List<Row> rows, Dictionary<string, Dictionary<string, PieceInstance>> parents, HashSet<(string, string)> matched, HashSet<(string, string)> picked)
    {
        _tree.Clear();
        _shown.Clear();
        var top = _tree.CreateItem();
        var listed = rows.Select(r => (r.Layer, r.Piece.Id)).ToHashSet();
        var under = rows.ToLookup(r => parents[r.Layer].TryGetValue(r.Piece.Id, out var up) && listed.Contains((r.Layer, up.Id)) ? (r.Layer, up.Id) : default);
        void Add(TreeItem parent, Row r)
        {
            var key = (r.Layer, r.Piece.Id);
            var item = _tree.CreateItem(parent);
            var parts = new[] { r.Name, r.Name == r.Kind ? "" : r.Kind, r.Pack, string.Join("  ", r.Piece.Counters.Select(c => $"{c.Key} {c.Value}")) };
            item.SetText(0, (r.PackMissing ? Missing : "") + string.Join("  ·  ", parts.Where(x => x.Length > 0)));
            item.SetTextOverrunBehavior(0, TextServer.OverrunBehavior.TrimEllipsis);
            item.SetMetadata(0, _shown.Count);
            _shown.Add(r);
            item.SetTooltipText(0, r.PackMissing
                ? $"{r.Name}\nIts pack is not installed here ({r.Pack}), so it is not drawn. Install the pack under Piece studio, or delete it here."
                : $"{r.Name}\n{r.Kind} · {r.Pack}\nat {r.Piece.Pos.X:0.#}, {r.Piece.Pos.Z:0.#}");
            if (r.PackMissing) item.SetCustomColor(0, TitleStyle.Warning);
            else if (!matched.Contains(key)) item.SetCustomColor(0, Ui.Muted);
            if (picked.Contains(key)) item.Select(0);
            foreach (var child in under[key]) Add(item, child);
            item.Collapsed = _folded.Contains(key) && item.GetChildCount() > 0;
        }
        foreach (var r in under[default]) Add(top, r);
    }

    void Folded(TreeItem item)
    {
        if (_filling || RowOf(item) is not { } r) return;
        var key = (r.Layer, r.Piece.Id);
        if (item.Collapsed) _folded.Add(key);
        else _folded.Remove(key);
    }

    Row? RowOf(TreeItem? item) =>
        item != null && item.GetMetadata(0).VariantType == Variant.Type.Int && item.GetMetadata(0).AsInt32() is var i && i >= 0 && i < _shown.Count ? _shown[i] : null;

    internal TreeItem? ItemOf(string layer, string id)
    {
        for (var item = _tree.GetRoot()?.GetFirstChild(); item != null; item = item.GetNextInTree())
            if (RowOf(item) is { } r && r.Layer == layer && r.Piece.Id == id) return item;
        return null;
    }

    List<Row> Picked()
    {
        var rows = new List<Row>();
        for (var item = _tree.GetNextSelected(null); item != null; item = _tree.GetNextSelected(item))
            if (RowOf(item) is { } r) rows.Add(r);
        return rows;
    }

    void SyncButtons() => _delete.Disabled = _tree.GetNextSelected(null) == null;

    void Locate(List<Row> rows)
    {
        if (rows.Count > 0) T.Locate(rows.Select(r => r.Piece).ToList());
    }

    void SelectMissing()
    {
        _tree.DeselectAll();
        for (var item = _tree.GetRoot()?.GetFirstChild(); item != null; item = item.GetNextInTree())
            if (RowOf(item) is { PackMissing: true }) item.Select(0);
        SyncButtons();
    }

    void Delete()
    {
        var rows = Picked();
        var allowed = rows.Where(r => S.CanTouch(r.Layer, r.Piece)).ToList();
        if (allowed.Count > 0) T.DeletePieces(allowed.Select(r => (r.Layer, r.Piece)));
        var refused = rows.Count - allowed.Count;
        Log(refused == 0
            ? $"Deleted {allowed.Count} piece{(allowed.Count == 1 ? "" : "s")}."
            : $"Deleted {allowed.Count}; {refused} {(refused == 1 ? "is" : "are")} outside what your seat may change.");
    }

    Variant DragFrom(Vector2 at)
    {
        if (!S.IsUmpire || RowOf(_tree.GetItemAtPosition(at)) is not { } grabbed) return new Variant();
        var rows = Picked();
        if (!rows.Contains(grabbed)) rows = new() { grabbed };
        rows = rows.Where(r => S.CanTouch(r.Layer, r.Piece)).ToList();
        if (rows.Count == 0) return new Variant();
        _tree.SetDragPreview(new Label { Text = rows.Count == 1 ? rows[0].Name : $"{rows.Count} pieces" });
        return string.Join(Apart, rows.Select(r => $"{r.Layer}{LayerFromId}{r.Piece.Id}"));
    }

    static List<(string Layer, string Id)> Dragged(Variant data) =>
        data.VariantType != Variant.Type.String ? new()
            : data.AsString().Split(Apart, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(LayerFromId)).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList();

    List<Command> DropAt(Vector2 at, Variant data)
    {
        var item = _tree.GetItemAtPosition(at);
        return NestCommands(Dragged(data), RowOf(item), item != null && _tree.GetDropSectionAtPosition(at) != 0);
    }

    internal List<Command> NestCommands(List<(string Layer, string Id)> dragged, TreeItem? onto, bool between) => NestCommands(dragged, RowOf(onto), between);

    List<Command> NestCommands(List<(string Layer, string Id)> dragged, Row? onto, bool between)
    {
        if (!S.IsUmpire || dragged.Count == 0) return new();
        var cmds = new List<Command>();
        foreach (var group in dragged.GroupBy(d => d.Layer))
        {
            if (onto != null && onto.Layer != group.Key) continue;
            var into = onto == null ? null : !between ? onto.Piece.Id : PieceTree.Parents(S.View.Layer(group.Key)).GetValueOrDefault(onto.Piece.Id)?.Id;
            if (into != null && S.View.Find(group.Key, into) is { } target && !S.CanTouch(group.Key, target)) continue;
            var ids = group.Select(d => d.Id).Where(id => S.View.Find(group.Key, id) is { } p && S.CanTouch(group.Key, p));
            cmds.AddRange(PieceTree.Nest(S.View, group.Key, ids, into));
        }
        return cmds;
    }

    internal void Nest(List<Command> cmds)
    {
        if (cmds.Count == 0) return;
        S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
        var attached = cmds.OfType<Attach>().ToList();
        int freed = cmds.Count - attached.Count;
        var to = attached.Select(a => (a.Layer, a.Parent)).Distinct().ToList();
        var toName = to.Count == 1 && S.View.Find(to[0].Layer, to[0].Parent) is { } p ? HolderWindow.NameOf(p, S) : $"{to.Count} pieces";
        if (attached.Count > 0) Say($"{Pieces(attached.Count)} attached to {toName}");
        else Say($"{Pieces(freed)} freed");
    }

    static string Pieces(int n) => n == 1 ? "1 piece" : $"{n} pieces";
}

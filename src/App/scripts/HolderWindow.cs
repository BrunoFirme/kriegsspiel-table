using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public partial class HolderWindow : FloatPanel
{
    const float Beside = 16, DragFrom = 10, TakenOutApart = 1.5f, TakenOutFrom = 2, StartWidth = 300;
    const int RowsShown = 3;
    static readonly Dictionary<(string Layer, string Id), HolderWindow> Shown = new();

    readonly Session _s;
    readonly Table _t;
    readonly string _layer, _id;
    readonly ItemList _list;
    readonly Control _tiles;
    readonly LineEdit _search;
    readonly Label _empty, _dragHint;
    readonly Button _all;
    string _drawn = "\0";
    int _gen;
    int _most = -1;
    Vector2? _offset;
    bool _moving;

    HolderWindow(Session s, Table t, PieceNode holder) : base(NameOf(holder.Piece, s))
    {
        _s = s;
        _t = t;
        _layer = holder.Layer;
        _id = holder.Piece.Id;
        MouseFilter = MouseFilterEnum.Stop;
        Resizable = true;
        CustomMinimumSize = new Vector2(StartWidth, 0);
        AddThemeStyleboxOverride("panel", Ui.Panel());
        Body.SizeFlagsVertical = SizeFlags.ExpandFill;
        _search = new LineEdit { PlaceholderText = "search name, tag…", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _search.TextChanged += _ => Redraw(force: true);
        Body.AddChild(_search);
        _list = PieceTiles.List();
        _list.ItemActivated += i => TakeOut(Meta((int)i));
        WireDrag();
        _tiles = PieceTiles.Host(_list);
        Body.AddChild(_tiles);
        _empty = Ui.Hint("Empty · hold pieces over it on the table a moment, or drop them here");
        Body.AddChild(_empty);
        _all = Ui.Btn("Take all out", TakeAllOut, "Sets them out in a row beside it");
        _all.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        Body.AddChild(_all);
        _dragHint = Ui.Hint("Drag a piece out onto the table · double-click sets it beside");
        Body.AddChild(_dragHint);
        Grabbed = _ => _moving = true;
        Moved = (_, _) => { _moving = false; if (OnScreen() is { } a) _offset = Position - a.at; };
        Closed = QueueFree;
        TreeExiting += () => Shown.Remove((_layer, _id));
        Ui.SwallowWheel(this);
    }

    public static void Open(Control root, Session s, Table t, PieceNode holder, int most = -1)
    {
        if (Shown.TryGetValue((holder.Layer, holder.Piece.Id), out var already) && IsInstanceValid(already))
        {
            already._most = most;
            already.Redraw(force: true);
            already.MoveToFront();
            return;
        }
        var w = new HolderWindow(s, t, holder) { _most = most };
        Shown[(holder.Layer, holder.Piece.Id)] = w;
        root.AddChild(w);
        w.Redraw(force: true);
        w.Follow();
    }

    public static (string Layer, string Holder)? Under(Vector2 screen)
    {
        foreach (var (key, w) in Shown)
            if (IsInstanceValid(w) && w.Visible && w.GetGlobalRect().HasPoint(screen)) return key;
        return null;
    }

    public static Command TakingOut(Session s, string layer, PieceInstance holder, List<Command> unstows)
    {
        if (holder.Holds == null && s.Store.Def(holder.Def)?.Holds != true)
            unstows.Add(new SetPieceOption { Layer = layer, Id = holder.Id, Name = PieceOptions.Holds, Value = true });
        return unstows.Count == 1 ? unstows[0] : new Batch { Items = unstows };
    }

    internal static string NameOf(PieceInstance p, Session s) => p.Label is { Length: > 0 } lb ? lb : s.Store.Def(p.Def)?.Name ?? p.Def;

    PieceNode? Holder => IsInstanceValid(_t) && _t.IsInsideTree() && _t.NodeOf(_layer, _id) is { } n && IsInstanceValid(n) ? n : null;

    bool CanTake => Holder is { } h && _s.CanTouch(_layer, h.Piece);

    string Meta(int i) => i >= 0 && i < _list.ItemCount ? _list.GetItemMetadata(i).AsString() : "";

    const string OutMark = "out:";

    Readiness ReadyState(PieceInstance h) => ReadyPiece.Of(h, id => _s.View.Find(_layer, id));

    public override void _Process(double delta)
    {
        if (Holder == null) { QueueFree(); return; }
        Redraw(force: false);
        FitRows();
        bool carrying = _t.Hand.Holding && GetGlobalRect().HasPoint(Pointer.Global(this));
        SelfModulate = carrying ? Ui.AccentInk : Colors.White;
        if (!_moving) Follow();
    }

    (Vector2 at, float radius)? OnScreen()
    {
        if (Holder is not { } h) return null;
        var cam = _t.Rig.Cam;
        var centre = new Vector3(h.Position.X, h.TopY, h.Position.Z);
        if (cam.IsPositionBehind(centre)) return null;
        var at = cam.UnprojectPosition(centre);
        float reach = Mathf.Max(h.Footprint.X, h.Footprint.Y) * 0.5f * (float)h.Piece.Scale;
        float radius = (cam.UnprojectPosition(centre + cam.GlobalBasis.X * reach) - at).Length();
        return (at, radius);
    }

    void Follow()
    {
        if (OnScreen() is not { } a || !GetViewportRect().HasPoint(a.at)) return;
        var size = Size;
        var right = new Vector2(a.at.X + a.radius + Beside, a.at.Y - size.Y / 2);
        var left = new Vector2(a.at.X - a.radius - Beside - size.X, right.Y);
        Position = _offset is { } o ? a.at + o : right.X + size.X <= GetViewportRect().Size.X ? right : left;
        Clamp();
    }

    void Redraw(bool force)
    {
        var holder = Holder?.Piece;
        var stowed = holder?.Stowed ?? new List<PieceInstance>();
        var top = Loose(stowed);
        if (_most >= 0 && top.Count > _most) top = top.Take(_most).ToList();
        var ready = holder?.Ready?.Id;
        var state = holder == null ? Readiness.None : ReadyState(holder);
        var released = state == Readiness.Out ? _s.View.Find(_layer, ready!) : null;
        if (state == Readiness.In && top.FirstOrDefault(p => p.Id == ready) is { } first) top = top.Where(p => p != first).Prepend(first).ToList();
        var shown = released == null ? top : top.Prepend(released).ToList();
        var sig = _search.Text + "|" + state + ready + "|" + string.Join(",", shown.Select(p => p.Id + ":" + (p.Label ?? "")));
        if (!force && sig == _drawn) return;
        _drawn = sig;
        int gen = ++_gen;
        SetTitle($"{(Holder is { } h ? NameOf(h.Piece, _s) : "")} · {top.Count}");
        _list.Clear();
        var q = _search.Text.Trim();
        var outKey = _t.Settings.Keys.Label("ready");
        foreach (var p in shown)
        {
            var d = _s.Store.Def(p.Def);
            var name = p.Label is { Length: > 0 } lb ? lb : d?.Name ?? p.Def;
            if (q.Length > 0 && !name.Contains(q, StringComparison.OrdinalIgnoreCase) && !(d?.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false)) continue;
            int inside = p.AllStowed().Count();
            bool isOut = p == released, isReady = p.Id == ready;
            var sub = isOut ? "Ready · out" : isReady ? "Ready"
                : (d == null ? "missing kind" : PiecesPanel.ShapeWord(d)) + (inside > 0 ? $" · holds {inside}" : "");
            int idx = _list.AddItem($"{name}\n{sub}", Ui.LoadingIcon);
            _list.SetItemMetadata(idx, isOut ? OutMark + p.Id : p.Id);
            var tip = d == null ? name : $"{name}\n{d.Name}\n{d.Size.W:0.##} × {d.Size.H:0.##} units · {PiecesPanel.ShapeWord(d)}";
            _list.SetItemTooltip(idx, isOut ? $"{tip}\nDouble-click or {outKey} recalls it" : isReady ? $"{tip}\n{outKey} on the holder releases it" : _s.IsUmpire ? $"{tip}\nRight-click to make it the ready piece" : tip);
            if (isReady) _list.SetItemCustomFgColor(idx, Ui.AccentInk);
            if (d != null) PieceTiles.Picture(_s, _list, idx, d, _s.Store.PackOfDef(p.Def), () => gen == _gen);
        }
        bool empty = top.Count == 0;
        _empty.Visible = empty;
        _search.Visible = _tiles.Visible = _dragHint.Visible = !empty;
        _all.Visible = top.Count > 1;
        _all.Disabled = !CanTake;
    }

    void FitRows()
    {
        if (!IsInstanceValid(this)) return;
        float tall = 0;
        if (_tiles.Visible && _list.ItemCount > 0 && _list.Size.X > 0)
        {
            float first = _list.GetItemRect(0).Position.Y, row = first, bottom = 0;
            int rows = 1;
            for (int i = 0; i < _list.ItemCount; i++)
            {
                var r = _list.GetItemRect(i);
                if (r.Position.Y > row && ++rows > RowsShown) break;
                row = r.Position.Y;
                bottom = Mathf.Max(bottom, r.End.Y);
            }
            var box = _list.GetThemeStylebox("panel");
            tall = bottom - first + (box?.GetMinimumSize().Y ?? 0) + _list.GetThemeConstant("v_separation");
        }
        if (Mathf.IsEqualApprox(_tiles.CustomMinimumSize.Y, tall)) return;
        _tiles.CustomMinimumSize = new Vector2(0, tall);
        ResetSize();
    }

    static List<PieceInstance> Loose(List<PieceInstance> stowed)
    {
        var riding = stowed.SelectMany(p => p.Attached).ToHashSet();
        return stowed.Where(p => !riding.Contains(p.Id)).ToList();
    }

    void WireDrag()
    {
        Vector2 press = Vector2.Zero;
        int item = -1;
        bool dragging = false;
        _list.GuiInput += e =>
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rb:
                    if (_list.GetItemAtPosition(rb.Position, true) is var at and >= 0) { _list.AcceptEvent(); ReadyMenu(Meta(at), _list.GetGlobalTransformWithCanvas() * rb.Position); }
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                    press = mb.Position;
                    item = _list.GetItemAtPosition(mb.Position, true);
                    dragging = false;
                    break;
                case InputEventMouseMotion mm when item >= 0 && !dragging && (mm.ButtonMask & MouseButtonMask.Left) != 0:
                    if ((mm.Position - press).Length() > DragFrom && CanTake && Meta(item) is { Length: > 0 } id && !id.StartsWith(OutMark))
                    {
                        dragging = true;
                        _t.BeginTakeOut(_layer, _id, id, at => IsInstanceValid(this) && GetGlobalRect().HasPoint(at));
                    }
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb:
                    if (dragging) _t.EndPaletteDrag(drop: !GetGlobalRect().HasPoint(mb.GlobalPosition));
                    item = -1;
                    dragging = false;
                    break;
            }
        };
    }

    void ReadyMenu(string meta, Vector2 at)
    {
        if (meta.Length == 0 || !CanTake || !_s.IsUmpire || Holder is not { } h) return;
        var pm = new PopupMenu();
        if (meta.StartsWith(OutMark)) pm.AddItem("Recall", RecallChoice);
        else
        {
            pm.AddCheckItem("Ready piece", ReadyChoice);
            pm.SetItemChecked(0, h.Piece.Ready?.Id == meta);
            pm.SetItemTooltip(0, "The one piece the holder releases and recalls with " + _t.Settings.Keys.Label("ready"));
        }
        pm.IdPressed += choice => ChooseReady(meta, choice);
        Menu = pm;
        pm.PopupHide += () => pm.QueueFree();
        AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    internal const int RecallChoice = 0, ReadyChoice = 1;
    internal PopupMenu? Menu { get; private set; }

    internal void ChooseReady(string meta, long choice)
    {
        if (Holder is not { } now || !CanTake || !_s.IsUmpire) return;
        if (choice == RecallChoice) _s.Do(new RecallReady { Layer = _layer, Holder = _id });
        else if (now.Piece.Ready?.Id == meta) _s.Do(new SetReady { Layer = _layer, Holder = _id });
        else _s.Do(new SetReady { Layer = _layer, Holder = _id, Ready = new ReadyPiece { Id = meta }, Move = meta });
    }

    void TakeOut(string id)
    {
        if (id.StartsWith(OutMark)) { ChooseReady(id, RecallChoice); return; }
        if (id.Length == 0 || !CanTake || Holder is not { } h) return;
        _s.Do(TakingOut(_s, _layer, h.Piece, new List<Command> { new Unstow { Layer = _layer, Holder = _id, Id = id } }));
    }

    void TakeAllOut()
    {
        if (!CanTake || Holder is not { } h || h.Piece.Stowed is not { Count: > 0 } stowed) return;
        var top = Loose(stowed);
        if (_most >= 0 && top.Count > _most) top = top.Take(_most).ToList();
        _s.Do(TakingOut(_s, _layer, h.Piece,
            top.Select((p, i) => (Command)new Unstow { Layer = _layer, Holder = _id, Id = p.Id, Pos = new Vec2(h.Piece.Pos.X + TakenOutFrom + i * TakenOutApart, h.Piece.Pos.Z) }).ToList()));
    }
}

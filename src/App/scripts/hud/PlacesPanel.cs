using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PlacesPanel : HudPart
{
    public const string Key = "places", Title = "Places";
    public const float Width = 280, Height = 320;
    const int NamedUpTo = 3;
    const float FrameBy = 1.2f, HereWidth = 96;

    public readonly FloatPanel Panel;
    readonly LineEdit _find;
    readonly Tree _tree;
    readonly Label _empty;
    readonly List<MapPlace> _shown = new();
    readonly HashSet<string> _folded = new();
    List<MapPlace> _places = new();
    IReadOnlyList<BodyOf>? _bodiesSeen;
    bool _stale = true, _hereStale;

    internal Tree Rows => _tree;
    internal LineEdit Find => _find;
    internal Button PlaceHereButton { get; }
    internal PopupMenu? Menu { get; private set; }
    internal LineEdit? NameField { get; private set; }
    internal ConfirmationDialog? Asking { get; private set; }

    public PlacesPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel(Key, Title);
        Panel.Resizable = true;
        Panel.CustomMinimumSize = shell.PanelSize(Key, Width, Height);
        Panel.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var box = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        _find = new LineEdit { PlaceholderText = "Find", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _find.TextChanged += _ => Fill();
        head.AddChild(_find);
        PlaceHereButton = Btn("Place here", PlaceHere, "Pins the spot the camera looks at, with this view");
        head.AddChild(PlaceHereButton);
        box.AddChild(head);
        _tree = new Tree
        {
            HideRoot = true, Columns = 2, SelectMode = Tree.SelectModeEnum.Single, AllowRmbSelect = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = Ui.Px(0, 120), TooltipText = "Click a place to go there · right-click to send players",
        };
        _tree.SetColumnExpand(1, false);
        _tree.SetColumnCustomMinimumWidth(1, Ui.Px(HereWidth));
        _tree.AddThemeConstantOverride("draw_guides", 0);
        _tree.AddThemeConstantOverride("draw_relationship_lines", 0);
        _tree.ItemMouseSelected += Clicked;
        _tree.ItemCollapsed += Folded;
        box.AddChild(_tree);
        _empty = Hint("No places");
        box.AddChild(_empty);
        Panel.Body.AddChild(box);
        Panel.VisibilityChanged += () => { if (Panel.Visible) Rebuild(); };
    }

    public void MarkStale() => _stale = true;

    public void Applied(Command c)
    {
        if (Reshapes(c)) _stale = true;
        else if (c is LayerCommand || ClockPanel.Moves(c)) _hereStale = true;
    }

    bool Reshapes(Command c) => c switch
    {
        AddSheet or RemoveSheet or MoveSheet or RenameSheet or CropSheet or LayBuilt or SetBuilt or RemoveBuilt or SetPlace or RemovePlace => true,
        RestoreTurn or LoadSave or SpawnPiece or RestorePiece or DeletePiece or SetDef or SetCalibration => true,
        LayerCommand lc => TouchedPiece.IdOf(lc) is { } id && S.View.Find(lc.Layer, id) is { } p && S.Def(p.Def)?.Tags.Contains(Doors.BuildingTag) == true,
        _ => false,
    };

    public void Tick()
    {
        if (!Panel.Visible) return;
        if (_stale) Rebuild();
        else if (_hereStale || !ReferenceEquals(_bodiesSeen, T.Avatars.Bodies)) RefreshHere();
    }

    List<PlaceBody> Bodies()
    {
        _bodiesSeen = T.Avatars.Bodies;
        return _bodiesSeen.Where(b => b.Piece != null).Select(b => new PlaceBody(b.Piece, b.Name)).ToList();
    }

    public void Rebuild()
    {
        _stale = _hereStale = false;
        _places = PlaceTree.Of(S.View, S.Def, Bodies());
        Fill();
    }

    void Fill()
    {
        _tree.Clear();
        _shown.Clear();
        var root = _tree.CreateItem();
        string q = _find.Text.Trim();
        foreach (var p in _places) Add(root, p, q);
        _empty.Visible = _shown.Count == 0;
        _empty.Text = _places.Count == 0 ? "No places" : "Nothing found";
    }

    static bool Matches(MapPlace p, string q) => q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Inside.Any(i => Matches(i, q));

    void Add(TreeItem parent, MapPlace p, string q)
    {
        if (!Matches(p, q)) return;
        var item = _tree.CreateItem(parent);
        item.SetText(0, p.Name);
        item.SetTooltipText(0, KindName(p.Kind));
        item.SetTextOverrunBehavior(0, TextServer.OverrunBehavior.TrimEllipsis);
        item.SetTextAlignment(1, HorizontalAlignment.Right);
        item.SetMetadata(0, _shown.Count);
        _shown.Add(p);
        ShowHere(item, p);
        foreach (var inner in p.Inside) Add(item, inner, q);
        item.Collapsed = q.Length == 0 && _folded.Contains(p.Key);
    }

    static string KindName(PlaceKind k) => k switch
    {
        PlaceKind.Sheet => "Map sheet",
        PlaceKind.Built => "Built",
        PlaceKind.Building => "Building",
        _ => "Pin",
    };

    void ShowHere(TreeItem item, MapPlace p)
    {
        string here = p.Here.Count == 0 ? "" : p.Here.Count <= NamedUpTo ? string.Join(", ", p.Here) : $"{p.Here.Count} players";
        string time = S.View.Clock.Apart is { } apart && apart.TryGetValue(p.Id, out var own) && p.Kind != PlaceKind.Pin ? ClockWords.Short(S.View.Clock.Cal, own.Now) : "";
        item.SetText(1, here.Length > 0 && time.Length > 0 ? $"{here} · {time}" : here + time);
        item.SetTooltipText(1, string.Join(", ", p.Here));
    }

    void RefreshHere()
    {
        _hereStale = false;
        PlaceTree.Count(_places, S.View, Bodies());
        foreach (var item in Items(_tree.GetRoot()))
            if (At(item) is { } p) ShowHere(item, p);
    }

    static IEnumerable<TreeItem> Items(TreeItem? item)
    {
        if (item == null) yield break;
        foreach (var child in item.GetChildren())
        {
            yield return child;
            foreach (var inner in Items(child)) yield return inner;
        }
    }

    MapPlace? At(TreeItem? item) => item != null && item.GetMetadata(0).AsInt32() is var i && i >= 0 && i < _shown.Count ? _shown[i] : null;

    internal TreeItem? Item(string name) => Items(_tree.GetRoot()).FirstOrDefault(i => At(i)?.Name == name);

    void Folded(TreeItem item)
    {
        if (At(item) is not { } p || _find.Text.Trim().Length > 0) return;
        if (item.Collapsed) _folded.Add(p.Key);
        else _folded.Remove(p.Key);
    }

    void Clicked(Vector2 at, long button)
    {
        if (At(_tree.GetSelected()) is not { } p) return;
        if (button == (long)MouseButton.Right) ShowMenu(p, _tree.GetScreenTransform() * at);
        else if (button == (long)MouseButton.Left) Fly(p);
    }

    void Fly(MapPlace p)
    {
        var rig = T.Rig;
        if (rig.Eye != null) rig.LeaveEye();
        if (p.View is { Length: PlacePin.ViewLength } v) rig.Show(new CameraRig.View(new Vector3(v[0], 0, v[1]), v[2], v[3], v[4], v[5] > 0.5f));
        else rig.Show(new CameraRig.View(Coords.ToWorld(p.At), Mathf.Max((float)p.Span * FrameBy, CameraRig.TableMinDistance), rig.Yaw, rig.Pitch, rig.TopDown));
    }

    void ShowMenu(MapPlace p, Vector2 at)
    {
        var menu = new PopupMenu();
        var acts = new List<Action>();
        void Item(string text, Action act)
        {
            menu.AddItem(text, acts.Count);
            acts.Add(act);
        }
        var bodies = T.Avatars.Bodies.Where(b => b.Piece != null).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var b in bodies) Item($"Send {b.Name} here", () => Send(p, [b]));
        if (bodies.Count > 1) Item("Send everyone here", () => Send(p, bodies));
        if (p.Kind == PlaceKind.Pin)
        {
            if (menu.ItemCount > 0) menu.AddSeparator();
            Item("Rename", () => Rename(p));
            Item("Forget", () => S.Do(new RemovePlace { Id = p.Id }));
        }
        if (PlaceClocks.MayKeep(p)) ClockRows(p, menu, acts);
        if (menu.ItemCount == 0)
        {
            menu.QueueFree();
            Say("No player has a body to send");
            return;
        }
        menu.IdPressed += id => acts[(int)id]();
        menu.PopupHide += () => menu.QueueFree();
        Ctx.Root.AddChild(menu);
        menu.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
        Menu = menu;
    }

    void ClockRows(MapPlace p, PopupMenu menu, List<Action> acts)
    {
        var clock = S.View.Clock;
        ClockTime? own = null;
        clock.Apart?.TryGetValue(p.Id, out own);
        if (!S.IsLead && own == null) return;
        if (menu.ItemCount > 0) menu.AddSeparator();
        void Row(string text, Action act, bool? ticked = null)
        {
            if (ticked is { } on)
            {
                menu.AddCheckItem(text, acts.Count);
                menu.SetItemChecked(menu.GetItemIndex(acts.Count), on);
            }
            else menu.AddItem(text, acts.Count);
            acts.Add(act);
        }
        if (S.IsLead) Row("Clock apart", () => S.Do(new SetClockApart { Place = p.Id, Clock = own == null ? PlaceClocks.Started(S.View, S.Def, p) : null }), own != null);
        if (own == null) return;
        Row("Take the table's time", () => S.Do(new SetClock { Seconds = clock.Now, TurnSeconds = own.TurnSeconds, Place = p.Id }));
        Row("Give the table this time", () => S.Do(new SetClock { Turn = clock.Turn, Seconds = own.Now, TurnSeconds = clock.TurnSeconds, Name = clock.Name }));
    }

    void Send(MapPlace p, List<BodyOf> bodies)
    {
        if (PlaceTree.Send(S.View, p, bodies.Select(b => new PlaceBody(b.Piece, b.Name))) is not { } move) return;
        S.Do(move);
        Say($"{string.Join(", ", bodies.Select(b => b.Name))} sent to {p.Name}");
    }

    void Rename(MapPlace p)
    {
        if (S.View.Places?.FirstOrDefault(x => x.Id == p.Id) is not { } pin) return;
        AskName("Rename", pin.Name, name =>
        {
            var renamed = pin.Clone();
            renamed.Name = name;
            S.Do(new SetPlace { Place = renamed });
        });
    }

    void PlaceHere()
    {
        var rig = T.Rig;
        var pos = Coords.FromWorld(rig.Eye ?? rig.Target);
        float[]? view = rig.Eye == null ? [rig.Target.X, rig.Target.Z, rig.Distance, rig.Yaw, rig.Pitch, rig.TopDown ? 1 : 0] : null;
        AskName("Place here", "", name => S.Do(new SetPlace { Place = new PlacePin { Name = name, Sheet = PlaceTree.SheetUnder(S.View, pos), Pos = pos, View = view } }));
    }

    void AskName(string title, string initial, Action<string> done)
    {
        var field = new LineEdit { Text = initial, PlaceholderText = "Name", MaxLength = PlacePin.MostNameLength, CustomMinimumSize = Ui.Px(240, 0) };
        var asked = InPlace.Ask(HudNode, title, title, () => { if (field.Text.Trim().Length > 0) done(field.Text.Trim()); }, null, field, field);
        NameField = field;
        Asking = asked.Shown as ConfirmationDialog;
    }
}

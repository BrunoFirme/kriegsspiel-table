using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class BoundsPanel : HudPart
{
    public const string Key = "bounds";
    const float RowHeight = 24, EyeWide = Ui.RowIconSide, NameWide = 96, CellWide = 52, CountWide = 18, CrossWide = Ui.RowIconSide;
    const string Line = "Pick a layer, then drag round the place on the table · release lays it · Shift+click sets exact corners, a plain click or the start closes it";

    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;
    BoundEditor Edit => T.Bounding;

    public BoundsPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel(Key, "Bounds");
        _box = Panel.Body;
        Panel.VisibilityChanged += () =>
        {
            Edit.Open = Panel.Visible;
            if (Panel.Visible)
            {
                Say(Line);
                Rebuild();
            }
        };
    }

    public void Rebuild()
    {
        if (!GodotObject.IsInstanceValid(_box) || !Panel.Visible) return;
        foreach (var c in _box.GetChildren())
        {
            _box.RemoveChild(c);
            c.QueueFree();
        }
        _box.AddChild(Header("Layers"));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        var players = Players();
        rows.AddChild(Heads(players));
        var layers = S.View.BoundLayers ?? [];
        for (int i = 0; i < layers.Count; i++) rows.AddChild(Row(layers[i], i, players));
        if (layers.Count == 0) rows.AddChild(Hint("No layers"));
        _box.AddChild(rows);
        _box.AddChild(Ui.AddRow("A new layer of bounds", Add, (int)RowHeight));
        if (Edit.Shape is { } shape) Card(shape);
        Panel.Fit();
    }

    List<string> Players() =>
        S.Peers.Where(p => p.Role == Role.Player).Select(p => p.Name).Concat(S.View.PlayerBounds?.Keys ?? Enumerable.Empty<string>())
            .Distinct(System.StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    HBoxContainer Heads(List<string> players)
    {
        var row = Strip();
        row.AddChild(new Control { CustomMinimumSize = Ui.Px(EyeWide + NameWide, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(Head("Everyone", Ui.Ink));
        foreach (var name in players) row.AddChild(Head(name, UmpireHue.Of(S, name) ?? Ui.Ink));
        row.AddChild(new Control { CustomMinimumSize = Ui.Px(CountWide + CrossWide, 0) });
        return row;
    }

    static Label Head(string text, Color colour)
    {
        var head = new Label
        {
            Text = text, ClipText = true, CustomMinimumSize = Ui.Px(CellWide, 0), HorizontalAlignment = HorizontalAlignment.Center,
            TooltipText = text, MouseFilter = Control.MouseFilterEnum.Stop,
        };
        head.AddThemeColorOverride("font_color", colour);
        head.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        return head;
    }

    HBoxContainer Row(BoundLayer layer, int index, List<string> players)
    {
        var row = Strip();
        bool shown = Edit.Shown(layer.Id);
        var eye = Ui.RowIcon(shown ? "eye" : "eye-off", shown ? "Hide on your screen" : "Show it again");
        eye.Pressed += () => Edit.Show(layer.Id, !shown);
        row.AddChild(eye);
        var pick = new Button
        {
            Text = layer.Name, ToggleMode = true, ButtonPressed = Edit.Layer == layer.Id, ClipText = true, FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = Ui.Px(NameWide, RowHeight), TooltipText = "Draw on this layer · double-click to rename",
        };
        if (shown)
            foreach (var ink in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
                pick.AddThemeColorOverride(ink, BoundShapes.TintOf(index));
        pick.Pressed += () => Edit.Pick(layer.Id);
        pick.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) Rename(layer); };
        row.AddChild(pick);
        row.AddChild(Cell(null, layer));
        foreach (var name in players) row.AddChild(Cell(name, layer));
        var count = new Label
        {
            Text = layer.Shapes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), CustomMinimumSize = Ui.Px(CountWide, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        count.AddThemeColorOverride("font_color", Ui.Muted);
        count.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        row.AddChild(count);
        row.AddChild(Remove(layer));
        return row;
    }

    Button Cell(string? player, BoundLayer layer)
    {
        var set = Bounds.SetFor(S.View, player, layer.Id);
        var shown = set == BoundRule.Same ? Bounds.RuleOf(S.View, null, layer.Id) : set;
        var cell = new Button
        {
            Text = shown.ToString(), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(CellWide, RowHeight), Flat = set == BoundRule.Same,
            TooltipText = (player ?? "Everyone") + (set == BoundRule.Same ? " · same as everyone" : "") + " · click to change, right-click to choose",
        };
        if (set == BoundRule.Same) cell.AddThemeColorOverride("font_color", Ui.Muted);
        cell.Pressed += () => Send(player, layer.Id, Next(player, set));
        cell.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) return;
            Choose(player, layer.Id, cell);
            cell.AcceptEvent();
        };
        return cell;
    }

    static BoundRule Next(string? player, BoundRule now) => now switch
    {
        BoundRule.Same => BoundRule.In,
        BoundRule.In => BoundRule.Out,
        BoundRule.Out => BoundRule.Off,
        _ => player == null ? BoundRule.In : BoundRule.Same,
    };

    void Choose(string? player, string layer, Control under)
    {
        var menu = new PopupMenu();
        var rules = player == null ? new[] { BoundRule.In, BoundRule.Out, BoundRule.Off } : new[] { BoundRule.Same, BoundRule.In, BoundRule.Out, BoundRule.Off };
        foreach (var rule in rules) menu.AddItem(rule == BoundRule.Same ? "Same as everyone" : rule.ToString(), (int)rule);
        menu.IdPressed += id => Send(player, layer, (BoundRule)(int)id);
        menu.PopupHide += menu.QueueFree;
        HudNode.AddChild(menu);
        menu.Popup(new Rect2I((Vector2I)under.GetGlobalMousePosition(), Vector2I.Zero));
    }

    void Send(string? player, string layer, BoundRule rule) => S.Do(new SetBoundRule { Player = player, LayerId = layer, Rule = rule });

    Button Remove(BoundLayer layer)
    {
        var cross = Ui.RowIcon("trash", "Remove this layer");
        cross.Pressed += () =>
        {
            if (layer.Shapes.Count == 0) S.Do(new RemoveBoundLayer { Id = layer.Id });
            else Ui.Confirm(HudNode, $"Remove \"{layer.Name}\"?", "Remove", () => S.Do(new RemoveBoundLayer { Id = layer.Id }));
        };
        return cross;
    }

    void Add()
    {
        int n = (S.View.BoundLayers?.Count ?? 0) + 1;
        S.Do(new SetBoundLayer { Bound = new BoundLayer { Name = $"Bounds {n}" } });
    }

    void Rename(BoundLayer layer)
    {
        var dlg = new ConfirmationDialog { Title = "Rename layer", OkButtonText = "Rename" };
        var edit = new LineEdit { Text = layer.Name, CustomMinimumSize = Ui.Px(240, 0), MaxLength = Bounds.LongestName };
        dlg.AddChild(edit);
        dlg.Confirmed += () =>
        {
            if (edit.Text.Trim().Length == 0 || S.View.BoundLayers?.FirstOrDefault(l => l.Id == layer.Id) is not { } now) return;
            var renamed = now.Clone();
            renamed.Name = edit.Text.Trim();
            S.Do(new SetBoundLayer { Bound = renamed });
        };
        Ui.Open(HudNode, dlg);
        Ui.FreeOnClose(dlg);
        edit.GrabFocus();
        edit.SelectAll();
    }

    void Card(BoundShape shape)
    {
        _box.AddChild(Header("Shape"));
        float reference = Edit.Reference(shape), unit = Edit.Unit(shape);
        var form = Ui.Form(Ui.PanelLabels);
        var floor = Metres((shape.Floor - reference) / unit);
        var ceiling = Metres((shape.Ceiling - reference) / unit);
        void Heights() => Edit.SetHeights(floor.Value, ceiling.Value);
        floor.ValueChanged += _ => Heights();
        ceiling.ValueChanged += _ => Heights();
        form.AddChild(new Label { Text = "Floor" });
        form.AddChild(floor);
        form.AddChild(new Label { Text = "Ceiling" });
        form.AddChild(ceiling);
        Ui.AlignForm(form);
        _box.AddChild(form);
        _box.AddChild(Ui.Fact("On", shape.On != null && Edit.NodeOf(shape) is { } n ? n.Def?.Name ?? "Piece" : "Map", Ui.PanelLabels));
        _box.AddChild(Ui.UnderControls(Btn("Delete shape", Edit.DeleteSelected), Ui.PanelLabels));
    }

    static SpinBox Metres(double value) => new()
    {
        MinValue = -1000, MaxValue = 1000, Step = 0.1, Value = System.Math.Round(value, 1), Suffix = "m", AllowGreater = true, AllowLesser = true,
        CustomMinimumSize = Ui.Px(92, 0), UpdateOnTextChanged = false,
    };

    static HBoxContainer Strip()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        return row;
    }
}

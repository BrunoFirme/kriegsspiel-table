using System;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class ReliefPanel
{
    const int SwatchesToARow = 4, PaintColumns = 8, MostGroundLayersShown = 6, GroundTallyWide = 22;
    const float PaintToolPx = 30, GroundRowHigh = 24;
    const string RubOut = "paint:none";

    static readonly (string Key, string Icon, string Tip)[] PaintTools =
    {
        ("brush", "draw", "Brush"), (ReliefTool.Line, "shape-line", "Line: a band of the Width"), (ReliefTool.Curve, "shape-curve", "Curve: a band of the Width along its bends"),
        ("polygon", "shape-area", "Area: click its corners"), ("rect", "shape-rect", "Rectangle"), ("ellipse", "shape-ellipse", "Ellipse"), ("fill", "fill", "Fill"),
    };

    GroundLayerOps? _ops;
    GroundLayerOps Ops => _ops ??= new GroundLayerOps(S, N, Say);
    VBoxContainer? _groundLayers;
    RowGrip? _groundGrip;
    string _lastGround = "paint:grass";

    void AddPaint()
    {
        if (Cfg.ReliefMode != RubOut) _lastGround = Cfg.ReliefMode;
        AddPaintTools();
        _box.AddChild(Header("Ground"));
        AddGrounds();
        AddPaintRows();
        AddGroundLayers();
    }

    void AddPaintTools()
    {
        bool blocked = S.Relief.PaintBlocked;
        var tools = new GridContainer { Columns = PaintColumns };
        tools.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        tools.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        foreach (var (key, icon, tip) in PaintTools)
            tools.AddChild(PaintTool(icon, tip, Cfg.ReliefPaint == key, blocked, () =>
            {
                Cfg.ReliefPaint = key;
                Take(Cfg.ReliefMode);
            }));
        bool rubbing = Cfg.ReliefMode == RubOut;
        tools.AddChild(PaintTool("erase", "Rub out painted ground on this layer", rubbing, blocked, () => Take(rubbing ? _lastGround : RubOut)));
        _box.AddChild(tools);
        if (blocked) _box.AddChild(Hint(S.Relief.Locked ? "Locked layer" : "Hidden layer"));
    }

    static Button PaintTool(string icon, string tip, bool on, bool off, Action press)
    {
        var b = new Button
        {
            ToggleMode = true, ButtonPressed = on && !off, Disabled = off, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
            CustomMinimumSize = Ui.Px(PaintToolPx, PaintToolPx), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        b.AddChild(new ToolIcon { Kind = icon });
        b.Pressed += press;
        return b;
    }

    void AddGrounds()
    {
        var kinds = GroundKinds.Paints.ToList();
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        for (int i = 0; i < kinds.Count; i += SwatchesToARow)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
            foreach (var kind in kinds.Skip(i).Take(SwatchesToARow))
            {
                var mode = "paint:" + kind;
                var b = GroundChip.Of(GroundKinds.Of(kind) ?? GroundKinds.None, Color.FromHtml(GroundKinds.PaintColor(kind, S.View.Lighting.MapStyle)), Title(kind), Cfg.ReliefMode == mode);
                b.Pressed += () => Take(mode);
                row.AddChild(b);
            }
            rows.AddChild(row);
        }
        _box.AddChild(Ui.Well(rows));
    }

    void AddChoices(GridContainer form)
    {
        if (GroundKinds.Of(Cfg.ReliefMode[6..]) != GroundKinds.Wood) return;
        var forests = GroundKinds.Forests.Select(f => (f.Id, f.Name, ForestTip(f.Id))).ToArray();
        form.AddChild(new Label { Text = "Forest" });
        form.AddChild(Ui.Chips(forests, GroundKinds.Forests[Cfg.ReliefForest].Id, GroundsToARow, id =>
        {
            Cfg.ReliefForest = GroundKinds.ForestNamed(id);
            Cfg.Save();
            Rebuild();
        }));
    }

    static string ForestTip(string id) => id switch
    {
        "regional" => "The trees of the culture and the place",
        "broadleaf" => "Oak, beech and lime",
        "conifer" => "Pine, spruce and fir",
        "mixed" => "Broadleaf and conifer together",
        "birch" => "Birch with some pine",
        _ => "Palms and olives",
    };

    void AddPaintRows()
    {
        var form = Ui.Form(Ui.PanelLabels);
        AddChoices(form);
        if (Cfg.ReliefPaint == "brush")
        {
            form.AddChild(new Label { Text = "Tip" });
            form.AddChild(Ui.Chips(Tips, Cfg.ReliefTip, Tips.Length, tip =>
            {
                Cfg.ReliefTip = tip;
                Take(Cfg.ReliefMode);
            }));
        }
        if (Cfg.ReliefPaint == "brush")
            Row(form, Cfg, "Size", 0.2, 400, 0.1, Cfg.ReliefBrush, x => $"{x:0.#} u", x => Cfg.ReliefBrush = (float)x, "The brush's radius in map units · = and − or Ctrl+wheel size it on the table", exp: true);
        else if (Cfg.ReliefPaint is ReliefTool.Line or ReliefTool.Curve)
            Row(form, Cfg, "Width", Settings.NarrowestBand, Settings.WidestBand, 0.5, Cfg.ReliefBand, x => $"{x:0.#} m", x => Cfg.ReliefBand = (float)x, "The band's width on the ground, in metres", exp: true);
        else if (Cfg.ReliefPaint == "ellipse") AddCircle(form);
        Row(form, Cfg, "Density", 0.05, 1, 0.05, Cfg.ReliefDensity, x => $"{x * 100:0}%", x => Cfg.ReliefDensity = (float)x,
            "How much of the ground takes it, in clumps: low for sparse wood or scattered rock; rubbing out thins what is painted. Going over again adds no more");
        if (GroundKinds.Of(Cfg.ReliefMode[6..]) is { } picked)
        {
            var parameters = GroundKinds.Parameters(picked);
            for (int i = 0; i < parameters.Count; i++)
            {
                int which = i;
                Row(form, Cfg, parameters[i].Name, 0, 100, 5, Cfg.ReliefDetail(picked, which), x => $"{x:0}%", x => Cfg.SetReliefDetail(picked, which, (int)x), parameters[i].Tip);
            }
        }
        Ui.AlignForm(form);
        _box.AddChild(form);
        if (Cfg.ReliefPaint == "fill" && FillOptions.StopAtGrid(Cfg, S.View.Grid) is { } stops) _box.AddChild(stops);
    }

    void AddGroundLayers()
    {
        _box.AddChild(Header("Layers"));
        _groundLayers = new VBoxContainer();
        _groundLayers.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        _box.AddChild(_groundLayers);
        FillGroundLayers();
    }

    public void RebuildGroundLayers()
    {
        if (_groundLayers == null || !GodotObject.IsInstanceValid(_groundLayers) || T == null) return;
        FillGroundLayers();
        Panel?.Fit();
    }

    internal RowGrip? GroundGrip => _groundGrip;

    internal void SyncGroundDrop() => _groundGrip?.Sync();

    void FillGroundLayers()
    {
        foreach (var c in _groundLayers!.GetChildren()) { _groundLayers.RemoveChild(c); c.QueueFree(); }
        var layers = S.View.GroundLayers;
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        _groundGrip = S.IsUmpire && layers.Count > 1
            ? new RowGrip(rows, HudNode, "ground-row:", layers.Select(l => l.Id), 0, "Drag to restack: an upper layer's ground covers a lower's · right-click to move it a step", Ops.Move)
            : null;
        for (int place = 0; place < layers.Count; place++) rows.AddChild(GroundLayerRow(layers[place], place));
        if (layers.Count > MostGroundLayersShown)
        {
            var scroll = new ScrollContainer { CustomMinimumSize = Ui.Px(0, MostGroundLayersShown * (GroundRowHigh + Ui.Gap.Hair)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddChild(rows);
            _groundLayers.AddChild(scroll);
        }
        else _groundLayers.AddChild(rows);
        _groundLayers.AddChild(GroundLayerFoot());
    }

    HBoxContainer GroundLayerRow(GroundLayerDef layer, int place)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        var id = layer.Id;
        var pick = new Button
        {
            Text = layer.Name, ToggleMode = true, ButtonPressed = S.Relief.Layer == id, ClipText = true, Alignment = HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = Ui.Px(84, 22),
            TooltipText = "Paint on this layer · double-click to rename",
        };
        pick.Pressed += () =>
        {
            S.Relief.Layer = id;
            T.RefreshHint();
            Rebuild();
        };
        pick.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) AskGroundLayerName(id, layer.Name);
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) GroundLayerMenu(layer, pick);
        };
        row.AddChild(pick);
        int painted = Ops.Painted(id);
        var tally = new Label
        {
            Text = painted > 0 ? painted.ToString() : "", CustomMinimumSize = Ui.Px(GroundTallyWide, 0), HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Ui.Muted, MouseFilter = painted > 0 ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore,
            TooltipText = painted == 0 ? "" : painted == 1 ? "Painted on 1 sheet" : $"Painted on {painted} sheets",
        };
        tally.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        row.AddChild(tally);
        bool shown = !S.Relief.Hidden(id);
        var eye = Ui.RowIcon(shown ? "eye" : "eye-off", shown ? "Hide this layer's ground (on your screen only)" : "Show it again");
        eye.Pressed += () =>
        {
            S.Relief.Show(id, !shown);
            T.RefreshHint();
            Rebuild();
        };
        row.AddChild(eye);
        var padlock = Ui.RowIcon(layer.Locked ? "lock" : "lock-open", layer.Locked ? "Unlock: its ground can be painted again" : "Lock: its ground cannot be painted or rubbed out");
        padlock.Disabled = !S.IsUmpire;
        padlock.Pressed += () => S.Do(new LockGroundLayer { Id = id, Locked = !layer.Locked });
        row.AddChild(padlock);
        var more = Ui.RowIcon("more", "Rename or remove");
        more.Pressed += () => GroundLayerMenu(layer, more);
        row.AddChild(more);
        if (_groundGrip != null) row.AddChild(_groundGrip.Handle(place, layer.Name));
        _groundGrip?.Takes(row, place);
        return row;
    }

    HBoxContainer GroundLayerFoot()
    {
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        var add = new Button
        {
            FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(0, GroundRowHigh), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "A fresh ground layer over the chosen one", Disabled = !S.IsUmpire,
        };
        add.AddChild(new ToolIcon { Kind = "plus", Color = Ui.Muted });
        add.Pressed += AddGroundLayer;
        foot.AddChild(add);
        var chosen = S.Relief.Chosen;
        var wipe = Ui.RowIcon("erase", $"Rub out all the ground painted on {chosen?.Name ?? "this layer"}");
        wipe.Disabled = !S.IsUmpire || S.Relief.Locked;
        wipe.Pressed += () =>
        {
            if (chosen == null) return;
            int sheets = Ops.Painted(chosen.Id);
            if (sheets == 0) { Log("Nothing painted on this layer."); return; }
            Ui.Confirm(HudNode, $"Rub out all the ground painted on \"{chosen.Name}\"?", "Rub out", () => Ops.Clear(chosen.Id), sheets == 1 ? "On 1 sheet; one undo brings it back." : $"On {sheets} sheets; one undo brings it back.");
        };
        foot.AddChild(wipe);
        return foot;
    }

    void AddGroundLayer()
    {
        var id = "g-" + Guid.NewGuid().ToString("N")[..10];
        int at = Math.Max(0, S.View.GroundLayers.FindIndex(l => l.Id == S.Relief.Layer));
        S.Relief.Layer = id;
        S.Do(new AddGroundLayer { Id = id, Index = at });
    }

    internal enum GroundLayerItem { Rename, Remove }

    void GroundLayerMenu(GroundLayerDef layer, Control under)
    {
        var menu = new PopupMenu();
        menu.AddItem("Rename", (int)GroundLayerItem.Rename);
        menu.AddSeparator();
        menu.AddItem("Remove", (int)GroundLayerItem.Remove);
        menu.SetItemDisabled(menu.GetItemIndex((int)GroundLayerItem.Remove), S.View.GroundLayers.Count == 1);
        menu.IdPressed += item => GroundLayerDo(layer.Id, (GroundLayerItem)(int)item);
        Ui.PopupBelow(HudNode, menu, under);
    }

    internal void GroundLayerDo(string id, GroundLayerItem item)
    {
        if (S.View.GroundLayers.FirstOrDefault(l => l.Id == id) is not { } layer) return;
        if (item == GroundLayerItem.Rename)
        {
            AskGroundLayerName(id, layer.Name);
            return;
        }
        if (S.View.GroundLayers.Count == 1) return;
        int sheets = Ops.Painted(id);
        Ui.Confirm(HudNode, $"Remove \"{layer.Name}\"?", "Remove", () => Ops.Remove(id), sheets == 0 ? null : sheets == 1 ? "The ground painted on it goes too, on 1 sheet." : $"The ground painted on it goes too, on {sheets} sheets.");
    }

    void AskGroundLayerName(string id, string was)
    {
        var dlg = new ConfirmationDialog { Title = "Rename layer", OkButtonText = "Rename" };
        var edit = new LineEdit { Text = was, CustomMinimumSize = Ui.Px(240, 0), MaxLength = GroundLayerEdits.LongestName };
        dlg.AddChild(edit);
        dlg.Confirmed += () => { if (edit.Text.Trim().Length > 0) S.Do(new RenameGroundLayer { Id = id, Name = edit.Text.Trim() }); };
        Ui.Open(HudNode, dlg);
        Ui.FreeOnClose(dlg);
        edit.GrabFocus();
        edit.SelectAll();
    }
}

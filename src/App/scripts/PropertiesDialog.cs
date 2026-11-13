using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PropertiesDialog : HudPart
{
    readonly PieceNode _node;
    readonly PieceInstance _p;
    readonly PieceDef? _def;
    readonly string _layer;
    readonly AcceptDialog _dlg;
    readonly VBoxContainer _box;

    PropertiesDialog(HudContext c, PieceNode node) : base(c)
    {
        _node = node;
        _p = node.Piece;
        _def = node.Def;
        _layer = node.Layer;
        _dlg = new AcceptDialog { Title = _def?.Name ?? _p.Def, OkButtonText = "Done", Exclusive = false };
        _box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
    }

    public static void Open(HudContext c, PieceNode node) => new PropertiesDialog(c, node).Show();

    void Show()
    {
        var p = _p;
        var layer = _layer;
        _box.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _dlg.AddChild(_box);
        _box.AddChild(Hint($"{(layer == Layers.Umpire ? "umpire board" : layer)} · at {p.Pos.X:0.0}, {p.Pos.Z:0.0} · {p.RotDeg:0}°"));
        AddFields();
        AddSize();
        AddTint();
        AddAttachments();
        if (S.IsUmpire && layer == Layers.Umpire) AddForScripts();
        Ui.Open(HudNode, _dlg, new Vector2I(460, 80));
        Ui.FreeOnClose(_dlg);
    }

    void AddFields()
    {
        var p = _p;
        var def = _def;
        var layer = _layer;
        var dlg = _dlg;
        _box.AddChild(Header("Text"));
        var grid = Ui.Form(Ui.DialogLabels);
        _box.AddChild(grid);
        grid.AddChild(new Label { Text = "Label" });
        var label = new LineEdit { Text = p.Label ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.TextSubmitted += v => S.Do(new SetLabel { Layer = layer, Id = p.Id, Value = v });
        label.FocusExited += () => { if (label.Text != (p.Label ?? "")) S.Do(new SetLabel { Layer = layer, Id = p.Id, Value = label.Text }); };
        grid.AddChild(label);
        grid.AddChild(new Label { Text = "Description" });
        var note = new TextEdit
        {
            Text = p.Note ?? "", PlaceholderText = "Shown on hover to whoever sees the piece, never printed on it", CustomMinimumSize = new Vector2(0, 70),
            WrapMode = TextEdit.LineWrappingMode.Boundary, ScrollFitContentHeight = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var sentNote = p.Note ?? "";
        void CommitNote() { if (GodotObject.IsInstanceValid(note) && note.Text != sentNote) { sentNote = note.Text; S.Do(new SetNote { Layer = layer, Id = p.Id, Value = note.Text }); } }
        note.FocusExited += CommitNote;
        dlg.VisibilityChanged += () => { if (!dlg.Visible) CommitNote(); };
        grid.AddChild(note);
        var textNames = (def?.TextLines.Select(t => t.Name) ?? Enumerable.Empty<string>()).Concat(p.Text.Keys).Distinct().ToList();
        foreach (var tn in textNames)
        {
            grid.AddChild(new Label { Text = tn });
            var le = new LineEdit { Text = p.Text.GetValueOrDefault(tn) ?? def?.TextLines.FirstOrDefault(t => t.Name == tn)?.Default ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var name = tn;
            le.TextSubmitted += v => S.Do(new SetText { Layer = layer, Id = p.Id, Name = name, Value = v });
            le.FocusExited += () => { if (le.Text != (p.Text.GetValueOrDefault(name) ?? "")) S.Do(new SetText { Layer = layer, Id = p.Id, Name = name, Value = le.Text }); };
            grid.AddChild(le);
        }
        Ui.AlignForm(grid);
    }

    void AddSize()
    {
        _box.AddChild(Header("Size and tint"));
        var tg = Ui.Form(Ui.DialogLabels, 3);
        var cur = T.Current(_node);
        double lo = Math.Log(Table.MinScale), hi = Math.Log(Table.MaxScale);
        var scS =
            new HSlider { MinValue = lo, MaxValue = hi, Step = 0.01, Value = Math.Clamp(Math.Log(cur.scale), lo, hi), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(80, 0) };
        var scN = new SpinBox { MinValue = Table.MinScale, MaxValue = Table.MaxScale, Step = 0.01, Value = cur.scale, Prefix = "×", CustomMinimumSize = new Vector2(92, 0) };
        bool sync = false;
        scS.ValueChanged += v =>
        {
            if (sync) return;
            sync = true;
            scN.Value = Math.Exp(v);
            sync = false;
            T.SetSelectionTransform(null, Math.Exp(v));
        };
        scN.ValueChanged += v =>
        {
            if (sync) return;
            sync = true;
            scS.Value = Math.Log(v);
            sync = false;
            T.SetSelectionTransform(null, v);
        };
        tg.AddChild(new Label { Text = "Size" });
        tg.AddChild(scS);
        tg.AddChild(scN);
        Ui.AlignForm(tg);
        _box.AddChild(tg);
    }

    const int TintSwatch = 22;

    void AddTint()
    {
        var p = _p;
        var layer = _layer;
        var tg = Ui.Form(Ui.DialogLabels);
        var tintRow = new HBoxContainer();
        tintRow.AddThemeConstantOverride("separation", Ui.Gap.Row);
        var now = Coords.ParseColor(p.TintOverride ?? _def?.Tint, Colors.White);
        foreach (var (name, hex) in ColorChoices.All)
        {
            var colour = Coords.ParseColor(hex, Colors.White);
            var choice = Ui.Swatch(colour, new Vector2(TintSwatch, TintSwatch), name, colour.IsEqualApprox(now));
            choice.Pressed += () => S.Do(new SetTint { Layer = layer, Id = p.Id, Value = hex });
            tintRow.AddChild(choice);
        }
        var cp = new ColorPickerButton { Color = now, CustomMinimumSize = new Vector2(44, 26), EditAlpha = false, TooltipText = "Any colour" };
        Ui.WhenPicked(cp, c => S.Do(new SetTint { Layer = layer, Id = p.Id, Value = "#" + c.ToHtml(false) }));
        tintRow.AddChild(cp);
        var reset = Btn("Reset", () => S.Do(new SetTint { Layer = layer, Id = p.Id, Value = null }), "Back to the tint the piece was made with");
        Ui.MakeQuiet(reset);
        tintRow.AddChild(reset);
        tg.AddChild(new Label { Text = "Tint" });
        tg.AddChild(tintRow);
        Ui.AlignForm(tg);
        _box.AddChild(tg);
    }

    void AddAttachments()
    {
        var p = _p;
        var layer = _layer;
        _box.AddChild(Header("Attached pieces"));
        if (p.Attached.Count == 0) _box.AddChild(Hint("None"));
        foreach (var cid in p.Attached)
        {
            var ch = T.Find(layer, cid);
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = ch != null ? (S.Def(ch.Def)?.Name ?? ch.Def) : cid, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true });
            var c2 = cid;
            if (p.ReadyNow?.Id != cid) row.AddChild(Btn("Detach", () => S.Do(new Detach { Layer = layer, Child = c2 })));
            _box.AddChild(row);
        }
    }

    void AddForScripts()
    {
        var p = _p;
        var def = _def;
        var dlg = _dlg;
        _box.AddChild(Header("Scripting"));
        var code = p.Script != null ? "its own" : def?.Script != null ? "its pack's" : "none";
        var facts = Ui.Form(Ui.DialogLabels);
        void Fact(string name, string value)
        {
            facts.AddChild(new Label { Text = name });
            facts.AddChild(new Label { Text = value, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = value, MouseFilter = Control.MouseFilterEnum.Pass });
        }
        Fact("Id", p.Id);
        if (p.Guid != null && p.Guid != p.Id) Fact("Module GUID", p.Guid);
        Fact("Kind", p.Def);
        if (S.Store.PackOfDef(p.Def)?.Id is { } pk) Fact("Pack", pk);
        Fact("Script", code);
        if ((p.Tags ?? def?.Tags) is { Count: > 0 } tags) Fact("Tags", string.Join(", ", tags));
        Ui.AlignForm(facts);
        _box.AddChild(facts);
        var sr = new HBoxContainer();
        sr.AddThemeConstantOverride("separation", Ui.Gap.Row);
        sr.AddChild(Btn("Copy id", () => AppClipboard.SetText(p.Id), "For board.find(\"…\") in a script"));
        if (p.Guid != null && p.Guid != p.Id) { var g = p.Guid; sr.AddChild(Btn("Copy GUID", () => AppClipboard.SetText(g), "The GUID its module gave it; board.find takes it too")); }
        sr.AddChild(Btn("Scripting…", () => { dlg.Hide(); Ctx.ShowScriptWindow(p.Id); }, "This piece's script in the scripts window"));
        _box.AddChild(sr);
    }
}

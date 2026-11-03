using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

internal sealed class StudioLayersPanel
{
    readonly PieceStudio _s;
    VBoxContainer _body = new();

    public StudioLayersPanel(PieceStudio s) => _s = s;

    StudioCanvas Canvas => _s.Canvas;

    public VBoxContainer Body { set => _body = value; }

    public void Rebuild()
    {
        if (_body == null || !GodotObject.IsInstanceValid(_body) || !_body.IsInsideTree()) return;
        PieceStudio.Clear(_body);
        if (_s.View == "model" && _s.Model != null) { _s.Model.Panels.BuildParts(_s, _body); _s.ToolPanel.Refit(); return; }
        if (_s.View != "paint" || !_s.Faces.CanvasOpen) return;
        var doc = Canvas.Doc;
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        for (int i = doc.Layers.Count - 1; i >= 0; i--) list.AddChild(LayerRow(doc.Layers[i]));
        if (doc.Layers.Count == 0) list.AddChild(Ui.Hint("No layers: add one below."));
        _body.AddChild(list);

        var add = new HBoxContainer();
        add.AddThemeConstantOverride("separation", 3);
        Button Ico(HBoxContainer row, string icon, string tip, Action a, bool enabled = true)
        {
            var b = new Button { CustomMinimumSize = new Vector2(30, 28), TooltipText = tip, FocusMode = Control.FocusModeEnum.None, Disabled = !enabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddChild(new StudioIcon { Kind = icon });
            b.Pressed += a;
            row.AddChild(b);
            return b;
        }
        Ico(add, "paint-layer", "A new paint layer", () => { Canvas.AddLayer(new ArtLayer { Name = Canvas.NextName("Paint"), Kind = "paint" }); Canvas.Commit("New layer"); if (Canvas.Tool is "move" or "crop") Canvas.SetTool("brush"); });
        Ico(add, "image", "Pictures from files (PNG, JPEG, WebP, SVG, BMP, TGA, TIFF) — or paste one with Ctrl+V, or drop files on the face", () => _s.PickFiles(ImageFiles.Filter, files => { foreach (var f in files) AddFromFile(f); Canvas.Commit("Pictures"); }, "Pictures as layers"));
        Ico(add, "text-layer", "A line of text", () =>
        {
            var l = new ArtLayer { Kind = "text", Name = Canvas.NextName("Text"), Text = "Text", X = doc.W / 2f, Y = doc.H / 2f, Font = _s.Prefs.TextFont, Bold = _s.Prefs.TextBold, Size = _s.Prefs.TextSize, Color = Hex.Of(Canvas.Ink) };
            Canvas.AddLayer(l); Canvas.Commit("Text"); Canvas.SetTool("text"); _s.ToolPanel.FocusTextEdit();
        });
        Ico(add, "block-layer", "A unit plate: its shape, colours, label and unit symbol", () =>
        {
            var l = new ArtLayer { Kind = "block", Name = Canvas.NextName("Plate"), Block = new BlockSpec { BW = doc.W, BH = doc.H, Label = _s.Sel?.Name ?? "", Symbol = "infantry" } };
            Canvas.AddLayer(l, 0); Canvas.Commit("Unit plate");
        });
        _body.AddChild(add);
        var sel = Canvas.Sel;
        int at = sel != null ? doc.Layers.IndexOf(sel) : -1;
        var ops = new HBoxContainer();
        ops.AddThemeConstantOverride("separation", 3);
        Ico(ops, "dup", "Duplicate   (Ctrl+D)", Duplicate, sel != null);
        Ico(ops, "up", "Up one", () => MoveLayer(1), sel != null && at < doc.Layers.Count - 1);
        Ico(ops, "down", "Down one", () => MoveLayer(-1), sel != null && at > 0);
        Ico(ops, "merge", "Merge into the layer below", () => _ = MergeDown(), sel != null && at > 0);
        Ico(ops, "trash", "Delete the layer", Delete, sel != null);
        _body.AddChild(ops);

        if (sel != null) LayerSettings(sel);
        FaceSettings();
        _s.ToolPanel.Refit();
    }

    Control LayerRow(ArtLayer l)
    {
        bool on = Canvas.Doc.Selected == l.Id;
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = l.Kind switch { "image" => "A picture", "text" => "Text", "block" => "A unit plate", _ => "Paint" } + " · click to pick, double-click to rename" };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(on ? new Color(1, 1, 1, 0.13f) : new Color(1, 1, 1, 0.03f), 3, 4, 2));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        panel.AddChild(row);
        var eye = new Button { Flat = true, CustomMinimumSize = new Vector2(24, 24), FocusMode = Control.FocusModeEnum.None, TooltipText = l.Visible ? "Shown: click to hide" : "Hidden: click to show" };
        eye.AddChild(new StudioIcon { Kind = l.Visible ? "eye" : "eye-off", Color = l.Visible ? Ui.Ink : Ui.Muted });
        eye.Pressed += () => { l.Visible = !l.Visible; Canvas.Redraw(); Canvas.Commit(l.Visible ? "Show layer" : "Hide layer"); Rebuild(); };
        row.AddChild(eye);
        var lk = new Button { Flat = true, CustomMinimumSize = new Vector2(22, 24), FocusMode = Control.FocusModeEnum.None, TooltipText = l.Locked ? "Locked: nothing can change it. Click to unlock" : "Click to lock it against changes" };
        lk.AddChild(new StudioIcon { Kind = l.Locked ? "lock" : "unlock", Color = l.Locked ? Ui.Ink : Ui.Faint });
        lk.Pressed += () => { l.Locked = !l.Locked; Canvas.Commit(l.Locked ? "Lock layer" : "Unlock layer"); Rebuild(); };
        row.AddChild(lk);
        var thumbBg = new PanelContainer { CustomMinimumSize = new Vector2(44, 28) };
        thumbBg.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0.3f, 0.3f, 0.32f), 1, 0, 0));
        var thumb = new TextureRect { Texture = Canvas.R.LayerTexture(l.Id), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha } };
        thumbBg.AddChild(thumb);
        thumbBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(thumbBg);
        var name = new Label { Text = l.Name, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(60, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (!l.Visible) name.Modulate = Ui.Muted;
        row.AddChild(name);
        if (l.Opacity < 0.999f) row.AddChild(new Label { Text = $"{l.Opacity * 100:0}%", Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        panel.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb) return;
            if (mb.DoubleClick) AskLayerName(l);
            else if (Canvas.Doc.Selected != l.Id) { Canvas.Select(l.Id); _s.RebuildToolPanel(); }
            panel.AcceptEvent();
        };
        return panel;
    }

    void AskLayerName(ArtLayer l)
    {
        var dlg = new ConfirmationDialog { Title = "Rename layer", OkButtonText = "Rename" };
        var edit = new LineEdit { Text = l.Name, CustomMinimumSize = new Vector2(240, 0) };
        dlg.AddChild(edit);
        void Done() { if (edit.Text.Trim().Length > 0) { l.Name = edit.Text.Trim(); Canvas.Commit("Rename layer"); Rebuild(); } }
        dlg.Confirmed += Done;
        edit.TextSubmitted += _ => { Done(); dlg.Hide(); };
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
        edit.GrabFocus(); edit.SelectAll();
    }

    void MoveLayer(int dir)
    {
        if (Canvas.Sel is not { } l) return;
        if (!Canvas.Doc.Swap(l, dir)) return;
        Canvas.Redraw();
        Canvas.Commit(dir > 0 ? "Layer up" : "Layer down");
        Rebuild();
    }

    async Task MergeDown()
    {
        if (Canvas.Sel is not { } top) return;
        var ls = Canvas.Doc.Layers;
        int i = ls.IndexOf(top);
        if (i <= 0) return;
        var below = ls[i - 1];
        var a = await Canvas.R.Flatten(cut: false, only: below.Id);
        var b = await Canvas.R.Flatten(cut: false, only: top.Id);
        int w = a.GetWidth(), h = a.GetHeight();
        var da = a.GetData(); var db = b.GetData();
        float oa = below.Visible ? below.Opacity : 0, ob = top.Visible ? top.Opacity : 0;
        var o = LayerPixels.Over(da, oa, db, ob);
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, o);
        var src = ArtStore.AddSrc(_s.Store, _s.Pack.Id, img);
        Canvas.R.Remember(src, img);
        var merged = new ArtLayer { Name = below.Name, Kind = "image", Src = src, SrcW = w, SrcH = h, X = w / 2f, Y = h / 2f };
        Canvas.Doc.ReplacePair(i, merged);
        Canvas.R.Sync();
        Canvas.Select(merged.Id);
        Canvas.Commit("Merge down");
    }

    void LayerSettings(ArtLayer l)
    {
        var into = Inspector.Group(_body, l.Name);
        var op = new HBoxContainer();
        op.AddChild(new Label { Text = "Opacity", CustomMinimumSize = new Vector2(56, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var s = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = Math.Round(l.Opacity * 100), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var v = new Label { Text = $"{l.Opacity * 100:0}%", CustomMinimumSize = new Vector2(36, 0), HorizontalAlignment = HorizontalAlignment.Right };
        s.ValueChanged += x => { l.Opacity = (float)x / 100; v.Text = $"{x:0}%"; Canvas.Redraw(); _s.Faces.CommitSoon("Opacity"); };
        s.DragEnded += _ => Canvas.Commit("Opacity");
        op.AddChild(s); op.AddChild(v);
        into.AddChild(op);
        switch (l.Kind)
        {
            case "image":
            {
                var row = new HBoxContainer();
                row.AddChild(Ui.Btn("Mirror ⟷", () => { l.Sx = -l.Sx; Canvas.R.Moved(l); Canvas.Commit("Mirror"); }));
                row.AddChild(Ui.Btn("Mirror ↕", () => { l.Sy = -l.Sy; Canvas.R.Moved(l); Canvas.Commit("Mirror"); }));
                row.AddChild(Ui.Btn("1 : 1", () => { l.Sx = Mathf.Sign(l.Sx); l.Sy = Mathf.Sign(l.Sy); Canvas.R.Moved(l); Canvas.Commit("Real size"); }, "One picture pixel to one face pixel"));
                into.AddChild(row);
                var row2 = new HBoxContainer();
                row2.AddChild(Ui.Btn("Remove the background", () => KeyOut(l), "Clears the colour around the edges of the picture (a scan's paper, a photo's backdrop). Tolerance is the Fill tool's."));
                row2.AddChild(Ui.Btn("Uncrop", () => { l.Crop = null; Canvas.Refresh(l); Canvas.Commit("Uncrop"); }));
                into.AddChild(row2);
                into.AddChild(Ui.Hint($"{l.SrcW} × {l.SrcH} px picture" + (l.Crop is { } c ? $", cropped to {c[2]:0} × {c[3]:0}" : "") + ". Crop it with the Crop tool (C); paint on it, or rub parts of it out, with the layer selected."));
                break;
            }
            case "block" when l.Block is { } b:
                BlockSettings(into, l, b);
                break;
            case "text":
                into.AddChild(Ui.Btn("Edit the words…", () => { Canvas.SetTool("text"); _s.ToolPanel.FocusTextEdit(); }));
                break;
            default:
                into.AddChild(Ui.Hint($"{l.Ops.Count} stroke{(l.Ops.Count == 1 ? "" : "s")} and shape{(l.Ops.Count == 1 ? "" : "s")}."));
                if (l.Ops.Count > 0) into.AddChild(Ui.Btn("Clear this layer", () => { l.Ops.Clear(); Canvas.Refresh(l); Canvas.Commit("Clear layer"); Rebuild(); }));
                break;
        }
    }

    void BlockSettings(VBoxContainer into, ArtLayer l, BlockSpec b)
    {
        void Changed(string what) { Canvas.Refresh(l); Canvas.Commit(what); }
        var g = Ui.Form(76);
        g.AddChild(new Label { Text = "Plate" });
        var shape = new OptionButton { FitToLongestItem = false, TooltipText = "The plate's own shape on the face; the piece's outline is set on its Block layer in 3D" };
        foreach (var (n, id) in new[] { ("Rectangle", "box"), ("Rounded", "rounded"), ("Round", "circle"), ("Hex", "hex") }) { shape.AddItem(n); shape.SetItemMetadata(shape.ItemCount - 1, id); if (id == b.Shape) shape.Selected = shape.ItemCount - 1; }
        shape.ItemSelected += i => { b.Shape = shape.GetItemMetadata((int)i).AsString(); Changed("Plate shape"); };
        g.AddChild(shape);
        g.AddChild(new Label { Text = "Fill" });
        g.AddChild(ColourBtn(b.Fill, c => { b.Fill = c; Changed("Block fill"); }));
        g.AddChild(new Label { Text = "Border" });
        var br = new HBoxContainer();
        br.AddChild(ColourBtn(b.Border, c => { b.Border = c; Changed("Border"); }));
        var bw = new SpinBox { MinValue = 0, MaxValue = 120, Value = b.BorderPx, Suffix = "px", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bw.ValueChanged += x => { b.BorderPx = (float)x; Changed("Border"); };
        br.AddChild(bw);
        g.AddChild(br);
        g.AddChild(new Label { Text = "Label" });
        var lab = new LineEdit { Text = b.Label, PlaceholderText = "e.g. 2/IR 14" };
        lab.TextChanged += t => { b.Label = t; Canvas.Refresh(l); _s.Faces.CommitSoon("Label"); };
        lab.FocusExited += () => Canvas.Commit("Label");
        lab.TextSubmitted += _ => Canvas.Commit("Label");
        g.AddChild(lab);
        g.AddChild(new Label { Text = "Lettering" });
        var lf = new HBoxContainer();
        var font = new OptionButton { FitToLongestItem = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var (n, id) in new[] { ("Bold", "bold"), ("Serif", "serif"), ("Plain", "sans") }) { font.AddItem(n); font.SetItemMetadata(font.ItemCount - 1, id); if (id == b.Font) font.Selected = font.ItemCount - 1; }
        font.ItemSelected += i => { b.Font = font.GetItemMetadata((int)i).AsString(); Changed("Lettering"); };
        lf.AddChild(font);
        lf.AddChild(ColourBtn(b.LabelColor, c => { b.LabelColor = c; Changed("Label colour"); }));
        g.AddChild(lf);
        g.AddChild(new Label { Text = "Symbol" });
        var sym = new OptionButton { FitToLongestItem = false };
        foreach (var (n, id) in new[] { ("None", "none"), ("Infantry", "infantry"), ("Cavalry", "cavalry"), ("Artillery", "artillery"), ("Horse artillery", "horse-artillery"), ("Engineers", "engineers"), ("Headquarters", "hq"), ("Text…", "text") })
        { sym.AddItem(n); sym.SetItemMetadata(sym.ItemCount - 1, id); if (id == b.Symbol) sym.Selected = sym.ItemCount - 1; }
        sym.ItemSelected += i => { b.Symbol = sym.GetItemMetadata((int)i).AsString(); Changed("Symbol"); };
        g.AddChild(sym);
        g.AddChild(new Label { Text = "Symbol text" });
        var st = new HBoxContainer();
        var symText = new LineEdit { Text = b.SymbolText, PlaceholderText = "for Text…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        symText.TextChanged += t => { b.SymbolText = t; if (b.Symbol != "text") { b.Symbol = "text"; sym.Selected = sym.ItemCount - 1; } Canvas.Refresh(l); _s.Faces.CommitSoon("Symbol text"); };
        symText.FocusExited += () => Canvas.Commit("Symbol text");
        st.AddChild(symText);
        st.AddChild(ColourBtn(b.SymbolColor, c => { b.SymbolColor = c; Changed("Symbol colour"); }));
        g.AddChild(st);
        g.AddChild(new Label { Text = "Symbol size" });
        var ss = new HSlider { MinValue = 0.3, MaxValue = 1.8, Step = 0.02, Value = b.SymbolScale };
        ss.ValueChanged += x => { b.SymbolScale = (float)x; Canvas.Refresh(l); _s.Faces.CommitSoon("Symbol size"); };
        ss.DragEnded += _ => Canvas.Commit("Symbol size");
        g.AddChild(ss);
        Ui.AlignForm(g);
        into.AddChild(g);
        into.AddChild(Ui.Btn("Stretch it over the face", () => { b.BW = Canvas.Doc.W; b.BH = Canvas.Doc.H; l.X = l.Y = 0; l.Rot = 0; l.Sx = l.Sy = 1; Changed("Plate fits the face"); }));
    }

    ColorPickerButton ColourBtn(string hex, Action<string> set)
    {
        var b = new ColorPickerButton { Color = Hex.To(hex, Colors.White), EditAlpha = true, CustomMinimumSize = new Vector2(44, 24) };
        b.PopupClosed += () => set(Hex.Of(b.Color));
        return b;
    }

    void FaceSettings()
    {
        var into = Inspector.Group(_body, "The face");
        var row = new HBoxContainer();
        var see = new CheckBox { Text = "See-through background", ButtonPressed = Canvas.Doc.Bg == null, TooltipText = "Off: the whole picture is filled with a colour first" };
        var bg = new ColorPickerButton { Color = Hex.To(Canvas.Doc.Bg, new Color(0.85f, 0.81f, 0.71f)), EditAlpha = false, CustomMinimumSize = new Vector2(44, 24), Disabled = Canvas.Doc.Bg == null };
        see.Toggled += on => { Canvas.Doc.Bg = on ? null : Hex.Of(bg.Color); bg.Disabled = on; Canvas.Redraw(); Canvas.Commit("Background"); };
        bg.PopupClosed += () => { if (Canvas.Doc.Bg != null) { Canvas.Doc.Bg = Hex.Of(bg.Color); Canvas.Redraw(); Canvas.Commit("Background"); } };
        row.AddChild(see); row.AddChild(bg);
        into.AddChild(row);
    }

    void KeyOut(ArtLayer l)
    {
        if (l.Src == null || Canvas.R.Img(l.Src) is not { } src) return;
        var img = (Image)src.Duplicate();
        int w = img.GetWidth(), h = img.GetHeight();
        var d = img.GetData();
        int n = LayerPixels.KeyOut(d, w, h, _s.Prefs.FillTolerance);
        if (n == 0) { _s.Flash("Nothing round the edges matched the corners' colour."); return; }
        img.SetData(w, h, false, Image.Format.Rgba8, d);
        var name = ArtStore.AddSrc(_s.Store, _s.Pack.Id, img);
        Canvas.R.Remember(name, img);
        l.Src = name;
        Canvas.Refresh(l);
        Canvas.Commit("Remove the background");
        _s.Flash($"Cleared {n * 100 / Math.Max(1, w * h)}% of the picture.");
    }

    string? _layerClip;
    string? _clipMark;

    public void Copy()
    {
        if (Canvas.Sel is not { } l) return;
        _layerClip = System.Text.Json.JsonSerializer.Serialize(l);
        _clipMark = null;
        _ = CopyLayerPicture(l);
    }

    async Task CopyLayerPicture(ArtLayer l)
    {
        var img = await Canvas.R.Flatten(cut: false, only: l.Id);
        var used = img.GetUsedRect();
        if (used.Size.X > 0 && used.Size.Y > 0) img = img.GetRegion(used);
        var err = AppClipboard.SetImage(img);
        _clipMark = ClipboardMark();
        _s.Flash(err == null ? $"Copied \"{l.Name}\" (Ctrl+V pastes it here or in any other program)." : $"Copied \"{l.Name}\" here; the system clipboard said: {err}");
    }

    public void Paste()
    {
        if (_layerClip != null && System.Text.Json.JsonSerializer.Deserialize<ArtLayer>(_layerClip) is { } l && !ClipboardChangedSinceCopy())
        {
            Canvas.AddLayer(Canvas.Doc.TakeIn(l, 12));
            Canvas.Commit("Paste layer");
            Canvas.SetTool("move");
            return;
        }
        if (ImageFiles.FromClipboard() is { } img)
        {
            Canvas.AddImage(img, Canvas.NextName("Pasted"));
            Canvas.Commit("Paste picture");
            _layerClip = null;
        }
        else _s.Flash("Nothing to paste: copy a picture or a layer first.");
    }

    bool ClipboardChangedSinceCopy() => _clipMark != null && ClipboardMark() is { Length: > 0 } now && now != _clipMark;

    static string ClipboardMark()
    {
        if (!AppClipboard.HasImage()) return "";
        var img = AppClipboard.GetImage();
        if (img == null || img.IsEmpty()) return "";
        return $"{img.GetWidth()}x{img.GetHeight()}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(img.GetData()))}";
    }

    public void Duplicate()
    {
        if (Canvas.Sel is not { } l) return;
        var c = System.Text.Json.JsonSerializer.Deserialize<ArtLayer>(System.Text.Json.JsonSerializer.Serialize(l))!;
        Canvas.AddLayer(Canvas.Doc.TakeIn(c, 10));
        Canvas.Commit("Duplicate layer");
    }

    public void Delete()
    {
        if (Canvas.Sel is not { } l) return;
        var next = Canvas.Doc.Remove(l);
        Canvas.R.Sync();
        Canvas.Select(next);
        Canvas.Commit("Delete layer");
    }

    public void AddFromFile(string path, Vector2? at = null)
    {
        var src = ArtStore.AddSrcFile(_s.Store, _s.Pack.Id, path, out var img);
        if (src == null || img == null) { _s.Flash("Could not read " + Path.GetFileName(path)); return; }
        Canvas.R.Remember(src, img);
        float k = Mathf.Min(1, Mathf.Min(Canvas.Doc.W * 0.96f / img.GetWidth(), Canvas.Doc.H * 0.96f / img.GetHeight()));
        var c = at ?? new Vector2(Canvas.Doc.W, Canvas.Doc.H) / 2;
        var l = new ArtLayer { Name = Canvas.NextName(NewPieces.Pretty(Path.GetFileNameWithoutExtension(path))), Kind = "image", Src = src, SrcW = img.GetWidth(), SrcH = img.GetHeight(), X = c.X, Y = c.Y, Sx = k, Sy = k };
        Canvas.AddLayer(l);
        Canvas.SetTool("move");
    }
}

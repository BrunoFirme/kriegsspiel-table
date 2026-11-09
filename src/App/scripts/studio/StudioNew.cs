using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using KTable.Client;
using KTable.Client.Effects;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class NewPieces
{
    readonly PieceStudio _s;

    public NewPieces(PieceStudio s) => _s = s;

    PackageStore Store => _s.Store;
    PackManifest Pack => _s.Pack;

    public void NewPiece(int kind)
    {
        switch (kind)
        {
            case 1:
            {
                var doc = new ArtDoc { W = 512, H = 256 };
                doc.Layers.Add(new ArtLayer { Name = "Plate", Kind = "block", Block = new BlockSpec { BW = 512, BH = 256, Label = "New block", Symbol = "infantry" } });
                doc.Layers.Add(new ArtLayer { Name = "Paint", Kind = "paint" });
                doc.Selected = doc.Layers[0].Id;
                _ = AddPieceFromDoc(doc, "New block", "box", 2.0, "move");
                break;
            }
            case 2: case 3: case 4: case 5:
            {
                var (w, h, shape, name) = kind switch { 2 => (512, 256, "box", "Token"), 3 => (512, 512, "box", "Square token"), 4 => (512, 512, "circle", "Round token"), _ => (512, 444, "hex", "Hex token") };
                var doc = new ArtDoc { W = w, H = h };
                var field = new ArtLayer { Name = "Field", Kind = "paint" };
                var outline = ArtDraw.Outline(shape, w, h);
                field.Ops.Add(new PaintOp { K = "shape", P = ArtXf.Flat(outline), W = 0, Fc = "#d9cfb5ff", C = "#d9cfb5ff" });
                doc.Layers.Add(field);
                doc.Layers.Add(new ArtLayer { Name = "Paint", Kind = "paint" });
                _ = AddPieceFromDoc(doc, name, shape, 2.0, "brush");
                break;
            }
            case 6:
                _s.PickFiles(ImageFiles.Filter, AddImageFiles, "One piece per picture");
                break;
            case 7:
                if (ImageFiles.FromClipboard() is { } img) _ = AddPieceFromImage(img, "Pasted piece");
                else _s.Flash("No picture on the clipboard.");
                break;
            case 8:
                _s.PickFiles(ModelImport.Filter, f => ImportModels(f), "One 3D piece per model file");
                break;
            case 9:
                NewModelFromShapes();
                break;
        }
    }

    public void Dropped(string[] files)
    {
        var pics = files.Where(ImageFiles.IsImage).ToArray();
        var models = files.Where(ModelImport.IsModel).ToArray();
        if (models.Length > 0 && _s.View == "model" && _s.Model is { } m && _s.Sel is { } sel && m.Holds(sel) && !EffectFx.IsEffect(sel)
            && m.GetGlobalRect().HasPoint(Pointer.Global(m))) AddModelFiles(models);
        else if (models.Length > 0) ImportModels(models);
        if (pics.Length == 0) return;
        var canvas = _s.Canvas;
        if (_s.View == "paint" && _s.Faces.CanvasOpen && canvas.GetGlobalRect().HasPoint(Pointer.Global(canvas)))
        {
            var at = canvas.ToDoc(Pointer.Local(canvas));
            foreach (var f in pics) _s.LayerPanel.AddFromFile(f, at);
            canvas.Commit("Pictures");
        }
        else AddImageFiles(pics);
    }

    public async void AddImageFiles(string[] files)
    {
        try { await AddImageFilesAsync(files); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    internal async Task AddImageFilesAsync(string[] files)
    {
        PieceDef? last = null;
        foreach (var f in files)
        {
            var src = ArtStore.AddSrcFile(Store, Pack.Id, f, out var img);
            if (src == null || img == null) continue;
            var doc = PieceFaces.DocAround(img, src);
            last = await MakePiece(doc, Pretty(Path.GetFileNameWithoutExtension(f)), PackEdit.SeeThrough(img) ? "alpha" : "box", 2.0);
        }
        if (last == null) { _s.Flash("No pictures it could read."); return; }
        _s.RebuildList();
        _s.Paper.LayOutAll();
        _s.Select(last, frame: true);
    }

    async Task AddPieceFromImage(Image img, string name)
    {
        var src = ArtStore.AddSrc(Store, Pack.Id, img);
        var d = await MakePiece(PieceFaces.DocAround(img, src), name, PackEdit.SeeThrough(img) ? "alpha" : "box", 2.0);
        _s.RebuildList();
        _s.Paper.LayOutAll();
        _s.Select(d, frame: true);
    }

    async Task AddPieceFromDoc(ArtDoc doc, string name, string shape, double longest, string tool)
    {
        var d = await MakePiece(doc, name, shape, longest);
        _s.RebuildList();
        _s.Paper.LayOutAll();
        _s.FaceKey = "front";
        _s.Inspector.Fresh();
        _s.Select(d, frame: true);
        if (tool == "brush")
        {
            await _s.SetViewAsync("model");
            _s.Model?.SetTool("paint");
            return;
        }
        _s.SetView("paint");
        _s.Canvas.SetTool(tool);
    }

    public async Task<PieceDef> NewBlock(BlockSpec spec, string name, double longest)
    {
        var doc = new ArtDoc { W = (int)spec.BW, H = (int)spec.BH };
        doc.Layers.Add(new ArtLayer { Name = "Plate", Kind = "block", Block = spec });
        doc.Layers.Add(new ArtLayer { Name = "Paint", Kind = "paint" });
        doc.Selected = doc.Layers[0].Id;
        return await NewFromDoc(doc, name, "box", longest);
    }

    public async Task<PieceDef> NewFromDoc(ArtDoc doc, string name, string shape, double longest)
    {
        var d = await MakePiece(doc, name, shape, longest);
        _s.RebuildList();
        _s.Paper.LayOutAll();
        _s.FaceKey = "front";
        _s.Inspector.Fresh();
        _s.Select(d, frame: true);
        return d;
    }

    async Task<PieceDef> MakePiece(ArtDoc doc, string name, string shape, double longest)
    {
        var r = new ArtRenderer { Doc = doc, LoadSrc = n => ArtStore.LoadSrc(Store, Pack.Id, n) };
        _s.AddChild(r);
        r.RebuildAll();
        var img = await r.Flatten();
        r.QueueFree();
        var sha = PackEdit.AddImage(Store, Pack, img);
        _s.MadeHere.Add(sha);
        ArtStore.Save(Store, Pack.Id, sha, doc);
        var def = PackEdit.NewDef(Store, Pack, name, sha, img.GetWidth(), img.GetHeight(), longest, doc.Outline != null ? "alpha" : shape);
        _s.SavePack();
        return def;
    }

    public void NewEffect(string name, double w, double h, EffectDef fx)
    {
        var def = FirmeEffects.Def(PackEdit.NewDefId(Pack, name), name, w, h, fx);
        Pack.Pieces.Add(def);
        Store.RegisterDef(Pack, def);
        Added(def);
    }

    public PieceDef? CopyFirme(PieceDef src)
    {
        if (Store.Pack(FirmeEffects.PackId) is not { } firme) return null;
        PieceDef def;
        try { def = PackEdit.CopyInto(Store, firme, src, Pack); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _s.Flash("Not copied: " + e.Message);
            return null;
        }
        Added(def);
        return def;
    }

    void Added(PieceDef def)
    {
        _s.SavePack();
        _s.RebuildList();
        _s.Paper.LayOutAll();
        _s.Inspector.Fresh();
        _s.Select(def, frame: true);
        _s.SetView("table");
    }

    internal static string Pretty(string stem) => Naming.PieceName(stem);

    public Control Cards(Action chosen)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Row);
        void Row(string title, params (string icon, string name, string what, Action act)[] cards)
        {
            box.AddChild(Ui.Header(title));
            var grid = new GridContainer { Columns = 5 };
            grid.AddThemeConstantOverride("h_separation", Ui.Gap.Row);
            grid.AddThemeConstantOverride("v_separation", Ui.Gap.Row);
            foreach (var (icon, name, what, act) in cards)
            {
                var a = act;
                grid.AddChild(Card(icon, name, what, () => { chosen(); a(); }));
            }
            box.AddChild(grid);
        }
        Row("Start from a shape",
            ("block-layer", "Unit block", "Colours, a label and the unit symbol", () => NewPiece(1)),
            ("shape-rect", "Tile", "A blank 2 : 1 tile", () => NewPiece(2)),
            ("round", "Round token", "A blank disc", () => NewPiece(4)),
            ("ngon", "Hex token", "A blank hexagon", () => NewPiece(5)),
            ("part", "Figure on a base", "A base and a box to build on", () => NewPiece(9)));
        Row("Start from files",
            ("image", "Pictures", "A piece from each, as it is or framed as a token", TokenFromPictures),
            ("3d", "3D model", "glTF, GLB, FBX, OBJ, STL, PLY or Collada", () => NewPiece(8)),
            ("text-layer", "PDF book", "Every page of a PDF, read at the table", BookFromPdf),
            ("pick", "Clipboard", "The picture you just copied", () => NewPiece(7)));
        box.AddChild(Ui.Header("From " + FirmeEffects.PackName));
        var fx = new GridContainer { Columns = 5 };
        fx.AddChild(Card(EffectFx.Icon(new EffectDef { Kind = "cannon" }), "Effects and props", "Smoke, fire, volleys, weather, camps, field works and markers, copied into this pack to change as you like",
            () => { chosen(); ShowFirmePieces(); }));
        box.AddChild(fx);
        return box;
    }

    public void ShowFirmePieces()
    {
        if (Store.Pack(FirmeEffects.PackId) is not { } firme) { _s.Flash(FirmeEffects.PackName + " is not installed."); return; }
        var dlg = new AcceptDialog { Title = FirmeEffects.PackName, OkButtonText = Ui.CloseWord };
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(740, 460), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", Ui.Gap.Row);
        var eras = new[] { ("ancient", "Bows, blades and siege engines"), ("napoleonic", "The age of the musket"), ("modern", "The World Wars and after"), ("naval", "At sea") };
        void Group(string title, IEnumerable<PieceDef> pieces)
        {
            var grid = new GridContainer { Columns = 4 };
            grid.AddThemeConstantOverride("h_separation", Ui.Gap.Row);
            grid.AddThemeConstantOverride("v_separation", Ui.Gap.Row);
            foreach (var d in pieces)
            {
                var src = d;
                grid.AddChild(FirmeCard(firme, d, () => { dlg.Hide(); CopyFirme(src); }));
            }
            if (grid.GetChildCount() == 0) { grid.QueueFree(); return; }
            col.AddChild(Ui.Header(title));
            col.AddChild(grid);
        }
        foreach (var (era, title) in eras) Group(title, firme.Pieces.Where(d => d.Tags.Contains(era)));
        Group("Any table", firme.Pieces.Where(d => !eras.Any(e => d.Tags.Contains(e.Item1))));
        scroll.AddChild(col);
        dlg.AddChild(scroll);
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }

    Control FirmeCard(PackManifest firme, PieceDef d, Action press)
    {
        if (EffectFx.IsEffect(d)) return Card(EffectFx.Icon(d.Effect ?? EffectKinds.FromName(d.Name)), d.Name, FirmeEffects.What(d), press);
        return PackEdit.LoadAsset(Store, firme, d.Image) is { } img
            ? Card(ImageTexture.CreateFromImage(img), d.Name, FirmeEffects.What(d), press)
            : Card("image", d.Name, FirmeEffects.What(d), press);
    }

    static Control Card(Texture2D picture, string name, string what, Action press)
    {
        var card = Card("", name, what, press);
        if (card.GetChild(0).GetChild(0) is Control holder)
        {
            foreach (var c in holder.GetChildren()) c.QueueFree();
            holder.CustomMinimumSize = new Vector2(48, 48);
            var tr =
                new TextureRect { Texture = picture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore };
            holder.AddChild(tr);
            tr.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        return card;
    }

    static Control Card(string icon, string name, string what, Action press)
    {
        var card =
            new PanelContainer { CustomMinimumSize = new Vector2(168, 0), MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = what };
        var normal = Ui.Box(new Color(1, 1, 1, 0.05f), 3, 10, 8);
        var hover = Ui.Box(new Color(1, 1, 1, 0.10f), 3, 10, 8);
        card.AddThemeStyleboxOverride("panel", normal);
        card.MouseEntered += () => card.AddThemeStyleboxOverride("panel", hover);
        card.MouseExited += () => card.AddThemeStyleboxOverride("panel", normal);
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        card.AddChild(v);
        var holder = new Control { CustomMinimumSize = new Vector2(28, 28), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        Control glyph = icon is "round" or "ngon" or "star" ? new ShapeGlyph { Kind = icon } : new StudioIcon { Kind = icon };
        glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        holder.AddChild(glyph);
        glyph.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        v.AddChild(holder);
        var n = new Label { Text = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddChild(n);
        var w = new Label { Text = what, Modulate = Ui.Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(148, 0) };
        w.AddThemeFontSizeOverride("font_size", 11);
        v.AddChild(w);
        card.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) { card.AcceptEvent(); press(); } };
        return card;
    }

    public void Show()
    {
        var dlg = new AcceptDialog { Title = "New piece", OkButtonText = Ui.CloseWord };
        dlg.AddChild(Cards(() => dlg.Hide()));
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }

    const string PdfFilter = "*.pdf ; PDF";
    const double BookLongestSide = 5.0;

    void BookFromPdf() => _s.PickFiles(PdfFilter, f => { if (f.Length > 0) RenderBook(f[0]); }, "A PDF to read at the table");

    void RenderBook(string file)
    {
        var into = Path.Combine(Path.GetTempPath(), "ktable-pdf", System.Guid.NewGuid().ToString("N")[..8]);
        var pages = new List<string>();
        _s.Flash($"Reading {Path.GetFileName(file)}…");
        ConverterTool.Run(new[] { "pdf-pages", file, "--out", into },
            line => { if (line.StartsWith("PAGE ", StringComparison.Ordinal) && line.Split(' ', 5) is { Length: 5 } p) pages.Add(p[4]); },
            code =>
            {
                if (pages.Count == 0) { _s.Flash(code == 0 ? "That PDF has no pages." : "That PDF could not be read."); return; }
                MakeBook(pages, Pretty(Path.GetFileNameWithoutExtension(file)));
                KTable.Core.BestEffort.Try(() => Directory.Delete(into, true));
            });
    }

    void MakeBook(List<string> files, string name)
    {
        var pages = new List<(string sha, int w, int h)>();
        foreach (var f in files)
            if (PackEdit.AddImageFile(Store, Pack, f) is { } a) pages.Add((a.sha, a.w, a.h));
        if (pages.Count == 0) { _s.Flash("No pages it could read."); return; }
        var def = PackEdit.NewDef(Store, Pack, name, pages[0].sha, pages[0].w, pages[0].h, BookLongestSide, "box", new[] { "book" });
        def.States = pages.Select((p, i) => new PieceStateDef { Name = $"Page {i + 1}", Image = p.sha }).ToList();
        _s.SavePack();
        _s.RebuildList();
        _s.Paper.LayOutAll();
        _s.Select(def, frame: true);
        _s.Flash($"{name}: {pages.Count} pages. At the table, double-click it to read it.");
    }

    void TokenFromPictures() => _s.PickFiles(ImageFiles.Filter, AskTokenFrame, "Pictures for tokens");

    void AskTokenFrame(string[] files)
    {
        if (files.Length == 0) return;
        var dlg = new ConfirmationDialog { Title = files.Length == 1 ? "A piece from a picture" : $"Pieces from {files.Length} pictures", OkButtonText = "Make" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Group);
        dlg.AddChild(box);
        box.AddChild(ToolPanel.Small("Frame"));
        string frame = "As it is";
        var frames = new HFlowContainer { CustomMinimumSize = new Vector2(460, 0) };
        var group = new ButtonGroup();
        var g = Ui.Form(Ui.DialogLabels);
        foreach (var f in new[] { "As it is" }.Concat(PieceFramer.Outlines.Where(o => o != PieceFramer.AlongThePicture)))
        {
            var b =
                new Button { Text = f, ToggleMode = true, ButtonGroup = group, ButtonPressed = f == frame, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(70, 0),
                    TooltipText = f == "As it is" ? "The picture's own outline and size" : "Cut to this shape with a border, then slid into place" };
            var ff = f;
            b.Pressed += () => { frame = ff; g.Visible = ff != "As it is"; };
            frames.AddChild(b);
        }
        box.AddChild(frames);
        g.Visible = false;
        g.AddChild(new Label { Text = "Border" });
        var br = new HBoxContainer();
        var col = new ColorPickerButton { Color = new Color(0.12f, 0.12f, 0.12f), EditAlpha = false, CustomMinimumSize = new Vector2(44, 24) };
        var w =
            new HSlider { MinValue = 0, MaxValue = 48, Step = 1, Value = 14, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(140, 0), TooltipText = "0: no border" };
        br.AddChild(col);
        br.AddChild(w);
        br.AddChild(Ui.ValueBox(w, v => $"{v:0} px", 44));
        g.AddChild(br);
        g.AddChild(new Label { Text = "Detail" });
        var size = new OptionButton();
        size.AddItem("512 px (a counter)");
        size.AddItem("1024 px (close-ups)");
        g.AddChild(size);
        Ui.AlignForm(g);
        box.AddChild(g);
        dlg.Confirmed += () => { if (frame == "As it is") AddImageFiles(files); else _ = MakeTokens(files, frame, col.Color, (float)w.Value, size.Selected == 1 ? 1024 : 512); };
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }

    public async Task MakeTokens(string[] files, string frame, Color border, float borderPx, int size)
    {
        PieceDef? last = null;
        foreach (var f in files)
        {
            var src = ArtStore.AddSrcFile(Store, Pack.Id, f, out var img);
            if (src == null || img == null) { _s.Flash("Could not read " + Path.GetFileName(f)); continue; }
            var doc = new ArtDoc { W = size, H = size };
            float k = Mathf.Max(size / (float)img.GetWidth(), size / (float)img.GetHeight());
            var pic = new ArtLayer { Name = "Picture", Kind = "image", Src = src, SrcW = img.GetWidth(), SrcH = img.GetHeight(), X = size / 2f, Y = size / 2f, Sx = k, Sy = k };
            doc.Layers.Add(pic);
            var outline = PieceFramer.Preset(frame, size, size);
            doc.Outline = ArtXf.Flat(outline);
            if (borderPx > 0) doc.Layers.Add(PieceFramer.BorderLayer(outline, border, borderPx, size));
            doc.Selected = pic.Id;
            last = await MakePiece(doc, Pretty(Path.GetFileNameWithoutExtension(f)), "alpha", 1.0);
        }
        if (last == null) return;
        _s.RebuildList();
        _s.Paper.LayOutAll();
        _s.FaceKey = "front";
        _s.Inspector.Fresh();
        _s.Select(last, frame: true);
        _s.SetView("paint");
        _s.Canvas.SetTool("move");
        _s.Flash("Drag the picture to frame it, its corners to zoom it; the border and the cut stay put");
    }

    public async void ImportModels(string[] files)
    {
        try { await ImportModelsAsync(files); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    internal async Task ImportModelsAsync(string[] files)
    {
        var read = new List<(string File, MeshData Mesh)>();
        foreach (var f in files)
        {
            _s.Flash($"Reading {Path.GetFileName(f)}…");
            await _s.Frames(2);
            var m = ModelImport.Load(f, out var err);
            if (m == null) { _s.Flash($"Could not read {Path.GetFileName(f)}: {err}"); continue; }
            if (m.Tris > 400_000) { _s.Flash($"{Path.GetFileName(f)} has {m.Tris:#,0} triangles: too many for a piece on the table (bring it under 400,000 in a 3D program first)."); continue; }
            read.Add((f, m));
        }
        if (read.Count == 0) return;
        var (scale, fitted, widest) = ModelStudio.ImportScale(read.Select(r => r.Mesh.Bounds()));
        foreach (var (f, m) in read)
        {
            await _s.NewModelPiece(Pretty(Path.GetFileNameWithoutExtension(f)), m, "mesh", scale: scale);
            _s.Flash($"{Path.GetFileName(f)}: {m.Tris:#,0} triangles, {m.Tex.Count(t => t != null)} picture{(m.Tex.Count(t => t != null) == 1 ? "" : "s")} painted into one.");
        }
        if (fitted)
            _s.Flash(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                read.Count == 1 ? "Its own units make it {0:0.###} u wide: fitted to 2 u." : "Their own units make the widest {0:0.###} u: fitted to 2 u, the rest in proportion.", widest));
    }

    void NewModelFromShapes() => _ = _s.NewModelPiece("New model", ModelImport.Primitive("base"), "base", addFigure: true);

    public void PickModelPart() => _s.PickFiles(ModelImport.Filter, f => AddModelFiles(f), "Models to build onto the piece");

    internal int AddModelFiles(string[] files)
    {
        int added = 0;
        foreach (var f in files)
        {
            var m = ModelImport.Load(f, out var err);
            if (m == null) { _s.Flash($"Could not read {Path.GetFileName(f)}: {err}"); continue; }
            if (_s.Model == null) return added;
            _s.Model.AddParts(m, Pretty(Path.GetFileNameWithoutExtension(f)), "mesh");
            added++;
        }
        return added;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class StudioDemo
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly PieceStudio _s;
    readonly StudioSteps _steps;

    public StudioDemo(PieceStudio studio) { _s = studio; _steps = new StudioSteps(studio); }

    public async void Run(string steps)
    {
        try { await RunAsync(steps); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    async Task RunAsync(string steps)
    {
        foreach (var raw in steps.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (verb, arg) = raw.IndexOf(':') is var i and >= 0 ? (raw[..i], raw[(i + 1)..]) : (raw, "");
            try { await Step(verb, arg); }
            catch (Exception e) { GD.PrintErr($"[studio] step '{raw}' failed: {e.Message}"); }
            await Frames(2);
        }
    }

    Task Frames(int n) => _s.Frames(n);
    async Task Wait(double s) => await _s.ToSignal(_s.GetTree().CreateTimer(s), Timer.SignalName.Timeout);

    float[] Nums(string a) => a.Split(',').Select(x => float.Parse(x, Inv)).ToArray();

    async Task Step(string verb, string arg)
    {
        if (await _steps.Run(verb, arg) || await StudioStep(verb, arg) || await ModelStep(verb, arg) || await PieceStep(verb, arg)) return;
        GD.PrintErr($"[studio] unknown step {verb}");
    }

    async Task<bool> StudioStep(string verb, string arg)
    {
        switch (verb)
        {
            case "newmenu":
                _s.NewPieces.Show();
                await Frames(3);
                break;
            case "fxmenu":
                _s.NewPieces.ShowEffectPresets();
                await Frames(3);
                break;
            case "wait":
                await Wait(double.Parse(arg, Inv));
                break;
            case "report":
                Report();
                break;
            case "tintpick":
            {
                var b = _s.FindChildren("*", nameof(ColorPickerButton), true, false).OfType<ColorPickerButton>().FirstOrDefault(x => x.IsVisibleInTree() && x.TooltipText.StartsWith("The colour of the piece"));
                if (b == null) { GD.PrintErr("[studio] no tint button in view"); break; }
                b.GetPopup().Popup();
                await Frames(2);
                b.GetPicker().Color = b.Color = Color.FromHtml(arg);
                b.GetPopup().Hide();
                await Frames(6);
                GD.Print($"[studio] tint picked {arg}");
                break;
            }
            case "shot":
                await Frames(3);
                _s.GetViewport().GetTexture().GetImage().SavePng(arg);
                GD.Print($"[studio] shot {arg}");
                break;
            case "closepanel":
                _s.ToolPanel.Panel.Closed?.Invoke();
                await Frames(2);
                break;
            case "swap":
                _s.SwapSides();
                await Wait(1.0);
                break;
            case "panel":
                GD.Print($"[studio] tool panel {(_s.ToolPanel.Panel.Visible ? "open" : "shut")}; tool {(_s.View == "model" ? _s.Model?.Tool : _s.Canvas.Tool)}");
                break;
            case "pictures":
                _s.NewPieces.AddImageFiles(arg.Split('|'));
                await Wait(2.0);
                break;
            case "frame":
                if (_s.Sel != null)
                {
                    if (arg.Length > 0) await _s.Framer.SetOutline(_s.Sel, 0, arg);
                    await _s.Framer.Frame(_s.Sel, new Color(0.12f, 0.12f, 0.12f), 10, new Color(0.85f, 0.81f, 0.71f));
                    await Wait(1.0);
                }
                break;
            case "token":
            {
                var parts = arg.Split('|');
                await _s.NewPieces.MakeTokens(new[] { parts[0] }, parts.Length > 1 ? parts[1] : "Round", new Color(0.12f, 0.12f, 0.12f), 14, 512);
                await Wait(0.8);
                break;
            }
            case "quit":
                _s.GetTree().Quit();
                break;
            case "image":
                _s.LayerPanel.AddFromFile(arg);
                _s.Canvas.Commit("Picture");
                await Frames(3);
                break;
            default: return false;
        }
        return true;
    }

    async Task<bool> ModelStep(string verb, string arg)
    {
        switch (verb)
        {
            case "model":
                _s.NewPieces.ImportModels(arg.Split('|'));
                await Wait(2.5);
                break;
            case "mstamp":
            {
                var n = Nums(arg);
                for (int k = 0; k + 1 < n.Length; k += 2) GD.Print($"[studio] stamp at {n[k]},{n[k + 1]}: {(Stamp(new Vector2(n[k], n[k + 1])) ? "hit" : "missed")}");
                await Frames(2);
                break;
            }
            case "mfaces":
                GD.Print("[studio] shape faces: " + string.Join(" | ", _s.Model?.ShapeFaces.Keys().Select(f => f.label) ?? Enumerable.Empty<string>()));
                break;
            case "mreport":
                GD.Print("[studio] model: " + (_s.Model is { } reported ? ModelReport(reported) : null) + $"; piece {_s.Sel?.Name} kind {_s.Sel?.Kind} mesh {_s.Sel?.Mesh?[..8]} tex {_s.Sel?.Texture?[..8]} add-on {_s.Sel?.AddOnMesh?[..8] ?? "none"} outline {_s.Sel?.Shape} size {_s.Sel?.Size.W:0.###}x{_s.Sel?.Size.H:0.###}");
                break;
            case "decal":
                if (ImageFiles.Load(arg) is { } di) _s.Model?.Decal.Set(di, System.IO.Path.GetFileNameWithoutExtension(arg));
                break;
            case "mpicture":
            {
                var bits = arg.Split('|');
                if (_s.Model is { SelPartId: { } part } pm) { GD.Print("[studio] " + pm.ShapeFaces.PutPicture($"p:{part}:{bits[0]}", bits[1])); _s.RebuildModelPanels(); }
                await Frames(3);
                break;
            }
            case "mdecal":
            {
                var n = Nums(arg);
                if (_s.Model == null) break;
                if (n.Length > 2) _s.Model.Decal.Px = n[2];
                if (n.Length > 3) _s.Model.Decal.Turn = n[3];
                int got = PressDecal(_s.Model, new Vector2(n[0], n[1]), n.Length <= 4 || n[4] != 0);
                GD.Print($"[studio] decal at {n[0]},{n[1]}: {got} texels");
                await Frames(3);
                break;
            }
            default: return false;
        }
        return true;
    }

    bool Stamp(Vector2 viewFraction)
    {
        if (_s.Model is not { } m) return false;
        var at = viewFraction * (Vector2)m.Vp.Size;
        if (m.Tools.Ray(at) is not { } hit) return false;
        m.BeginStroke();
        m.Tools.Stamp(hit);
        m.EndStroke();
        return true;
    }

    static int PressDecal(ModelStudio m, Vector2 viewFraction, bool press)
    {
        var at = viewFraction * (Vector2)m.Vp.Size;
        m.Tools.Hover = at;
        m.RedrawOverlay();
        if (!press) return 0;
        m.BeginStroke();
        int n = m.Decal.Project(at);
        m.EndStroke();
        return n;
    }

    static string ModelReport(ModelStudio m) => $"{m.Doc.Parts.Count} parts ({string.Join(", ", m.Doc.Parts.Select(p => $"{p.Name}:{p.Kind}{(p.Visible ? "" : " hidden")} tris {m.Views.GetValueOrDefault(p.Id)?.Data.Tris} paint {(m.Views.GetValueOrDefault(p.Id)?.Paint.Count(b => b != 0) ?? 0) / 4}px"))}), scale {m.Doc.Scale:0.###}, baked {m.Doc.Baked?[..8]}, dirty mesh {m.DirtyMesh} tex {m.DirtyTex}";

    async Task<bool> PieceStep(string verb, string arg)
    {
        switch (verb)
        {
            case "tab":
                _s.Inspector.Tabs.CurrentTab = int.Parse(arg, Inv);
                await Frames(3);
                break;
            case "scrolltab":
                if (_s.Inspector.Tabs.GetCurrentTabControl() is ScrollContainer sc) { sc.ScrollVertical = int.Parse(arg, Inv); await Frames(3); }
                break;
            case "script":
                await SaveScript(arg);
                break;
            case "marker":
            {
                var m = _s.Pack.Pieces.FirstOrDefault(x => x.Name.Contains(arg, StringComparison.OrdinalIgnoreCase) && x != _s.Sel);
                if (m != null) _s.Apply(x => x.Attachments.Add(new AttachmentDef { Piece = m.Id, Offset = new Vec2(x.Size.W * 0.8, 0) }), inspector: true);
                break;
            }
            case "markers":
                if (arg == "end") { _s.Markers.End(); _s.RebuildInspector(); } else _s.Markers.Begin();
                await Wait(0.8);
                break;
            case "dragmarker":
                await DragMarker(arg);
                break;
            default: return false;
        }
        return true;
    }

    async Task SaveScript(string arg)
    {
        _s.Inspector.Tabs.CurrentTab = 2;
        await Frames(2);
        if (_s.Script.Editor == null) return;
        _s.Script.Editor.Text = arg.Replace("\\n", "\n");
        var save = _s.Inspector.Tabs.GetCurrentTabControl().FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "Save and run");
        save?.EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(1.0);
        var inst = _s.Paper.Instance(_s.Sel);
        GD.Print($"[studio] script saved ({_s.Sel?.Script?.Length} chars); the piece shows {inst?.Widgets.Count} widgets: {string.Join(" | ", inst?.Widgets.Select(w => $"{w.Kind} '{w.Label}' at {w.X:0.##},{w.Z:0.##}") ?? Array.Empty<string>())}; log lines {_s.Script.LogCount}");
    }

    async Task DragMarker(string arg)
    {
        var n = Nums(arg);
        if (!_s.Markers.TryMarker((int)n[0], out var id) || _s.S.View.Find(Layers.Umpire, id) is not { } mp) { GD.Print("[studio] no such marker"); return; }
        _s.T.SetSelection(new[] { id });
        var node = _s.T.NodeOf(Layers.Umpire, id)!;
        var a = _s.T.Rig.Cam.UnprojectPosition(node.GlobalPosition);
        var b = _s.T.Rig.Cam.UnprojectPosition(node.GlobalPosition + Coords.ToWorld(new Vec2(n[1], n[2])) - Coords.ToWorld(new Vec2(0, 0)));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a });
        await Frames(1);
        for (int i = 1; i <= 6; i++)
        {
            var at2 = a.Lerp(b, i / 6f);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at2, GlobalPosition = at2, Relative = (b - a) / 6 });
            await Frames(1);
        }
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
        await Wait(1.2);
        var basePiece = _s.Paper.Instance(_s.Sel)!;
        var now = _s.S.View.Find(Layers.Umpire, id)!;
        GD.Print($"[studio] marker {n[0]} moved from {mp.Pos.X:0.##},{mp.Pos.Z:0.##} to {now.Pos.X:0.##},{now.Pos.Z:0.##}; base at {basePiece.Pos.X:0.##},{basePiece.Pos.Z:0.##} turned {basePiece.RotDeg:0}; offset kept {_s.Sel!.Attachments[(int)n[0]].Offset.X:0.###},{_s.Sel.Attachments[(int)n[0]].Offset.Z:0.###}");
    }

    void Report()
    {
        var doc = _s.Canvas.Doc;
        GD.Print($"[studio] view {_s.View}, piece '{_s.Sel?.Name}' ({_s.Sel?.Kind}, {_s.Sel?.Shape}, {_s.Sel?.Size.W:0.##}×{_s.Sel?.Size.H:0.##}), face {_s.FaceKey}, tool {_s.Canvas.Tool}, doc {doc.W}×{doc.H}, outline {(doc.Outline?.Count ?? 0) / 2} pts, measures {doc.Measures.Count / 4}");
        foreach (var l in doc.Layers)
            GD.Print($"[studio]   layer '{l.Name}' {l.Kind} vis={l.Visible} lock={l.Locked} op={l.Opacity:0.##} xf=({l.X:0.#},{l.Y:0.#}) rot={l.Rot:0.#} s=({l.Sx:0.###},{l.Sy:0.###}) ops={l.Ops.Count} [{string.Join(" ", l.Ops.Select(o => $"{o.K}{(o.E ? "/erase" : "")}:{o.P.Count / 2}pt{(o.Fc != null ? "/fill" + o.Fc[..7] : "")}{(o.W > 0 ? "/" + o.C[..7] : "")}"))}]{(l.Crop != null ? " crop=" + string.Join(",", l.Crop.Select(c => c.ToString("0", Inv))) : "")}");
        if (_s.Sel != null)
        {
            foreach (var f in PieceFaces.Of(_s.Sel)) GD.Print($"[studio]   face {f.Key}: {(f.Sha == null ? "none" : f.Sha[..10] + (ArtStore.Load(_s.Store, _s.Pack.Id, f.Sha) is { } d ? $" (doc, {d.Layers.Count} layers)" : " (flat)"))}");
            GD.Print($"[studio]   undo {_s.Canvas.CanUndo} redo {_s.Canvas.CanRedo}; dirty {_s.Faces.Dirty}; instances {_s.S.View.Layer(Layers.Umpire).Count}");
            if (_s.Paper.Instance(_s.Sel) is { } shown) GD.Print($"[studio]   on the paper: tint {shown.TintOverride ?? _s.Sel.Tint}, {shown.Decals.Count} decals, {shown.Widgets.Count} widgets, face down {shown.FaceDown}");
        }
    }
}

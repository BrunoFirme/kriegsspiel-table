using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

internal sealed class StudioMenu
{
    readonly PieceStudio _s;

    public StudioMenu(PieceStudio s) => _s = s;

    StudioCanvas Canvas => _s.Canvas;

    public void Show(Control at)
    {
        var prefs = _s.Prefs;
        var pm = new PopupMenu();
        pm.AddSeparator("Pack");
        pm.AddItem("New piece…", 1);
        pm.SetItemAccelerator(pm.GetItemIndex(1), (Key)KeyModifierMask.MaskCtrl | Key.N);
        pm.AddItem("Pack details…", 15);
        if (_s.View == "paint" && _s.Faces.CanvasOpen) { pm.AddSeparator("Picture"); pm.AddItem("Picture size…", 3); pm.AddCheckItem("Rulers", 4); pm.SetItemChecked(pm.ItemCount - 1, prefs.Rulers); }
        pm.AddSeparator("Tidy up");
        pm.AddItem("Remove pictures nothing uses any more", 5);
        pm.AddItem("Lay the pieces out again on the table", 6);
        pm.AddSeparator();
        pm.AddItem("Settings…", 13);
        pm.AddSeparator("Help");
        pm.AddItem("Keys and mouse", 8);
        pm.SetItemAccelerator(pm.GetItemIndex(8), Key.F1);
        pm.AddItem("The piece studio guide…", 9);
        pm.AddItem("For makers…", 10);
        pm.AddItem("Scripting guide…", 11);
        pm.AddItem("Scripting reference…", 12);
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 1: _s.NewPieces.Show(); break;
                case 3: AskDocSize(); break;
                case 4: prefs.Rulers = !prefs.Rulers; Canvas.Redraw(); prefs.Save(); break;
                case 5: _ = RemoveUnused(); break;
                case 6: _s.Paper.LayOutAll(force: true); break;
                case 7: _s.Paper.AskReference(); break;
                case 8: ShowKeys(); break;
                case 9: GuideReader.Show(_s.Root, "community-guide.md", "the-piece-studio"); break;
                case 10: GuideReader.Show(_s.Root, "makers.md"); break;
                case 11: GuideReader.Show(_s.Root, "scripting.md"); break;
                case 12: GuideReader.ShowEntry(_s.Root, null); break;
                case 13: AppSettings.OpenWindow(_s.Root, _s.Cfg); break;
                case 15: PackDetails(); break;
            }
        };
        pm.PopupHide += () => pm.QueueFree();
        _s.Root.AddChild(pm);
        var r = at.GetGlobalRect();
        pm.Popup(new Rect2I((Vector2I)(r.Position + new Vector2(0, r.Size.Y + 2)).Round(), Vector2I.Zero));
    }

    async Task RemoveUnused()
    {
        if (await _s.SettleWork() is { } problem) { _s.Flash("Nothing removed: " + problem.Replace("\n", "; ")); return; }
        if (_s.Gone) return;
        int n;
        try { n = RemoveUnusedNow(); }
        catch (Exception e) { _s.Flash("Could not clear out the pack: " + e.Message); return; }
        _s.Flash(n == 0 ? "Nothing to remove: every file is in use." : $"Moved {n} file{(n == 1 ? "" : "s")} nothing uses to the trash.");
    }

    void PackDetails()
    {
        var pack = _s.Pack;
        var dlg = new ConfirmationDialog { Title = "Pack details", OkButtonText = "Save" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        dlg.AddChild(box);
        var authors = new LineEdit { Text = string.Join(", ", pack.Source.Authors), PlaceholderText = "your name, and anyone who made it with you", CustomMinimumSize = new Vector2(420, 0) };
        var about = new TextEdit { Text = pack.Description ?? "", WrapMode = TextEdit.LineWrappingMode.Boundary, CustomMinimumSize = new Vector2(420, 140), PlaceholderText = "What the pack holds, its period and scale, how its pieces are meant to be used" };
        box.AddChild(new Label { Text = "Made by" }); box.AddChild(authors);
        box.AddChild(new Label { Text = "Description" }); box.AddChild(about);
        dlg.Confirmed += () =>
        {
            pack.Source.Authors = authors.Text.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).Distinct().ToList();
            pack.Description = about.Text.Trim().Length == 0 ? null : about.Text.Trim();
            _s.SavePack();
        };
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }

    internal int RemoveUnusedNow()
    {
        var live = new List<string>();
        if (_s.Faces.CanvasOpen) { live.Add(ArtStore.Serialize(Canvas.Doc)); live.AddRange(Canvas.History); }
        live.Add(string.Join(" ", Canvas.R.CachedNames));
        if (_s.Model != null) live.AddRange(_s.Model.KeepTexts());
        int n = PackEdit.RemoveUnusedAssets(_s.Store, _s.Pack) + ArtStore.Sweep(_s.Store, _s.Pack, live);
        _s.SavePack();
        return n;
    }

    public void ShowKeys() => GuideReader.Show(_s.Root, KTable.Client.Docs.KeysPage.File, "piece-studio");

    void AskDocSize()
    {
        var dlg = new ConfirmationDialog { Title = "Picture size", OkButtonText = "Apply" };
        var g = Ui.Form(90);
        var w = new SpinBox { MinValue = 8, MaxValue = 4096, Value = Canvas.Doc.W, Suffix = "px" };
        var h = new SpinBox { MinValue = 8, MaxValue = 4096, Value = Canvas.Doc.H, Suffix = "px" };
        var how = new OptionButton();
        how.AddItem("Scale everything with it"); how.AddItem("Keep sizes, grow or cut round the middle");
        g.AddChild(new Label { Text = "Width" }); g.AddChild(w);
        g.AddChild(new Label { Text = "Height" }); g.AddChild(h);
        g.AddChild(new Label { Text = "Layers" }); g.AddChild(how);
        Ui.AlignForm(g);
        var box = new VBoxContainer();
        box.AddChild(g);
        box.AddChild(Ui.Hint("A face of a piece two units wide looks crisp on the table at 512 px; a picture for close-up screenshots wants 1024 or more."));
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            int nw = (int)w.Value, nh = (int)h.Value;
            var doc = Canvas.Doc;
            if (how.Selected == 0)
            {
                float kx = nw / (float)doc.W, ky = nh / (float)doc.H;
                foreach (var l in doc.Layers)
                {
                    var t = new Transform2D(0, new Vector2(kx, ky), 0, Vector2.Zero) * l.Xf();
                    l.SetXf(t);
                }
                if (doc.Outline != null) for (int i = 0; i + 1 < doc.Outline.Count; i += 2) { doc.Outline[i] *= kx; doc.Outline[i + 1] *= ky; }
                for (int i = 0; i + 1 < doc.Measures.Count; i += 2) { doc.Measures[i] *= kx; doc.Measures[i + 1] *= ky; }
                Canvas.ResizeDocument(nw, nh, Vector2.Zero);
            }
            else Canvas.ResizeDocument(nw, nh, new Vector2(nw - doc.W, nh - doc.H) / 2);
            Canvas.Commit("Picture size");
            _s.RebuildFaceBar();
        };
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }
}

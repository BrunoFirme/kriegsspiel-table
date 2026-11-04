using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class ScenarioDialog
{
    const string PlainBackdrop = "Plain", OwnBackdrop = "Your own picture";

    void AddSurroundings()
    {
        var now = S.View.Lighting;
        int own = Backdrops.All.Count + 1;
        var pick = new OptionButton { CustomMinimumSize = new Vector2(220, 0), Disabled = !S.IsUmpire };
        pick.AddItem(PlainBackdrop);
        foreach (var b in Backdrops.All) pick.AddItem(b.Name);
        pick.AddItem(OwnBackdrop);
        int Shown(string backdrop) => backdrop.Length == 0 ? 0 : Backdrops.All.ToList().FindIndex(b => b.Id == backdrop) is var i and >= 0 ? i + 1 : own;
        pick.Selected = Shown(now.Backdrop);
        var choose = Btn("Choose a picture…", () => ChooseBackdrop(() => pick.Selected = Shown(S.View.Lighting.Backdrop)),
            "A panorama twice as wide as it is tall wraps all the way round; any picture or web link will do");
        choose.Visible = pick.Selected == own;
        choose.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        pick.ItemSelected += i =>
        {
            choose.Visible = i == own;
            if (i == own) { ChooseBackdrop(() => pick.Selected = Shown(S.View.Lighting.Backdrop)); return; }
            SendLook(l => l.Backdrop = i == 0 ? Backdrops.None : Backdrops.All[(int)i - 1].Id);
        };
        var form = Ui.Form(84);
        form.AddChild(new Label { Text = "Backdrop" });
        form.AddChild(pick);
        form.AddChild(new Control());
        form.AddChild(choose);
        Ui.AlignForm(form);
        _box.AddChild(Header("Around the table"));
        _box.AddChild(form);
        _box.AddChild(Hint("Everyone at the table sees it past the table's edge."));

        var tableForm = Ui.Form(84);
        var table = LightRow(tableForm, "Surface", 0, 1, 0.05, now.TableShade, ShadeWord);
        table.TooltipText = "The table around and under the map sheets: squared paper … grey … black";
        table.Editable = S.IsUmpire;
        Ui.AlignForm(tableForm);
        bool dragging = false;
        table.DragStarted += () => dragging = true;
        table.ValueChanged += v => { PreviewLook(l => l.TableShade = v); if (!dragging) SendLook(l => l.TableShade = v); };
        table.DragEnded += _ => { dragging = false; SendLook(l => l.TableShade = table.Value); };
        _box.AddChild(Header("Table"));
        _box.AddChild(tableForm);
    }

    static string ShadeWord(double v) => v < 0.1 ? "paper" : v < 0.45 ? "dim paper" : v < 0.6 ? "grey" : v < 0.95 ? "dark" : "black";

    Lighting Changed(Action<Lighting> change)
    {
        var l = KTable.Core.Json.Clone(S.View.Lighting);
        change(l);
        return l;
    }

    void PreviewLook(Action<Lighting> change)
    {
        var l = Changed(change);
        T.ApplyLighting(l.FromClock ? DayLight.At(S.View.Clock, l) : l);
    }

    void SendLook(Action<Lighting> change) => S.Do(new SetLighting { Lighting = Changed(change) });

    void ChooseBackdrop(Action undone)
    {
        var root = S.Store.Root;
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter }, Title = "Backdrop", Size = new Vector2I(900, 600) };
        Ui.RememberFolder(fd);
        FileThumbs.Show(fd);
        bool answered = false;
        void Answer(string? path)
        {
            if (answered) return;
            answered = true;
            if (path == null) { undone(); return; }
            Task.Run(() =>
            {
                string? name = null;
                try { name = KeepBackdrop(root, path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PrintErr("[backdrop] " + path + ": " + e.Message); }
                Callable.From(() => Kept(path, name, undone)).CallDeferred();
            });
        }
        PictureLinks.Offer(fd, root, Answer);
        fd.FileSelected += Answer;
        fd.Canceled += () => Answer(null);
        fd.CloseRequested += () => Answer(null);
        HudNode.AddChild(fd);
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    void Kept(string path, string? name, Action undone)
    {
        if (name == null)
        {
            Log($"[color=#ff7060]{Fmt.Bb(Path.GetFileName(path))} could not be used as a backdrop[/color]");
            undone();
            return;
        }
        var c = new SetLighting { Lighting = Changed(l => l.Backdrop = name) };
        if (N.Server != null) S.Do(c); else N.ShareThen(new[] { name }, c);
    }

    static string? KeepBackdrop(string root, string path)
    {
        const int Widest = 8192;
        const float Quality = 0.86f;
        if (new FileInfo(path).Length <= WebPictures.MostBytes && PictureLinks.Keep(root, path) is { } kept) return kept;
        using var img = ImageFiles.Load(path);
        if (img == null || img.IsEmpty()) return null;
        if (img.GetWidth() > Widest) img.Resize(Widest, Math.Max(1, img.GetHeight() * Widest / img.GetWidth()), Image.Interpolation.Lanczos);
        return new WebPictures(root).Keep(img.SaveWebpToBuffer(true, Quality));
    }
}

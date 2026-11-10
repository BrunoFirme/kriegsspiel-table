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
    void AddSurroundings(Lighting now)
    {
        var backdrop = new BackdropPick(now.Backdrop, own: true, editable: S.IsUmpire);
        var pick = backdrop.Pick;
        var colour = backdrop.Colour;
        colour.ColorChanged += _ => PreviewLook(l => l.Backdrop = backdrop.Hex);
        Ui.WhenPicked(colour, c => SendLook(l => l.Backdrop = "#" + c.ToHtml(false)));
        var choose = Btn("Choose a picture…", () => ChooseBackdrop(() => pick.Selected = backdrop.Shown(S.View.Lighting.Backdrop)),
            "A panorama twice as wide as it is tall wraps all the way round; any picture or web link will do");
        choose.Visible = backdrop.IsOwn(pick.Selected);
        pick.ItemSelected += i =>
        {
            choose.Visible = backdrop.IsOwn(i);
            if (backdrop.IsOwn(i)) { ChooseBackdrop(() => pick.Selected = backdrop.Shown(S.View.Lighting.Backdrop)); return; }
            if (BackdropPick.Chosen(i) is not { } chosen) { SendLook(l => l.Backdrop = backdrop.Hex); return; }
            var look = Changed(l => l.Backdrop = chosen.Id);
            if (chosen.Light is { } name && LightPresets.Named(name) is { } light)
            {
                look = LightPresets.Lit(look, light);
                S.Do(new SetLighting { Lighting = look });
                RefreshLook(look);
                return;
            }
            S.Do(new SetLighting { Lighting = look });
        };
        var form = Ui.Form(Ui.DialogLabels);
        form.AddChild(new Label { Text = "Backdrop" });
        form.AddChild(backdrop.Row(choose));
        Ui.AlignForm(form);
        _box.AddChild(Header("Around the table"));
        _box.AddChild(form);

        var tableForm = Ui.Form(Ui.DialogLabels);
        var table = LightRow(tableForm, "Surface", 0, Lighting.SeeThrough, 0.05, now.TableShade, ShadeWord);
        table.TooltipText = SurfaceTip;
        table.Editable = S.IsUmpire;
        Ui.AlignForm(tableForm);
        bool dragging = false;
        table.DragStarted += () => dragging = true;
        table.ValueChanged += v => { PreviewLook(l => l.TableShade = v); if (!dragging) SendLook(l => l.TableShade = v); };
        table.DragEnded += _ => { dragging = false; SendLook(l => l.TableShade = table.Value); };
        _box.AddChild(Header("Table"));
        _box.AddChild(tableForm);
    }

    void AddGround()
    {
        var now = S.View.Lighting;
        var style = new OptionButton { CustomMinimumSize = new Vector2(220, 0), Disabled = !S.IsUmpire, TooltipText = "How ground painted with the Relief tool is drawn: its colours, outlines and marks" };
        foreach (var (_, name) in MapStyles.All) style.AddItem(name);
        style.Selected = MapStyles.All.ToList().FindIndex(m => m.Id == MapStyles.Of(now.MapStyle));
        style.ItemSelected += i => SendLook(l => l.MapStyle = MapStyles.All[(int)i].Id);
        var mapForm = Ui.Form(Ui.DialogLabels);
        mapForm.AddChild(new Label { Text = "Style" });
        mapForm.AddChild(style);
        bool scaled = S.View.Calibration.UnitsPerMeter is > 0;
        var snow = LightRow(mapForm, "Snow line", 0, SnowLineMost, SnowLineStep, now.SnowLine, v => v <= 0 ? "none" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} m", v));
        snow.TooltipText = scaled ? "Above this height every ground takes snow: bare trees, ice on water" : GroundKinds.NeedsScale;
        snow.Editable = S.IsUmpire && scaled;
        bool snowing = false;
        snow.DragStarted += () => snowing = true;
        snow.ValueChanged += v => { PreviewLook(l => l.SnowLine = v); if (!snowing) SendLook(l => l.SnowLine = v); };
        snow.DragEnded += _ => { snowing = false; SendLook(l => l.SnowLine = snow.Value); };
        Ui.AlignForm(mapForm);
        _box.AddChild(Header("Ground"));
        _box.AddChild(mapForm);
        var contours = new CheckBox
        {
            Text = "Contour lines over the relief",
            ButtonPressed = now.Contours,
            Disabled = !S.IsUmpire,
            TooltipText = "Redrawn as the ground is shaped; every fifth line is bolder",
        };
        var spacing = Hint(ContourWords());
        spacing.Visible = now.Contours;
        contours.Toggled += on => { spacing.Visible = on; SendLook(l => l.Contours = on); };
        _box.AddChild(contours);
        _box.AddChild(spacing);
        var streams = new CheckBox
        {
            Text = "Streams from the relief",
            ButtonPressed = now.Streams,
            Disabled = !S.IsUmpire || !scaled,
            TooltipText = scaled ? "Brooks and rivers where the relief gathers water, over every ground but painted water" : GroundKinds.NeedsScale,
        };
        streams.Toggled += on => SendLook(l => l.Streams = on);
        _box.AddChild(streams);
    }

    const double SnowLineMost = 5000, SnowLineStep = 50;

    string ContourWords()
    {
        if (S.View.Calibration.UnitsPerMeter is not > 0) return "Calibrate the map's scale to space them in real heights and number the bold ones.";
        return ContourLines.PlanFor(S.View, S.Ground) is { Numbered: true } plan
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "Every {0:0.##} {1} of height, numbered every {2:0.##}.", plan.Every, plan.Unit, plan.Every * ContourPlan.IndexEvery)
            : "Spaced in real heights and numbered once the scale is set and there is relief.";
    }

    public const string SurfaceTip = "The table around and under the map sheets: squared paper … grey … black … see-through, with no table under the sheets";

    public static string ShadeWord(double v) => Lighting.IsSeeThrough(v) ? "see-through" : v < 0.1 ? "paper" : v < 0.45 ? "dim paper" : v < 0.6 ? "grey" : v < 0.95 ? "dark" : "black";

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
        var fd =
            new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter }, Title = "Backdrop", Size = new Vector2I(900, 600) };
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
        if (N.Server != null) S.Do(c);
        else N.ShareThen(new[] { name }, c);
    }

    internal static string? KeepBackdrop(string root, string path)
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

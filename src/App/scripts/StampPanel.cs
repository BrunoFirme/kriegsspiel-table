using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class StampPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly ScrollContainer _scroll;
    readonly HFlowContainer _thumbs;
    readonly VBoxContainer _below;
    const int Thumb = 96, Width = 330, Height = 420;
    const double NewSize = 1;

    public StampPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel("decals", "Decals");
        Panel.Resizable = true;
        Panel.CustomMinimumSize = shell.PanelSize("decals", Width, Height);
        Panel.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _thumbs = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _thumbs.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        _thumbs.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        _scroll.AddChild(_thumbs);
        _below = new VBoxContainer();
        _below.AddThemeConstantOverride("separation", Ui.Gap.Row);
        Panel.Body.AddChild(_scroll);
        Panel.Body.AddChild(_below);
    }

    public void Rebuild()
    {
        if (T == null) return;
        foreach (var box in new Container[] { _thumbs, _below })
            foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
        var palette = S.View.DecalPalette;
        _scroll.Visible = palette.Count > 0;
        if (palette.Count == 0) _below.AddChild(Hint("No decals"));
        else Entries(palette);
        if (S.IsUmpire) _below.AddChild(Btn("Add a decal…", ChoosePicture, "A picture file or a web link; it joins the scenario's decals for every umpire"));
        if (S.View.Decals.Values.Any(l => l.Count > 0) && S.IsUmpire)
            _below.AddChild(Btn("Lift every stamp off", ConfirmClear, "Takes every decal stamped on the table off it; decals on pieces stay"));
        Panel.Fit();
    }

    void Entries(List<PaletteDecal> palette)
    {
        foreach (var entry in palette)
        {
            var e = entry;
            var b = new Button
            {
                ToggleMode = true, ButtonPressed = T.Decals.Armed == e, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(Thumb, Thumb),
                TooltipText = (e.Name.Length > 0 ? e.Name : "Decal") + (S.IsUmpire ? " · right-click to take it out of the decals" : ""),
            };
            var pic = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            pic.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            if (KTable.Client.PieceFiles.DecalFile(S.View, S.Store, e.Picture) is { } file
                && S.Assets.Texture(file.Package, file.Path, t => { if (GodotObject.IsInstanceValid(pic)) pic.Texture = t; }) is { } tex)
                pic.Texture = tex;
            b.AddChild(pic);
            b.Pressed += () =>
            {
                T.Decals.Arm(T.Decals.Armed == e ? null : e);
                if (T.Tool != Tool.Decal) T.SetTool(Tool.Decal);
                Rebuild();
            };
            b.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && S.IsUmpire) Remove(e);
            };
            _thumbs.AddChild(b);
        }
    }

    void Remove(PaletteDecal e)
    {
        if (T.Decals.Armed == e) T.Decals.Arm(null);
        S.Do(new SetDecalPalette { Entries = S.View.DecalPalette.Where(x => x != e).Select(x => x.Clone()).ToList() });
    }

    void ChoosePicture()
    {
        var root = S.Store.Root;
        var fd =
            new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter }, Title = "Decal picture",
                Size = new Vector2I(900, 600) };
        Ui.RememberFolder(fd);
        FileThumbs.Show(fd);
        bool answered = false;
        void Answer(string? path)
        {
            if (answered || path == null) return;
            answered = true;
            var name = PictureLinks.Keep(root, path);
            if (name == null) { Log($"{System.IO.Path.GetFileName(path)} could not be used as a picture."); return; }
            var entry = new PaletteDecal { Name = System.IO.Path.GetFileNameWithoutExtension(path), Picture = name, Size = NewSize };
            var cmd = new SetDecalPalette { Entries = S.View.DecalPalette.Select(x => x.Clone()).Append(entry).ToList() };
            if (N.Server != null) S.Do(cmd);
            else N.ShareThen(new[] { name }, cmd);
        }
        PictureLinks.Offer(fd, root, Answer);
        fd.FileSelected += Answer;
        HudNode.AddChild(fd);
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    void ConfirmClear()
    {
        int n = S.View.Decals.Where(kv => S.CanEdit(kv.Key)).Sum(kv => kv.Value.Count);
        Ui.Confirm(HudNode, "Lift every stamp off", "Lift off", () =>
            S.Do(new Batch
            {
                Items = S.View.Decals.Where(kv => S.CanEdit(kv.Key))
                    .SelectMany(kv => kv.Value.Where(d => d.Id != null).Select(d => (Command)new RemoveDecal { Layer = kv.Key, DecalId = d.Id! })).ToList(),
            }),
            $"Take all {n} decals stamped on the table off it? Undo puts them back.");
    }
}

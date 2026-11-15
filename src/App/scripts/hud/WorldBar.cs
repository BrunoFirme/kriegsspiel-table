using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class WorldBar : HudPart
{
    public const string Key = "world";
    public readonly FloatPanel Panel;
    Button? _fly;

    public WorldBar(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel(Key, "View");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        if (S.IsUmpire)
        {
            var walls = Ui.Marked(Ui.IconBtn("walls", "Walls down", "Buildings show only the bottom of their walls, so you see inside"));
            walls.ToggleMode = true;
            walls.ButtonPressed = T.Cutaway.On;
            walls.Toggled += on => T.Cutaway.Set(on);
            row.AddChild(walls);
            row.AddChild(GoTo());
        }
        if (S.IsPlayer)
        {
            var fly = _fly = Ui.Marked(Ui.IconBtn("fly", "Fly", "F · up Space, down C"));
            fly.ToggleMode = true;
            fly.ButtonPressed = T.Walk.Flying;
            fly.Toggled += on => T.Walk.Fly(on);
            T.Walk.FlyingChanged += ShowFlying;
            fly.TreeExiting += () => T.Walk.FlyingChanged -= ShowFlying;
            row.AddChild(fly);
            row.AddChild(BodyPicker());
            Wear(Cfg.Body);
        }
        Panel.Body.AddChild(row);
        shell.OpenPanel(Key);
    }

    void ShowFlying()
    {
        if (_fly is { } fly && GodotObject.IsInstanceValid(fly)) fly.SetPressedNoSignal(T.Walk.Flying);
    }

    const int LoadId = -2;

    MenuButton BodyPicker()
    {
        var pick = new MenuButton { Text = "Body ▾", Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = "Walk as a piece from the table's packs, or as a model of your own" };
        var menu = pick.GetPopup();
        var bodies = new List<string>();
        void Picked(long id)
        {
            if (id == LoadId) PickModelFile();
            else if (id >= 0 && id < bodies.Count) Wear(bodies[(int)id]);
        }
        menu.IdPressed += Picked;
        pick.AboutToPopup += () =>
        {
            foreach (var old in menu.GetChildren().OfType<PopupMenu>().ToList()) old.QueueFree();
            menu.Clear();
            bodies.Clear();
            menu.AddItem("Load a model…", LoadId);
            foreach (var pack in S.View.Packs.Select(S.Store.Pack).OfType<PackManifest>().OrderBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase))
            {
                var models = pack.Pieces.Where(IsBody).OrderBy(d => d.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
                if (models.Count == 0) continue;
                var sub = new PopupMenu();
                foreach (var d in models)
                {
                    bodies.Add(AvatarBody.OfPiece(d.Id));
                    sub.AddItem(d.Name, bodies.Count - 1);
                }
                sub.IdPressed += Picked;
                menu.AddSubmenuNodeItem(pack.Name, sub);
            }
        };
        return pick;
    }

    static bool IsBody(PieceDef d) => d.Effect == null && !d.Tags.Contains("die");

    void PickModelFile()
    {
        var fd =
            new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ModelImport.Filter }, Title = "Your body", Size = new Vector2I(900, 600) };
        FileBrowser.Fit(fd);
        HudNode.AddChild(fd);
        fd.FileSelected += Load;
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    public void Load(string path)
    {
        if (BodyFiles.Keep(path, S.Store.Root, out var files, out var error) is not { } body)
        {
            Say($"{Path.GetFileName(path)}: {error}");
            return;
        }
        Say($"Sending {Path.GetFileName(path)} to the table…");
        N.UploadThen(files, () => Wear(body));
    }

    void Wear(string? body)
    {
        if (body == null || AvatarBody.Clean(body) == null) return;
        if (AvatarBody.Piece(body) is { } def && S.Def(def) == null) return;
        if (!BodyFiles.Present(body, S.Store.Root)) return;
        if (AvatarBody.Model(body) != null && T.Walk.Body != body)
        {
            N.UploadThen(BodyFiles.Of(body), () => PutOn(body));
            return;
        }
        PutOn(body);
    }

    void PutOn(string body)
    {
        T.Walk.Body = body;
        if (Cfg.Body == body) return;
        Cfg.Body = body;
        Cfg.Save();
    }

    MenuButton GoTo()
    {
        var go = new MenuButton { Text = "Go to ▾", Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = "Fly the camera to a player's figure" };
        var menu = go.GetPopup();
        var peers = new System.Collections.Generic.List<long>();
        go.AboutToPopup += () =>
        {
            menu.Clear();
            peers.Clear();
            foreach (var (peer, name) in T.Avatars.Named())
            {
                peers.Add(peer);
                menu.AddItem(name, peers.Count - 1);
            }
            if (peers.Count == 0)
            {
                menu.AddItem("No one walking", -1);
                menu.SetItemDisabled(0, true);
            }
            if (!T.FlewTo) return;
            menu.AddSeparator();
            menu.AddItem("Back", int.MaxValue);
        };
        menu.IdPressed += id =>
        {
            if (id == int.MaxValue) T.FlyBack();
            else if (id >= 0 && id < peers.Count) T.FlyToFigure(peers[(int)id]);
        };
        return go;
    }
}

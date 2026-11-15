using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class BodyChoice : HudPart
{
    const int LoadId = -2;

    public BodyChoice(HudContext ctx) : base(ctx) { }

    public MenuButton Picker()
    {
        var pick = new MenuButton { Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = "Your body\nA piece from the table's packs, or a 3D model of your own" };
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
            var offered = S.View.Packs.Prepend(AvatarBody.StandardPack).Distinct().Select(S.Store.Pack).OfType<PackManifest>();
            foreach (var pack in offered.OrderBy(p => p.Id != AvatarBody.StandardPack).ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase))
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

    public string Worn => T.Walk.BodyPiece is { } id && T.Pieces.NodeOf(Layers.Umpire, id) is { } n && GodotObject.IsInstanceValid(n) ? n.CardTitle : "Body";

    static bool IsBody(PieceDef d) => AvatarBody.Takeable(d) && !d.Tags.Contains(AvatarBody.OwnModelTag);

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

    public void Wear(string? body)
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
}

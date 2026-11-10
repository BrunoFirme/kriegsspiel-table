using Godot;
using KTable.Core;
using static KTable.App.TitleStyle;

namespace KTable.App;

static class NewPackDialog
{
    public static void Open(Main main, TitleScreen t)
    {
        var dlg = new ConfirmationDialog { Title = "New pack", OkButtonText = "Create and edit" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(new Label { Text = "Name of the pack" });
        var name = new LineEdit { Text = $"{(t.PlayerName.Text.Trim().Length > 0 ? t.PlayerName.Text.Trim() : "My")} pieces" };
        box.AddChild(name);
        box.AddChild(new Label { Text = "Your packs upload to the host automatically when you place one of their pieces in a game.", Modulate = Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            var id = PackEdit.CreatePack(new PackageStore(PackageStore.DefaultRoot), name.Text, t.PlayerName.Text.Trim());
            t.Shelf.FileNew(true, id);
            main.OpenEditor(id);
        };
        name.TextSubmitted += _ =>
        {
            dlg.Hide();
            var id = PackEdit.CreatePack(new PackageStore(PackageStore.DefaultRoot), name.Text, t.PlayerName.Text.Trim());
            t.Shelf.FileNew(true, id);
            main.OpenEditor(id);
        };
        Ui.Open(main, dlg, new Vector2I(460, 170));
    }
}

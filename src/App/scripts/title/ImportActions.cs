using System;
using Godot;
using KTable.Core;
using static KTable.App.TitleStyle;

namespace KTable.App;

static class ImportActions
{
    public static void Put(HBoxContainer row, Main main, Action<string, string> converted)
    {
        var install = Primary("Install a .ktpack file…", () =>
        {
            var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.ktpack ; Kriegsspiel Table file" }, Size = new Vector2I(820, 520) };
            FileBrowser.Fit(fd);
            main.AddChild(fd);
            fd.FileSelected += main.InstallFile;
            fd.PopupCentered();
            Ui.FreeOnClose(fd);
        });
        install.TooltipText = "A pack, or a scenario with its packs, shared as one file (you can also drop it on this window)";
        row.AddChild(install);
        var convert = new Button { Text = "Convert from…", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, ShelfView.ActionHeight),
            TooltipText = "A digital tabletop module or save (local, or a Workshop link) becomes a pack and a scenario" };
        convert.AddThemeFontSizeOverride("font_size", ShelfView.ActionFont);
        convert.Pressed += () => Ui.Open(main, new ImportDialog(PackageStore.DefaultRoot, converted));
        row.AddChild(convert);
    }
}

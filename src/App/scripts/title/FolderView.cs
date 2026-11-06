using System;
using System.Collections.Generic;
using Godot;
using KTable.Client;

namespace KTable.App;

sealed class FolderView
{
    readonly Node _parent;
    public readonly Library Lib;
    string _view = "";

    public FolderView(Node parent, Library lib) { _parent = parent; Lib = lib; }

    public IEnumerable<(string folder, List<T> items)> Grouped<T>(IEnumerable<T> items, Func<T, string> idOf, bool pack) => Lib.Grouped(items, idOf, pack, _view);

    public Control Strip(Action refresh, Control? beside = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        if (!Lib.Folders.Contains(_view)) _view = "";
        void Add(string label, string key)
        {
            var b = new Button { Text = label, ToggleMode = true, ButtonPressed = _view == key, FocusMode = Control.FocusModeEnum.None };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () => { _view = key; refresh(); };
            flow.AddChild(b);
        }
        Add("All", "");
        foreach (var f in Lib.Folders) Add(f, f);
        row.AddChild(flow);
        if (beside != null) row.AddChild(beside);
        var menu = new MenuButton { Text = "Folders ▾", FocusMode = Control.FocusModeEnum.None, TooltipText = "Folders are labels on this PC; nothing moves on disk" };
        var pm = menu.GetPopup();
        pm.AddItem("New folder…", 1);
        pm.AddItem(_view.Length > 0 ? $"Rename '{_view}'…" : "Rename folder… (open one first)", 2);
        pm.AddItem(_view.Length > 0 ? $"Delete folder '{_view}'" : "Delete folder (open one first)", 3);
        pm.SetItemDisabled(1, _view.Length == 0);
        pm.SetItemDisabled(2, _view.Length == 0);
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 1:
                    TextPrompt.Open(_parent, "New folder", "Name", "", n => { if (Lib.AddFolder(n)) { _view = Library.CleanName(n); refresh(); } });
                    break;
                case 2:
                    TextPrompt.Open(_parent, "Rename folder", "Name", _view, n => { if (Lib.RenameFolder(_view, n)) { _view = Library.CleanName(n); refresh(); } });
                    break;
                case 3:
                {
                    var f = _view;
                    Ui.Confirm(_parent, $"Delete folder \"{f}\"", "Delete", () =>
                    {
                        Lib.RemoveFolder(f);
                        _view = "";
                        refresh();
                    });
                    break;
                }
            }
        };
        row.AddChild(menu);
        return row;
    }
}

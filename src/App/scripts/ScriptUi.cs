using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

public partial class ScriptUi : Control
{
    Session _s = null!;
    Net _n = null!;
    readonly Dictionary<string, AcceptDialog> _dialogs = new();

    public void Bind(Session s, Net n)
    {
        _s = s;
        _n = n;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Rebuild();
    }

    public void Rebuild()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        if (!IsInstanceValid(this) || _s == null) return;
        var size = GetViewportRect().Size;
        var b = new UiBuilder(Send, (id, ready) => UiBuilder.AssetTexture(_s, id, ready));
        foreach (var n in _s.View.Ui)
        {
            var c = b.Build(n, size);
            if (c == null) continue;
            UiBuilder.Place(c, n, size, null);
            AddChild(c);
        }
    }

    void Send(string id, string ev, string value)
    {
        if (!_s.IsUmpire) return;
        _s.Do(new UiEvent { Id = id, Event = ev, Value = value });
    }

    public void ShowDialog(ShowDialog d)
    {
        if (d.To != null && d.To != $"p:{_s.Me.PeerId}") return;
        if (!_s.IsUmpire) return;
        if (d.Kind == "picture") { AskPicture(d); return; }
        var dlg = new AcceptDialog { Title = d.Title.Length > 0 ? d.Title : "Script", OkButtonText = "OK" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddThemeConstantOverride("separation", 6);
        dlg.AddChild(box);
        Func<string?> value;
        switch (d.Kind)
        {
            case "memo":
            {
                var te = new TextEdit { Text = d.Text, CustomMinimumSize = new Vector2(420, 220) };
                box.AddChild(te); value = () => te.Text;
                break;
            }
            case "input":
            {
                var le = new LineEdit { Text = d.Text };
                box.AddChild(le); value = () => le.Text;
                le.TextSubmitted += _ => dlg.EmitSignal(AcceptDialog.SignalName.Confirmed);
                break;
            }
            case "options" or "color":
            {
                box.AddChild(Ui.Hint(d.Text.Length > 0 && d.Kind == "options" ? d.Text : ""));
                var ob = new OptionButton();
                var opts = d.Kind == "color" ? new List<string> { "White", "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Brown", "Teal", "Grey", "Black" } : d.Options;
                foreach (var o in opts) ob.AddItem(o);
                var pre = opts.IndexOf(d.Text); if (pre >= 0) ob.Selected = pre;
                box.AddChild(ob); value = () => ob.ItemCount > 0 ? ob.GetItemText(ob.Selected) : null;
                break;
            }
            default:
                box.AddChild(Ui.Hint(d.Text)); value = () => "True";
                dlg.OkButtonText = "Yes";
                break;
        }
        bool answered = false;
        dlg.AddCancelButton("Cancel");
        dlg.Confirmed += () => { if (answered) return; answered = true; _s.Do(new DialogResult { Token = d.Token, Value = value() }); };
        dlg.Canceled += () => { if (answered) return; answered = true; _s.Do(new DialogResult { Token = d.Token, Value = null }); };
        dlg.CloseRequested += () => { if (answered) return; answered = true; _s.Do(new DialogResult { Token = d.Token, Value = null }); };
        _dialogs[d.Token] = dlg;
        dlg.TreeExiting += () => { if (_dialogs.GetValueOrDefault(d.Token) == dlg) _dialogs.Remove(d.Token); };
        Ui.Open(this, dlg);
        Ui.FreeOnClose(dlg);
    }

    void AskPicture(ShowDialog d)
    {
        var root = _s.Store.Root;
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter }, Title = d.Title.Length > 0 ? d.Title : "Picture", Size = new Vector2I(900, 600) };
        Ui.RememberFolder(fd);
        FileThumbs.Show(fd);
        bool answered = false;
        void Answer(string? path)
        {
            if (answered) return;
            answered = true;
            var name = path == null ? null : PictureLinks.Keep(root, path);
            if (path != null && name == null) _s.OnError($"{System.IO.Path.GetFileName(path)} could not be used as a picture");
            var result = new DialogResult { Token = d.Token, Value = name };
            if (name == null || _n.Server != null) _s.Do(result);
            else _n.ShareThen(new[] { name }, result);
        }
        PictureLinks.Offer(fd, root, Answer);
        fd.FileSelected += Answer;
        fd.Canceled += () => Answer(null);
        fd.CloseRequested += () => Answer(null);
        AddChild(fd);
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }
}

using System;
using System.Collections.Generic;
using Godot;
using KTable.Client.Panels;
using KTable.Core.Panels;
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

    readonly Dictionary<string, string> _local = new();
    PanelView? _view;

    readonly HashSet<string> _hiding = new();
    readonly HashSet<string> _said = new();

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) PanelFocus.Pressed(mb.GlobalPosition);
        else if (e is InputEventKey key && PanelFocus.Key(key)) GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        PanelFocus.Forget(_local);
        PanelCursors.Reset();
    }

    public void Applied(Command c)
    {
        foreach (var one in PanelChanges.Flat(c))
        {
            if (one is SetUi { Piece: null })
            {
                _local.Clear();
                _said.Clear();
            }
            else if (one is SetUiAttributes { Piece: null } sua) PanelChanges.Forget(_local, _s.View.UiLayout, sua.Id);
        }
        var moves = PanelMoves.Of(c, _s.View.UiLayout, null);
        foreach (var m in moves) if (!m.Showing) _hiding.Add(PanelScene.Key(m.Path));
        Rebuild();
        foreach (var m in moves)
        {
            _view?.Announce(m);
            if (_view?.Scene?.At(m.Path) is not { } box || !_view.Nodes.TryGetValue(box, out var node)) continue;
            var key = PanelScene.Key(m.Path);
            PanelAnimate.Play(node, m, m.Showing ? null : () => { if (_hiding.Remove(key)) Rebuild(); });
        }
    }

    public void Rebuild()
    {
        foreach (var c in GetChildren()) if (c is not AcceptDialog and not FileDialog) c.QueueFree();
        if (!IsInstanceValid(this) || _s == null) return;
        var layout = _s.View.UiLayout ?? (_s.View.Ui.Count > 0 ? PanelState.Legacy(_s.View.Ui) : null);
        if (layout == null || layout.Elements.Count == 0) return;
        var screen = GetViewportRect().Size;
        float scale = Mathf.Max(0.01f, screen.Y / PanelScene.ScreenHeight);
        var canvas = new V2(screen.X / scale, PanelScene.ScreenHeight);
        var (color, host, admin) = PanelSeats.Of(_s.Me, _s.Peers);
        layout = PanelClasses.Apply(layout, _local);
        var scene = PanelScene.Build(layout, canvas, new PanelContext(layout.Colors, color, host, admin), PanelPictures.Meter(_s), _local, _hiding);
        _view = new PanelView(PanelHost.Hooks(_s, Send, (name, ready) => PanelPictures.Texture(_s, name, ready), _local, () => Callable.From(Rebuild).CallDeferred(), _said));
        var root = _view.Show(scene, scene.Root);
        root.Scale = new Vector2(scale, scale);
        AddChild(root);
    }

    void Send(List<int> path, string ev, string value)
    {
        if (!_s.IsUmpire) return;
        _s.Do(new UiEvent { Path = path, Event = ev, Value = value });
    }

    public void ShowDialog(ShowDialog d)
    {
        if (d.To != null && d.To != $"p:{_s.Me.PeerId}") return;
        if (!_s.IsUmpire) return;
        if (d.Kind == "picture") { AskPicture(d); return; }
        var dlg = new AcceptDialog { Title = d.Title.Length > 0 ? d.Title : "Script", OkButtonText = "OK" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddThemeConstantOverride("separation", Ui.Gap.Row);
        dlg.AddChild(box);
        Func<string?> value;
        switch (d.Kind)
        {
            case "memo":
            {
                var te = new TextEdit { Text = d.Text, CustomMinimumSize = new Vector2(420, 220) };
                box.AddChild(te);
                value = () => te.Text;
                break;
            }
            case "input":
            {
                var le = new LineEdit { Text = d.Text };
                box.AddChild(le);
                value = () => le.Text;
                le.TextSubmitted += _ => dlg.EmitSignal(AcceptDialog.SignalName.Confirmed);
                break;
            }
            case "options" or "color":
            {
                box.AddChild(Ui.Hint(d.Text.Length > 0 && d.Kind == "options" ? d.Text : ""));
                var ob = new OptionButton();
                var opts = d.Kind == "color" ? new List<string> { "White", "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Brown", "Teal", "Grey", "Black" } : d.Options;
                foreach (var o in opts) ob.AddItem(o);
                var pre = opts.IndexOf(d.Text);
                if (pre >= 0) ob.Selected = pre;
                box.AddChild(ob);
                value = () => ob.ItemCount > 0 ? ob.GetItemText(ob.Selected) : null;
                break;
            }
            default:
                box.AddChild(Ui.Hint(d.Text));
                value = () => "True";
                dlg.OkButtonText = "Yes";
                break;
        }
        bool answered = false;
        dlg.AddCancelButton("Cancel");
        dlg.Confirmed += () =>
        {
            if (answered) return;
            answered = true;
            _s.Do(new DialogResult { Token = d.Token, Value = value() });
        };
        dlg.Canceled += () =>
        {
            if (answered) return;
            answered = true;
            _s.Do(new DialogResult { Token = d.Token, Value = null });
        };
        dlg.CloseRequested += () =>
        {
            if (answered) return;
            answered = true;
            _s.Do(new DialogResult { Token = d.Token, Value = null });
        };
        _dialogs[d.Token] = dlg;
        dlg.TreeExiting += () => { if (_dialogs.GetValueOrDefault(d.Token) == dlg) _dialogs.Remove(d.Token); };
        Ui.Open(this, dlg);
        Ui.FreeOnClose(dlg);
    }

    void AskPicture(ShowDialog d)
    {
        var root = _s.Store.Root;
        var fd =
            new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter }, Title = d.Title.Length > 0 ? d.Title : "Picture",
                Size = new Vector2I(900, 600) };
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

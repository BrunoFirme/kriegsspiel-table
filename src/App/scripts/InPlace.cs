using System;
using Godot;

namespace KTable.App;

public sealed class Question
{
    internal Action<string>? OnCancelText;
    internal Action<string, Action>? OnAlso;

    public event Action? Canceled;
    public event Action? Closed;

    public string CancelButtonText { set => OnCancelText?.Invoke(value); }

    public void Also(string text, Action act) => OnAlso?.Invoke(text, act);

    internal void Cancel() => Canceled?.Invoke();
    internal void Close() => Closed?.Invoke();
}

static class InPlace
{
    const string SubKey = "in_place_sub";
    const float SheetWidth = 480;
    const int BarePadding = 10;
    static readonly Color Scrim = new(0, 0, 0, 0.42f);

    public static Window? Owner(Node n) =>
        n.IsInsideTree() && n.GetWindow() is { } w && w != n.GetTree().Root ? w : null;

    public static Question Ask(Node parent, string title, string okText, Action onOk, string? text, Control? body = null, Control? focus = null, int width = 360) =>
        Owner(parent) is { } w ? Sheet(w, title, okText, onOk, text, body, focus) : Dialog(parent, title, okText, onOk, text, body, focus, width);

    static Question Dialog(Node parent, string title, string okText, Action onOk, string? text, Control? body, Control? focus, int width)
    {
        var dlg = new ConfirmationDialog { Title = title, OkButtonText = okText };
        Ui.NoCloseButton(dlg);
        var q = new Question();
        if (text != null)
        {
            dlg.DialogText = text;
            dlg.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
            dlg.GetLabel().CustomMinimumSize = new Vector2(width, 0);
        }
        else
        {
            dlg.GetLabel().Visible = false;
            if (body == null) Bare(dlg);
        }
        if (body != null) dlg.AddChild(body);
        if (focus is LineEdit le) dlg.RegisterTextEnter(le);
        int also = 0;
        q.OnCancelText = t => dlg.CancelButtonText = t;
        q.OnAlso = (t, act) =>
        {
            var key = "also" + also++;
            dlg.AddButton(t, false, key);
            dlg.CustomAction += a => { if (a == key) { dlg.Hide(); act(); } };
        };
        dlg.Confirmed += onOk;
        dlg.Canceled += q.Cancel;
        dlg.VisibilityChanged += () => { if (!dlg.Visible) Callable.From(q.Close).CallDeferred(); };
        Ui.Open(parent, dlg, body != null ? null : text != null ? new Vector2I(width + 28, 60) : new Vector2I(200, 20));
        Ui.FreeOnClose(dlg);
        if (text == null && body == null) Callable.From(() => Fit(dlg)).CallDeferred();
        focus?.CallDeferred(Control.MethodName.GrabFocus);
        return q;
    }

    static void Bare(ConfirmationDialog dlg)
    {
        var panel = Ui.WindowFill(Ui.Raised, BarePadding, BarePadding);
        panel.ContentMarginTop = 0;
        dlg.AddThemeStyleboxOverride("panel", panel);
        dlg.AddThemeConstantOverride("buttons_separation", 0);
        dlg.MinSize = dlg.MinSize with { Y = 0 };
    }

    static void Fit(Window dlg)
    {
        if (!GodotObject.IsInstanceValid(dlg) || !dlg.Visible) return;
        dlg.Size = dlg.Size with { Y = (int)Mathf.Ceil(dlg.GetContentsMinimumSize().Y) };
        dlg.MoveToCenter();
    }

    static Question Sheet(Window owner, string title, string okText, Action onOk, string? text, Control? body, Control? focus)
    {
        var q = new Question();
        var scrim = new SheetScrim { Color = Scrim, MouseFilter = Control.MouseFilterEnum.Stop };
        scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var sheet = new PanelContainer();
        sheet.AddThemeStyleboxOverride("panel", Ui.WindowFill(Ui.Raised, 18, 14));
        sheet.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        sheet.GrowVertical = Control.GrowDirection.Begin;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Block);
        sheet.AddChild(box);
        var head = new Label { Text = title };
        head.AddThemeFontOverride("font", Ui.SansStrong());
        head.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(head);
        if (text != null) box.AddChild(Capped(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }));
        if (body != null) box.AddChild(Capped(body));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var cancel = Ui.Btn("Cancel", () => scrim.Answer(false));
        var ok = Ui.Primary(okText, () => scrim.Answer(true));
        row.AddChild(cancel);
        row.AddChild(ok);
        box.AddChild(row);
        scrim.AddChild(sheet);

        var closeOnEscape = owner is AcceptDialog ad && ad.DialogCloseOnEscape;
        if (owner is AcceptDialog held) held.DialogCloseOnEscape = false;
        scrim.Answered += yes =>
        {
            if (owner is AcceptDialog back && GodotObject.IsInstanceValid(back)) back.DialogCloseOnEscape = closeOnEscape;
            if (yes == true) onOk();
            else if (yes == false) q.Cancel();
            q.Close();
        };
        q.OnCancelText = t => cancel.Text = t;
        q.OnAlso = (t, act) =>
        {
            var b = Ui.Btn(t, () => { scrim.Answer(null); act(); });
            row.AddChild(b);
            row.MoveChild(b, cancel.GetIndex());
        };
        if (focus is LineEdit le) le.TextSubmitted += _ => scrim.Answer(true);
        owner.AddChild(scrim, false, Node.InternalMode.Back);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(scrim)) return;
            if ((focus ?? ok) is { FocusMode: not Control.FocusModeEnum.None } target) target.GrabFocus();
        }).CallDeferred();
        return q;
    }

    static HBoxContainer Capped(Control c)
    {
        var row = new HBoxContainer();
        c.CustomMinimumSize = c.CustomMinimumSize with { X = Math.Max(c.CustomMinimumSize.X, SheetWidth) };
        c.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        row.AddChild(c);
        return row;
    }

    public static VBoxContainer SubPage(Control page, string title, string back)
    {
        if (page.HasMeta(SubKey) && page.GetMeta(SubKey).AsGodotObject() is Control open && GodotObject.IsInstanceValid(open)) open.QueueFree();
        var host = page.GetParent();
        var sub = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        sub.AddThemeConstantOverride("separation", Ui.Gap.Block);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var backBtn = new Button { Text = "‹ " + back, Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        backBtn.AddThemeColorOverride("font_color", Ui.Muted);
        backBtn.AddThemeColorOverride("font_hover_color", Ui.Ink);
        head.AddChild(backBtn);
        var name = new Label { Text = title, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        name.AddThemeFontOverride("font", Ui.SansStrong());
        name.AddThemeFontSizeOverride("font_size", 15);
        head.AddChild(name);
        sub.AddChild(head);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", Ui.Gap.Group);
        sub.AddChild(body);
        host.AddChild(sub);
        host.MoveChild(sub, page.GetIndex() + 1);
        page.Visible = false;
        page.SetMeta(SubKey, sub);
        backBtn.Pressed += () =>
        {
            if (GodotObject.IsInstanceValid(page)) { page.Visible = true; page.RemoveMeta(SubKey); }
            sub.QueueFree();
        };
        return body;
    }
}

sealed partial class SheetScrim : ColorRect
{
    bool _done;
    public event Action<bool?>? Answered;

    public void Answer(bool? yes)
    {
        if (_done) return;
        _done = true;
        QueueFree();
        Answered?.Invoke(yes);
    }

    public override void _Input(InputEvent e)
    {
        if (_done || !IsVisibleInTree()) return;
        if (e.IsActionPressed("ui_cancel")) { GetViewport().SetInputAsHandled(); Answer(false); }
        else if (e.IsActionPressed("ui_accept") && GetViewport().GuiGetFocusOwner() is not (TextEdit or LineEdit or Button)) { GetViewport().SetInputAsHandled(); Answer(true); }
    }
}

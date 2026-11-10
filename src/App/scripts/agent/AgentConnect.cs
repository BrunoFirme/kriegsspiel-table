using Godot;

namespace KTable.App;

static class AgentConnect
{
    public const string Title = "Connect an AI agent…";
    const string PageTitle = "Connect an AI agent", Back = "AI agent";
    const int Mark = 24;
    static readonly string[] Asks = { "Build a block for the 2nd Hussars.", "Give the guns a Fire button.", "Lay out these two brigades for the opening." };

    static (HBoxContainer Head, VBoxContainer Body) Step(VBoxContainer box, int n, string title)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Block);
        var mark =
            new Label { Text = n.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(Mark, Mark),
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        mark.AddThemeStyleboxOverride("normal", Ui.Box(new Color(1, 1, 1, 0.08f), Mark / 2, 0, 0));
        mark.AddThemeFontOverride("font", Ui.SansStrong());
        mark.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(mark);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", Ui.Gap.Row);
        row.AddChild(body);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var name = new Label { Text = title, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, Mark) };
        name.AddThemeFontOverride("font", Ui.SansStrong());
        name.AddThemeFontSizeOverride("font_size", 15);
        head.AddChild(name);
        body.AddChild(head);
        box.AddChild(row);
        return (head, body);
    }

    static Label Status()
    {
        var l = new Label { VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    public static void Show(VBoxContainer page, AgentEndpoint a, Settings cfg)
    {
        a.TurnOn();
        bool copied = a.On;
        if (copied) AppClipboard.SetText(a.Note);
        var box = AgentFence.Mark(InPlace.SubPage(page, PageTitle, Back));
        box.AddThemeConstantOverride("separation", Ui.Gap.Section);

        var (pasteHead, pasteBody) = Step(box, 1, "Paste the note into your agent's chat");
        var pasted = Status();
        pasteHead.AddChild(pasted);
        var copy = new Button { TooltipText = "The note for your agent", FocusMode = Control.FocusModeEnum.None };
        pasteHead.AddChild(copy);
        var note = new TextEdit { Text = a.Note, Editable = false, Visible = false, WrapMode = TextEdit.LineWrappingMode.Boundary, CustomMinimumSize = new Vector2(0, 150) };
        note.AddThemeFontSizeOverride("font_size", 12);
        var show = new LinkButton { Text = "Show the note", Underline = LinkButton.UnderlineMode.OnHover, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        show.AddThemeFontSizeOverride("font_size", 12);
        show.AddThemeColorOverride("font_color", Ui.Muted);
        show.Pressed += () => { note.Visible = !note.Visible; show.Text = note.Visible ? "Hide the note" : "Show the note"; };
        pasteBody.AddChild(show);
        pasteBody.AddChild(note);

        var (readyHead, _) = Step(box, 2, "Wait for it to say it is ready");
        var reached = Status();
        readyHead.AddChild(reached);

        var (_, askBody) = Step(box, 3, "Ask for what you need");
        foreach (var ask in Asks) askBody.AddChild(Ui.Hint("“" + ask + "”"));

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", Ui.Gap.Group);
        actions.AddChild(Ui.Btn("Technical settings…", () => AgentPanel.Show(page, a, cfg), "Address, port, key and the agent's settings file, for doing it by hand"));
        var power = Ui.Btn("", () => { if (a.On) a.TurnOff(); else a.TurnOn(); });
        Ui.MakeQuiet(power);
        actions.AddChild(power);
        box.AddChild(actions);
        box.AddChild(AgentNotice.Line(page));

        copy.Pressed += () =>
        {
            if (!a.On) a.TurnOn();
            if (!a.On) return;
            AppClipboard.SetText(a.Note);
            copied = true;
            Sync();
        };

        void Sync()
        {
            if (!GodotObject.IsInstanceValid(box)) return;
            note.Text = a.Note;
            power.Text = a.On ? "Switch off" : "Switch on";
            copy.Text = copied && a.On ? "Copy again" : "Copy";
            pasted.Text = !a.On ? a.Problem ?? "Switched off" : copied ? "Copied" : "";
            pasted.Modulate = !a.On ? (a.Problem != null ? Ui.Danger : Ui.Muted) : Ui.Good;
            pasted.AutowrapMode = a.Problem != null && !a.On ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
            pasted.SizeFlagsHorizontal = a.Problem != null && !a.On ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkEnd;
            reached.Text = !a.On ? "" : a.Calls > 0 ? $"Connected · {a.Calls} call{(a.Calls == 1 ? "" : "s")}" : "Waiting";
            reached.Modulate = a.Calls > 0 ? Ui.Good : Ui.Muted;
        }
        a.Changed += Sync;
        box.TreeExiting += () => a.Changed -= Sync;
        Sync();
    }
}

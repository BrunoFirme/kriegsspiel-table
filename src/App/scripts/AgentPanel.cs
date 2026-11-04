using System;
using Godot;

namespace KTable.App;

static class AgentPanel
{
    static HBoxContainer Row(VBoxContainer box, string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        box.AddChild(row);
        return row;
    }

    public static void Simple(VBoxContainer box, AgentEndpoint a, Settings cfg)
    {
        box.AddThemeConstantOverride("separation", 8);
        var state = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(state);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Ui.Btn(AgentConnect.Title, () => AgentConnect.Show(box, a, cfg), "Switches it on and copies a note to paste into your agent's chat"));
        var technical = Ui.Btn("Technical settings…", () => Show(box, a, cfg), "Helper command, address, port, key and the agent's settings file, for doing it by hand");
        Ui.MakeQuiet(technical);
        row.AddChild(technical);
        var off = Ui.Btn("Switch off", () => a.TurnOff(), "Your agent can no longer use the app until you connect again");
        Ui.MakeQuiet(off);
        row.AddChild(off);
        box.AddChild(row);
        box.AddChild(AgentNotice.Line(box));
        void Sync()
        {
            if (!GodotObject.IsInstanceValid(box)) return;
            state.Text = a.On ? $"On: an AI agent on this PC can use the app{(a.Calls > 0 ? $" ({a.Calls} call{(a.Calls == 1 ? "" : "s")} so far)" : "")}." : a.Problem ?? "Off. An AI agent on this PC can use the app once connected.";
            state.Modulate = a.Problem != null && !a.On ? Ui.Danger : Ui.Muted;
            off.Visible = a.On;
        }
        a.Changed += Sync;
        box.TreeExiting += () => a.Changed -= Sync;
        Sync();
    }

    public static void Build(VBoxContainer box, AgentEndpoint a, Settings cfg)
    {
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(AgentNotice.Line(box));
        var on = new CheckBox { Text = "AI agent on", ButtonPressed = a.On, FocusMode = Control.FocusModeEnum.None };
        box.AddChild(on);
        var state = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) };
        box.AddChild(state);
        box.AddChild(Ui.Hint("An MCP server on this PC only. The helper speaks stdio and finds or opens the app by itself; the address speaks Streamable HTTP with the key in an Authorization header. The agent acts with your seat's rights and the log names its changes agent. It stays on until switched off, restarts included."));

        var helper = new LineEdit { Editable = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var helperRow = Row(box, "Helper (stdio)");
        helperRow.AddChild(helper);
        helperRow.AddChild(Ui.Btn("Copy", () => AppClipboard.SetText(a.HelperCommand), "The command an agent runs to reach the app"));

        var address = new LineEdit { Editable = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var addressRow = Row(box, "Address (HTTP)");
        addressRow.AddChild(address);
        addressRow.AddChild(Ui.Btn("Copy", () => AppClipboard.SetText(a.Address)));

        var token = new LineEdit { Editable = false, Secret = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var tokenRow = Row(box, "Key");
        tokenRow.AddChild(token);
        tokenRow.AddChild(Ui.Btn("Copy the header", () => AppClipboard.SetText(a.Header), "Authorization: Bearer and the key"));
        tokenRow.AddChild(Ui.Btn("New key", () => Ui.Confirm(box, "A new key", "Make a new one", a.RenewToken, "An agent set up by hand over HTTP loses its way in until it is given the new one; the helper picks it up by itself."), "The old key stops working"));

        var port = new SpinBox { MinValue = 1024, MaxValue = 65535, Step = 1, Value = cfg.AgentPort, Rounded = true, CustomMinimumSize = new Vector2(110, 0), TooltipText = "When it is taken, the next free one is used and kept" };
        Row(box, "Port").AddChild(port);
        port.ValueChanged += v => a.SetPort((int)v);

        var config = Row(box, "MCP settings");
        config.AddChild(Ui.Btn("Copy for the helper", () => AppClipboard.SetText(a.Config), "An mcpServers entry that runs the helper"));
        config.AddChild(Ui.Btn("Copy for HTTP", () => AppClipboard.SetText(a.HttpConfig), "An mcpServers entry with the address and the key"));

        void Sync()
        {
            if (!GodotObject.IsInstanceValid(box)) return;
            on.SetPressedNoSignal(a.On);
            token.Text = a.Token;
            address.Text = a.Address;
            helper.Text = a.HelperCommand.Length > 0 ? a.HelperCommand : "not found next to the app";
            if (!port.HasFocus()) port.SetValueNoSignal(a.Port);
            state.Text = a.On ? $"On at {a.Address} · {a.Calls} call{(a.Calls == 1 ? "" : "s")}" : a.Problem ?? "Off.";
            state.Modulate = a.Problem != null && !a.On ? Ui.Danger : a.On ? Ui.Ink : Ui.Muted;
        }
        on.Toggled += v =>
        {
            if (v) a.TurnOn();
            else a.TurnOff();
            Sync();
        };
        a.Changed += Sync;
        box.TreeExiting += () => a.Changed -= Sync;
        Sync();
    }

    public static void Show(VBoxContainer page, AgentEndpoint a, Settings cfg)
    {
        Build(InPlace.SubPage(page, "Technical settings", "AI agent"), a, cfg);
    }
}

using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class SliderValueTests
{
    static async Task<(HSlider Slider, LineEdit Box, Control Holder)> Pair(TestRunner r, double min, double max, double step, double value, System.Func<double, string> show)
    {
        var holder = new HBoxContainer();
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(120, 0) };
        holder.AddChild(s);
        var box = Ui.ValueBox(s, show, 48);
        holder.AddChild(box);
        r.AddChild(holder);
        await r.Frames(1);
        return (s, box, holder);
    }

    static async Task Type(TestRunner r, LineEdit box, string text)
    {
        box.GrabFocus();
        box.Text = text;
        box.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
        await r.Frames(1);
    }

    [NodeTest]
    static async Task A_typed_number_moves_the_slider_to_the_value_it_shows(TestRunner r)
    {
        var (s, box, holder) = await Pair(r, 0, 1, 0.05, 0.6, v => $"{v * 100:0}%");
        bool ended = false;
        s.DragEnded += changed => ended = changed;
        await Type(r, box, "35");
        Check.That(Mathf.IsEqualApprox(s.Value, 0.35), $"35 on a percent readout sets 0.35, got {s.Value}");
        Check.That(ended, "a typed value finishes like a drag, so it is saved and applied");
        Check.Equal("35%", box.Text, "the box shows the value as the readout writes it");
        holder.QueueFree();
    }

    [NodeTest]
    static async Task A_sum_typed_in_the_box_works_on_the_value_it_shows(TestRunner r)
    {
        var (s, box, holder) = await Pair(r, 0.2, 400, 0.1, 6, v => $"{v:0.#} u");
        await Type(r, box, "*1.5");
        Check.That(Mathf.IsEqualApprox(s.Value, 9), $"*1.5 makes 6 u 9 u, got {s.Value}");
        await Type(r, box, "-1.5");
        Check.That(Mathf.IsEqualApprox(s.Value, 7.5), $"where nothing is below zero, -1.5 takes 1.5 off: {s.Value}");
        await Type(r, box, "x/3+0.5");
        Check.That(Mathf.IsEqualApprox(s.Value, 3), $"x is the value there: {s.Value}");
        await Type(r, box, "(2+3)*4 u");
        Check.That(Mathf.IsEqualApprox(s.Value, 20), $"a sum in full, unit and all: {s.Value}");
        await Type(r, box, "3/0");
        Check.That(Mathf.IsEqualApprox(s.Value, 20) && box.Text == "20 u", "a sum that cannot be worked out changes nothing");
        Check.That(box.TooltipText.Contains("sum", System.StringComparison.Ordinal), "the tooltip says sums work: " + box.TooltipText);
        holder.QueueFree();
    }

    [NodeTest]
    static async Task A_spin_box_works_out_a_sum_on_its_value_when_it_is_left_or_entered(TestRunner r)
    {
        var spin = new SpinBox { MinValue = 0, MaxValue = 1000, Step = 0.01, Value = 10, Suffix = "px" };
        Ui.Sums(spin);
        r.AddChild(spin);
        await r.Frames(1);
        var edit = spin.GetLineEdit();
        void Typed(string text)
        {
            edit.Text = text;
            edit.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }
        edit.EmitSignal(Control.SignalName.FocusEntered);
        Typed("*1.5");
        edit.EmitSignal(Control.SignalName.FocusExited);
        await r.Frames(2);
        Check.That(Mathf.IsEqualApprox(spin.Value, 15), $"left, *1.5 makes 10 15: {spin.Value}");
        edit.EmitSignal(Control.SignalName.FocusEntered);
        Typed("x-2.5");
        edit.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = Key.Enter, Pressed = true });
        edit.EmitSignal(LineEdit.SignalName.TextSubmitted, edit.Text);
        await r.Frames(2);
        Check.That(Mathf.IsEqualApprox(spin.Value, 12.5), $"entered, x-2.5 takes 2.5 off: {spin.Value}");
        var changes = new System.Collections.Generic.List<double>();
        spin.ValueChanged += v => changes.Add(v);
        edit.EmitSignal(Control.SignalName.FocusEntered);
        Typed("-2.5");
        edit.EmitSignal(Control.SignalName.FocusExited);
        await r.Frames(2);
        Check.That(Mathf.IsEqualApprox(spin.Value, 10), $"where nothing is below zero, -2.5 takes 2.5 off: {spin.Value}");
        Check.Equal("10", string.Join(", ", changes), "and the value changes once, straight to it");
        spin.QueueFree();
    }

    [NodeTest]
    static async Task A_typed_value_beyond_the_ends_stops_at_the_end_and_words_are_refused(TestRunner r)
    {
        var (s, box, holder) = await Pair(r, 0.2, 400, 0.1, 6, v => $"{v:0.#} u");
        await Type(r, box, "1000");
        Check.That(Mathf.IsEqualApprox(s.Value, 400), $"past the right end the slider stops there, got {s.Value}");
        await Type(r, box, "abc");
        Check.That(Mathf.IsEqualApprox(s.Value, 400), "a word changes nothing");
        Check.Equal("400 u", box.Text, "and the box shows the value again");
        await Type(r, box, "12.5");
        Check.That(Mathf.IsEqualApprox(s.Value, 12.5), $"a decimal lands on its own value, got {s.Value}");
        holder.QueueFree();
    }

    [NodeTest]
    static async Task A_comma_typed_as_the_decimal_mark_reads_as_one(TestRunner r)
    {
        var (s, box, holder) = await Pair(r, 0.05, 1, 0.05, 0.35, v => $"{v * 100:0}%");
        await Type(r, box, "0,5");
        Check.That(Mathf.IsEqualApprox(s.Value, 0.05), $"0,5 on a percent readout is half a percent, the nearest being the left end, got {s.Value}");
        var (s2, box2, holder2) = await Pair(r, 0, 5, 0.05, 1, v => $"{v:0.##}");
        await Type(r, box2, "1,25");
        Check.That(Mathf.IsEqualApprox(s2.Value, 1.25), $"1,25 is one and a quarter, not 125, got {s2.Value}");
        var (s3, box3, holder3) = await Pair(r, 0, 3000, 1, 10, v => $"{v:#,0} m");
        await Type(r, box3, "1.6");
        Check.That(Mathf.IsEqualApprox(s3.Value, 2), $"a readout written with thousands still reads its own numbers, got {s3.Value}");
        await Type(r, box3, "1200");
        Check.That(Mathf.IsEqualApprox(s3.Value, 1200), $"1200 on a readout that shows 1,200 m lands on 1200, got {s3.Value}");
        box3.GrabFocus();
        await r.Frames(1);
        box3.ReleaseFocus();
        await r.Frames(1);
        Check.That(Mathf.IsEqualApprox(s3.Value, 1200), $"a box left as it was keeps its value, got {s3.Value}");
        await Type(r, box3, "1,300 m");
        Check.That(Mathf.IsEqualApprox(s3.Value, 1300), $"a box that writes 1,200 m reads 1,300 m as thirteen hundred, got {s3.Value}");
        holder.QueueFree();
        holder2.QueueFree();
        holder3.QueueFree();
    }

    [NodeTest]
    static async Task Escape_puts_the_value_back_and_dragging_keeps_the_box_current(TestRunner r)
    {
        var (s, box, holder) = await Pair(r, 1, 40, 0.5, 4, v => $"{v:0.#}");
        box.GrabFocus();
        box.Text = "30";
        box.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await r.Frames(1);
        Check.That(Mathf.IsEqualApprox(s.Value, 4), "Escape leaves the slider where it was");
        Check.Equal("4", box.Text, "and shows its value again");
        s.Value = 12;
        Check.Equal("12", box.Text, "moving the slider updates the box");
        holder.QueueFree();
    }

    [NodeTest]
    static async Task A_readout_made_of_words_cannot_be_typed_into(TestRunner r)
    {
        var (_, box, holder) = await Pair(r, 0, 1, 0.1, 0.5, v => v < 0.5 ? "paper" : "grey");
        Check.That(!box.Editable, "a slider whose readout is words keeps a plain readout");
        holder.QueueFree();
    }
}

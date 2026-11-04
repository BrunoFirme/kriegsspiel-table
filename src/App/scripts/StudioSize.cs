using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class SizeForm
{
    const string TtsBlock = "a TTS block is about 1.4–2 u wide";

    public static Label Rows(GridContainer g, StudioPrefs prefs, Size2 now, string depth, Action<Size2> set, bool enabled = true)
    {
        var size = now;
        g.AddChild(new Label { Text = "Width" });
        var wBox = new SpinBox { MinValue = 0.02, MaxValue = 500, Step = 0.01, Value = size.W, Suffix = "u", Editable = enabled };
        g.AddChild(wBox);
        g.AddChild(new Label { Text = depth });
        var hBox = new SpinBox { MinValue = 0.02, MaxValue = 500, Step = 0.01, Value = size.H, Suffix = "u", Editable = enabled };
        g.AddChild(hBox);
        g.AddChild(new Label { Text = "" });
        var keep = new CheckBox { Text = "Keep the proportions", ButtonPressed = true, Disabled = !enabled };
        g.AddChild(keep);
        var metres = Ui.Hint("");
        void Metres() => metres.Text = $"≈ {size.W * prefs.MetresPerUnit:0} × {size.H * prefs.MetresPerUnit:0} m at {prefs.MetresPerUnit:0.#} m per unit · {TtsBlock}";
        Metres();
        bool sync = false;
        double? Aspect() => size.H > 1e-9 ? size.W / size.H : null;
        void Set(Size2 s)
        {
            size = s;
            set(s);
            Metres();
        }
        wBox.ValueChanged += v =>
        {
            if (sync) return;
            double h = keep.ButtonPressed && Aspect() is { } a ? Math.Round(v / a, 4) : size.H;
            sync = true;
            hBox.Value = h;
            sync = false;
            Set(new Size2(v, h));
        };
        hBox.ValueChanged += v =>
        {
            if (sync) return;
            double w = keep.ButtonPressed && Aspect() is { } a ? Math.Round(v * a, 4) : size.W;
            sync = true;
            wBox.Value = w;
            sync = false;
            Set(new Size2(w, v));
        };
        return metres;
    }

    public static Control Tint(string hex, string tip, Action<string> set)
    {
        var row = new HBoxContainer();
        var tint = new ColorPickerButton { Color = Coords.ParseColor(hex, Colors.White), CustomMinimumSize = new Vector2(48, 24), EditAlpha = false, TooltipText = tip };
        tint.PopupClosed += () => set("#" + tint.Color.ToHtml(false));
        row.AddChild(tint);
        row.AddChild(Ui.Btn("White", () => { tint.Color = Colors.White; set("#FFFFFF"); }));
        return row;
    }
}

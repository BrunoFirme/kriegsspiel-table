using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Prefs;

namespace KTable.App;

static class TintRowTests
{
    [NodeTest]
    static async Task A_tint_chip_keeps_the_tint_for_the_next_start_and_says_so(TestRunner r)
    {
        var cfg = new Settings();
        var box = new VBoxContainer();
        AppSettings.AddTint(box, cfg);
        r.AddChild(box);
        await r.Frame();
        Button[] Chips() => box.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b is not ColorPickerButton && !b.IsQueuedForDeletion()).ToArray();
        Label Next() => box.FindChildren("*", "Label", true, false).OfType<Label>().First(l => l.Text.StartsWith("From the next start"));
        Check.Equal(InterfaceTint.Presets.Count, Chips().Length, "a chip per tint");
        Check.That(!Next().IsVisibleInTree(), "the tint in use needs no note");
        var walnut = InterfaceTint.Presets.First(p => p.Name == "Walnut");
        Chips().First(b => b.TooltipText == walnut.Name).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        Check.Equal(walnut.Tint, cfg.Tint, "walnut kept");
        Check.That(Next().IsVisibleInTree(), "a new tint waits for the next start");
        Check.Equal(InterfaceTint.Presets.Count, Chips().Length, "chips redrawn once");
        Chips().First(b => b.TooltipText == "Charcoal").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        Check.Equal(InterfaceTint.Charcoal, cfg.Tint, "charcoal kept");
        Check.That(!Next().IsVisibleInTree(), "back to the tint in use");
        ColorPickerButton Own() => box.FindChildren("*", "ColorPickerButton", true, false).OfType<ColorPickerButton>().Single(b => !b.IsQueuedForDeletion());
        StyleBoxFlat Face(Control c) => (StyleBoxFlat)c.GetThemeStylebox("normal");
        Check.Equal(Chips()[0].CustomMinimumSize, Own().CustomMinimumSize, "the picker is a chip's size");
        Check.Equal(Own().Color, Face(Own()).BgColor, "the picker's chip carries its colour as the presets do");
        Own().Color = new Color("#336699");
        Own().EmitSignal(ColorPickerButton.SignalName.PopupClosed);
        await r.Frame();
        await r.Frame();
        Check.Equal("#336699", cfg.Tint, "a picked colour kept");
        Check.Equal(Ui.AccentInk, Face(Own()).BorderColor, "the picker is lit for a tint of its own");
        Check.That(Chips().All(b => Face(b).BorderColor != Ui.AccentInk), "no preset lit");
        box.QueueFree();
        await r.Frame();
    }
}

using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PickZoneRows : HudPart
{
    readonly Func<GridContainer, string, double, double, double, double, Func<double, string>, Action<double>, string, HSlider> _held;

    public PickZoneRows(HudContext ctx, Func<GridContainer, string, double, double, double, double, Func<double, string>, Action<double>, string, HSlider> held) : base(ctx)
    {
        _held = held;
    }

    const float CoatSwatchPx = 22;
    const double ShareStep = 0.05;
    const string Peace = "peace", War = "war";
    const string LivesOnTip = "Its living from the land, from none to all · laid again at once";
    const string HeldByTip = "The culture of the troops that hold it", CoatTip = "Their coat · the chosen colour again for the culture's usual";

    static readonly (string Key, string Text, string Tip)[] States =
    {
        (Peace, "Peace", "At peace"), (War, "War", "At war; the enemy is the umpire's to name"),
    };

    static readonly (string Key, string Text, string Tip)[] Riches =
    {
        (nameof(WealthLevel.Poor), "Poor", "A poor household"), (nameof(WealthLevel.Middling), "Middling", "A household of middling means"),
        (nameof(WealthLevel.Rich), "Rich", "A rich household"),
    };

    BuildZone Zone => T.Build.Zone;

    static (string Key, string Text, string Tip, Texture2D? Sign)[] Cultures => BuildPanel.AllCultures();

    static T Shut<T>(T control, bool shut) where T : Control
    {
        if (control is BaseButton own) own.Disabled = shut;
        foreach (var button in control.FindChildren("*", "BaseButton", true, false).OfType<BaseButton>()) button.Disabled = shut;
        return control;
    }

    void Picker(GridContainer form, string label, string now, Action<string> pick)
    {
        form.AddChild(new Label { Text = label });
        form.AddChild(Shut(BuildPanel.CulturePicker(Cultures, now, pick), !Zone.May));
    }

    void Chips(GridContainer form, string label, (string Key, string Text, string Tip)[] items, string now, Action<string> pick)
    {
        form.AddChild(new Label { Text = label });
        form.AddChild(Shut(Ui.Chips(items, now, items.Length, pick), !Zone.May));
    }

    public void LivesOn(GridContainer form, BuiltThing town)
    {
        double share = PlaceZones.FarmingOf(town, town.Setting(BuiltThings.Scale, S.View.BuiltPerMetre ?? 0));
        var slider = _held(form, "Lives on", 0, 1, ShareStep, share, x => $"{x * 100:0}%", Zone.LivesOn, LivesOnTip);
        slider.Editable = Zone.May;
    }

    public void Hold(GridContainer form, BuiltThing town)
    {
        Chips(form, "State", States, town.Setting(PlaceZones.War, 0) >= 1 ? War : Peace, key => Zone.War(key == War));
        int epoch = BuildTool.Epoch(S);
        int held = Manners.For(epoch, town.Settings.TryGetValue(PlaceZones.HeldBy, out double by) ? Manners.Known((int)by) : Manners.Of(town));
        double? coat = town.Settings.TryGetValue(PlaceZones.Coat, out double kept) ? kept : null;
        Picker(form, "Held by", Manners.Id(held), key => Zone.Holder(Manners.Of(key), coat));
        form.AddChild(new Label { Text = "Coat" });
        var swatches = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        swatches.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        foreach (var (name, hex) in ColorChoices.All)
        {
            double colour = PlaceZones.CoatOf(hex) ?? 0;
            bool chosen = coat == colour;
            var swatch = Ui.Swatch(Coords.ParseColor(hex, Colors.Black), new Vector2(CoatSwatchPx, CoatSwatchPx), name + " · " + CoatTip, chosen);
            swatch.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            swatch.Pressed += () => Zone.Holder(held, chosen ? null : colour);
            swatches.AddChild(swatch);
        }
        form.AddChild(Shut(swatches, !Zone.May));
    }

    public void Building(BuiltThing thing, BuiltPart part, VBoxContainer box)
    {
        if (S.View.Sheets.FirstOrDefault(s => s.Id == thing.Sheet) is not { } sheet) return;
        var form = Ui.Form(Ui.PanelLabels);
        Picker(form, "Culture", Manners.Id(Manners.For(BuildTool.Epoch(S), PlaceZones.CultureOf(thing, sheet, part))), Zone.Culture);
        Chips(form, "Wealth", Riches, PlaceZones.WealthOf(part).ToString(), key => Zone.Wealth(Enum.Parse<WealthLevel>(key)));
        Ui.AlignForm(form);
        box.AddChild(form);
    }
}

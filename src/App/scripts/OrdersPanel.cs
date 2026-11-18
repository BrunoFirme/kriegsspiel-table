using System.Globalization;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class OrdersPanel : HudPart
{
    public const string Key = "orders";
    const int ChipWidth = 52;
    const double PaceStep = 5;

    public readonly FloatPanel Panel;

    public OrdersPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel(Key, "Orders");
    }

    public void Rebuild()
    {
        if (T == null || !Panel.Visible) return;
        foreach (var c in Panel.Body.GetChildren()) { Panel.Body.RemoveChild(c); c.QueueFree(); }
        var orders = T.Orders;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Pieces" });
        int going = orders.Going.Count, onOrder = orders.Going.Count(n => n.Piece.Timed != null);
        form.AddChild(new Label { Text = onOrder > 0 ? $"{going} · {onOrder} on an order" : going.ToString(CultureInfo.InvariantCulture) });
        if (S.View.OrdersOn)
        {
            form.AddChild(new Label { Text = "By" });
            form.AddChild(TravellerRow(orders));
            form.AddChild(new Label { Text = "Pace" });
            form.AddChild(PaceRow(orders));
            form.AddChild(new Label { Text = "Stops" });
            form.AddChild(StopsRow(orders));
        }
        Ui.AlignForm(form);
        Panel.Body.AddChild(form);
        if (!S.View.OrdersOn) Panel.Body.AddChild(Ui.UnderControls(new Label { Text = "Off in this scenario", Modulate = Ui.Muted }, Ui.PanelLabels));
        if (orders.AnyOnOrder) Panel.Body.AddChild(Ui.UnderControls(Ui.Btn("Stop", orders.Stop, "Ends their orders where they stand"), Ui.PanelLabels));
        Panel.Fit();
    }

    static string Named(string traveller) => char.ToUpperInvariant(traveller[0]) + traveller[1..];

    static HBoxContainer TravellerRow(PieceOrders orders)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var all = orders.Travellers();
        for (int i = 0; i < all.Count; i++)
        {
            string by = all[i];
            var chip = new Button
            {
                Text = Named(by), ToggleMode = true, ButtonPressed = by == orders.By, FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = Ui.Px(ChipWidth, 0), TooltipText = i < 9 ? $"{Named(by)}   ({i + 1})" : Named(by),
            };
            chip.Pressed += () => orders.Choose(by);
            row.AddChild(chip);
        }
        return row;
    }

    static HBoxContainer PaceRow(PieceOrders orders)
    {
        var s = new HSlider
        {
            MinValue = PieceOrders.LeastPace * 100, MaxValue = PieceOrders.MostPace * 100, Step = PaceStep, Value = orders.Pace * 100, CustomMinimumSize = Ui.Px(90, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "A share of the traveller's usual pace on each way",
        };
        var shown = Ui.ValueBox(s, v => $"{v:0}%", 40);
        s.ValueChanged += v => orders.SetPace(v / 100);
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        row.AddChild(s);
        row.AddChild(shown);
        return row;
    }

    static HBoxContainer StopsRow(PieceOrders orders)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var add = new Button
        {
            Text = "Add", ToggleMode = true, ButtonPressed = orders.AddingStops, FocusMode = Control.FocusModeEnum.None,
            TooltipText = "While on, clicks on the table set stops on the way · Shift+click does the same",
        };
        add.Toggled += orders.SetAddingStops;
        row.AddChild(add);
        row.AddChild(new Label { Text = orders.Stops == 0 ? "none" : orders.Stops.ToString(CultureInfo.InvariantCulture), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        if (orders.Stops > 0) row.AddChild(Ui.Btn("Clear", orders.ClearStops, "Takes the stops away · Backspace takes the last"));
        return row;
    }
}

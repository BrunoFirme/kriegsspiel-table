using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceOrdersTests
{
    const double Side = 60, MetresPerUnit = 50;

    static async Task<HudTests.Rig> Road(TestRunner r, bool on = true)
    {
        var h = await HudTests.Host(r, pieces: 3);
        h.S.Do(new SetCalibration { UnitsPerMeter = 1 / MetresPerUnit });
        h.S.Do(new AddSheet { Sheet = new SheetDef { Id = "s-road", Name = "Road", Pos = new Vec2(Side / 2, Side / 2), Size = new Size2(Side, Side) } });
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresPerUnit, HeightUnit = Side };
        h.S.Do(new LayBuilt { Thing = BuiltRoads.Lay(site, "s-road", [new Spot(8, 30), new Spot(52, 30)], "road", false) });
        h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "p1", Pos = new Vec2(10, 30) });
        h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "p2", Pos = new Vec2(12, 30) });
        h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "p3", Pos = new Vec2(200, 200) });
        if (on) h.S.Do(new AllowOrders { On = true });
        await r.Frames(3);
        return h;
    }

    static InputEventMouseButton Click() => new() { ButtonIndex = MouseButton.Left, Pressed = true };

    static PieceInstance Piece(HudTests.Rig h, string id) => h.S.View.Find(Layers.Umpire, id)!;

    static PieceNode Node(HudTests.Rig h, string id) => h.Table.Pieces.NodeOf(Layers.Umpire, id)!;

    static Button Pressing(Control under, string text) =>
        under.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == text);

    static async Task Press(TestRunner r, Button b)
    {
        if (b.ToggleMode) b.ButtonPressed = !b.ButtonPressed;
        b.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
    }

    [NodeTest]
    static async Task Orders_shows_only_where_the_scenario_allows_it_and_the_piece_can_be_routed(TestRunner r)
    {
        var off = await Road(r, on: false);
        Check.That(!PieceOrders.CanOrder(off.S, [Node(off, "p1")]), "no Orders row while the scenario has orders off");
        off.S.Do(new AllowOrders { On = true });
        await r.Frames(2);
        Check.That(PieceOrders.CanOrder(off.S, [Node(off, "p1")]), "Orders on a piece standing on a road");
        Check.That(!PieceOrders.CanOrder(off.S, [Node(off, "p3")]), "no Orders on a piece off every routable sheet");
    }

    [NodeTest]
    static async Task The_card_orders_the_picked_pieces_with_the_mouse_alone_and_the_clock_walks_them(TestRunner r)
    {
        var h = await Road(r);
        var orders = h.Table.Orders;
        orders.Arm([Node(h, "p1"), Node(h, "p2")]);
        await r.Frames(2);
        var card = h.Hud.OrderCard.Panel;
        Check.That(h.Table.Tool == Tool.Order && card.Visible, "Orders arms the tool and opens its card");
        await Press(r, Pressing(card, "Rider"));
        Check.Equal(Travellers.Rider, orders.By, "the traveller picked on the card");
        await Press(r, Pressing(h.Hud.OrderCard.Panel, "Add"));
        Check.That(orders.AddingStops, "Add sets stops with plain clicks");
        orders.Press(Click(), new Vec2(30, 30));
        Check.Equal(1, orders.Stops, "a click while adding sets a stop");
        await Press(r, Pressing(h.Hud.OrderCard.Panel, "Add"));
        orders.Press(Click(), new Vec2(48, 30));
        await r.Frames(3);
        Check.Equal(Tool.Select, h.Table.Tool, "the next plain click gives the order and leaves the tool");
        var lead = Piece(h, "p1").Timed;
        var second = Piece(h, "p2").Timed;
        Check.That(lead != null && second != null, "both pieces are on an order");
        Check.Equal((Travellers.Rider, new Vec2(48, 30)), (lead!.By, lead.End), "the lead's order");
        Check.Equal(new Vec2(50, 30), second!.End, "the second keeps its place beside the lead");
        h.S.Do(new StepClock { Seconds = (long)System.Math.Ceiling(lead.Seconds / 2) });
        await r.Frames(3);
        Check.That(Piece(h, "p1").Pos.X > 15, "the clock moved the piece along its route");
        Check.That(System.Math.Abs(Node(h, "p1").Position.X - Coords.ToWorld(Piece(h, "p1").Pos, 0).X) < 0.01f, "the piece is drawn where the clock puts it");
        orders.Arm([Node(h, "p1"), Node(h, "p2")]);
        await r.Frames(2);
        await Press(r, Pressing(h.Hud.OrderCard.Panel, "Stop"));
        await r.Frames(3);
        Check.That(Piece(h, "p1").Timed == null && Piece(h, "p2").Timed == null, "Stop on the card ends both orders");
    }

    [NodeTest]
    static async Task Keys_are_only_shortcuts_and_Esc_leaves_with_nothing_given(TestRunner r)
    {
        var h = await Road(r);
        var orders = h.Table.Orders;
        orders.Arm([Node(h, "p1")]);
        Check.That(orders.Pressed(new InputEventKey { Keycode = Key.Key3, Pressed = true }), "3 picks the third traveller");
        Check.Equal(Travellers.Cart, orders.By, "the cart");
        orders.Press(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, ShiftPressed = true }, new Vec2(30, 30));
        Check.Equal(1, orders.Stops, "Shift+click sets a stop");
        Check.That(orders.Pressed(new InputEventKey { Keycode = Key.Backspace, Pressed = true }) && orders.Stops == 0, "Backspace takes it back");
        h.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await r.Frames(2);
        Check.Equal(Tool.Select, h.Table.Tool, "Esc leaves the tool");
        Check.That(Piece(h, "p1").Timed == null && !h.Hud.OrderCard.Panel.Visible, "no order was given and the card closed");
    }
}

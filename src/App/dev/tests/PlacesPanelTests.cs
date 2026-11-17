using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PlacesPanelTests
{
    const string U = Layers.Umpire;

    internal static async Task<(HudTests.Rig Rig, PlacesPanel Places)> Opened(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        h.S.Do(new SetPlace { Place = new PlacePin { Name = "The Tavern", Sheet = "s0", Pos = new Vec2(3, 3) } });
        h.S.Do(new SetPlace { Place = new PlacePin { Name = "Mill", Pos = new Vec2(40, 40) } });
        h.Hud.Strip.Buttons[PlacesPanel.Key].EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var places = h.Hud.Places!;
        Check.That(places.Panel.Visible, "the Places button on the strip opens the panel");
        return (h, places);
    }

    static string Shown(PlacesPanel places)
    {
        string Of(TreeItem item) => string.Join(" ", item.GetChildren().Select(c => c.GetChildCount() > 0 ? $"{c.GetText(0)} [{Of(c)}]" : c.GetText(0)));
        return places.Rows.GetRoot() is { } root ? Of(root) : "";
    }

    static async Task Click(TestRunner r, PlacesPanel places, string name, MouseButton button)
    {
        var item = places.Item(name);
        Check.That(item != null, $"a row for {name}: {Shown(places)}");
        item!.Select(0);
        places.Rows.EmitSignal(Tree.SignalName.ItemMouseSelected, Vector2.Zero, (long)button);
        await r.Frames(1);
    }

    internal static async Task Pick(TestRunner r, PlacesPanel places, string name, string row)
    {
        await Click(r, places, name, MouseButton.Right);
        var menu = places.Menu!;
        int at = Enumerable.Range(0, menu.ItemCount).FirstOrDefault(i => menu.GetItemText(i) == row, -1);
        Check.That(at >= 0, $"{row} is on {name}'s menu");
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, (long)menu.GetItemId(at));
        await r.Frames(2);
    }

    static async Task Name(TestRunner r, PlacesPanel places, string name)
    {
        Check.That(places.NameField != null && places.Asking != null, "a name is asked for");
        places.NameField!.Text = name;
        places.Asking!.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await r.Frames(3);
    }

    [NodeTest]
    static async Task The_panel_lists_the_sheet_with_its_pin_and_the_pin_on_no_sheet_and_a_click_brings_the_camera_there(TestRunner r)
    {
        var (h, places) = await Opened(r);
        Check.Equal("Map sheet [The Tavern] Mill", Shown(places), "the tree");
        await Click(r, places, "The Tavern", MouseButton.Left);
        var at = Coords.FromWorld(h.Table.Rig.Target);
        Check.That((at - new Vec2(3, 3)).Length < 0.01, $"the camera looks at the pin: {at}");
        await Click(r, places, "Map sheet", MouseButton.Left);
        Check.That(Coords.FromWorld(h.Table.Rig.Target).Length < 0.01, "and at the sheet's middle");
    }

    [NodeTest]
    static async Task Place_here_pins_the_spot_the_camera_looks_at_with_its_view_under_the_name_given(TestRunner r)
    {
        var (h, places) = await Opened(r);
        h.Table.Rig.Centre(Coords.ToWorld(new Vec2(-2, 4)));
        places.PlaceHereButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Name(r, places, "Ford");
        var pin = h.S.View.Places!.Single(p => p.Name == "Ford");
        Check.That((pin.Pos - new Vec2(-2, 4)).Length < 0.01 && pin.Sheet == "s0" && pin.View is { Length: PlacePin.ViewLength }, $"kept at {pin.Pos} on {pin.Sheet} with a view");
        Check.Equal("Map sheet [Ford The Tavern] Mill", Shown(places), "the new pin in the tree");
        h.Table.Rig.Centre(Coords.ToWorld(new Vec2(30, 30)));
        await Click(r, places, "Ford", MouseButton.Left);
        Check.That((Coords.FromWorld(h.Table.Rig.Target) - new Vec2(-2, 4)).Length < 0.01, "a click goes back to the kept view");
    }

    [NodeTest]
    static async Task Send_moves_a_player_s_body_to_the_place_and_the_row_says_who_is_there(TestRunner r)
    {
        var (h, places) = await Opened(r);
        h.Table.Avatars.Bodies = [new BodyOf("p1", 5, "Ann", 0), new BodyOf("p2", 6, "Bob", 1)];
        await r.Frames(2);
        Check.Equal("Ann, Bob", places.Item("Map sheet")!.GetText(1), "both stand on the sheet");
        await Pick(r, places, "Mill", "Send Ann here");
        Check.That((h.S.View.Find(U, "p1")!.Pos - new Vec2(40, 40)).Length < 0.01, "Ann's body stands at the Mill");
        await r.Frames(2);
        Check.Equal(("Ann", "Bob"), (places.Item("Mill")!.GetText(1), places.Item("Map sheet")!.GetText(1)), "who is where now");
        await Pick(r, places, "The Tavern", "Send everyone here");
        Check.Equal("Ann, Bob", places.Item("The Tavern")!.GetText(1), "both at the Tavern");
        Check.That((h.S.View.Find(U, "p1")!.Pos - h.S.View.Find(U, "p2")!.Pos).Length > 0.5, "standing apart");
    }

    [NodeTest]
    static async Task A_pin_is_renamed_and_forgotten_from_its_menu_and_undo_brings_it_back(TestRunner r)
    {
        var (h, places) = await Opened(r);
        await Pick(r, places, "Mill", "Rename");
        await Name(r, places, "Old mill");
        Check.Equal("Map sheet [The Tavern] Old mill", Shown(places), "renamed");
        await Pick(r, places, "Old mill", "Forget");
        Check.Equal("Map sheet [The Tavern]", Shown(places), "forgotten");
        h.S.UndoLast();
        await r.Frames(3);
        Check.Equal("Map sheet [The Tavern] Old mill", Shown(places), "back after undo");
    }

    [NodeTest]
    static async Task The_find_box_narrows_the_tree_to_matching_places_and_where_they_stand(TestRunner r)
    {
        var (_, places) = await Opened(r);
        places.Find.Text = "tav";
        places.Find.EmitSignal(LineEdit.SignalName.TextChanged, "tav");
        await r.Frames(1);
        Check.Equal("Map sheet [The Tavern]", Shown(places), "narrowed");
        places.Find.Text = "nowhere";
        places.Find.EmitSignal(LineEdit.SignalName.TextChanged, "nowhere");
        await r.Frames(1);
        Check.Equal("", Shown(places), "nothing left");
        places.Find.Text = "";
        places.Find.EmitSignal(LineEdit.SignalName.TextChanged, "");
        await r.Frames(1);
        Check.Equal("Map sheet [The Tavern] Mill", Shown(places), "all again");
    }
}

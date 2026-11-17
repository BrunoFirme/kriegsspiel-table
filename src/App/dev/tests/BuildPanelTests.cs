using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class BuildPanelTests
{
    const double Side = 40, MapScale = 0.02, TownReach = 5;
    const int TownSeed = 7;

    sealed record Opened(HudTests.Rig H, ReliefPanel Relief, Control Body, BuildPrefs Prefs)
    {
        public void Show(string mode, string place = BuiltKinds.Settlement)
        {
            Prefs.Do(mode);
            Prefs.Place = place;
            Relief.Rebuild();
        }

        public List<string> Rows => All<Label>(Body).Where(l => l.HasMeta(Ui.RowLabelMeta)).Select(l => l.Text).ToList();

        public List<string> Heads => All<Label>(Body).Where(l => l.HasMeta(Ui.HeaderMeta)).Select(l => l.GetMeta(Ui.HeaderMeta).AsString()).ToList();

        public OptionButton Culture => All<OptionButton>(Body)[0];

        public Button Named(string text) => All<Button>(Body).First(b => b.Text == text || All<Label>(b).Any(l => l.Text == text));

        public bool Has(string text) => All<Button>(Body).Any(b => b.Text == text);

        public string Faults => string.Join("; ", UiRuleTests.Problems(Body, "Build"));

        public void Type(string text)
        {
            var box = All<LineEdit>(Body).Last(entry => entry.GetParent() is GridContainer);
            box.Text = text;
            box.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        public Control Field(string label) =>
            All<Label>(Body).Where(l => l.Text == label && l.HasMeta(Ui.RowLabelMeta) && l.GetParent() is GridContainer).Select(l => (Control)l.GetParent().GetChild(l.GetIndex() + 1)).First();

        public OptionButton Picker(string label) => (OptionButton)Field(label);

        public HSlider Slider(string label) => All<HSlider>(Field(label))[0];

        public Button Chip(string label, string text) => All<Button>(Field(label)).First(b => b.Text == text);

        public bool CultureLeads => All<GridContainer>(Body).FirstOrDefault(grid => grid.GetChild(0) is Label) is { } form && form.GetChild(0) is Label { Text: "Culture" } && form.GetChild(1) is OptionButton;
    }

    static List<T> All<T>(Node under) where T : Node => under.FindChildren("*", "", true, false).OfType<T>().ToList();

    static async Task<Opened> Open(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0);
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, h.S.Store.Root);
        h.S.Relief.Remember(sha, flat);
        h.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        h.S.Do(new SetCalibration { UnitsPerMeter = MapScale });
        await r.Frames(8);
        var prefs = h.Table.Settings.Build;
        (prefs.Mode, prefs.Place, prefs.Manner) = (BuildPrefs.Laying, BuiltKinds.Settlement, Manners.Id(Manners.Continental));
        h.Table.SetTool(Tool.Build);
        if (!h.Table.Rig.TopDown) h.Table.Rig.ToggleTopDown();
        h.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side);
        await r.Frames(2);
        var relief = h.Hud.Relief!;
        relief.Rebuild();
        return new Opened(h, relief, relief.Panel.Body, prefs);
    }

    [NodeTest]
    static async Task The_Build_panel_asks_the_Culture_first_on_each_page_it_decides(TestRunner r)
    {
        var p = await Open(r);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "Shape", "Houses", "Spread", "Order", "Walls", "Plan", "Name"]), "a settlement: " + string.Join(", ", p.Rows));
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Show(BuildPrefs.Things);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "From"]), "a building: " + string.Join(", ", p.Rows));
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Show(BuildPrefs.Blocks);
        Check.That(p.Rows.Count == 0, "a block asks nothing: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Homes);
        Check.That(p.Rows.SequenceEqual(["Size"]), "the houses brush: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Lamps);
        Check.That(p.Rows.SequenceEqual(["Lamps", "Size"]) && p.Faults.Length == 0, "the lamps brush: " + string.Join(", ", p.Rows) + " " + p.Faults);
        p.Show(BuildPrefs.Wealth);
        Check.That(p.Rows.SequenceEqual(["Wealth", "Size"]) && p.Faults.Length == 0, "the wealth brush: " + string.Join(", ", p.Rows) + " " + p.Faults);
        p.Show(BuildPrefs.Places, BuildPrefs.Redoubt);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "Outworks"]), "a redoubt: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Details);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "Plots", "Buildings", "Fields", "Size"]), "details: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Roads);
        Check.That(!p.Rows.Contains("Culture"), "roads belong to no culture: " + string.Join(", ", p.Rows));
        var modern = Json.Clone(p.H.S.View.Lighting);
        modern.MapStyle = MapStyles.Modern;
        p.H.S.Do(new SetLighting { Lighting = modern });
        await r.Frames(2);
        p.Show(BuildPrefs.Places, BuildPrefs.Redoubt);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture"]), "a battery has only its culture: " + string.Join(", ", p.Rows));
    }

    [NodeTest]
    static async Task Add_mix_at_the_foot_of_the_Culture_list_asks_the_mix_alone_and_Save_mix_lays_places_in_it(TestRunner r)
    {
        var p = await Open(r);
        int cultures = Manners.In(Manners.Later).Count();
        Check.That(p.Culture.ItemCount == cultures + 2 && p.Culture.GetItemText(p.Culture.ItemCount - 1) == "Add mix…", $"the Culture list ends with Add mix: {p.Culture.ItemCount} items");
        p.Culture.EmitSignal(OptionButton.SignalName.ItemSelected, p.Culture.ItemCount - 1);
        await r.Frames(2);
        Check.That(p.Rows.SequenceEqual(["Culture", "Built by", "Adorned by", "Quarter", "Name"]), "the mix is asked alone: " + string.Join(", ", p.Rows));
        Check.That(p.Culture.ItemCount == cultures, "a mix is made of the age's own cultures");
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Named("Country").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(p.Rows.Contains("Size") && !p.Rows.Contains("Built by"), "another page leaves the mix: " + string.Join(", ", p.Rows));
        p.Named("Town").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        p.Culture.EmitSignal(OptionButton.SignalName.ItemSelected, p.Culture.ItemCount - 1);
        await r.Frames(2);
        var builders = All<OptionButton>(p.Body)[1];
        builders.EmitSignal(OptionButton.SignalName.ItemSelected, builders.Selected == 0 ? 1 : 0);
        await r.Frames(2);
        p.Type("March");
        p.Named("Save mix").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var kept = p.H.S.View.Cultures?.SingleOrDefault();
        Check.That(kept is { Name: "March" } && !Manners.Parse(kept.Code).Pure && p.Prefs.Manner == kept.Code, $"the mix is kept in the scenario and chosen: {kept?.Code}, {p.Prefs.Manner}");
        Check.That(p.CultureLeads && p.Culture.GetItemText(p.Culture.Selected) == "March" && p.Rows.Contains("Houses"), "the tools are back with the mix on the Culture list");
        p.Named("Remove mix").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(p.H.S.View.Cultures is not { Count: > 0 } && !p.Has("Remove mix"), "Remove mix takes it off the list");
    }

    [NodeTest]
    static async Task A_picked_place_shows_its_Culture_first_and_who_built_and_adorned_it_under_Mix(TestRunner r)
    {
        var p = await Open(r);
        var sheet = p.H.S.View.Sheets.First(s => s.Id == "g");
        var town = Settlements.Lay(BuildTool.Site(p.H.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 1, 0, 0);
        p.H.S.Do(new LayBuilt { Thing = town });
        await r.Frames(2);
        p.Prefs.Do(BuildPrefs.Pick);
        var house = town.Parts.First(part => part.Class == "house");
        p.H.Table.Build.Press(BuiltGeometry.ToWorld(sheet, new SheetPoint(house.At[0], house.At[1])), false);
        p.H.Table.Build.Release();
        await r.Frames(2);
        p.Relief.Rebuild();
        Check.That(p.Rows.SequenceEqual(["Name", "Culture", "Houses", "Spread", "Order", "Lives on", "Ravaged", "State", "Held by", "Coat", "Walls", "Plan", "Built by", "Adorned by", "Quarter"]),
            "the picked town's rows: " + string.Join(", ", p.Rows));
        Check.That(p.Heads.SequenceEqual(["Mix"]), "its sections: " + string.Join(", ", p.Heads));
        var builders = p.Picker("Built by");
        builders.EmitSignal(OptionButton.SignalName.ItemSelected, builders.Selected == 0 ? 1 : 0);
        await r.Frames(3);
        p.Relief.Rebuild();
        Check.That(p.Rows.TakeLast(2).SequenceEqual(["Quarter", "Mix name"]) && p.Has("Keep mix") && !p.Has("Remove mix"), "a mixed place asks a name to keep its mix: " + string.Join(", ", p.Rows));
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Type("March");
        p.Named("Keep mix").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(p.H.S.View.Cultures?.SingleOrDefault() is { Name: "March" } && p.Has("Remove mix") && !p.Has("Keep mix") && !p.Rows.Contains("Mix name"), "kept, it can be removed, worded as on the Culture list");
    }

    static async Task<(Opened P, SheetDef Sheet, int House)> PickedTown(TestRunner r)
    {
        var p = await Open(r);
        var sheet = p.H.S.View.Sheets.First(s => s.Id == "g");
        var town = Settlements.Lay(BuildTool.Site(p.H.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 1, 0, 0);
        p.H.S.Do(new LayBuilt { Thing = town });
        await r.Frames(2);
        p.Prefs.Do(BuildPrefs.Pick);
        int house = town.Parts.FindIndex(part => part.Class == "house");
        await Press(r, p, sheet, town.Parts[house]);
        return (p, sheet, house);
    }

    static async Task Press(TestRunner r, Opened p, SheetDef sheet, BuiltPart part)
    {
        p.H.Table.Build.Press(BuiltGeometry.ToWorld(sheet, new SheetPoint(part.At[0], part.At[1])), false);
        p.H.Table.Build.Release();
        await Settled(r, p);
    }

    static async Task Settled(TestRunner r, Opened p)
    {
        await r.Frames(3);
        p.Relief.Rebuild();
    }

    static BuiltThing Town(Opened p) => p.H.S.View.Built.Single();

    static int Index(OptionButton picker, string text) => Enumerable.Range(0, picker.ItemCount).First(i => picker.GetItemText(i) == text);

    [NodeTest]
    static async Task A_picked_town_and_building_show_what_the_place_is_as_laid_and_a_change_sets_it(TestRunner r)
    {
        var (p, sheet, house) = await PickedTown(r);
        var town = Town(p);
        double farming = PlaceZones.FarmingOf(town, town.Setting(BuiltThings.Scale, MapScale));
        Check.That(System.Math.Abs(p.Slider("Lives on").Value - farming) <= 0.05, $"Lives on shows the generator's share: {p.Slider("Lives on").Value} for {farming}");
        Check.That(p.Chip("State", "Peace").ButtonPressed && !p.Chip("State", "War").ButtonPressed, "an untouched town is at peace");
        Check.That(p.Picker("Held by").Text == Manners.Named(Manners.Of(town)).Name, "it is held by its own culture: " + p.Picker("Held by").Text);
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Chip("State", "War").EmitSignal(BaseButton.SignalName.Pressed);
        await Settled(r, p);
        Check.That(Town(p).Setting(PlaceZones.War, 0) >= 1 && p.Chip("State", "War").ButtonPressed, "War is stored and shown");
        p.Picker("Held by").EmitSignal(OptionButton.SignalName.ItemSelected, Index(p.Picker("Held by"), "Northern"));
        await Settled(r, p);
        Check.That(Town(p).Setting(PlaceZones.HeldBy, -1) == Manners.Northern && p.Picker("Held by").Text == "Northern", "Held by is stored and shown");
        All<Button>(p.Field("Coat"))[1].EmitSignal(BaseButton.SignalName.Pressed);
        await Settled(r, p);
        Check.That(Town(p).Settings.TryGetValue(PlaceZones.Coat, out double coat) && PlaceZones.CoatText(coat) == ColorChoices.All[1].Hex, "the coat is stored");
        await Press(r, p, sheet, Town(p).Parts[house]);
        Check.That(p.Rows.Contains("Wealth") && p.Picker("Culture").Text == Manners.Named(Manners.Of(town)).Name && p.Chip("Wealth", "Middling").ButtonPressed,
            "a picked house shows its culture and wealth as laid: " + string.Join(", ", p.Rows));
        p.Picker("Culture").EmitSignal(OptionButton.SignalName.ItemSelected, Index(p.Picker("Culture"), "Ottoman"));
        await Settled(r, p);
        p.Chip("Wealth", "Rich").EmitSignal(BaseButton.SignalName.Pressed);
        await Settled(r, p);
        var picked = Town(p).Parts[house];
        Check.That(picked is { Culture: "ottoman", Wealth: "rich", Kept: true }, $"the house keeps its own culture and wealth: {picked.Culture}, {picked.Wealth}");
        Check.That(p.Picker("Culture").Text == "Ottoman" && p.Chip("Wealth", "Rich").ButtonPressed && p.Faults.Length == 0, "and shows them " + p.Faults);
    }

    [NodeTest]
    static async Task A_town_s_culture_change_lays_it_again_in_one_undo(TestRunner r)
    {
        var (p, _, _) = await PickedTown(r);
        var before = Json.Serialize(Town(p));
        p.Picker("Culture").EmitSignal(OptionButton.SignalName.ItemSelected, Index(p.Picker("Culture"), "Northern"));
        await Settled(r, p);
        Check.That(Manners.Of(Town(p)) == Manners.Northern && Json.Serialize(Town(p)) != before, "the town is laid again in its new culture");
        p.H.S.UndoLast();
        await Settled(r, p);
        Check.That(Json.Serialize(Town(p)) == before, "one undo brings the town back as it was");
    }

    [NodeTest]
    static async Task A_plain_umpire_sees_what_a_place_is_but_cannot_change_it(TestRunner r)
    {
        var (p, sheet, house) = await PickedTown(r);
        p.H.S.Me.Role = Role.Umpire;
        await Settled(r, p);
        Check.That(p.Rows.Contains("State") && p.Chip("State", "War").Disabled && p.Picker("Held by").Disabled && !p.Slider("Lives on").Editable, "the rows are shown shut");
        Check.That(All<Button>(p.Field("Coat")).All(b => b.Disabled), "and the coat too");
        p.H.Table.Build.Zone.War(true);
        p.H.Table.Build.Zone.LivesOn(0);
        await Press(r, p, sheet, Town(p).Parts[house]);
        Check.That(p.Picker("Culture").Disabled && p.Chip("Wealth", "Rich").Disabled, "a house's rows are shut too");
        p.H.Table.Build.Zone.Wealth(WealthLevel.Rich);
        p.H.Table.Build.Zone.Culture("ottoman");
        await Settled(r, p);
        var town = Town(p);
        Check.That(town.Setting(PlaceZones.War, 0) == 0 && !town.Settings.ContainsKey(Settlements.Farming) && town.Parts[house] is { Culture: null, Wealth: null }, "nothing is set");
    }
}

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

        public Button Named(string text) => All<Button>(Body).First(b => b.Text == text);

        public bool Has(string text) => All<Button>(Body).Any(b => b.Text == text);

        public string Faults => string.Join("; ", UiRuleTests.Problems(Body, "Build"));

        public void Type(string text)
        {
            var box = All<LineEdit>(Body).Last(entry => entry.GetParent() is GridContainer);
            box.Text = text;
            box.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        public bool CultureLeads => All<GridContainer>(Body).FirstOrDefault() is { } form && form.GetChild(0) is Label { Text: "Culture" } && form.GetChild(1) is OptionButton;
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
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "Houses", "Spread", "Order", "Walls", "Plan", "Radius", "Name"]), "a settlement: " + string.Join(", ", p.Rows));
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Show(BuildPrefs.Things);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture"]), "a building: " + string.Join(", ", p.Rows));
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Show(BuildPrefs.Blocks);
        Check.That(p.Rows.Count == 0, "a block asks nothing: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Homes);
        Check.That(p.Rows.SequenceEqual(["Houses", "Size"]), "the houses brush: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Lamps);
        Check.That(p.Rows.SequenceEqual(["Lamps", "Size"]) && p.Faults.Length == 0, "the lamps brush: " + string.Join(", ", p.Rows) + " " + p.Faults);
        p.Show(BuildPrefs.Places, BuildPrefs.Redoubt);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "Outworks", "Radius"]), "a redoubt: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Details);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "Plots", "Buildings", "Fields", "Size"]), "details: " + string.Join(", ", p.Rows));
        p.Show(BuildPrefs.Roads);
        Check.That(!p.Rows.Contains("Culture"), "roads belong to no culture: " + string.Join(", ", p.Rows));
        var modern = Json.Clone(p.H.S.View.Lighting);
        modern.MapStyle = MapStyles.Modern;
        p.H.S.Do(new SetLighting { Lighting = modern });
        await r.Frames(2);
        p.Show(BuildPrefs.Places, BuildPrefs.Redoubt);
        Check.That(p.CultureLeads && p.Rows.SequenceEqual(["Culture", "Radius"]), "a battery has only the radius a click lays: " + string.Join(", ", p.Rows));
    }

    [NodeTest]
    static async Task Add_mix_at_the_foot_of_the_Culture_list_asks_the_mix_alone_and_Save_mix_lays_places_in_it(TestRunner r)
    {
        var p = await Open(r);
        int cultures = Manners.In(Manners.Later).Count();
        Check.That(p.Culture.ItemCount == cultures + 2 && p.Culture.GetItemText(p.Culture.ItemCount - 1) == "Add mix…", $"the Culture list ends with Add mix: {p.Culture.ItemCount} items");
        p.Culture.EmitSignal(OptionButton.SignalName.ItemSelected, p.Culture.ItemCount - 1);
        await r.Frames(2);
        Check.That(p.Rows.SequenceEqual(["Culture", "Built by", "Adorned by", "Quarter", "Name"]) && All<ToolIcon>(p.Body).Count == 0, "the mix is asked alone: " + string.Join(", ", p.Rows));
        Check.That(p.Culture.ItemCount == cultures, "a mix is made of the age's own cultures");
        Check.That(p.Faults.Length == 0, p.Faults);
        p.Named("Brush").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(p.Rows.Contains("Size") && !p.Rows.Contains("Built by"), "another page leaves the mix: " + string.Join(", ", p.Rows));
        p.Named("Lay").EmitSignal(BaseButton.SignalName.Pressed);
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
        Check.That(p.CultureLeads && p.Culture.GetItemText(p.Culture.Selected) == "March" && All<ToolIcon>(p.Body).Count > 0, "the tools are back with the mix on the Culture list");
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
        Check.That(p.Rows.SequenceEqual(["Name", "Culture", "Houses", "Spread", "Order", "Ravaged", "Walls", "Plan", "Built by", "Adorned by", "Quarter"]), "the picked town's rows: " + string.Join(", ", p.Rows));
        Check.That(p.Heads.SequenceEqual(["Mix"]), "its sections: " + string.Join(", ", p.Heads));
        var builders = All<OptionButton>(p.Body)[1];
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
}

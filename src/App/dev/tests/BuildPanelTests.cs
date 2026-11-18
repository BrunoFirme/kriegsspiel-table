using System;
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

    static bool Offers(Opened p, string tip) => All<Control>(p.Body).Any(c => c.TooltipText.StartsWith(tip, System.StringComparison.Ordinal));

    static void Crowds(Opened p, bool on)
    {
        var light = Json.Clone(p.H.S.View.Lighting);
        light.Crowds = on;
        p.H.S.Do(new SetLighting { Lighting = light });
    }

    [NodeTest]
    static async Task Crowds_show_only_when_switched_on_and_their_brushes_set_who_lives_and_where_none_go(TestRunner r)
    {
        var p = await Open(r);
        var sheet = p.H.S.View.Sheets.First(s => s.Id == "g");
        var town = Settlements.Lay(BuildTool.Site(p.H.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 1, 0, 0);
        p.H.S.Do(new LayBuilt { Thing = town });
        await r.Frames(3);
        var crowds = p.H.Table.Crowds;
        p.Show(BuildPrefs.Lamps);
        Check.That(crowds.People == 0 && !crowds.Root.Visible, "with crowds off nobody is drawn");
        Check.That(!Offers(p, "How many live"), "and the Build panel has no population brush");
        Crowds(p, true);
        await r.Frames(4);
        int all = crowds.People;
        Check.That(all > 0 && crowds.Root.Visible, $"switched on, the town has its people ({all})");
        Check.That(p.H.S.View.Crowds?.Peoples.ContainsKey("continental") == true, "the host keeps the nationality the town was dressed by");
        p.Show(BuildPrefs.Lamps);
        Check.That(Offers(p, "How many live") && Offers(p, "Crowds keep out"), "the population and keep clear brushes sit beside the lamps");
        p.Show(BuildPrefs.Peopled);
        Check.That(p.Rows.SequenceEqual(["Level", "Size"]), "the population brush: " + string.Join(", ", p.Rows));
        (p.Prefs.Level, p.Prefs.Brush) = (0, BuildPrefs.LargestBrush);
        p.H.Table.Build.Press(new Vec2(0, 0), false);
        p.H.Table.Build.Release();
        await r.Frames(25);
        Check.That(crowds.People < all, $"a stroke at nobody empties the houses under it ({all} then {crowds.People})");
        p.H.S.UndoLast();
        await r.Frames(25);
        Check.Equal(all, crowds.People, "and undo brings the same people back");
        p.Show(BuildPrefs.KeptClear);
        p.H.Table.Build.Press(new Vec2(0, 0), false);
        p.H.Table.Build.Release();
        await r.Frames(3);
        Check.Equal(1, p.H.S.View.Crowds?.Clear?.Count ?? 0, "a keep clear stroke is kept");
        Crowds(p, false);
        await r.Frames(3);
        Check.That(crowds.People == 0 && !crowds.Root.Visible, "switched off, the crowds leave no trace");
    }

    static Button? Row(Node root, string text) =>
        All<PopupPanel>(root).Where(p => p.Visible).SelectMany(p => All<Button>(p)).FirstOrDefault(b => All<Label>(b).Any(l => l.Text == text));

    [NodeTest]
    static async Task Crowd_people_picked_with_the_Crowds_tool_go_where_a_right_click_sends_them(TestRunner r)
    {
        var p = await Open(r);
        var sheet = p.H.S.View.Sheets.First(s => s.Id == "g");
        p.H.S.Do(new LayBuilt { Thing = Settlements.Lay(BuildTool.Site(p.H.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 1, 0, 0) });
        Crowds(p, true);
        var crowds = p.H.Table.Crowds;
        for (int i = 0; i < 60 && crowds.Seen.Count < 5; i++)
        {
            p.H.S.Do(new AdvanceClock());
            await r.Frames(3);
        }
        Check.That(crowds.Seen.Count >= 5, $"people are out of doors ({crowds.Seen.Count})");
        p.H.Table.SetTool(Tool.Crowds);
        foreach (var seen in crowds.Seen.Take(5)) crowds.Picked.Add((seen.Town, seen.Person));
        var cam = p.H.Table.Rig.Cam;
        var market = Enumerable.Range(0, 16).Select(k => new Vec2(Math.Cos(k * 0.4) * TownReach * 1.6, Math.Sin(k * 0.4) * TownReach * 1.6))
            .MaxBy(v => crowds.Seen.Min(s => (Coords.FromWorld(s.At) - v).Length));
        p.H.Table.Rig.Frame(Coords.ToWorld(market), 1);
        await r.Frames(3);
        var screen = cam.UnprojectPosition(Coords.ToWorld(market));
        Check.That(crowds.Tool.Right(true, screen, market) && crowds.Tool.Right(false, screen, market), "a right-click with people picked opens the crowd's menu");
        await r.Frames(2);
        var go = Row(p.H.Table.GetTree().Root, "Go to") ?? throw new TestFailure("the menu offers Go to over open ground");
        Check.That(Row(p.H.Table.GetTree().Root, "Gather") != null && Row(p.H.Table.GetTree().Root, "Halt") != null, "and Gather and Halt beside it");
        go.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        var order = p.H.S.View.Crowds?.Orders?.SingleOrDefault() ?? throw new TestFailure("the order is kept");
        Check.Equal(5, order.Count, "for the five picked");
        p.H.S.UndoLast();
        await r.Frames(3);
        Check.That(p.H.S.View.Crowds?.Orders is not { Count: > 0 }, "undo takes the order back");
        p.H.S.Do(new SetCrowdOrder { Order = order.Clone() });
        p.H.S.Do(new AdvanceClock());
        p.H.S.Do(new AdvanceClock());
        await r.Frames(3);
        float upm = (float)MapScale;
        var at = order.Members.SelectMany(m => m.Value.Select(id => crowds.Where(p.H.S.View, crowds.TownOf(m.Key), id, p.H.S.View.Clock.Seconds, upm).At)).ToList();
        Check.That(at.All(a => (a - market).Length < 20 * upm), "a turn later they stand round the place they were sent to");
        p.H.Table.SetTool(Tool.Select);
        Check.Equal(0, crowds.Picked.Count, "leaving the tool lets go of the people picked");
    }

    static async Task<CrowdLayer> Peopled(TestRunner r, Opened p)
    {
        var sheet = p.H.S.View.Sheets.First(s => s.Id == "g");
        p.H.S.Do(new LayBuilt { Thing = Settlements.Lay(BuildTool.Site(p.H.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 1, 0, 0) });
        Crowds(p, true);
        var crowds = p.H.Table.Crowds;
        for (int i = 0; i < 60 && crowds.Seen.Count < 5; i++)
        {
            p.H.S.Do(new AdvanceClock());
            await r.Frames(3);
        }
        Check.That(crowds.Seen.Count >= 5, $"people are out of doors ({crowds.Seen.Count})");
        p.H.Table.SetTool(Tool.Crowds);
        return crowds;
    }

    [NodeTest]
    static async Task An_umpire_wears_a_crowd_person_as_a_piece_and_on_letting_go_they_walk_back_into_their_day(TestRunner r)
    {
        var p = await Open(r);
        var crowds = await Peopled(r, p);
        var who = crowds.Seen[0];
        crowds.Picked.Add((who.Town, who.Person));
        var key = CrowdPiece.Key(crowds.Roster(who.Town).Thing, who.Person);
        var screen = p.H.Table.Rig.Cam.UnprojectPosition(who.At);
        Check.That(crowds.Tool.Right(true, screen, Coords.FromWorld(who.At)) && crowds.Tool.Right(false, screen, Coords.FromWorld(who.At)), "the menu opens on one person");
        await r.Frames(2);
        var take = Row(p.H.Table.GetTree().Root, "Take control") ?? throw new TestFailure("one person picked can be taken over");
        take.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(6);
        var piece = p.H.S.View.Layer(Layers.Umpire).SingleOrDefault(x => x.Crowd == key) ?? throw new TestFailure("the person became a piece");
        Check.That(piece.Def == CrowdPiece.DefId && piece.Look != null, "a crowd person piece wearing the person's look");
        await r.Seconds(1);
        Check.That(p.H.Table.Walk.InControl, "and the umpire walks in it");
        Check.That(p.H.Table.Pieces.NodeOf(Layers.Umpire, piece.Id)?.Model?.Mesh != null, "drawn from the look");
        Check.That(!crowds.Seen.Any(s => s.Town == who.Town && s.Person == who.Person), "the crowd no longer draws them");
        p.H.Table.Walk.LeaveControl();
        await r.Seconds(3.5);
        await r.Frames(3);
        Check.That(p.H.S.View.Layer(Layers.Umpire).All(x => x.Crowd != key), "let go, the piece is gone");
        Check.That(p.H.S.View.Crowds?.Orders?.Any(o => o.Do == CrowdCommands.Home && o.From != null) == true, "and they walk back into their day from where they stood");
    }

    [NodeTest]
    static async Task An_umpire_adds_a_mob_where_they_click_and_ordered_off_the_map_it_walks_away_and_is_gone(TestRunner r)
    {
        var p = await Open(r);
        Crowds(p, true);
        var crowds = p.H.Table.Crowds;
        p.H.Table.SetTool(Tool.Crowds);
        crowds.Tool.Add(MadeMixes.Mob);
        Check.That(crowds.Tool.Resize(-1) && crowds.Tool.AddCount == 20, "the size steps down to twenty");
        var at = new Vec2(Side / 2 - 2, 0);
        var cam = p.H.Table.Rig.Cam;
        p.H.Table.Rig.Frame(Coords.ToWorld(at), 2);
        await r.Frames(3);
        var screen = cam.UnprojectPosition(Coords.ToWorld(at));
        crowds.Tool.Press(screen, false, false);
        crowds.Tool.Release(screen, false);
        await r.Frames(6);
        var made = p.H.S.View.Crowds?.Made?.SingleOrDefault() ?? throw new TestFailure("a click makes the crowd");
        Check.Equal(20, made.Count, "of the size chosen");
        Check.Equal(20, crowds.Picked.Count, "and picks it, ready for orders");
        Check.Equal(20, crowds.Seen.Count(s => crowds.Roster(s.Town).Made), "they stand where they were placed");
        Check.That(crowds.Tool.Adding == null, "one click, one crowd");
        var open = new Vec2(Side / 2 - 6, 3);
        var spot = cam.UnprojectPosition(Coords.ToWorld(open));
        Check.That(crowds.Tool.Right(true, spot, open) && crowds.Tool.Right(false, spot, open), "a right-click opens their menu");
        await r.Frames(2);
        var leave = Row(p.H.Table.GetTree().Root, "Leave the map") ?? throw new TestFailure("the menu offers Leave the map");
        Check.That(Row(p.H.Table.GetTree().Root, "Remove") != null, "and Remove");
        leave.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        p.H.S.Do(new AdvanceClock());
        p.H.S.Do(new AdvanceClock());
        for (int i = 0; i < 40 && p.H.S.View.Crowds?.Made != null; i++) await r.Frames(5);
        Check.That(p.H.S.View.Crowds?.Made == null && p.H.S.View.Crowds?.Orders == null, "off the map, the crowd and its order are gone");
    }

    static List<Button> Rows(Node root, string text) =>
        All<PopupPanel>(root).Where(p => p.Visible).SelectMany(p => All<Button>(p)).Where(b => All<Label>(b).Any(l => l.Text == text)).ToList();

    static async Task<(Opened P, CrowdLayer Crowds, int A, int B)> TwoCrowds(TestRunner r)
    {
        var p = await Open(r);
        Crowds(p, true);
        var crowds = p.H.Table.Crowds;
        var view = p.H.S.View;
        MadeCrowd Made(string id, string mix, int count, Vec2 at) =>
            new() { Id = id, Name = id, Count = count, Mix = mix, Nationality = CrowdMade.NationalityAt(view, at), At = at, Seed = 3, Start = view.Clock.Seconds };
        p.H.S.Do(new SetMadeCrowd { Crowd = Made("m-a", MadeMixes.Soldiers, 8, new Vec2(0, 0)) });
        p.H.S.Do(new SetMadeCrowd { Crowd = Made("m-b", MadeMixes.Mob, 12, new Vec2(0.4, 0)) });
        p.H.Table.SetTool(Tool.Crowds);
        p.H.Table.Rig.Frame(Coords.ToWorld(new Vec2(0.2, 0)), 1);
        await r.Frames(6);
        int a = crowds.TownOf("m-a"), b = crowds.TownOf("m-b");
        Check.That(a >= 0 && b >= 0, "both crowds stand on the table");
        for (int i = 0; i < 8; i++) crowds.Picked.Add((a, i));
        return (p, crowds, a, b);
    }

    static async Task RightClick(TestRunner r, Opened p, CrowdLayer crowds, Vector3 at)
    {
        var screen = p.H.Table.Rig.Cam.UnprojectPosition(at);
        var ground = Coords.FromWorld(at);
        Check.That(crowds.Tool.Right(true, screen, ground) && crowds.Tool.Right(false, screen, ground), "a right-click on the other crowd opens the menu");
        await r.Frames(2);
    }

    [NodeTest]
    static async Task The_menu_on_another_group_offers_block_escort_hold_and_release_and_on_a_fallen_person_heal(TestRunner r)
    {
        var (p, crowds, _, b) = await TwoCrowds(r);
        var root = p.H.Table.GetTree().Root;
        var them = crowds.Seen.First(s => s.Town == b);
        await RightClick(r, p, crowds, them.At);
        Check.That(new[] { "Block", "Escort", "Hold" }.All(n => Row(root, n) != null), "Block, Escort and Hold are offered against a group");
        Row(root, "Hold")!.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Rows(root, "Hold").Last().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.Equal(12, p.H.S.View.Crowds?.Fates?.Values.Count(f => f.State == CrowdFates.Held && f.By != null) ?? 0, "all of them are held by the soldiers, the nearest first");
        await RightClick(r, p, crowds, them.At);
        var release = Row(root, "Release") ?? throw new TestFailure("the held can be released");
        release.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(p.H.S.View.Crowds?.Fates is not { Count: > 0 }, "released, they are nobody's");
        var key = CrowdPiece.Key("m-b", them.Person);
        p.H.S.Do(new SetCrowdFates { Fates = new() { [key] = new CrowdFate { State = CrowdFates.Down, At = p.H.S.View.Clock.Seconds } } });
        await r.Frames(3);
        await RightClick(r, p, crowds, crowds.Seen.First(s => s.Town == b && s.Person == them.Person).At);
        (Row(root, "Heal") ?? throw new TestFailure("a fallen person can be healed")).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Rows(root, "Heal").Last().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(p.H.S.View.Crowds?.Fates?[key].Until is { } rise && rise > p.H.S.View.Clock.Seconds, "healed, they rise when the minutes are up");
        Check.That(p.H.S.View.Crowds?.Orders?.Any(o => o.Act == RigMotion.Kneel) == true, "while the healers kneel by them");
    }

    [NodeTest]
    static async Task A_crowd_told_to_kill_another_fells_the_number_stated_and_one_undo_takes_back_the_order_and_the_fallen(TestRunner r)
    {
        var (p, crowds, _, b) = await TwoCrowds(r);
        await RightClick(r, p, crowds, crowds.Seen.First(s => s.Town == b).At);
        var root = p.H.Table.GetTree().Root;
        var open = Row(root, "Kill") ?? throw new TestFailure("the menu offers Kill against them");
        Check.That(Row(root, "Brawl") != null && Row(root, "Seize") != null && Row(root, "Rout") != null, "beside Brawl, Seize and Rout");
        open.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Rows(root, "Kill").Last().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.Equal(3, p.H.S.View.Crowds?.Fates?.Count ?? 0, "a quarter of them fall, the nearest first");
        Check.Equal(1, p.H.S.View.Crowds?.Orders?.Count ?? 0, "as the soldiers go at them");
        p.H.S.UndoLast();
        await r.Frames(3);
        Check.That(p.H.S.View.Crowds?.Fates == null && p.H.S.View.Crowds?.Orders == null, "one undo takes back the order and the fallen");
    }

    [NodeTest]
    static async Task The_agent_makes_a_crowd_orders_it_reads_the_groups_waits_for_them_and_states_what_befalls_them(TestRunner r)
    {
        var p = await Open(r);
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => p.H.S, TableHud = () => p.H.Hud };
        r.AddChild(agent);
        var (off, said) = await AgentRecordTests.Ask(agent, "crowds", """{"action":"make","mix":"mob","count":12,"x":0,"z":0}""");
        Check.That(off && said.Contains("set_light crowds"), "with the crowds off it says how to turn them on: " + said);
        var (error, on) = await AgentRecordTests.Ask(agent, "set_light", """{"crowds":true}""");
        Check.That(!error && p.H.S.View.Lighting.Crowds, "set_light turns them on: " + on);
        var (_, made) = await AgentRecordTests.Ask(agent, "crowds", """{"action":"make","mix":"mob","count":12,"x":0,"z":0}""");
        var id = p.H.S.View.Crowds?.Made?.SingleOrDefault()?.Id ?? throw new TestFailure("the crowd is made: " + made);
        await r.Frames(4);
        var (_, given) = await AgentRecordTests.Ask(agent, "crowds", $$"""{"action":"order","id":"{{id}}","order":"goto","points":[[0.6,0]],"how":"run"}""");
        var order = p.H.S.View.Crowds?.Orders?.SingleOrDefault() ?? throw new TestFailure("the order is given: " + given);
        var (_, listed) = await AgentRecordTests.Ask(agent, "crowds", """{"action":"list"}""");
        Check.That(listed.Contains(order.Id) && listed.Contains(id), "list_crowds shows the crowd and its order: " + listed);
        p.H.S.Do(new AdvanceClock());
        var (_, waited) = await AgentRecordTests.Ask(agent, "wait_for", $$"""{"what":"crowd","id":"{{order.Id}}","seconds":5}""");
        Check.That(waited.Contains("all 12 are there"), "wait_for says when they are all there: " + waited);
        var (_, fell) = await AgentRecordTests.Ask(agent, "crowds", $$"""{"action":"fate","id":"{{id}}","count":3,"state":"dead"}""");
        Check.Equal(3, p.H.S.View.Crowds?.Fates?.Count ?? 0, "crowd_fates lays the stated dead: " + fell);
        Check.That(p.H.Net.Server!.Record.Any(e => e.Text.StartsWith("Mob · Go to", StringComparison.Ordinal)), "the order is in the umpires' record");
        Check.Equal(CrowdPaces.Run, order.Pace, "how sets its pace");
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

    static BuiltPainter.Shape Drawn(Opened p, SheetDef sheet, int part)
    {
        var shapes = BuiltPainter.ShapesOf(Town(p), Ground.FullWidth(sheet), BuiltGeometry.FullHeight(sheet), (float)MapScale);
        Check.That(shapes.Count == Town(p).Parts.Count, "every part of the town is drawn");
        return shapes[part];
    }

    [NodeTest]
    static async Task A_house_s_wealth_redraws_it_on_the_map_and_raised_and_undo_draws_it_as_it_was(TestRunner r)
    {
        var (p, sheet, house) = await PickedTown(r);
        await Press(r, p, sheet, Town(p).Parts[house]);
        string style = p.H.S.View.Lighting.MapStyle;
        long laid = BuiltPainter.KeyOf(Town(p));
        var plain = RaisedLooks.Of(Drawn(p, sheet, house), style, HouseSideM, HouseLongM);
        p.Chip("Wealth", "Rich").EmitSignal(BaseButton.SignalName.Pressed);
        await Settled(r, p);
        var rich = Drawn(p, sheet, house);
        Check.That(rich.Class == "townhouse" && BuiltPainter.KeyOf(Town(p)) != laid, "a rich house is drawn as a town house: " + rich.Class);
        Check.That(RaisedLooks.Of(rich, style, HouseSideM, HouseLongM) != plain, "and raised as one");
        var at = BuiltGeometry.ToWorld(sheet, BuiltSite.Round(Town(p).Parts[house]).At.Stored);
        p.H.Table.Build.Zone.Brush([at], BrushReach, false);
        await Settled(r, p);
        Check.That(Drawn(p, sheet, house).Class == "house" && BuiltPainter.KeyOf(Town(p)) == laid, "brushed poorer it is a plain house again");
        p.H.Table.Build.Zone.Brush([at], BrushReach, false);
        await Settled(r, p);
        Check.That(Drawn(p, sheet, house).Class == "cottage" && RaisedLooks.Of(Drawn(p, sheet, house), style, HouseSideM, HouseLongM).Storeys == 1, "and poorer still a cottage of one storey");
        for (int undo = 0; undo < 3; undo++) p.H.S.UndoLast();
        await Settled(r, p);
        Check.That(Town(p).Parts[house].Wealth == null && BuiltPainter.KeyOf(Town(p)) == laid && RaisedLooks.Of(Drawn(p, sheet, house), style, HouseSideM, HouseLongM) == plain, "undone, it is drawn as laid");
    }

    const double BrushReach = 0.05;
    const float HouseSideM = 6, HouseLongM = 9;

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

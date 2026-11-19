using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed partial class PopulacePanel
{
    const int PreviewSide = 150, TreeHigh = 300, DayChip = 44;
    const string NewColour = "#8a7a66";

    string? _peopleId, _packId;
    Nationality? _draft;
    string _kept = "";
    int _sample = 1;
    bool _more;
    readonly Dictionary<string, Nationality> _drafts = new();

    List<PackManifest> TablePacks => S.View.Packs.Select(p => S.Store.Pack(p)).OfType<PackManifest>().ToList();

    List<PackManifest> OwnPacks => TablePacks.Where(p => !StandardPacks.IsStandard(p.Id)).ToList();

    bool Dirty => _draft != null && Json.Serialize(_draft) != _kept;

    void Peoples()
    {
        var known = Nationalities.Offered(TablePacks).ToList();
        if (known.Count == 0)
        {
            _page.AddChild(Hint("No nationalities"));
            return;
        }
        if (_peopleId == null || known.All(n => n.Id != _peopleId)) Choose(known.FirstOrDefault(n => S.View.Crowds?.Peoples.ContainsKey(n.Id) == true)?.Id ?? known[0].Id);
        var n = _draft!;
        var flat = Flat(n);
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Nationality" });
        form.AddChild(Ui.Picker(known.Select(k => (k.Id, k.Name ?? k.Id, k.Id, (Texture2D?)null)).ToList(), _peopleId!, Pick));
        form.AddChild(new Label { Text = "Lives in" });
        form.AddChild(new Label { Text = LivesIn(), ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = Ui.Muted });
        if (S.IsHost) PackRow(form);
        Ui.AlignForm(form);
        _page.AddChild(form);
        _page.AddChild(Preview());
        _page.AddChild(Header("Who they are"));
        var who = Ui.Form(Ui.PanelLabels);
        Field(who, "Name", n.Name ?? "", v => n.Name = v.Length == 0 ? null : v);
        Field(who, "Faith", n.Faith ?? "", v => n.Faith = v.Length == 0 ? null : v);
        Field(who, "Year", n.Year?.ToString(CultureInfo.InvariantCulture) ?? "", v => n.Year = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ? y : null);
        Ui.AlignForm(who);
        _page.AddChild(who);
        _page.AddChild(Header("Looks"));
        var looks = Ui.Form(Ui.PanelLabels);
        Swatches(looks, "Skin", n.Skins ?? flat?.Skins, v => n.Skins = v);
        Swatches(looks, "Hair", n.Hairs ?? flat?.Hairs, v => n.Hairs = v);
        Swatches(looks, "Eyes", n.Eyes ?? flat?.Eyes, v => n.Eyes = v);
        Ui.AlignForm(looks);
        _page.AddChild(looks);
        _page.AddChild(Header("Their week"));
        var week = Ui.Form(Ui.PanelLabels);
        Week(week, n.Day ?? flat?.Day, () => n.Day ??= Json.Clone(flat?.Day) ?? new DayRules());
        Ui.AlignForm(week);
        _page.AddChild(week);
        _page.AddChild(Ui.UnderControls(Btn(_more ? "Fewer" : "Everything else…", Expand, "Clothes, callings, heights, timetables, lights: the whole record"), Ui.PanelLabels));
        if (_more) _page.AddChild(Tree(n));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var keep = Ui.Primary("Keep", Keep, "Sets it on this table, and the host writes it into the pack");
        keep.Disabled = !Dirty;
        row.AddChild(keep);
        var revert = Btn("Revert", () => Pick(_peopleId!, fresh: true), "Back to the record as kept");
        revert.Disabled = !Dirty;
        row.AddChild(revert);
        _page.AddChild(Ui.UnderControls(row, Ui.PanelLabels));
    }

    string LivesIn()
    {
        var view = S.View;
        var towns = Enumerable.Range(0, T.Crowds.Towns).Select(T.Crowds.Roster).Where(t => t.People.Id == _peopleId).Select(t => CrowdScript.NameOf(view, t.Thing)).ToList();
        return towns.Count == 0 ? "No town" : string.Join(", ", towns);
    }

    void PackRow(GridContainer form)
    {
        var own = OwnPacks;
        _packId = own.Any(p => p.Id == _packId) ? _packId : own.FirstOrDefault(p => p.Nationalities?.Any(x => x.Id == _peopleId) == true)?.Id ?? own.FirstOrDefault()?.Id;
        form.AddChild(new Label { Text = "Pack" });
        var packs = Ui.Picker(own.Count == 0 ? [("", "None of the table's own", "", null)] : own.Select(p => (p.Id, p.Name, p.Id, (Texture2D?)null)).ToList(), _packId ?? "", id => _packId = id);
        packs.Disabled = own.Count == 0;
        packs.TooltipText = own.Count == 0 ? "Keep sets it on this table only: the table has no pack of its own" : "Keep writes it into this pack too";
        form.AddChild(packs);
    }

    void Field(GridContainer form, string label, string value, Action<string> put)
    {
        form.AddChild(new Label { Text = label });
        var edit = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        void Commit(string text)
        {
            if (text.Trim() == value) return;
            put(text.Trim());
            Edited();
        }
        edit.TextSubmitted += Commit;
        edit.FocusExited += () => Commit(edit.Text);
        form.AddChild(edit);
    }

    Nationality? Flat(Nationality n)
    {
        try { return CrowdDress.Flat(n, Nationalities.Of(TablePacks).ToList()); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    void Swatches(GridContainer form, string label, List<string>? shown, Action<List<string>?> put)
    {
        var colours = new List<string>(shown ?? []);
        void Put() => put(colours.Count == 0 ? null : new List<string>(colours));
        form.AddChild(new Label { Text = label });
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        flow.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        for (int k = 0; k < colours.Count; k++)
        {
            int at = k;
            var pair = new HBoxContainer();
            pair.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.None));
            var swatch = Ui.ColourField(Coords.ParseColor(colours[k], Colors.Gray), "Click to change it");
            swatch.ColorChanged += c => colours[at] = "#" + c.ToHtml(false);
            swatch.PopupClosed += () => Edit(Put);
            pair.AddChild(swatch);
            var drop = Ui.RowIcon("trash", "Remove this colour");
            drop.Pressed += () => Edit(() =>
            {
                colours.RemoveAt(at);
                Put();
            });
            pair.AddChild(drop);
            flow.AddChild(pair);
        }
        var add = Ui.RowIcon("plus", $"Add a {label.ToLowerInvariant()} colour");
        add.Pressed += () => Edit(() =>
        {
            colours.Add(colours.Count > 0 ? colours[^1] : NewColour);
            Put();
        });
        flow.AddChild(add);
        form.AddChild(flow);
    }

    void Week(GridContainer form, DayRules? shown, Func<DayRules> own)
    {
        var day = shown ?? new DayRules();
        var names = S.View.Clock.Cal.WeekDays;
        form.AddChild(new Label { Text = "Market" });
        var chips = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        chips.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        foreach (var name in names)
        {
            string d = name;
            bool on = day.MarketDays?.Any(m => m.Equals(d, StringComparison.OrdinalIgnoreCase)) == true;
            var chip = new Button
            {
                Text = d.Length > 3 ? d[..3] : d, ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(DayChip, 0), TooltipText = d,
            };
            chip.Toggled += now => Edit(() => own().MarketDays = Toggled(day.MarketDays, d, now));
            chips.AddChild(chip);
        }
        form.AddChild(chips);
        form.AddChild(new Label { Text = "Holy day" });
        var holy = names.Select(x => (x, x, "", (Texture2D?)null)).Prepend(("", "None", "", null)).ToList();
        form.AddChild(Ui.Picker(holy, names.FirstOrDefault(x => x.Equals(day.HolyDay, StringComparison.OrdinalIgnoreCase)) ?? "", h => Edit(() => own().HolyDay = h.Length == 0 ? null : h)));
    }

    static List<string>? Toggled(List<string>? days, string day, bool on)
    {
        var now = (days ?? []).Where(x => !x.Equals(day, StringComparison.OrdinalIgnoreCase)).ToList();
        if (on) now.Add(day);
        return now.Count == 0 ? null : now;
    }

    Control Tree(Nationality n)
    {
        var tree = new RecordTree();
        foreach (var shown in new[] { nameof(Nationality.Id), nameof(Nationality.Name), nameof(Nationality.Faith), nameof(Nationality.Year), nameof(Nationality.Skins), nameof(Nationality.Hairs) })
            tree.Hidden.Add(shown);
        tree.Hidden.Add(nameof(Nationality.Eyes));
        tree.View.CustomMinimumSize = Ui.Px(0, TreeHigh);
        tree.Changed += () => Callable.From(Rebuild).CallDeferred();
        tree.Show(n);
        return tree.View;
    }

    void Expand()
    {
        _more = !_more;
        Rebuild();
    }

    void Edit(Action change)
    {
        change();
        Edited();
    }

    void Edited() => Callable.From(Rebuild).CallDeferred();

    void Pick(string id) => Pick(id, fresh: false);

    void Pick(string id, bool fresh)
    {
        if (fresh) _drafts.Remove(id);
        Choose(id);
        Rebuild();
    }

    void Choose(string id)
    {
        if (_draft != null && _peopleId != null && Dirty) _drafts[_peopleId] = _draft;
        _peopleId = id;
        var flat = S.View.Crowds?.Peoples.GetValueOrDefault(id);
        var source = OwnPacks.SelectMany(p => p.Nationalities ?? []).FirstOrDefault(x => x.Id == id) ?? Nationalities.Offered(TablePacks).FirstOrDefault(x => x.Id == id);
        var kept = Json.Clone(source ?? flat ?? new Nationality { Id = id });
        _kept = Json.Serialize(kept);
        _draft = _drafts.GetValueOrDefault(id) ?? kept;
    }

    Control Preview()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        var turntable = new FigureTurntable(Ui.Px(PreviewSide, PreviewSide));
        row.AddChild(turntable);
        if (Sample() is { } look) turntable.Show(look);
        var another = Btn("Another", Another, "Another of its people");
        another.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(another);
        return Ui.UnderControls(row, Ui.PanelLabels);
    }

    void Another()
    {
        _sample++;
        Rebuild();
    }

    CrowdLook? Sample()
    {
        if (_draft == null) return null;
        try
        {
            var flat = CrowdDress.Flat(_draft, Nationalities.Of(TablePacks).ToList());
            var callings = flat.Callings?.Keys.ToList() ?? [];
            var entry = new CrowdEntry
            {
                Seed = _sample * 7919, Nationality = flat.Id, Sex = _sample % 2 == 0 ? CrowdEntry.Woman : CrowdEntry.Man, Band = CrowdEntry.Adult,
                Calling = callings.Count > 0 ? callings[_sample % callings.Count] : "", Wealth = CrowdEntry.Middle,
            };
            return CrowdDress.Dress(entry, flat, CrowdDress.ArmyCoat(flat, _sample));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            Say("It does not dress yet: " + e.Message);
            return null;
        }
    }

    void Keep()
    {
        if (_draft == null || _peopleId == null) return;
        var n = Json.Clone(_draft);
        n.Id = _peopleId;
        if (CrowdDress.Problems(n) is { Count: > 0 } problems)
        {
            Say("It does not hold together: " + string.Join("; ", problems));
            return;
        }
        if (S.IsHost && OwnPacks.FirstOrDefault(p => p.Id == _packId) is { } pack && !InPack(pack, n)) return;
        S.Do(new SetPeoples { Peoples = { [n.Id] = CrowdDress.Flat(n, Nationalities.Of(TablePacks).ToList()) } });
        _kept = Json.Serialize(_draft);
        _drafts.Remove(_peopleId);
        Say($"{n.Name ?? n.Id} kept");
        Rebuild();
    }

    bool InPack(PackManifest pack, Nationality n)
    {
        StudioNationalities.Put(pack, Json.Clone(n));
        try
        {
            Json.SaveBig(PackEdit.ManifestPath(S.Store, pack.Id), pack);
            return true;
        }
        catch (System.IO.IOException e)
        {
            Say("The pack could not be written: " + e.Message);
            return false;
        }
    }
}

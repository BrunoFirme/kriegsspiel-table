using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class PopulacePanel
{
    const int HeadHigh = 34, HeadPad = 8;
    static readonly Color CardFill = new(1, 1, 1, 0.035f), HeadFill = new(1, 1, 1, 0.06f);

    static readonly (string Key, string Text, string Tip)[] Days =
    [
        ("", "Usual", "As the calendar and its people's market and holy days say"),
        (CrowdTownSet.Market, "Market", "Market day: the square fills"),
        (CrowdTownSet.Holy, "Holy", "Holy day: church, rest, few carts"),
        (CrowdTownSet.Curfew, "Curfew", "Everyone stays home but the soldiers"),
    ];

    static readonly (string Key, string Text, string Tip, Texture2D? Sign)[] Everies =
    [
        ("1", "every minute", "", null),
        ("5", "every 5 minutes", "", null),
        ("15", "every 15 minutes", "", null),
        ("60", "every hour", "", null),
    ];

    static readonly (string Key, string Text, string Tip, Texture2D? Sign)[] Voices =
    [
        (Earshot.Whisper.ToString(CultureInfo.InvariantCulture), "whispered", "Heard about 2 metres away", null),
        (Earshot.Say.ToString(CultureInfo.InvariantCulture), "aloud", "Heard about 15 metres away", null),
        (Earshot.Shout.ToString(CultureInfo.InvariantCulture), "shouted", "Heard about 60 metres away", null),
    ];

    string? _town;
    bool _adding;

    void Towns()
    {
        var owners = T.Crowds.Watch.Owners();
        if (owners.Count == 0)
        {
            _page.AddChild(Hint("No towns"));
            return;
        }
        if (owners.All(o => o.Id != _town)) _town = owners[0].Id;
        foreach (var o in owners)
        {
            bool open = o.Id == _town;
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", Ui.Box(open ? CardFill : new Color(0, 0, 0, 0), 4, 0, 0));
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
            box.AddChild(Head(owners, o, open));
            if (open) Card(box, o);
            card.AddChild(box);
            _page.AddChild(card);
        }
    }

    Button Head(IReadOnlyList<CrowdOwner> owners, CrowdOwner o, bool open)
    {
        var b = new Button { ToggleMode = true, ButtonPressed = open, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(0, HeadHigh), TooltipText = open ? "" : "Open its settings" };
        b.AddThemeStyleboxOverride("normal", Ui.Box(new Color(0, 0, 0, 0), 4, HeadPad, 2));
        b.AddThemeStyleboxOverride("hover", Ui.Box(HeadFill, 4, HeadPad, 2));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(HeadFill, 4, HeadPad, 2));
        b.AddThemeStyleboxOverride("hover_pressed", Ui.Box(HeadFill, 4, HeadPad, 2));
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        row.OffsetLeft = Ui.Px(HeadPad);
        row.OffsetRight = -Ui.Px(HeadPad);
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        var name = new Label { Text = Unique(owners, o), VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontOverride("font", Ui.SansStrong());
        row.AddChild(name);
        var about = new Label
        {
            Text = Summary(o), ClipText = true, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        about.AddThemeColorOverride("font_color", Ui.Muted);
        row.AddChild(about);
        b.AddChild(row);
        string id = o.Id;
        b.Pressed += () => PickTown(id);
        return b;
    }

    string Summary(CrowdOwner o)
    {
        var parts = new List<string>();
        if (T.Crowds.TownOf(o.Id) is >= 0 and var ti) parts.Add(T.Crowds.Roster(ti).People.Name ?? T.Crowds.Roster(ti).People.Id);
        if (SetOf(o.Id).Day is { } day) parts.Add(Days.First(d => d.Key == day).Text.ToLowerInvariant());
        parts.Add($"{o.Out} out");
        return string.Join(" · ", parts);
    }

    void PickTown(string owner)
    {
        _town = owner;
        _adding = false;
        Rebuild();
    }

    void Card(VBoxContainer box, CrowdOwner o)
    {
        var set = SetOf(o.Id);
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = o.Name, MaxLength = CrowdOrder.MostName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        name.TextSubmitted += text => Rename(o.Id, text);
        name.FocusExited += () => Rename(o.Id, name.Text);
        form.AddChild(name);
        form.AddChild(new Label { Text = "People" });
        form.AddChild(PeoplePicker(o));
        if (!o.Made)
        {
            form.AddChild(new Label { Text = "Day" });
            form.AddChild(Ui.Segments(Days.Select(d => (d.Text, (set.Day ?? "") == d.Key, d.Tip, (Action)(() => Change(o.Id, s => s.Day = d.Key.Length == 0 ? null : d.Key)))).ToList()));
            form.AddChild(new Label { Text = "Traffic" });
            var traffic = new CheckButton { ButtonPressed = !set.NoTraffic, FocusMode = Control.FocusModeEnum.None, TooltipText = "Carts, coaches and riders on its ways" };
            traffic.Toggled += on => Change(o.Id, s => s.NoTraffic = !on);
            form.AddChild(traffic);
        }
        Ui.AlignForm(form);
        box.AddChild(form);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        row.AddChild(Btn("Go there", () => GoTo(o.Id), "The camera over it"));
        row.AddChild(Btn("Pick them", () => PickThem(o.Id), "Its people out of doors, picked with the Crowds tool for an order"));
        row.AddChild(Btn("Script…", () => Ctx.ShowScriptWindow(CrowdScript.Of(o.Id)), S.View.Crowds?.Scripts?.ContainsKey(o.Id) == true ? "Its script" : "Write its script"));
        box.AddChild(Ui.UnderControls(row, Ui.PanelLabels));
        Talk(box, o.Id, set.Talk ?? new CrowdTalk());
    }

    OptionButton PeoplePicker(CrowdOwner o)
    {
        var offered = Nationalities.Offered(TablePacks).Select(n => (n.Id, n.Name ?? n.Id, "", (Texture2D?)null)).ToList();
        var view = S.View;
        if (view.Crowds?.Made?.FirstOrDefault(m => m.Id == o.Id) is { } made)
            return Ui.Picker(offered, made.Nationality, id => Remade(made, id));
        string built = view.Built.FirstOrDefault(b => b.Id == o.Id) is { } thing ? CrowdRoster.BuiltNationality(thing, view) : "";
        string builtName = offered.FirstOrDefault(n => n.Id == built).Item2 ?? built;
        return Ui.Picker([("", $"As built · {builtName}", "The nationality its houses were laid for", null), .. offered.Where(n => n.Id != built)],
            SetOf(o.Id).People ?? "", id => Change(o.Id, s => s.People = id.Length == 0 ? null : id));
    }

    void Remade(MadeCrowd made, string nationality)
    {
        var changed = made.Clone();
        changed.Nationality = nationality;
        S.Do(new SetMadeCrowd { Crowd = changed });
    }

    void Rename(string owner, string text)
    {
        string name = text.Trim();
        var view = S.View;
        if (view.Crowds?.Made?.FirstOrDefault(m => m.Id == owner) is { } made)
        {
            if (name.Length == 0 || name == made.Name) return;
            var renamed = made.Clone();
            renamed.Name = name;
            S.Do(new SetMadeCrowd { Crowd = renamed });
        }
        else if (view.Built.FirstOrDefault(b => b.Id == owner) is { } thing && (name.Length == 0 ? null : name) != thing.Name)
        {
            var renamed = thing.Clone();
            renamed.Name = name.Length == 0 ? null : name;
            S.Do(new SetBuilt { Thing = renamed });
        }
    }

    void GoTo(string owner)
    {
        if (T.Crowds.TownOf(owner) is not (>= 0 and var ti)) return;
        var places = T.Crowds.Roster(ti).Places;
        var seen = T.Crowds.Watch.OutNow(owner);
        Vec2? centre = places.Count > 0 ? new Vec2(places.Average(p => p.At.X), places.Average(p => p.At.Z))
            : seen.Count > 0 ? new Vec2(seen.Average(p => p.At.X), seen.Average(p => p.At.Z)) : null;
        if (centre is not { } at) return;
        T.Rig.Frame(Coords.ToWorld(at, T.Crowds.HeightAt(ti, at)), TownFrameM * (float)(S.View.Calibration.UnitsPerMeter ?? 1));
    }

    void PickThem(string owner)
    {
        if (T.Crowds.TownOf(owner) is not (>= 0 and var ti)) return;
        T.Crowds.ClearPicks();
        foreach (var one in T.Crowds.Watch.OutNow(owner))
            if (CrowdPiece.Parse(one.Id) is { } who) T.Crowds.Picked.Add((ti, who.Person));
        T.SetTool(Tool.Crowds);
    }

    void Talk(VBoxContainer box, string owner, CrowdTalk talk)
    {
        box.AddChild(Header("Talk"));
        for (int k = 0; k < talk.Lines.Count; k++) box.AddChild(Wide(Phrase(owner, k, talk.Lines[k])));
        if (_adding) box.AddChild(Wide(Fresh(owner)));
        box.AddChild(Wide(Ui.AddRow("Add a phrase its people say", StartAdding)));
        var form = Ui.Form(Ui.PanelLabels);
        string every = talk.EveryMin.ToString("0.##", CultureInfo.InvariantCulture);
        var paced = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        paced.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        paced.AddChild(Ui.Picker(Everies.Any(e => e.Key == every) ? Everies : [.. Everies, (every, $"every {every} minutes", "", null)], every,
            m => Talked(owner, t => t.EveryMin = double.Parse(m, CultureInfo.InvariantCulture))));
        paced.AddChild(Ui.Picker(Voices, talk.Loud.ToString(CultureInfo.InvariantCulture), v => Talked(owner, t => t.Loud = int.Parse(v, CultureInfo.InvariantCulture))));
        form.AddChild(new Label { Text = "Said" });
        form.AddChild(paced);
        Ui.AlignForm(form);
        box.AddChild(form);
    }

    static MarginContainer Wide(Control row)
    {
        var under = Ui.UnderControls(row, Ui.PanelLabels);
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return under;
    }

    void StartAdding()
    {
        _adding = true;
        Rebuild();
    }

    HBoxContainer Fresh(string owner)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var fresh = new LineEdit { MaxLength = CrowdTalk.MostLine, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "A phrase its people say now and then" };
        bool done = false;
        fresh.TextSubmitted += text =>
        {
            done = true;
            AddPhrase(owner, text, more: true);
        };
        fresh.FocusExited += () =>
        {
            if (done) return;
            done = true;
            AddPhrase(owner, fresh.Text, more: false);
        };
        row.AddChild(fresh);
        var drop = Ui.RowIcon("trash", "Leave it out");
        drop.Pressed += () => AddPhrase(owner, "", more: false);
        row.AddChild(drop);
        fresh.CallDeferred(Control.MethodName.GrabFocus);
        return row;
    }

    HBoxContainer Phrase(string owner, int k, string line)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var edit = new LineEdit { Text = line, MaxLength = CrowdTalk.MostLine, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        edit.TextSubmitted += text => Rephrase(owner, k, text);
        edit.FocusExited += () => Rephrase(owner, k, edit.Text);
        row.AddChild(edit);
        var remove = Ui.RowIcon("trash", "Remove this phrase");
        remove.Pressed += () => Talked(owner, t => t.Lines.RemoveAt(k));
        row.AddChild(remove);
        return row;
    }

    void Talked(string owner, Action<CrowdTalk> change) => Change(owner, s => change(s.Talk ??= new CrowdTalk()));

    void Rephrase(string owner, int k, string text)
    {
        string line = text.Trim();
        if (SetOf(owner).Talk?.Lines is not { } lines || k >= lines.Count || lines[k] == line) return;
        if (line.Length == 0) Talked(owner, t => t.Lines.RemoveAt(k));
        else Talked(owner, t => t.Lines[k] = line);
    }

    void AddPhrase(string owner, string text, bool more)
    {
        string line = text.Trim();
        _adding = more && line.Length > 0;
        if (line.Length > 0) Talked(owner, t => t.Lines.Add(line));
        else Callable.From(Rebuild).CallDeferred();
    }
}

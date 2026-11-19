using System;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class PopulacePanel
{
    static readonly (string Key, string Text, string Tip, Texture2D? Sign)[] Days =
    [
        ("", "Calendar", "The nationality's market and holy days", null),
        (CrowdTownSet.Working, "Working day", "", null),
        (CrowdTownSet.Market, "Market day", "", null),
        (CrowdTownSet.Holy, "Holy day", "", null),
        (CrowdTownSet.Curfew, "Curfew", "Everyone stays home but the soldiers", null),
    ];

    static readonly (string Key, string Text, string Tip, Texture2D? Sign)[] Everies =
    [
        ("1", "Minute", "", null),
        ("5", "5 minutes", "", null),
        ("15", "15 minutes", "", null),
        ("60", "Hour", "", null),
    ];

    static readonly (string Key, string Text, string Tip, Texture2D? Sign)[] Voices =
    [
        (Earshot.Whisper.ToString(CultureInfo.InvariantCulture), "Whisper", "About 2 metres", null),
        (Earshot.Say.ToString(CultureInfo.InvariantCulture), "Say", "About 15 metres", null),
        (Earshot.Shout.ToString(CultureInfo.InvariantCulture), "Shout", "About 60 metres", null),
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
        var o = owners.FirstOrDefault(x => x.Id == _town) ?? owners[0];
        _town = o.Id;
        var set = SetOf(o.Id);
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Town" });
        form.AddChild(Ui.Picker(owners.Select(x => (x.Id, Unique(owners, x), x.Made ? "Made crowd" : "Town", (Texture2D?)null)).ToList(), o.Id, PickTown));
        form.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = o.Name, MaxLength = CrowdOrder.MostName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        name.TextSubmitted += text => Rename(o.Id, text);
        name.FocusExited += () => Rename(o.Id, name.Text);
        form.AddChild(name);
        form.AddChild(new Label { Text = "People" });
        form.AddChild(new Label { Text = $"{o.Out} out · {o.People}" });
        if (!o.Made)
        {
            form.AddChild(new Label { Text = "Day" });
            form.AddChild(Ui.Picker(Days, set.Day ?? "", k => Change(o.Id, s => s.Day = k.Length == 0 ? null : k)));
            form.AddChild(new Label { Text = "Traffic" });
            var traffic = new CheckButton { ButtonPressed = !set.NoTraffic, FocusMode = Control.FocusModeEnum.None };
            traffic.Toggled += on => Change(o.Id, s => s.NoTraffic = !on);
            form.AddChild(traffic);
        }
        Ui.AlignForm(form);
        _page.AddChild(form);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        row.AddChild(Btn("Go to", () => GoTo(o.Id)));
        row.AddChild(Btn("Script…", () => Ctx.ShowScriptWindow(CrowdScript.Of(o.Id)), S.View.Crowds?.Scripts?.ContainsKey(o.Id) == true ? "Its script" : "Write its script"));
        _page.AddChild(Ui.UnderControls(row, Ui.PanelLabels));
        Talk(o.Id, set.Talk ?? new CrowdTalk());
    }

    void PickTown(string owner)
    {
        _town = owner;
        Rebuild();
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

    void Talk(string owner, CrowdTalk talk)
    {
        _page.AddChild(Header("Talk"));
        var form = Ui.Form(Ui.PanelLabels);
        string every = talk.EveryMin.ToString("0.##", CultureInfo.InvariantCulture);
        form.AddChild(new Label { Text = "Every" });
        form.AddChild(Ui.Picker(Everies.Any(e => e.Key == every) ? Everies : [.. Everies, (every, $"{every} minutes", "", null)], every,
            m => Talked(owner, t => t.EveryMin = double.Parse(m, CultureInfo.InvariantCulture))));
        form.AddChild(new Label { Text = "Voice" });
        form.AddChild(Ui.Picker(Voices, talk.Loud.ToString(CultureInfo.InvariantCulture), v => Talked(owner, t => t.Loud = int.Parse(v, CultureInfo.InvariantCulture))));
        Ui.AlignForm(form);
        _page.AddChild(form);
        for (int k = 0; k < talk.Lines.Count; k++) _page.AddChild(Wide(Phrase(owner, k, talk.Lines[k])));
        if (_adding) _page.AddChild(Wide(Fresh(owner)));
        _page.AddChild(Wide(Ui.AddRow("Add a phrase its people say", StartAdding)));
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

    static MarginContainer Wide(Control row)
    {
        var under = Ui.UnderControls(row, Ui.PanelLabels);
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return under;
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

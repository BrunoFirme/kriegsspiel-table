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
    const int LinesHigh = 96;

    static readonly (string Key, string Text, string Tip, Texture2D? Sign)[] TalkPlaces =
        new[] { CrowdUse.Market, CrowdUse.Square, CrowdUse.Well, CrowdUse.Tavern, CrowdUse.Worship, CrowdUse.Gate, CrowdUse.Quay, CrowdUse.Fields, CrowdUse.Barracks, CrowdUse.Street }
            .Select(u => (u.ToString().ToLowerInvariant(), u.ToString(), "", (Texture2D?)null)).ToArray();

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

    string? _talkTown;

    void Talk()
    {
        var owners = T.Crowds.Watch.Owners();
        if (owners.Count == 0)
        {
            _page.AddChild(Hint("No towns"));
            return;
        }
        string owner = _talkTown = owners.Any(o => o.Id == _talkTown) ? _talkTown! : owners[0].Id;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Town" });
        form.AddChild(Ui.Picker(owners.Select(o => (o.Id, Unique(owners, o), "", (Texture2D?)null)).ToList(), owner, PickTalkTown));
        Ui.AlignForm(form);
        _page.AddChild(form);
        var talks = SetOf(owner).Talk ?? [];
        for (int k = 0; k < talks.Count; k++) _page.AddChild(TalkCard(owner, k, talks[k]));
        if (talks.Count < CrowdTownSet.MostTalks)
            _page.AddChild(Ui.UnderControls(Btn("Add talk", () => Change(owner, s => (s.Talk ??= new()).Add(new CrowdTalk())), "Lines its people say at one kind of place"), Ui.PanelLabels));
    }

    void PickTalkTown(string owner)
    {
        _talkTown = owner;
        Rebuild();
    }

    Control TalkCard(string owner, int k, CrowdTalk talk)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Place" });
        form.AddChild(Ui.Picker(TalkPlaces, talk.Place, p => Change(owner, s => s.Talk![k].Place = p)));
        var every = Everies.Any(e => e.Key == Minutes(talk.EveryMin)) ? Everies : [.. Everies, (Minutes(talk.EveryMin), $"{Minutes(talk.EveryMin)} minutes", "", null)];
        form.AddChild(new Label { Text = "Every" });
        form.AddChild(Ui.Picker(every, Minutes(talk.EveryMin), m => Change(owner, s => s.Talk![k].EveryMin = double.Parse(m, CultureInfo.InvariantCulture))));
        form.AddChild(new Label { Text = "Voice" });
        form.AddChild(Ui.Picker(Voices, talk.Loud.ToString(CultureInfo.InvariantCulture), v => Change(owner, s => s.Talk![k].Loud = int.Parse(v, CultureInfo.InvariantCulture))));
        form.AddChild(new Label { Text = "Lines" });
        var lines = new TextEdit
        {
            Text = string.Join("\n", talk.Lines), CustomMinimumSize = Ui.Px(0, LinesHigh), WrapMode = TextEdit.LineWrappingMode.Boundary, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "One line a row; someone staying at the place says one now and then",
        };
        lines.FocusExited += () =>
        {
            var kept = LinesOf(lines.Text);
            if (!kept.SequenceEqual(talk.Lines)) Change(owner, s => s.Talk![k].Lines = kept);
        };
        form.AddChild(lines);
        Ui.AlignForm(form);
        box.AddChild(form);
        var remove = Ui.IconBtn("trash", "Remove this talk");
        remove.Pressed += () => Change(owner, s => s.Talk!.RemoveAt(k));
        box.AddChild(Ui.UnderControls(remove, Ui.PanelLabels));
        return box;
    }

    static string Minutes(double minutes) => minutes.ToString("0.##", CultureInfo.InvariantCulture);

    static List<string> LinesOf(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => l.Length > CrowdTownSet.MostLine ? l[..CrowdTownSet.MostLine] : l).Take(CrowdTownSet.MostLines).ToList();
}

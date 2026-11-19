using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class PopulacePanel : HudPart
{
    public const string Key = "populace", Title = "Populace";
    const int Width = 360, Height = 520, MostFound = 40;
    const float TownFrameM = 400;
    const string Opened = "Towns: name, day, traffic and what its people say · Nationalities: right-click the tree to add or remove, Keep sets it · Find: by name or calling";
    const string TownsPage = "Towns", PeoplesPage = "Nationalities", ForcePage = "In force", FindPage = "Find";
    static readonly string[] Pages = [TownsPage, PeoplesPage, ForcePage, FindPage];

    public readonly FloatPanel Panel;
    readonly VBoxContainer _bar = new(), _page = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    string _shown = TownsPage, _query = "";
    bool _due;

    public PopulacePanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel(Key, Title);
        Panel.Resizable = true;
        Panel.CustomMinimumSize = shell.PanelSize(Key, Width, Height);
        Panel.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _page.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_page);
        Panel.Body.AddChild(_bar);
        Panel.Body.AddChild(scroll);
        Panel.VisibilityChanged += () =>
        {
            if (!Panel.Visible) return;
            Say(Opened);
            Rebuild();
        };
    }

    public void Applied(Command c)
    {
        if (!Panel.Visible || _due || !Touches(c)) return;
        _due = true;
        Callable.From(Redo).CallDeferred();
    }

    void Redo()
    {
        _due = false;
        Rebuild();
    }

    static bool Touches(Command c) => c switch
    {
        Batch b => b.Items.Any(Touches),
        SetCrowdTown or SetCrowdScript or SetCrowdOrder or SetCrowdFates or SetMadeCrowd or SetPeoples or LayBuilt or SetBuilt or RemoveBuilt or Undo or RestoreTurn or LoadSave => true,
        _ => false,
    };

    public void Show(string page)
    {
        _shown = page;
        Rebuild();
    }

    public void Rebuild()
    {
        if (T == null || !Panel.Visible) return;
        Empty(_bar);
        Empty(_page);
        _bar.AddChild(Ui.Segments(Pages.Select(p => (p, p == _shown, "", (Action)(() => Show(p)))).ToList()));
        if (!S.View.Lighting.Crowds) _page.AddChild(Hint("Crowds are off"));
        else if (_shown == TownsPage) Towns();
        else if (_shown == PeoplesPage) Peoples();
        else if (_shown == ForcePage) Force();
        else FindPeople();
    }

    static void Empty(Control box)
    {
        foreach (var c in box.GetChildren())
        {
            box.RemoveChild(c);
            c.QueueFree();
        }
    }

    CrowdTownSet SetOf(string owner) => S.View.Crowds?.Towns?.GetValueOrDefault(owner)?.Clone() ?? new();

    void Change(string owner, Action<CrowdTownSet> change)
    {
        var set = SetOf(owner);
        change(set);
        S.Do(new SetCrowdTown { Id = owner, Set = set });
    }

    static string Unique(IReadOnlyList<CrowdOwner> owners, CrowdOwner o) => owners.Count(x => x.Name == o.Name) > 1 ? $"{o.Name} · {o.Id}" : o.Name;

    void Force()
    {
        var view = S.View;
        long now = view.Clock.Seconds;
        _page.AddChild(Header("Orders"));
        var orders = view.Crowds?.Orders?.Where(o => o.Until is not { } u || u > now).ToList() ?? [];
        if (orders.Count == 0) _page.AddChild(Hint("None"));
        foreach (var o in orders) _page.AddChild(Line(o.Name, o.Members.Values.Sum(m => m.Count), "End this order", () => End(o, now)));
        _page.AddChild(Header("Fates"));
        var fates = view.Crowds?.Fates?.Where(f => f.Value.Holds(now)).GroupBy(f => f.Value.State).ToList() ?? [];
        if (fates.Count == 0) _page.AddChild(Hint("None"));
        foreach (var g in fates.OrderBy(g => CrowdFates.All.ToList().IndexOf(g.Key)))
        {
            var keys = g.Select(f => f.Key).ToList();
            _page.AddChild(Line(Capital(g.Key), keys.Count, "Back to their day", g.Key == CrowdFates.Kept ? null : () => Restore(keys)));
        }
        _page.AddChild(Header("Made crowds"));
        var made = view.Crowds?.Made ?? [];
        if (made.Count == 0) _page.AddChild(Hint("None"));
        foreach (var m in made) _page.AddChild(Line(m.Name, m.Count, "Remove this crowd", () => S.Do(new SetMadeCrowd { Crowd = m.Clone(), Remove = true })));
    }

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static HBoxContainer Line(string name, int count, string tip, Action? remove)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        row.AddChild(new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, TooltipText = name });
        row.AddChild(new Label { Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        if (remove == null) return row;
        var b = Ui.RowIcon("trash", tip);
        b.Pressed += remove;
        row.AddChild(b);
        return row;
    }

    void End(CrowdOrder o, long now)
    {
        var ended = o.Clone();
        ended.Until = Math.Max(ended.Start, now);
        S.Do(new SetCrowdOrder { Order = ended });
    }

    void Restore(List<string> keys) =>
        S.Do(new Batch { Items = keys.Chunk(SetCrowdFates.Most).Select(c => (Command)new SetCrowdFates { Fates = c.ToDictionary(k => k, _ => (CrowdFate?)null) }).ToList() });

    void FindPeople()
    {
        var box = new LineEdit { PlaceholderText = "Find", ClearButtonEnabled = true, Text = _query, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var found = new VBoxContainer();
        found.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        box.TextChanged += q => Found(found, q);
        _page.AddChild(box);
        _page.AddChild(found);
        Found(found, _query);
        box.CallDeferred(Control.MethodName.GrabFocus);
    }

    void Found(VBoxContainer found, string query)
    {
        _query = query;
        Empty(found);
        string q = query.Trim();
        if (q.Length < 2) return;
        var layer = T.Crowds;
        int shown = 0;
        for (int ti = 0; ti < layer.Towns && shown < MostFound; ti++)
        {
            var roster = layer.Roster(ti);
            string town = CrowdScript.NameOf(S.View, roster.Thing);
            for (int i = 0; i < roster.Residents.Count && shown < MostFound; i++)
            {
                var e = roster.Residents[i].Entry;
                if (!layer.Lives(ti, i) || !(e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Calling.Contains(q, StringComparison.OrdinalIgnoreCase))) continue;
                shown++;
                var (t, p) = (ti, i);
                var b = Btn($"{e.Name} · {e.Calling} · {town}", () => Visit(t, p));
                b.Alignment = HorizontalAlignment.Left;
                b.Flat = true;
                found.AddChild(b);
            }
        }
        if (shown == 0) found.AddChild(Hint("Nobody"));
    }

    void Visit(int ti, int i)
    {
        if (T.Crowds.Person(ti, i) is not { } person) return;
        T.Crowds.ClearPicks();
        T.Crowds.Picked.Add((ti, i));
        T.SetTool(Tool.Crowds);
        T.Rig.Frame(Coords.ToWorld(person.At, T.Crowds.HeightAt(ti, person.At)), 1);
    }
}

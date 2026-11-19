using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class StudioStates
{
    readonly PieceStudio _s;
    const int ChipsToARow = 10;

    public StudioStates(PieceStudio s) => _s = s;

    public int Chosen { get; set; }

    public void Choose(int state) => _ = ChooseAsync(state);

    public async Task ChooseAsync(int state)
    {
        if (_s.Sel is not { } d) return;
        state = StateLooks.Clamp(d, state);
        bool back = _s.CurFace is { Back: true } f && !PieceFaces.IsShapeFace(f.Key);
        Chosen = state;
        if (_s.View == "paint")
        {
            await _s.SwitchFace(PieceFaces.FaceFor(d, state, back));
            if (_s.Sel == d && _s.CurFace is { State: >= 0 } open) Chosen = open.State;
            if (_s.Model is { } m && m.Holds(d)) await m.Open(d);
            _s.RebuildLayers();
        }
        else if (_s.View == "model") await _s.OpenModel(d);
        else _s.RebuildLayers();
    }

    public void Add(int from, string? name = null)
    {
        if (_s.Sel is not { } d || (d.States.Count > 0 && (from < 0 || from >= d.States.Count))) return;
        _s.Apply(x =>
        {
            if (x.States.Count == 0) x.States.Add(StateLooks.FirstOf(x));
            var copy = StateLooks.Copy(x, Math.Max(0, from));
            copy.Name = name ?? copy.Name;
            x.States.Add(copy);
            Chosen = x.States.Count - 1;
        }, list: true, what: "a new state");
        if (_s.View == "paint") _ = _s.SwitchFace(PieceFaces.FaceFor(d, Chosen, false));
        else if (_s.View == "model") _ = _s.OpenModel(d);
        else _s.RebuildLayers();
    }

    public void AddCopy(string? name = null) => Add(_s.State, name ?? $"State {Math.Max(1, _s.Sel?.States.Count ?? 0) + 1}");

    public void Build(VBoxContainer page)
    {
        if (_s.Sel is not { } d || EffectFx.IsEffect(d)) return;
        var body = Inspector.Group(page, "States");
        int now = _s.State, count = Math.Max(1, d.States.Count);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        for (int from = 0; from < count; from += ChipsToARow)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
            var chips = new List<(string Text, bool Lit, string Tip, Action Open)>();
            for (int i = from; i < Math.Min(count, from + ChipsToARow); i++)
            {
                int at = i;
                chips.Add(($"{i + 1}", i == now, StateLooks.Name(d, i), () => Choose(at)));
            }
            row.AddChild(Ui.Segments(chips));
            if (from + ChipsToARow >= count) row.AddChild(Ui.Btn("＋", () => AddCopy(), "A new state, a copy of this one"));
            rows.AddChild(row);
        }
        body.AddChild(rows);
        if (d.States.Count > 0) body.AddChild(NameRow(d, now));
    }

    HBoxContainer NameRow(PieceDef d, int now)
    {
        var st = d.States[now];
        var named = new HBoxContainer();
        named.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var name = new LineEdit { Text = st.Name, PlaceholderText = $"State {now + 1}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "The state's name" };
        void Commit()
        {
            var t = name.Text.Trim();
            if (t != st.Name && _s.Sel == d && now < d.States.Count && d.States[now] == st) _s.Apply(x => x.States[now].Name = t, what: "a state's name");
        }
        name.TextSubmitted += _ => Commit();
        name.FocusExited += Commit;
        named.AddChild(name);
        var remove = Ui.RowIcon("trash", "Take this state away   (Ctrl+Z brings it back)");
        remove.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        remove.Pressed += () => _s.RemoveState(now);
        named.AddChild(remove);
        return named;
    }
}

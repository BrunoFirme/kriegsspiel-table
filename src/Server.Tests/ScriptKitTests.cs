using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting;

namespace KTable.Server.Tests;

public class ScriptKitTests
{
    const string U = Layers.Umpire;
    const string P = "p-000060";
    const string Def = "kitblock";

    static readonly PieceDef Block = new()
    {
        Id = Def, Name = "Kit block", Size = new(2, 1),
        Counters = { new CounterDef { Name = "Strength", Min = 0, Max = 20, Default = 9 } },
        States = { new PieceStateDef { Name = "Fresh" }, new PieceStateDef { Name = "Shaken" }, new PieceStateDef { Name = "Routed" } },
        TextLines = { new TextLineDef { Name = "Orders" } },
    };

    static readonly KitKind Kind = KitKind.Of(Block);

    static Table Laid()
    {
        var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, Block);
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = Def, Id = P, Pos = new(3, 3) });
        return t;
    }

    static PieceInstance Piece(Table t) => t.Server.State.Find(U, P)!;

    static List<string> Errors(Table t) => t.Server.Scripts.Console.Where(l => l.StartsWith("Script error", StringComparison.Ordinal)).ToList();

    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Table, HashSet<string>> Answered = new();

    static void Do(Table t, Command c)
    {
        t.Server.Handle(Table.Host, c);
        var answered = Answered.GetOrCreateValue(t);
        for (int round = 0; round < 3; round++)
        {
            var open = Flat(t.Server.Log).OfType<ShowDialog>().Where(d => answered.Add(d.Token)).ToList();
            if (open.Count == 0) return;
            foreach (var d in open) t.Server.Handle(Table.Host, new DialogResult { Token = d.Token, Value = d.Options.FirstOrDefault() ?? "Hold" });
        }
    }

    static void Exercise(Table t)
    {
        var p = Piece(t);
        for (int i = 0; i < p.Widgets.Count; i++)
        {
            var w = Piece(t).Widgets.ElementAtOrDefault(i);
            if (w?.OnClick != null)
            {
                Do(t, new ClickWidget { Layer = U, Id = P, Index = i + 1 });
                Do(t, new ClickWidget { Layer = U, Id = P, Index = i + 1, Alt = true });
            }
            if (w?.OnChange != null)
            {
                Do(t, new EditWidget { Layer = U, Id = P, Index = i + 1, Value = "Hold the ridge", Typing = true });
                Do(t, new EditWidget { Layer = U, Id = P, Index = i + 1, Value = "Hold the ridge" });
            }
        }
        for (int i = 0; i < Piece(t).MenuItems.Count; i++) Do(t, new PickMenuItem { Layer = U, Id = P, Index = i + 1 });
        Do(t, new HoldPiece { Layer = U, Id = P, Held = true });
        Do(t, new MovePiece { Layer = U, Id = P, Pos = new(6, 7) });
        Do(t, new SetState { Layer = U, Id = P, State = 1 });
        for (int i = 0; i < Piece(t).MenuItems.Count; i++) Do(t, new PickMenuItem { Layer = U, Id = P, Index = i + 1 });
        Do(t, new SetState { Layer = U, Id = P, State = 0 });
        Do(t, new FlipPiece { Layer = U, Id = P, FaceDown = true });
        Do(t, new FlipPiece { Layer = U, Id = P, FaceDown = false });
        Do(t, new AdvanceClock());
        Do(t, new NumberTyped { Layer = U, Id = P, Number = 4 });
    }

    static void Load(Table t, string script) => Do(t, new SetScript { Layer = U, Id = P, Text = script });

    static void Proves(string script, string what)
    {
        using var t = Laid();
        Load(t, script);
        Assert.True(Errors(t).Count == 0, $"{what} loads: {string.Join("\n", Errors(t))}\n{script}");
        Exercise(t);
        Load(t, script);
        Exercise(t);
        Assert.True(Errors(t).Count == 0, $"{what} runs: {string.Join("\n", Errors(t))}\n{script}");
    }

    static string Inserted(KitExample e, string script = "") => ScriptKit.Add(script, ScriptKit.Fit(e, Kind, script)).Text;

    public static TheoryData<string> Names() => new(ScriptKit.Examples.Select(e => e.Name));

    static KitExample Named(string name) => ScriptKit.Examples.Single(e => e.Name == name);

    [Fact]
    public void The_examples_come_with_the_server_and_each_has_its_own_name()
    {
        Assert.True(ScriptKit.Examples.Count >= 20);
        Assert.Equal(ScriptKit.Examples.Count, ScriptKit.Examples.Select(e => e.Name).Distinct().Count());
        Assert.All(ScriptKit.Examples, e => Assert.Null(e.Missing(Kind)));
    }

    [Theory, MemberData(nameof(Names))]
    public void Each_example_inserted_in_an_empty_script_loads_and_answers_every_click_menu_row_and_event(string name) =>
        Proves(Inserted(Named(name)), name);

    [Theory, MemberData(nameof(Names))]
    public void Each_example_inserted_twice_takes_new_names_and_still_runs(string name)
    {
        var once = Inserted(Named(name));
        var twice = ScriptKit.Add(once, ScriptKit.Fit(Named(name), Kind, once));
        if (twice.Refused != null) return;
        Proves(twice.Text, name + " twice");
    }

    [Fact]
    public void Every_example_together_in_one_script_runs()
    {
        var script = "";
        foreach (var e in ScriptKit.Examples)
        {
            var (text, refused) = ScriptKit.Add(script, ScriptKit.Fit(e, Kind, script));
            Assert.Null(refused);
            script = text;
        }
        Assert.Single(script.Split('\n'), l => l.StartsWith("function on_load", StringComparison.Ordinal));
        Assert.Single(script.Split('\n'), l => l.StartsWith("function on_save", StringComparison.Ordinal));
        Proves(script, "every example");
    }

    [Fact]
    public void A_kept_number_survives_a_reload()
    {
        using var t = Laid();
        var script = Inserted(Named("A number with − and +"));
        Load(t, script);
        int plus = Piece(t).Widgets.FindIndex(w => w.Label == "+") + 1;
        Do(t, new ClickWidget { Layer = U, Id = P, Index = plus });
        Do(t, new ClickWidget { Layer = U, Id = P, Index = plus });
        Assert.Contains(Piece(t).Widgets, w => w.Label == "2");
        Load(t, script);
        Assert.Contains(Piece(t).Widgets, w => w.Label == "2");
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void What_an_example_draws_follows_the_piece_into_every_state_once()
    {
        using var t = Laid();
        Load(t, Inserted(Named("A number with − and +")));
        for (int s = 1; s <= 3; s++)
        {
            Do(t, new SetState { Layer = U, Id = P, State = s % 3 });
            Assert.Equal(Piece(t).State, Piece(t).DrawnOnState);
            Assert.Single(Piece(t).Widgets, w => w.Label == "+");
        }
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void A_kind_of_one_state_gets_no_redraw_on_a_state_change()
    {
        var one = new KitKind(2, 1, [], [], []);
        var script = ScriptKit.Add("", ScriptKit.Fit(Named("Fixed lettering"), one, "")).Text;
        Assert.DoesNotContain("on_state", script);
        Assert.Contains("kit_draw()", script);
    }

    [Fact]
    public void What_is_typed_on_the_back_shows_on_the_front()
    {
        using var t = Laid();
        Load(t, Inserted(Named("Typed on the back, shown on the front")));
        int box = Piece(t).Widgets.FindIndex(w => w.Kind == "input") + 1;
        Do(t, new EditWidget { Layer = U, Id = P, Index = box, Value = "Col. Pogue", Typing = true });
        Assert.Contains(Piece(t).Widgets, w => w.Face == "front" && w.Label == "Col. Pogue");
    }

    [Fact]
    public void The_next_state_row_is_there_in_every_state_once()
    {
        using var t = Laid();
        Load(t, Inserted(Named("Next state, round and round")));
        for (int i = 0; i < 4; i++)
        {
            Assert.Single(Piece(t).MenuItems);
            Do(t, new PickMenuItem { Layer = U, Id = P, Index = 1 });
        }
        Assert.Equal(1, Piece(t).State);
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void An_example_goes_into_a_script_written_by_hand_with_one_line_handlers_and_other_parameter_names()
    {
        const string Own = "local hits = 0\nfunction on_load() self.menu_add(\"Hit\", \"hit\") end\nfunction hit(who) hits = hits + 1 end\nfunction on_state(a, b) print(b) end\n";
        var script = Inserted(Named("Mark it with a ring"), Own);
        script = Inserted(Named("Say when it changes state"), script);
        script = Inserted(Named("A number with − and +"), script);
        Assert.Contains("function on_load(saved)", script);
        Assert.Contains("local new = b", script);
        Proves(script, "a hand-written script");
        using var t = Laid();
        Load(t, script);
        Assert.Contains(Piece(t).MenuItems, m => m.Label == "Hit");
        Assert.Contains(Piece(t).MenuItems, m => m.Label == "Mark");
    }

    [Fact]
    public void An_example_needing_on_save_is_refused_where_the_script_keeps_its_own()
    {
        var (_, refused) = ScriptKit.Add("function on_save() return \"x\" end\n", ScriptKit.Fit(Named("A tick box"), Kind, ""));
        Assert.NotNull(refused);
    }

    public static TheoryData<int, int> Pairs()
    {
        var d = new TheoryData<int, int>();
        for (int t = 0; t < ScriptHelper.Triggers.Length; t++)
            for (int a = 0; a < ScriptHelper.Actions.Length; a++)
                if (ScriptHelper.Missing(ScriptHelper.Actions[a], ScriptHelper.Triggers[t], Kind) == null) d.Add(t, a);
        return d;
    }

    [Theory, MemberData(nameof(Pairs))]
    public void Every_helper_trigger_and_action_makes_a_script_that_runs(int trigger, int action)
    {
        var c = new HelperChoice
        {
            Trigger = trigger, Action = action, Label = "Fire \"now\"", Text = "{name} holds", Choices = "Advance, Hold", Amount = -2, State = 1, Dice = 2, Sides = 6,
        };
        var script = ScriptKit.Add("", ScriptHelper.Make(c, Kind, "")).Text;
        script = ScriptKit.Add(script, ScriptHelper.Make(c, Kind, script)).Text;
        Proves(script, $"{ScriptHelper.Triggers[trigger].Name} → {ScriptHelper.Actions[action].Name}");
    }

    [Fact]
    public void The_helper_keeps_away_actions_that_would_call_themselves_or_need_what_the_kind_lacks()
    {
        var bare = new KitKind(1, 1, [], [], []);
        var flip = ScriptHelper.Triggers.Single(t => t.Event == "on_flip");
        Assert.NotNull(ScriptHelper.Missing(ScriptHelper.Actions.Single(a => a.Name == "Turn it over"), flip, Kind));
        Assert.NotNull(ScriptHelper.Missing(ScriptHelper.Actions.Single(a => a.Name == "Add to a counter"), ScriptHelper.Triggers[0], bare));
        Assert.NotNull(ScriptKit.Examples.Single(e => e.Name == "Next state, round and round").Missing(bare));
    }
}

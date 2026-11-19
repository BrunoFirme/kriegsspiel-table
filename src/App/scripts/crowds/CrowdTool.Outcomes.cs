using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;
using KTable.Core.Protocol;
using C = KTable.Core.Model.CrowdCommands;
using F = KTable.Core.Model.CrowdFates;

namespace KTable.App;

sealed partial class CrowdTool
{
    const double AroundM = 15;
    const int Minute = 60, ShareKilled = 4;

    float Upm => (float)(c.S.View.Calibration.UnitsPerMeter ?? 1);

    string Key((int Town, int Person) p) => CrowdPiece.Key(layer.Roster(p.Town).Thing, p.Person);

    Vec2 At((int Town, int Person) p) => layer.Where(c.S.View, p.Town, p.Person, c.S.View.Clock.Seconds, Upm).At;

    List<(string Key, Vec2 At)> Keyed(IEnumerable<(int Town, int Person)> people) => people.Select(p => (Key(p), At(p))).ToList();

    static Vec2 Middle(IReadOnlyCollection<(string Key, Vec2 At)> people) =>
        people.Count == 0 ? default : new(people.Average(p => p.At.X), people.Average(p => p.At.Z));

    string? FateOf((int Town, int Person) p) => c.S.View.Crowds?.FateOf(Key(p), c.S.View.Clock.Seconds);

    List<(int Town, int Person)> Them((int Town, int Person) one, string? against)
    {
        if (against != null && c.S.View.Crowds?.Orders?.FirstOrDefault(o => o.Id == against) is { } given)
            return layer.Members(given).Where(m => !layer.Picked.Contains(m)).ToList();
        return layer.Within(At(one), AroundM * Upm).Where(m => !layer.Picked.Contains(m)).ToList();
    }

    void StateRows(MenuCard menu, Vec2 at)
    {
        var picked = Chosen();
        if (picked.Count == 0) return;
        var states = picked.Select(FateOf).ToList();
        menu.Separator();
        if (picked.Count == 1)
        {
            menu.Page("State", () =>
            {
                foreach (var (state, name) in new (string? State, string Name)[] { (null, "Up"), (F.Down, "Down"), (F.Dead, "Dead"), (F.Held, "Held"), (F.Gone, "Gone") })
                    menu.Check(name, states[0] == state, () => Fates(state == null ? CrowdOutcomes.Lifted([Key(picked[0])]) : CrowdOutcomes.Set([Key(picked[0])], state, Now)));
            });
            return;
        }
        var standing = picked.Where((_, k) => states[k] == null).ToList();
        if (standing.Count > 0)
            menu.Page("Fell", () => Stated(menu, "Fell", [("Down", standing.Count, 0), ("Dead", standing.Count, 0)], (n, minutes) =>
            {
                var keyed = Keyed(standing);
                var down = CrowdOutcomes.Spread(keyed, at, n[0], F.Down, Now, minutes * Minute);
                var dead = CrowdOutcomes.Spread(keyed.Where(p => !down.ContainsKey(p.Key)), at, n[1], F.Dead, Now, minutes * Minute);
                Fates(down.Concat(dead).ToDictionary(f => f.Key, f => f.Value));
            }));
        var down = picked.Where((_, k) => states[k] == F.Down).Select(Key).ToList();
        var fallen = picked.Where((_, k) => states[k] is F.Down or F.Dead).Select(Key).ToList();
        var held = picked.Where((_, k) => states[k] == F.Held).ToList();
        var dead = picked.Where((_, k) => states[k] == F.Dead).ToList();
        if (down.Count > 0) menu.Item("Heal", () => Fates(CrowdOutcomes.Lifted(down)));
        if (fallen.Count > 0) menu.Item("Raise", () => Fates(CrowdOutcomes.Lifted(fallen)));
        if (standing.Count > 0) menu.Item("Hold", () => Fates(CrowdOutcomes.Set(standing.Select(Key), F.Held, Now)));
        if (held.Count > 0)
        {
            menu.Item("Release", () => Fates(CrowdOutcomes.Lifted(held.Select(Key))));
            menu.Page("Execute", () => Stated(menu, "Execute", [("Dead", held.Count, held.Count)], (n, minutes) => Fates(CrowdOutcomes.Spread(Keyed(held), at, n[0], F.Dead, Now, minutes * Minute))));
        }
        if (dead.Count > 0)
            menu.Page("Clear the dead", () => Stated(menu, "Clear the dead", [], (_, minutes) => Fates(CrowdOutcomes.Spread(Keyed(dead), at, dead.Count, F.Gone, Now, minutes * Minute))));
    }

    void OutcomeRows(MenuCard menu, (int Town, int Person) one, string? against)
    {
        var them = Them(one, against);
        if (them.Count == 0 || Chosen().Count == 0) return;
        menu.Separator();
        if (FateOf(one) == F.Down) menu.Page("Heal", () => Stated(menu, "Heal", [], (_, minutes) => Heal(one, minutes)));
        menu.Page("Kill", () => Stated(menu, "Kill", [("Dead", them.Count, Math.Max(1, them.Count / ShareKilled))], (n, minutes) =>
            Outcome(C.Attack, them, minutes, null, null, keyed => CrowdOutcomes.Spread(keyed.Them, keyed.Ours, n[0], F.Dead, Now, minutes * Minute), until: false)));
        menu.Page("Brawl", () => Stated(menu, "Brawl", [("Down, ours", Chosen().Count, 0), ("Down, theirs", them.Count, 0)], (n, minutes) =>
            Outcome(C.Attack, them, minutes, C.Attack, null, keyed => CrowdOutcomes.Spread(keyed.Us, keyed.Theirs, n[0], F.Down, Now, minutes * Minute)
                .Concat(CrowdOutcomes.Spread(keyed.Them, keyed.Ours, n[1], F.Down, Now, minutes * Minute)).ToDictionary(f => f.Key, f => f.Value))));
        menu.Item("Chase", () => Outcome(C.Attack, them, 0, C.Flee, null, null, until: false, pace: CrowdPaces.Run));
        menu.Item("Rout", () => Outcome(C.Halt, them, 0, C.Flee, null, null, until: false, act: RigMotion.Cheer));
        var middle = Middle(Keyed(them));
        menu.Item("Block", () => Give(C.Form, [Between(middle)], shape: CrowdShapes.Line, facing: Facing(middle)));
        menu.Item("Escort", () => Give(C.Follow, [middle], against: against, shape: CrowdShapes.Ring));
        menu.Page("Hold", () => Stated(menu, "Hold", [("Held", them.Count, them.Count)], (n, minutes) =>
            Fates(CrowdOutcomes.Spread(Keyed(them), Middle(Keyed(Chosen())), n[0], F.Held, Now, minutes * Minute, Keyed(Chosen())))));
        var held = them.Where(p => c.S.View.Crowds?.Fates?.GetValueOrDefault(Key(p))?.State == F.Held).ToList();
        if (held.Count > 0) menu.Item("Release", () => Fates(CrowdOutcomes.Lifted(held.Select(Key))));
        menu.Page("Seize", () => Stated(menu, "Seize", [("Held", them.Count, Math.Min(them.Count, Chosen().Count))], (n, minutes) =>
            Outcome(C.Attack, them, minutes, null, null, keyed => CrowdOutcomes.Spread(keyed.Them, keyed.Ours, n[0], F.Held, Now, minutes * Minute, keyed.Us), act: RigMotion.Shove)));
        menu.Page("Rob", () => Stated(menu, "Rob", [], (_, minutes) => Outcome(C.Surround, them, minutes, C.Yield, null, null, act: RigMotion.Shove)));
        menu.Page("Search", () => Stated(menu, "Search", [], (_, minutes) => Outcome(C.Surround, them, minutes, C.Yield, null, null, act: RigMotion.Work)));
        menu.Page("Give", () => Stated(menu, "Give", [], (_, minutes) => Outcome(C.GoTo, them, minutes, C.Halt, null, null, act: RigMotion.Work)));
        menu.Page("Address", () => Stated(menu, "Address", [], (_, minutes) => Outcome(C.Halt, them, minutes, C.Gather, null, null)));
        if (against != null && c.S.View.Crowds?.Orders?.FirstOrDefault(o => o.Id == against) is { } given) menu.Item("Join", () => Join(given));
    }

    long Now => c.S.View.Clock.Seconds;

    const double SettleReachM = 150;

    void SettleRows(MenuCard menu, Vec2 at)
    {
        var made = Chosen().GroupBy(p => p.Town).Where(g => layer.Roster(g.Key).Made && g.Count() == layer.Roster(g.Key).Residents.Count).Select(g => layer.Roster(g.Key).Thing).ToList();
        if (made.Count == 0 || layer.TownNear(at, SettleReachM * Upm) is not { } town) return;
        string name = c.S.View.Built.FirstOrDefault(b => b.Id == town)?.Name is { Length: > 0 } called ? called : "the town";
        menu.Item($"Settle in {name}", () => c.S.Do(new Batch { Items = CrowdMade.Settling(c.S.View, made, town) }));
    }

    const double BlockShare = 0.35;

    Vec2 Between(Vec2 them)
    {
        var us = Middle(Keyed(Chosen()));
        return new Vec2(them.X + (us.X - them.X) * BlockShare, them.Z + (us.Z - them.Z) * BlockShare);
    }

    void Heal((int Town, int Person) one, int minutes)
    {
        if (c.S.View.Crowds?.Fates?.GetValueOrDefault(Key(one)) is not { } fate) return;
        long rise = Now + Math.Max(1, minutes) * Minute;
        var healed = fate.Clone();
        healed.Until = rise;
        var items = new List<Command> { new SetCrowdFates { Fates = new() { [Key(one)] = healed } } };
        if (Order(C.GoTo, [layer.FallenAt(one.Town, one.Person)], CrowdShapes.Loose, 0, 0, RigMotion.Kneel, null, null, null) is { } go)
        {
            go.Until = rise;
            items.Add(new SetCrowdOrder { Order = go });
        }
        c.S.Do(new Batch { Items = items });
    }

    void Stated(MenuCard menu, string name, (string Name, int Most, int Value)[] counts, Action<int[], int> give)
    {
        var values = counts.Select(k => Math.Clamp(k.Value, 0, k.Most)).ToArray();
        int minutes = CrowdOutcomes.UsualMinutes;
        for (int k = 0; k < counts.Length; k++)
        {
            int slot = k;
            menu.Stepper(counts[k].Name, values[k], 0, counts[k].Most, v => values[slot] = v);
        }
        menu.Stepper("Minutes", minutes, 0, CrowdOutcomes.MostMinutes, v => minutes = v);
        menu.Item(name, () => give(values, minutes));
    }

    sealed record Sides(List<(string Key, Vec2 At)> Us, List<(string Key, Vec2 At)> Them, Vec2 Ours, Vec2 Theirs);

    void Outcome(string ours, List<(int Town, int Person)> them, int minutes, string? theirs, string? theirAct, Func<Sides, Dictionary<string, CrowdFate?>>? fates,
        bool until = true, string? pace = null, string? act = null)
    {
        var us = Keyed(Chosen());
        var sides = new Sides(us, Keyed(them), Middle(us), default);
        sides = sides with { Theirs = Middle(sides.Them) };
        long? end = until && minutes > 0 ? Now + minutes * Minute : null;
        string ourId = NewOrder(), theirId = NewOrder();
        var items = new List<Command>();
        if (Order(ours, ours == C.Halt ? [] : [sides.Theirs], CrowdShapes.Loose, 0, 0, act, null, theirs != null ? theirId : null, pace) is { } mine)
        {
            (mine.Id, mine.Until) = (ourId, end);
            if (ours == C.Surround) mine.Shape = CrowdShapes.Ring;
            items.Add(new SetCrowdOrder { Order = mine });
        }
        if (theirs != null)
        {
            var members = them.GroupBy(p => p.Town).ToDictionary(g => layer.Roster(g.Key).Thing, g => g.Select(p => p.Person).Order().ToList());
            items.Add(new SetCrowdOrder
            {
                Order = new CrowdOrder
                {
                    Id = theirId, Name = $"{C.NameOf(theirs)} · {them.Count}", Members = members, Said = $"{them.Count}", Do = theirs, To = theirs is C.Halt or C.Yield ? [] : [sides.Ours],
                    Against = theirs == C.Attack ? ourId : null, Pace = theirs == C.Flee ? CrowdPaces.Run : Pace, Act = theirAct, Start = Now, Until = end,
                },
            });
        }
        if (fates?.Invoke(sides) is { Count: > 0 } changed) items.Add(new SetCrowdFates { Fates = changed });
        if (items.Count > 0) c.S.Do(new Batch { Items = items });
    }

    static string NewOrder() => "o-" + Guid.NewGuid().ToString("N")[..10];

    void Join(CrowdOrder given)
    {
        if (Order(given.Do, given.To.ToList(), given.Shape, given.Facing, given.WidthM, given.Act, given.Piece, given.Against, given.Pace) is not { } joined) return;
        joined.Name = given.Name;
        c.S.Do(new SetCrowdOrder { Order = joined });
    }

    void Fates(Dictionary<string, CrowdFate?> fates)
    {
        if (fates.Count > 0) c.S.Do(new SetCrowdFates { Fates = fates.Take(SetCrowdFates.Most).ToDictionary(f => f.Key, f => f.Value) });
    }
}

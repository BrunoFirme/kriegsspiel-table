using KTable.Core.Model;

namespace KTable.Core.Protocol;

public interface IAppliesItself
{
    string? Applied(GameState g, ref Command? inverse);
}

public sealed class SetPopulation : Command, IAppliesItself
{
    public string Thing { get; set; } = "";
    public List<int> Parts { get; set; } = new();
    public List<int> Levels { get; set; } = new();

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (g.Built.FirstOrDefault(b => b.Id == Thing) is not { } thing) return "no such built thing";
        if (Parts.Count != Levels.Count || Parts.Count == 0) return "a level for each part";
        if (Parts.Any(p => p < 0 || p >= thing.Parts.Count)) return "no such part";
        if (Levels.Any(l => l is < Population.Least or > Population.Most)) return $"a level from {Population.Least} to {Population.Most}";
        inverse = new SetPopulation { Thing = Thing, Parts = Parts.ToList(), Levels = Parts.Select(p => Population.Of(thing.Parts[p])).ToList() };
        for (int i = 0; i < Parts.Count; i++) thing.Parts[Parts[i]].Population = Levels[i] == Population.Usual ? null : Levels[i];
        return null;
    }

    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetKeepClear : Command, IAppliesItself
{
    public List<ClearStroke> Strokes { get; set; } = new();

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (Strokes.Count > CrowdState.MostStrokes) return $"at most {CrowdState.MostStrokes} strokes";
        if (Strokes.Any(s => !s.Sound)) return "a stroke needs points and a width";
        inverse = new SetKeepClear { Strokes = g.Crowds?.Clear?.Select(s => s.Clone()).ToList() ?? new() };
        var crowds = g.Crowds ?? new CrowdState();
        crowds.Clear = Strokes.Count == 0 ? null : Strokes.Select(s => s.Clone()).ToList();
        g.Crowds = crowds.Empty ? null : crowds;
        return null;
    }

    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetCrowdOrder : Command, IAppliesItself
{
    public CrowdOrder Order { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Remove { get; set; }

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (Order.Id.Length == 0) return "an order needs its id";
        if (!Remove && Order.Problem() is { } problem) return problem;
        var crowds = g.Crowds ?? new CrowdState();
        var orders = crowds.Orders ??= new();
        int at = orders.FindIndex(o => o.Id == Order.Id);
        if (at < 0 && Remove) return "no such order";
        if (at < 0 && orders.Count >= CrowdState.MostOrders) return $"at most {CrowdState.MostOrders} orders on a table";
        inverse = at >= 0 ? new SetCrowdOrder { Order = orders[at].Clone() } : new SetCrowdOrder { Order = Order.Clone(), Remove = true };
        if (Remove) orders.RemoveAt(at);
        else
        {
            var kept = Order.Clone();
            kept.Tidy();
            if (at >= 0) orders[at] = kept;
            else orders.Add(kept);
        }
        if (orders.Count == 0) crowds.Orders = null;
        g.Crowds = crowds.Empty ? null : crowds;
        return null;
    }

    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class RestampCrowdOrder : Command, IAppliesItself
{
    public string Id { get; set; } = "";
    public List<Vec2> To { get; set; } = new();
    public long Start { get; set; }

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (g.Crowds?.Orders?.FirstOrDefault(o => o.Id == Id) is not { } order) return "no such order";
        if (To.Count is 0 or > CrowdOrder.MostPoints || To.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Z))) return "the order's places are off the table";
        if (Start < order.Start || order.Until is { } until && Start > until) return "a restamp falls within its order";
        inverse = new RestampCrowdOrder { Id = Id, To = order.To.ToList(), Start = order.Start };
        (order.To, order.Start) = (To.ToList(), Start);
        return null;
    }

    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetCrowdFates : Command, IAppliesItself
{
    public const int Most = 5000;
    public Dictionary<string, CrowdFate?> Fates { get; set; } = new();

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (Fates.Count is 0 or > Most) return $"1 to {Most} people at a time";
        if (Fates.Any(f => CrowdPiece.Parse(f.Key) == null || f.Value != null && !CrowdFates.All.Contains(f.Value.State))) return "no such person or state";
        var crowds = g.Crowds ?? new CrowdState();
        var fates = crowds.Fates ??= new();
        inverse = new SetCrowdFates { Fates = Fates.Keys.ToDictionary(k => k, k => fates.TryGetValue(k, out var was) ? was.Clone() : null) };
        foreach (var (key, fate) in Fates)
            if (fate == null) fates.Remove(key);
            else fates[key] = fate.Clone();
        if (fates.Count == 0) crowds.Fates = null;
        g.Crowds = crowds.Empty ? null : crowds;
        return null;
    }

    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetMadeCrowd : Command, IAppliesItself
{
    public MadeCrowd Crowd { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Remove { get; set; }

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (Crowd.Id.Length == 0) return "a made crowd needs its id";
        if (!Remove && (Crowd.Problem() ?? CrowdMade.Refusal(g, Crowd.At) ?? CrowdMade.Unsettled(g, Crowd)) is { } problem) return problem;
        var crowds = g.Crowds ?? new CrowdState();
        var made = crowds.Made ??= new();
        int at = made.FindIndex(m => m.Id == Crowd.Id);
        if (at < 0 && Remove) return "no such crowd";
        if (!Remove && made.Where((_, i) => i != at).Sum(m => m.Count) + Crowd.Count > g.Lighting.MadeMost) return $"at most {g.Lighting.MadeMost} made people on this table";
        inverse = at >= 0 ? new SetMadeCrowd { Crowd = made[at].Clone() } : new SetMadeCrowd { Crowd = Crowd.Clone(), Remove = true };
        if (Remove) made.RemoveAt(at);
        else if (at >= 0) made[at] = Crowd.Clone();
        else made.Add(Crowd.Clone());
        if (made.Count == 0) crowds.Made = null;
        g.Crowds = crowds.Empty ? null : crowds;
        return null;
    }

    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [CrowdScript.Of(Crowd.Id)];
}

public sealed class SetCrowdScript : Command, IAppliesItself
{
    public string Id { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; }

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (!CrowdScript.Owns(g, Id)) return "no such town or crowd";
        var crowds = g.Crowds ?? new CrowdState();
        var scripts = crowds.Scripts ??= new();
        inverse = new SetCrowdScript { Id = Id, Text = scripts.GetValueOrDefault(Id)?.Script };
        if (string.IsNullOrWhiteSpace(Text)) scripts.Remove(Id);
        else if (scripts.TryGetValue(Id, out var kept)) kept.Script = Text;
        else scripts[Id] = new CrowdScript { Script = Text };
        if (scripts.Count == 0) crowds.Scripts = null;
        g.Crowds = crowds.Empty ? null : crowds;
        return null;
    }

    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [CrowdScript.Of(Id)];
}

public sealed class SetCrowdTown : Command, IAppliesItself
{
    public string Id { get; set; } = "";
    public CrowdTownSet Set { get; set; } = new();

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (!CrowdScript.Owns(g, Id)) return "no such town or crowd";
        if (Set.Problem() is { } problem) return problem;
        var crowds = g.Crowds ?? new CrowdState();
        var towns = crowds.Towns ??= new();
        inverse = new SetCrowdTown { Id = Id, Set = towns.GetValueOrDefault(Id)?.Clone() ?? new() };
        if (Set.Empty) towns.Remove(Id);
        else towns[Id] = Set.Clone();
        if (towns.Count == 0) crowds.Towns = null;
        g.Crowds = crowds.Empty ? null : crowds;
        return null;
    }

    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetCrowdScriptState : Command, IAppliesItself
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (g.Crowds?.Scripts?.GetValueOrDefault(Id) is not { } kept) return "no script for that town or crowd";
        inverse = new SetCrowdScriptState { Id = Id, State = kept.State ?? "" };
        kept.State = State.Length == 0 ? null : State;
        return null;
    }

    public override Sender SentBy() => Sender.Runtime;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [CrowdScript.Of(Id)];
}

public sealed class SetLook : Command, IAppliesItself
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Layer { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Person { get; set; }
    public CrowdLook? Look { get; set; }

    public string? Applied(GameState g, ref Command? inverse)
    {
        if (Look != null && CrowdWardrobe.Problem(Look) is { } problem) return problem;
        if (Person != null)
        {
            if (CrowdPiece.Parse(Person) == null) return "no such person";
            var crowds = g.Crowds ?? new CrowdState();
            var dressed = crowds.Dressed ??= new();
            inverse = new SetLook { Person = Person, Look = dressed.GetValueOrDefault(Person)?.Clone() };
            if (Look == null) dressed.Remove(Person);
            else dressed[Person] = Look.Clone();
            if (dressed.Count == 0) crowds.Dressed = null;
            g.Crowds = crowds.Empty ? null : crowds;
            return null;
        }
        if (Layer == null || Id == null || g.Find(Layer, Id) is not { } piece) return "no such piece";
        inverse = new SetLook { Layer = Layer, Id = Id, Look = piece.Look?.Clone() };
        piece.Look = Look?.Clone();
        return null;
    }

    public override Sender SentBy() => Sender.Umpire;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => Layer != null && Id != null ? [(Layer, Id)] : [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class SetPeoples : Command, IAppliesItself
{
    public Dictionary<string, Nationality?> Peoples { get; set; } = new();

    public string? Applied(GameState g, ref Command? inverse)
    {
        var crowds = g.Crowds ?? new CrowdState();
        if (Peoples.Keys.Union(crowds.Peoples.Keys).Count() > CrowdState.MostPeoples) return $"at most {CrowdState.MostPeoples} nationalities on a table";
        if (Peoples.Any(p => p.Value != null && (p.Value.Id != p.Key || CrowdDress.Problems(p.Value).Count > 0))) return "a nationality that does not hold together";
        inverse = new SetPeoples { Peoples = Peoples.Keys.ToDictionary(k => k, k => crowds.Peoples.TryGetValue(k, out var was) ? Json.Clone(was) : null) };
        foreach (var (id, people) in Peoples)
            if (people == null) crowds.Peoples.Remove(id);
            else crowds.Peoples[id] = Json.Clone(people);
        g.Crowds = crowds.Empty ? null : crowds;
        return null;
    }

    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Table;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

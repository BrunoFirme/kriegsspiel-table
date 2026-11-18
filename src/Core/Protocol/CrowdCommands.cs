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

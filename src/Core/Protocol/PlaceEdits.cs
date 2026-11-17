using KTable.Core.Model;

namespace KTable.Core.Protocol;

static class PlaceEdits
{
    public static string? Apply(GameState g, Command c, ref Command? inverse) => c switch
    {
        SetPlace sp => Set(g, sp, ref inverse),
        RemovePlace rp => Remove(g, rp, ref inverse),
        _ => "not a place edit",
    };

    static string? Set(GameState g, SetPlace sp, ref Command? inverse)
    {
        var pin = sp.Place;
        string name = pin.Name.Trim();
        if (name.Length is 0 or > PlacePin.MostNameLength) return "a place needs a name of up to 80 letters";
        if (!double.IsFinite(pin.Pos.X) || !double.IsFinite(pin.Pos.Z)) return "that place is off the table";
        if (pin.View is { } view && (view.Length != PlacePin.ViewLength || view.Any(v => !float.IsFinite(v)))) return "that view cannot be kept";
        if (pin.Sheet != null && g.Sheets.All(s => s.Id != pin.Sheet)) return "no such sheet";
        var pins = g.Places ?? new();
        int at = string.IsNullOrEmpty(pin.Id) ? -1 : pins.FindIndex(p => p.Id == pin.Id);
        if (at < 0 && pins.Count >= PlacePin.MostPins) return "too many places";
        if (string.IsNullOrEmpty(pin.Id)) pin.Id = g.NewId(PlacePin.IdPrefix);
        g.Places = pins;
        pin.Name = name;
        if (at >= 0)
        {
            inverse = new SetPlace { Place = pins[at].Clone() };
            pins[at] = pin.Clone();
            return null;
        }
        pins.Insert(sp.Index is { } index ? Math.Clamp(index, 0, pins.Count) : pins.Count, pin.Clone());
        StateMachine.BumpId(g, pin.Id);
        inverse = new RemovePlace { Id = pin.Id };
        return null;
    }

    static string? Remove(GameState g, RemovePlace rp, ref Command? inverse)
    {
        if (g.Places is not { } pins || pins.FindIndex(p => p.Id == rp.Id) is var at && at < 0) return "no such place";
        inverse = new SetPlace { Place = pins[at].Clone(), Index = at };
        pins.RemoveAt(at);
        if (pins.Count == 0) g.Places = null;
        return null;
    }
}

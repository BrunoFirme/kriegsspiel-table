using KTable.Core.Model;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

internal sealed class ScriptCrowds(ScriptHost host)
{
    const double UsualWithinM = 15;

    ICrowdWatch? Watch => host.Server.Crowds;

    public void Install(Table g)
    {
        var crowds = new Table(host.Vm);
        crowds["towns"] = Fn(_ => Owners(made: false));
        crowds["made"] = Fn(_ => Owners(made: true));
        crowds["people"] = Fn(a => People(Arg(a, 0), null));
        crowds["find"] = Fn(a => Find(Arg(a, 0).CastToString() ?? ""));
        g["crowds"] = DynValue.NewTable(crowds);
    }

    public DynValue Self(string owner)
    {
        var st = host.Server.State;
        var t = DynValue.NewTable(host.Vm);
        t.Table["id"] = S(owner);
        t.Table["name"] = S(CrowdScript.NameOf(st, owner));
        t.Table["made"] = B(st.Crowds?.Made?.Any(m => m.Id == owner) == true);
        t.Table["people"] = Fn(a => People(Arg(a, 0), owner));
        return t;
    }

    public DynValue Find(string id) => Watch?.Find(id) is { } one ? Person(one) : DynValue.Nil;

    DynValue Owners(bool made)
    {
        var list = DynValue.NewTable(host.Vm);
        foreach (var o in Watch?.Owners() ?? [])
            if (o.Made == made) list.Table.Append(Owner(o));
        return list;
    }

    public DynValue Owner(CrowdOwner o)
    {
        var t = DynValue.NewTable(host.Vm);
        t.Table["id"] = S(o.Id);
        t.Table["name"] = S(o.Name);
        t.Table["made"] = B(o.Made);
        t.Table["count"] = N(o.People);
        t.Table["out"] = N(o.Out);
        string id = o.Id;
        t.Table["people"] = Fn(a => People(Arg(a, 0), id));
        return t;
    }

    public DynValue People(DynValue filter, string? owner)
    {
        var f = filter.Type == DataType.Table ? filter.Table : null;
        string? town = owner ?? Text(f, "town");
        var near = f?.Get("near") is { Type: DataType.Table } n ? host.PosOf(n) : null;
        double within = (f?.Get("within").CastToNumber() ?? UsualWithinM) * (host.Server.State.Calibration.UnitsPerMeter is { } u and > 0 ? u : 1);
        var zone = Text(f, "zone") is { } z ? host.Server.State.Zones.FirstOrDefault(x => x.Id == z || x.Name == z) : null;
        var list = DynValue.NewTable(host.Vm);
        foreach (var p in Watch?.OutNow(town) ?? [])
        {
            if (f != null && !Matches(p, f)) continue;
            if (near is { } at && (p.At - at).Length > within) continue;
            if (zone != null && !zone.Contains(p.At)) continue;
            list.Table.Append(Person(p));
        }
        return list;
    }

    static string? Text(Table? f, string key) => f?.Get(key) is { Type: DataType.String } v ? v.String : null;

    static bool Same(Table f, string key, string? value) => Text(f, key) is not { } wanted || string.Equals(wanted, value, StringComparison.OrdinalIgnoreCase);

    static bool Matches(CrowdSeen p, Table f) =>
        Same(f, "place", p.Place) && Same(f, "calling", p.Calling) && Same(f, "sex", p.Sex) && Same(f, "age", p.Band) && Same(f, "wealth", p.Wealth)
        && (f.Get("staying") is not { Type: DataType.Boolean } staying || staying.Boolean == p.Staying);

    public DynValue Person(CrowdSeen p)
    {
        var t = DynValue.NewTable(host.Vm);
        t.Table["id"] = S(p.Id);
        t.Table["name"] = S(p.Name);
        t.Table["calling"] = S(p.Calling);
        t.Table["sex"] = S(p.Sex);
        t.Table["age"] = S(p.Band);
        t.Table["wealth"] = S(p.Wealth);
        t.Table["town"] = S(p.Owner);
        t.Table["place"] = p.Place == null ? DynValue.Nil : S(p.Place);
        t.Table["x"] = N(p.At.X);
        t.Table["z"] = N(p.At.Z);
        t.Table["staying"] = B(p.Staying);
        string id = p.Id;
        t.Table["speak"] = Fn(a => ScriptSpeech.Speak(host, id, Arg(a, 0), Arg(a, 1), Arg(a, 2), CrowdPiece.Layer));
        return t;
    }
}

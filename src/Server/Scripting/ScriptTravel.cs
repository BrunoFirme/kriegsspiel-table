using KTable.Core.Model;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

internal sealed class ScriptTravel(ScriptHost host)
{
    HostTravel Travel => host.Server.Travel;

    public void Install(Table g)
    {
        var map = new Table(host.Vm);
        map["travel"] = Fn(a => Route(a, "map.travel") is { } r ? N(r.Total) : DynValue.Nil);
        map["route"] = Fn(a => Route(a, "map.route") is { } r ? Points(r) : DynValue.Nil);
        g["map"] = DynValue.NewTable(map);
    }

    Route? Route(CallbackArguments a, string fn)
    {
        var by = Arg(a, 2);
        if (!by.IsNil() && by.Type != DataType.String) throw new ScriptRuntimeException($"{fn}(a, b, by): by is a traveller's name");
        if (TravelMaps.Unknown(Travel.Travellers(), by.IsNil() ? null : by.String, out var who) is { } unknown) throw new ScriptRuntimeException($"{fn}: {unknown}");
        if (host.PosOf(Arg(a, 0)) is not { } from || host.PosOf(Arg(a, 1)) is not { } to) return null;
        var guards = host.Guards;
        double started = guards.Elapsed;
        int built = Travel.MapsBuilt;
        var map = Travel.MapFor(from, to);
        if (Travel.MapsBuilt != built) guards.Deadline += guards.Elapsed - started;
        return map?.Find(from, to, who!);
    }

    DynValue Points(Route r)
    {
        var list = new Table(host.Vm);
        for (int i = 0; i < r.Points.Count; i++)
        {
            var point = new Table(host.Vm);
            point["x"] = r.Points[i].X;
            point["z"] = r.Points[i].Z;
            point["seconds"] = i == 0 ? 0 : r.Seconds[i - 1];
            list.Append(DynValue.NewTable(point));
        }
        return DynValue.NewTable(list);
    }
}

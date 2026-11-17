using KTable.Core.Model;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

static class GameFields
{
    public static DynValue Of(GameState st, GameClock clock, string key) => key switch
    {
        "turn" => N(st.Clock.Turn),
        "time" => S(clock.GameTime),
        "calibration" => st.Calibration.UnitsPerMeter is { } u ? N(u) : DynValue.Nil,
        "north" => N(st.Calibration.North),
        _ => ScriptClock.Field(clock, key) ?? DynValue.Nil,
    };
}

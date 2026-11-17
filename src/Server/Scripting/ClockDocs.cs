using static KTable.Server.Scripting.ApiDoc;

namespace KTable.Server.Scripting;

static class ClockDocs
{
    public static void Fields()
    {
        F("game", "seconds", "number", "The game time as a count of seconds, for sums: later minus earlier is the seconds between.");
        See("game.step", "on_clock");
        F("game", "rate", "number", "How fast the clock runs: game seconds a real second, 0 while it is paused.");
        See("game.run", "game.pause");
        F("game", "turn_length", "number", "The turn length in seconds: what Next turn adds to the time. 0 when the turn length is None.");
        F("game", "turn_name", "string?", "The name given to the turn in play, or nil.");
    }

    public static void Calls()
    {
        M("game", "step(seconds)", "Moves the game time by so many seconds without ending the turn.");
        P("seconds", "number", "Seconds to add; a negative number moves the time back.");
        Note("Every script's `on_clock` runs, and `on_timer` for each timer the time passed.");
        X("""
            local round = 0
            function next_round()
              game.step(6)
              round = round + 1
              self.set_counter("Round", round)
            end
            """);
        See("game.jump", "on_clock");

        M("game", "jump(time)", "Sets the game time without ending the turn.");
        P("time", "string|number", "A time in the game's calendar, as \"1812-07-22T06:00\", or a count of seconds as `game.seconds` gives.");
        See("game.step", "game.time");

        M("game", "run(rate)", "Lets the game time flow by itself.");
        O("rate", "number", "Game seconds a real second. Default 1.");
        See("game.pause", "game.rate");

        M("game", "pause()", "Stops the game time flowing.");
        See("game.run");

        M("game", "timer(name, when, { everyone })", "Sets a timer on the game clock, or moves the one of that name.");
        P("name", "string", "The timer's name, shown beside the clock.");
        P("when", "string|number", "A time in the game's calendar, or a number of seconds from now.");
        O("opts.everyone", "boolean", "true: players see it too. Default: umpires only.");
        X("""
            game.timer("Torch", 3600)
            function on_timer(name)
              if name == "Torch" then game.say("The torch gutters out.") end
            end
            """);
        See("game.clear_timer", "game.timers", "on_timer");

        M("game", "clear_timer(name)", "Removes the timer of that name.");
        P("name", "string", "The timer's name.");
        See("game.timer");

        M("game", "timers()", "The timers on the game clock, soonest first: { { name =, due =, seconds_left =, everyone = }, … }.", null, "table[]");
        R("A list of timers; `due` is a time as `game.time` writes it, `seconds_left` is negative once it is past.");
        See("game.timer");
    }

    public static void Events()
    {
        E("event", "on_clock(before, now)", "The game time moved by a step, a jump, a turn or a pause.");
        P("before", "number", "The time before, as a count of seconds.");
        P("now", "number", "The time now, as a count of seconds.");
        X("""
            function on_clock(before, now)
              local hours = (now - before) / 3600
              self.set_counter("Fatigue", self.counters.Fatigue + math.floor(hours))
            end
            """);
        See("game.seconds", "game.step");

        E("event", "on_timer(name)", "The game time passed a timer.");
        P("name", "string", "The timer's name.");
        See("game.timer");
    }
}

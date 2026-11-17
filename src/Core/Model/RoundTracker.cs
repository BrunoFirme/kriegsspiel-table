namespace KTable.Core.Model;

public static class RoundTracker
{
    public const string DefaultName = "Round tracker", Tag = "tracker", MarkerTag = "marker", RoundCounter = "Round", SecondsCounter = "Seconds", Face = "#24382D";
    public const double Width = 2.4, Height = 1.6, Thickness = 0.05;
    public const int UsualSeconds = 6, MostRounds = 9999, PixelsPerUnit = 20;

    public const string Script = """
        function on_load()
          self.button{ z = 0.48, w = 2, h = 0.4, label = "Next round", font = 0.2, on_click = "next_round" }
          self.menu_add("Reset", "reset")
        end

        function next_round()
          game.step(self.counters.Seconds)
          self.set_counter("Round", self.counters.Round + 1)
        end

        function reset()
          self.set_counter("Round", 0)
        end
        """;

    public static bool Is(PieceDef? def) => def?.Tags.Contains(Tag) == true;

    public static PieceDef Def(string id, string? image) => new()
    {
        Id = id, Name = DefaultName, Kind = "flat", Image = image, Size = new Size2(Width, Height), Shape = "box", Thickness = Thickness, Tags = new() { MarkerTag, Tag },
        Counters =
        {
            new CounterDef { Name = RoundCounter, Min = 0, Max = MostRounds, Place = new PrintPlace { X = 0.06, Y = 0.08, W = 0.88, H = 0.32 } },
            new CounterDef { Name = SecondsCounter, Min = 1, Max = CalendarDef.SecondsInDay, Default = UsualSeconds, Place = new PrintPlace { X = 0.06, Y = 0.44, W = 0.88, H = 0.2 } },
        },
        Script = Script,
    };
}

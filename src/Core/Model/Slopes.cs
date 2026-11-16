namespace KTable.Core.Model;

public enum Footing { Pad, Bank, StoneBase, RoofYard, Stilts }

public enum Terracing { None, DryStone, Lynchet, Paddy, EarthBank }

public enum HillForm { Perched, Arx, Hillfort, Citadel, Kastro, Kremlin, Kale, Castle, Yamajiro, Meeting, Ridgeline, Camp }

public enum Doctrine { Crest, Forward, Reverse, Caves }

public sealed record Slopes(Footing House, Terracing Fields, double Street, double Lane, double Road, HillForm Hill, Doctrine Works, double TerraceSteepest = Slopes.Built)
{
    public const double Built = 0.577, Japanese = 0.84;

    static readonly Dictionary<int, Slopes> Book = new()
    {
        [Manners.Roman] = new(Footing.Pad, Terracing.DryStone, 0.12, 0.2, 0.125, HillForm.Arx, Doctrine.Crest),
        [Manners.Tribal] = new(Footing.Pad, Terracing.Lynchet, 0.15, 0.25, 0.2, HillForm.Hillfort, Doctrine.Crest),
        [Manners.Persian] = new(Footing.RoofYard, Terracing.DryStone, 0.15, 0.35, 0.15, HillForm.Citadel, Doctrine.Crest),
        [Manners.Egyptian] = new(Footing.Pad, Terracing.None, 0.08, 0.15, 0.08, HillForm.Citadel, Doctrine.Crest),
        [Manners.Chinese] = new(Footing.Pad, Terracing.Paddy, 0.12, 0.3, 0.12, HillForm.Citadel, Doctrine.Crest),
        [Manners.Gothic] = new(Footing.Bank, Terracing.Lynchet, 0.15, 0.25, 0.15, HillForm.Castle, Doctrine.Crest),
        [Manners.Rus] = new(Footing.Pad, Terracing.None, 0.12, 0.2, 0.12, HillForm.Kremlin, Doctrine.Crest),
        [Manners.Byzantine] = new(Footing.StoneBase, Terracing.DryStone, 0.15, 0.3, 0.15, HillForm.Kastro, Doctrine.Crest),
        [Manners.Saracen] = new(Footing.RoofYard, Terracing.DryStone, 0.15, 0.35, 0.15, HillForm.Citadel, Doctrine.Crest),
        [Manners.Mongol] = new(Footing.Pad, Terracing.None, 0.1, 0.15, 0.1, HillForm.Camp, Doctrine.Crest),
        [Manners.Continental] = new(Footing.Bank, Terracing.DryStone, 0.1, 0.2, 0.067, HillForm.Perched, Doctrine.Crest),
        [Manners.Northern] = new(Footing.Bank, Terracing.DryStone, 0.12, 0.2, 0.08, HillForm.Meeting, Doctrine.Crest),
        [Manners.Ottoman] = new(Footing.StoneBase, Terracing.DryStone, 0.15, 0.3, 0.12, HillForm.Kale, Doctrine.Crest),
        [Manners.American] = new(Footing.Bank, Terracing.None, 0.1, 0.15, 0.0875, HillForm.Meeting, Doctrine.Crest),
        [Manners.Japanese] = new(Footing.Pad, Terracing.Paddy, 0.12, 0.25, 0.1, HillForm.Yamajiro, Doctrine.Crest, Japanese),
        [Manners.Western] = new(Footing.Pad, Terracing.DryStone, 0.12, 0.2, 0.08, HillForm.Perched, Doctrine.Reverse),
        [Manners.Soviet] = new(Footing.Pad, Terracing.None, 0.1, 0.15, 0.08, HillForm.Ridgeline, Doctrine.Forward),
        [Manners.NorthAfrican] = new(Footing.Bank, Terracing.DryStone, 0.15, 0.3, 0.12, HillForm.Ridgeline, Doctrine.Reverse),
        [Manners.Pacific] = new(Footing.Stilts, Terracing.Paddy, 0.12, 0.25, 0.1, HillForm.Ridgeline, Doctrine.Caves),
        [Manners.Nippon] = new(Footing.Pad, Terracing.Paddy, 0.1, 0.2, 0.05, HillForm.Yamajiro, Doctrine.Caves, Japanese),
    };

    public static Slopes Of(int manner) => Book[Manners.Known(manner)];

    public double Grade(string cls) => cls switch { "lane" or "track" or "path" => Lane, "street" => Street, _ => Road };
}

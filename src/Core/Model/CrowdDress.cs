using System.Globalization;
using System.Text;
using E = KTable.Core.Model.CrowdEntry;
using S = KTable.Core.Model.CrowdSlots;

namespace KTable.Core.Model;

public static class CrowdDress
{
    const int MostDepth = 8, PersonStream = 11, DressStream = 12, NameStream = 13, ArmyStream = 14;
    const string Grey = "#808080", Lamp = "#ffd27a", Copy = "=", Or = "|", Wealth = "wealth", Army = "army";
    const double SecondsInYear = 365.25 * CalendarDef.SecondsInDay, HeightPlay = 0.07, ChildPlay = 0.6, RichTaller = 0.02, BuildLeast = 0.93, BuildPlay = 0.15, RichBuild = 0.04;
    const double StoopLeast = 0.1, StoopPlay = 0.12, FixedWealth = 0.6, Lips = 0.72, LipRed = 0.86, LipLift = 8;
    static readonly List<string> Greys = ["#8f8a83", "#aba69d", "#c8c3b9"];
    static readonly (string Slot, string Word)[] Fallbacks =
        [(S.Outer, Wealth), (S.Under, E.Poor), (S.Legs, E.Poor), (S.Hat, E.Poor), (S.Accent, "leather"), (S.Trim, "=under")];

    public static Nationality? Find(string? id, IEnumerable<Nationality>? own = null) =>
        id == CrowdBaseline.Id ? CrowdBaseline.Person : own?.FirstOrDefault(n => n.Id == id) ?? Nationalities.All.FirstOrDefault(n => n.Id == id);

    public static Nationality Flat(Nationality n, IEnumerable<Nationality>? own = null)
    {
        var chain = new List<Nationality>();
        for (var at = n; at != null && chain.Count < MostDepth && !chain.Contains(at); at = at.Base is { } b ? Find(b, own) : null) chain.Add(at);
        if (!chain.Contains(CrowdBaseline.Person)) chain.Add(CrowdBaseline.Person);
        T? First<T>(Func<Nationality, T?> field) where T : class => chain.Select(field).FirstOrDefault(v => v != null);
        Dictionary<string, TV> Merged<TV>(Func<Nationality, Dictionary<string, TV>?> field)
        {
            var all = new Dictionary<string, TV>();
            foreach (var link in Enumerable.Reverse(chain))
                foreach (var (k, v) in field(link) ?? new()) all[k] = v;
            return all;
        }
        var days = chain.Select(c => c.Day).Where(d => d != null).ToList();
        return new Nationality
        {
            Id = n.Id, Name = n.Name ?? n.Id, Base = n.Base, Culture = First(c => c.Culture), Year = chain.Select(c => c.Year).First(y => y != null), Faith = First(c => c.Faith),
            Heights = Merged(c => c.Heights), Skins = First(c => c.Skins), Hairs = First(c => c.Hairs), Eyes = First(c => c.Eyes),
            Dyes = Merged(c => c.Dyes), Materials = Merged(c => c.Materials), ArmyCoats = First(c => c.ArmyCoats), CallingNames = Merged(c => c.CallingNames),
            Callings = Merged(c => c.Callings), Dress = Enumerable.Reverse(chain).SelectMany(c => c.Dress ?? []).ToList(), Names = Merged(c => c.Names), Naming = First(c => c.Naming),
            Day = new DayRules
            {
                HolyDay = days.Select(d => d!.HolyDay).FirstOrDefault(v => v != null), HolyAt = days.Select(d => d!.HolyAt).FirstOrDefault(v => v != null),
                Prayers = days.Select(d => d!.Prayers).FirstOrDefault(v => v != null), Rest = days.Select(d => d!.Rest).FirstOrDefault(v => v != null),
                MarketDays = days.Select(d => d!.MarketDays).FirstOrDefault(v => v != null), DarkBy = days.Select(d => d!.DarkBy).FirstOrDefault(v => v != null),
            },
        };
    }

    public static string ArmyCoat(Nationality flat, int townSeed) =>
        flat.ArmyCoats is { Count: > 0 } coats ? coats[new Dice(townSeed, ArmyStream).Below(coats.Count)] : Grey;

    public static string CallingName(Nationality flat, string calling) => flat.CallingNames?.GetValueOrDefault(calling) ?? calling;

    public static E Person(Nationality flat, int id, int seed, long now, string? sex = null, string? band = null, string? calling = null)
    {
        var dice = new Dice(seed, PersonStream);
        sex ??= dice.Chance(0.5) ? E.Man : E.Woman;
        band ??= Weighted(dice, CrowdBaseline.BandShares);
        calling ??= band == E.Child ? CrowdBaseline.Youngster : Calling(flat, dice, sex, band);
        if (band == E.Elder && calling == CrowdBaseline.Soldier) calling = CrowdBaseline.Labourer;
        string wealth = CrowdBaseline.CallingWealth.TryGetValue(calling, out var fixedWealth) && dice.Chance(FixedWealth) ? fixedWealth
            : Weighted(dice, new Dictionary<string, double> { [E.Poor] = 60, [E.Middle] = 32, [E.Rich] = 8 });
        var (least, most) = CrowdBaseline.BandAges[band];
        int age = least + dice.Below(most - least + 1);
        var entry = new E
        {
            Id = id, Seed = seed, Nationality = flat.Id, Sex = sex, Band = band, Calling = calling, Wealth = wealth, Faith = flat.Faith,
            Born = now - (long)((age + dice.Next()) * SecondsInYear),
        };
        entry.Name = Name(flat, entry);
        return entry;
    }

    static string Calling(Nationality flat, Dice dice, string sex, string band)
    {
        var offered = (flat.Callings ?? []).Where(c => c.Value > 0 && c.Key.StartsWith(sex + ":", StringComparison.Ordinal))
            .ToDictionary(c => c.Key[(sex.Length + 1)..], c => c.Value);
        if (band == E.Youth && sex == E.Man) offered = offered.Where(c => c.Key is CrowdBaseline.Labourer or CrowdBaseline.Farmer).ToDictionary();
        return offered.Count == 0 ? CrowdBaseline.Labourer : Weighted(dice, offered);
    }

    static string Weighted(Dice dice, IReadOnlyDictionary<string, double> shares)
    {
        double x = dice.Next() * shares.Values.Sum();
        foreach (var (key, share) in shares.OrderBy(s => s.Key, StringComparer.Ordinal))
            if ((x -= share) < 0) return key;
        return shares.Keys.OrderBy(k => k, StringComparer.Ordinal).Last();
    }

    public static string Name(Nationality flat, E entry)
    {
        var dice = new Dice(entry.Seed, NameStream);
        var rule = (flat.Naming ?? []).FirstOrDefault(r => (r.Sex == null || r.Sex == entry.Sex) && (r.Callings == null || r.Callings.Contains(entry.Calling))
            && (r.Wealth == null || r.Wealth.Contains(entry.Wealth)) && (r.Chance <= 0 || dice.Chance(r.Chance)));
        if (rule == null) return "";
        var name = new StringBuilder();
        var pattern = rule.Pattern;
        for (int i = 0; i < pattern.Length; i++)
        {
            int close = pattern[i] == '{' ? pattern.IndexOf('}', i) : -1;
            if (close < 0)
            {
                name.Append(pattern[i]);
                continue;
            }
            name.Append(Pick(dice, Names(flat, pattern[(i + 1)..close], entry.Sex)) ?? "");
            i = close;
        }
        return name.ToString().Trim();
    }

    static List<string>? Names(Nationality flat, string list, string sex) =>
        flat.Names?.GetValueOrDefault(list + "-" + sex) ?? flat.Names?.GetValueOrDefault(list);

    public static CrowdLook Look(E entry, Nationality flat, string armyCoat) => entry.Look ??= Dress(entry, flat, armyCoat);

    public static CrowdLook Dress(E entry, Nationality flat, string armyCoat)
    {
        var dice = new Dice(entry.Seed, DressStream);
        var colours = new Dictionary<string, string>();
        var worn = new Dictionary<string, Garment>();
        var enlisted = new HashSet<string>();
        string? item = null;
        string skin = Pick(dice, flat.Skins) ?? Grey;
        string hair = entry.Band == E.Elder ? Pick(dice, Greys)! : Pick(dice, flat.Hairs) ?? Grey;
        colours[S.Skin] = skin;
        colours[S.Hair] = hair;
        colours[S.Brow] = hair;
        colours[S.Eye] = Pick(dice, flat.Eyes) ?? Grey;
        colours[S.Lip] = Lip(skin);
        colours[S.Lamp] = Lamp;
        foreach (var material in CrowdBaseline.Materials)
            if (flat.Materials?.GetValueOrDefault(material) is { } stuff && S.All.Contains(material)) colours[material] = stuff;
        foreach (var rule in flat.Dress ?? [])
        {
            if (!Fits(rule, entry) || rule.Chance > 0 && !dice.Chance(rule.Chance)) continue;
            foreach (var part in rule.Off ?? []) worn.Remove(part);
            foreach (var g in rule.Wear ?? [])
            {
                var put = g.Clone();
                put.Kind = PickKind(dice, g.Kind);
                worn[g.Part] = put;
            }
            foreach (var (slot, word) in rule.Colours ?? [])
            {
                if (word == Army) enlisted.Add(slot);
                else if (enlisted.Contains(slot)) continue;
                colours[slot] = Colour(word, colours, entry, flat, armyCoat, dice);
            }
            if (rule.Item != null) item = rule.Item.Length == 0 ? null : rule.Item;
        }
        foreach (var (slot, word) in Fallbacks)
            if (!colours.ContainsKey(slot)) colours[slot] = Colour(word, colours, entry, flat, armyCoat, dice);
        foreach (var slot in S.All)
            colours.TryAdd(slot, Grey);
        double mean = flat.Heights?.GetValueOrDefault(entry.Sex) ?? CrowdBaseline.Person.Heights![entry.Sex];
        double play = (dice.Next() + dice.Next() + dice.Next() - 1.5) * HeightPlay * (entry.Band == E.Child ? ChildPlay : 1);
        bool rich = entry.Wealth == E.Rich;
        return new CrowdLook
        {
            HeightM = mean * CrowdBaseline.BandHeight[entry.Band] + play + (rich ? RichTaller : 0),
            Build = BuildLeast + dice.Next() * BuildPlay + (rich ? RichBuild : 0),
            Stoop = entry.Band == E.Elder ? StoopLeast + dice.Next() * StoopPlay : 0,
            Worn = Wardrobe.Parts.Where(worn.ContainsKey).Select(p => worn[p]).ToList(), Colours = colours, Item = item,
        };
    }

    static bool Fits(DressRule r, E e) => (r.Sex == null || r.Sex == e.Sex) && (r.Callings == null || r.Callings.Contains(e.Calling))
        && (r.Wealth == null || r.Wealth.Contains(e.Wealth)) && (r.Bands == null || r.Bands.Contains(e.Band));

    static string PickKind(Dice dice, string kind) => kind.Contains(Or) ? Pick(dice, kind.Split(Or).ToList())! : kind;

    static string Colour(string word, Dictionary<string, string> colours, E entry, Nationality flat, string armyCoat, Dice dice)
    {
        if (word.StartsWith(Copy, StringComparison.Ordinal)) return colours.GetValueOrDefault(word[1..]) ?? Grey;
        if (word.StartsWith('#')) return word.Contains(Or) ? Pick(dice, word.Split(Or).ToList())! : word;
        if (word == Army) return armyCoat;
        string dye = word == Wealth ? entry.Wealth : word;
        if (flat.Dyes?.GetValueOrDefault(dye) is { Count: > 0 } dyes) return Pick(dice, dyes)!;
        return flat.Materials?.GetValueOrDefault(word) ?? Grey;
    }

    static string? Pick(Dice dice, List<string>? from) => from is { Count: > 0 } ? from[dice.Below(from.Count)] : null;

    static string Lip(string skin)
    {
        if (!Hex(skin)) return Grey;
        int v = int.Parse(skin.AsSpan(1), NumberStyles.HexNumber);
        int r = Math.Min(255, (int)Math.Round((v >> 16 & 255) * LipRed + LipLift)), g = (int)Math.Round((v >> 8 & 255) * Lips), b = (int)Math.Round((v & 255) * Lips);
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    static bool Hex(string s) => s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);

    public static List<string> Problems(Nationality n)
    {
        var problems = new List<string>();
        var words = new HashSet<string>([Wealth, Army, .. CrowdBaseline.Wealths, .. CrowdBaseline.Materials]);
        void Word(string where, string word)
        {
            bool fine = word.StartsWith(Copy, StringComparison.Ordinal) ? S.All.Contains(word[1..])
                : word.StartsWith('#') ? word.Split(Or).All(Hex)
                : words.Contains(word);
            if (!fine) problems.Add($"{where}: unknown colour {word}");
        }
        foreach (var (rule, i) in (n.Dress ?? []).Select((r, i) => (r, i)))
        {
            string at = $"dress rule {i + 1}";
            foreach (var (slot, word) in rule.Colours ?? [])
            {
                if (!S.All.Contains(slot)) problems.Add($"{at}: unknown slot {slot}");
                Word(at, word);
            }
            foreach (var part in rule.Off ?? [])
                if (!Wardrobe.Parts.Contains(part)) problems.Add($"{at}: unknown part {part}");
            foreach (var g in rule.Wear ?? [])
            {
                if (!Wardrobe.Kinds.TryGetValue(g.Part, out var kinds)) problems.Add($"{at}: unknown part {g.Part}");
                else if (g.Kind.Split(Or).FirstOrDefault(k => !kinds.Contains(k)) is { } bad) problems.Add($"{at}: unknown {g.Part} {bad}");
                if (g.Slot != null && !S.All.Contains(g.Slot)) problems.Add($"{at}: unknown slot {g.Slot}");
            }
            if (rule.Item is { Length: > 0 } item && CrowdBaseline.Items.All(h => h.Id != item)) problems.Add($"{at}: unknown hand item {item}");
            if (rule.Bands?.FirstOrDefault(b => !CrowdBaseline.Bands.Contains(b)) is { } band) problems.Add($"{at}: unknown age band {band}");
            if (rule.Wealth?.FirstOrDefault(w => !CrowdBaseline.Wealths.Contains(w)) is { } wealth) problems.Add($"{at}: unknown wealth {wealth}");
        }
        var named = (n.Skins ?? []).Concat(n.Hairs ?? []).Concat(n.Eyes ?? []).Concat(n.ArmyCoats ?? []);
        foreach (var hex in named.Concat(n.Dyes?.Values.SelectMany(d => d) ?? []).Concat(n.Materials?.Values.AsEnumerable() ?? []))
            if (!Hex(hex)) problems.Add($"not a colour: {hex}");
        foreach (var rule in n.Naming ?? [])
            foreach (var token in rule.Pattern.Split('{').Skip(1).Select(t => t.Split('}')[0]))
                if (new[] { E.Man, E.Woman }.Any(sex => Names(Flat(n), token, sex) == null)) problems.Add($"naming: no list {token}");
        return problems;
    }
}

namespace KTable.Core.Model;

public static class PlaceNames
{
    public sealed record Tongue(string Key, string Name, string[] Stems, string[] Places, string[] Waters, string[] Areas);

    public const int MostLetters = 60;
    const char Stem = '*', Place = '@';
    const int Tries = 12;
    const string Vowels = "aeiouhéèâî";

    public static readonly Tongue[] Tongues =
    [
        new("german", "German",
            ["Alt", "Neu", "Ober", "Nieder", "Kirch", "Mühl", "Stein", "Wald", "Eich", "Linden", "Birk", "Buch", "Roth", "Schwarz", "Weiss", "Hohen", "Lang", "Sand", "Kalt", "Schön",
                "Gross", "Klein", "Wies", "Brunn", "Kron", "Wolf", "Hirsch", "Falken", "Adler", "Rosen"],
            ["dorf", "heim", "hausen", "feld", "stadt", "ingen", "bach", "berg", "burg", "au", "hofen", "rode", "kirchen", "weiler", "thal", "stein", "brück", "furt"],
            ["bach", "ach", "graben", "wasser", "see"],
            ["berg", "wald", "holz", "höhe", "heide", "forst", "kopf", "grund"]),
        new("french", "French",
            ["Beau", "Belle", "Mont", "Roche", "Clair", "Haute", "Neuf", "Grand", "Franche", "Bonne", "Riche", "Aigue", "Pierre", "Chante", "Mire", "Val", "Cham", "Ville"],
            ["mont", "ville", "fort", "val", "court", "champ", "bourg", "lieu", "pré", "fontaine", "roche", "rive"],
            ["Ruisseau de @", "Rivière de @", "Étang de @"],
            ["Bois de @", "Forêt de @", "Côte de @", "Plaine de @", "Hauts de @"]),
        new("english", "English",
            ["Ash", "Oak", "Thorn", "Stan", "Whit", "Black", "Red", "Green", "Long", "High", "Nether", "Upper", "Mill", "Kirk", "Wood", "Marsh", "Stone", "Brad", "Nor", "Sut", "West", "East",
                "Fen", "Hazel", "Elm", "Wolf", "Hart", "Swin", "Ox", "Hol"],
            ["ton", "ham", "ford", "ley", "bury", "wick", "worth", "field", "bridge", "stead", "combe", "by", "thorpe", "well", "don", "hurst"],
            ["@ Brook", "@ Beck", "@ Burn", "@ Water"],
            ["@ Wood", "@ Hill", "@ Down", "@ Heath", "@ Common", "@ Moor"]),
        new("spanish", "Spanish",
            ["Villa", "Torre", "Monte", "Fuente", "Val", "Casa", "Campo", "Peña", "Castro", "Riba"],
            ["verde", "real", "mayor", "fuerte", "grande", "* del Río", "* de la Sierra", "* del Rey", "* de la Vega", "* de Arriba", "* de Abajo"],
            ["Arroyo de @", "Río de @", "Laguna de @"],
            ["Sierra de @", "Cerro de @", "Monte de @", "Dehesa de @", "Llano de @"]),
        new("italian", "Italian",
            ["Monte", "Castel", "Villa", "Borgo", "Torre", "Ponte", "Campo", "Rocca", "Pietra", "Fonte", "Colle", "Serra", "Casal"],
            ["verde", "forte", "grande", "maggiore", "reale", "* San Pietro", "* San Martino", "* Santa Maria"],
            ["Torrente @", "Rio @", "Fosso di @", "Lago di @"],
            ["Bosco di @", "Colle di @", "Costa di @", "Piano di @"]),
        new("russian", "Russian",
            ["Nov", "Star", "Bel", "Krasn", "Chern", "Zelen", "Dub", "Berez", "Kamen", "Pesk", "Lip", "Sosn", "Bor", "Gor", "Les", "Polyan"],
            ["ovo", "ino", "ovka", "oye", "skoye", "ovskoye", "itsa", "ki", "ichi"],
            ["ka", "itsa", "ovka", "aya"],
            ["*aya Gora", "*ovy Les", "*ovaya Roshcha"]),
    ];

    public static Tongue Of(string key) => Tongues.FirstOrDefault(t => t.Key == key) ?? Tongues[0];

    public static string Make(string tongue, NameKind kind, int seed)
    {
        var t = Of(tongue);
        var rng = new Random(seed);
        return Formed(t, kind switch { NameKind.Water => t.Waters, NameKind.Area => t.Areas, _ => t.Places }, rng);
    }

    static string Formed(Tongue t, string[] forms, Random rng)
    {
        string stem = t.Stems[rng.Next(t.Stems.Length)], form = forms[rng.Next(forms.Length)];
        for (int i = 0; i < Tries && Clashes(stem, form); i++) form = forms[rng.Next(forms.Length)];
        if (form.Contains(Place)) return Elided(form, Formed(t, t.Places, rng));
        return form.Contains(Stem) ? form.Replace(Stem.ToString(), stem) : stem + form;
    }

    static string Elided(string form, string place)
    {
        const string Of = "de @";
        return form.Contains(Of) && Vowels.Contains(char.ToLowerInvariant(place[0])) ? form.Replace(Of, "d'" + place) : form.Replace(Place.ToString(), place);
    }

    static bool Clashes(string stem, string form)
    {
        if (form.Contains(Place)) return false;
        string head = stem.ToLowerInvariant(), tail = (form.Contains(Stem) ? form[(form.IndexOf(Stem) + 1)..] : form).ToLowerInvariant();
        if (tail.Length == 0 || tail[0] == ' ') return false;
        return tail.StartsWith(head) || head.EndsWith(tail) || head[^1] == tail[0] || head.Length >= 2 && tail.Length >= 2 && head[^2..] == tail[..2];
    }

    public static string? Tidy(string? name) => name?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, MostLetters)] : null;
}

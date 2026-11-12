namespace KTable.Client.Prefs;

public sealed class CaptureProfile
{
    public string Name { get; set; } = "";
    public bool SightingGif { get; set; }
    public bool GifLarge { get; set; }
    public bool FirstPerson { get; set; }
    public string Format { get; set; } = Settings.SightingFormats[0];
    public bool HideText { get; set; }
    public double Lens { get; set; } = SightingDraft.NormalLens;
    public double EyeShare { get; set; } = 1;
    public double Radius { get; set; }
    public double Sharp { get; set; } = 1;
    public double Blur { get; set; } = SightingDraft.BlurAtEdge;

    const double Close = 1e-6;

    public bool SameAs(CaptureProfile o) =>
        SightingGif == o.SightingGif && FirstPerson == o.FirstPerson
        && Format == o.Format && HideText == o.HideText && Near(Lens, o.Lens) && Near(EyeShare, o.EyeShare)
        && Near(Radius, o.Radius) && Near(Sharp, o.Sharp) && Near(Blur, o.Blur);

    static bool Near(double a, double b) => Math.Abs(a - b) < Close * Math.Max(1, Math.Abs(a));

    public CaptureProfile Named(string name)
    {
        var copy = (CaptureProfile)MemberwiseClone();
        copy.Name = name;
        return copy;
    }

    public bool Fit()
    {
        if (!double.IsFinite(Radius) || Radius <= 0) return false;
        Name = CaptureProfiles.Tidy(Name);
        if (Array.IndexOf(Settings.SightingFormats, Format) < 0) Format = Settings.SightingFormats[0];
        Lens = SightingDraft.FitLens(Lens);
        EyeShare = double.IsFinite(EyeShare) ? Math.Clamp(EyeShare, 0, SightingDraft.EyeHighestShare) : 1;
        Sharp = double.IsFinite(Sharp) ? Math.Clamp(Sharp, 0, 1) : 1;
        Blur = double.IsFinite(Blur) ? Math.Clamp(Blur, SightingDraft.LeastBlur, SightingDraft.MostBlur) : SightingDraft.BlurAtEdge;
        return Name.Length > 0;
    }
}

public static class CaptureProfiles
{
    public const int Most = 6, LongestName = 24;
    const string Stem = "Profile";

    public static IReadOnlyList<CaptureProfile> Of(Settings s, string scenario) =>
        s.CaptureProfiles.TryGetValue(scenario, out var list) ? list : [];

    public static int Lit(IReadOnlyList<CaptureProfile> list, CaptureProfile now)
    {
        for (int i = 0; i < list.Count; i++) if (list[i].SameAs(now)) return i;
        return -1;
    }

    public static CaptureProfile? Add(Settings s, string scenario, CaptureProfile now)
    {
        if (!s.CaptureProfiles.TryGetValue(scenario, out var list)) s.CaptureProfiles[scenario] = list = new();
        if (list.Count >= Most) return null;
        int n = list.Count + 1;
        while (Taken(list, $"{Stem} {n}", -1)) n++;
        var made = now.Named($"{Stem} {n}");
        list.Add(made);
        return made;
    }

    public static bool Keep(Settings s, string scenario, int index, CaptureProfile now)
    {
        if (!s.CaptureProfiles.TryGetValue(scenario, out var list) || index < 0 || index >= list.Count) return false;
        list[index] = now.Named(list[index].Name);
        return true;
    }

    public static bool Rename(Settings s, string scenario, int index, string name)
    {
        name = Tidy(name);
        if (!s.CaptureProfiles.TryGetValue(scenario, out var list) || index < 0 || index >= list.Count || name.Length == 0 || Taken(list, name, index)) return false;
        list[index].Name = name;
        return true;
    }

    public static bool Remove(Settings s, string scenario, int index)
    {
        if (!s.CaptureProfiles.TryGetValue(scenario, out var list) || index < 0 || index >= list.Count) return false;
        list.RemoveAt(index);
        if (list.Count == 0) s.CaptureProfiles.Remove(scenario);
        return true;
    }

    public static string Tidy(string? name)
    {
        var one = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length > LongestName ? one[..LongestName].TrimEnd() : one;
    }

    static bool Taken(List<CaptureProfile> list, string name, int except)
    {
        for (int i = 0; i < list.Count; i++) if (i != except && string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static void Fit(Dictionary<string, List<CaptureProfile>> byScenario)
    {
        foreach (var (scenario, list) in byScenario.ToList())
        {
            var kept = new List<CaptureProfile>();
            foreach (var p in list ?? [])
                if (p != null && p.Fit() && !Taken(kept, p.Name, -1) && kept.Count < Most) kept.Add(p);
            if (kept.Count == 0) byScenario.Remove(scenario);
            else byScenario[scenario] = kept;
        }
    }
}

namespace KTable.Converter;

public static class KtMarker
{
    public const string Def = "ktdef:";
    public const string Pack = "ktpack:";

    public sealed class Info
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Authors { get; set; } = new();
        public string License { get; set; } = "";
    }

    public static bool SafePackId(string id)
    {
        if (id.Length is 0 or > 80 || id.Contains("..") || !char.IsAsciiLetterOrDigit(id[0]) || !char.IsAsciiLetterOrDigit(id[^1])) return false;
        if (!id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')) return false;
        if (id is "scenarios" or "sheets" || id.StartsWith("std.")) return false;
        return id.Split('.')[0] is not ("con" or "prn" or "aux" or "nul" or "com1" or "com2" or "com3" or "com4" or "com5" or "com6" or "com7" or "com8" or "com9"
            or "lpt1" or "lpt2" or "lpt3" or "lpt4" or "lpt5" or "lpt6" or "lpt7" or "lpt8" or "lpt9");
    }

    public static Info? Find(TtsSave save)
    {
        var card = save.Objects.FirstOrDefault(o => o.Description.StartsWith(Pack));
        if (card == null) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Info>(card.Description[Pack.Length..], Config.Opts); }
        catch { return null; }
    }
}

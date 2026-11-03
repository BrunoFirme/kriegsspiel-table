using KTable.Core;
using KTable.Server.Scripting.Translate;

namespace KTable.Converter;

public static class ScriptTally
{
    public static int Run(Config cfg, List<string> rest)
    {
        var files = rest.Count > 0
            ? rest.Select(r => File.Exists(r) ? r : Path.Combine(cfg.WorkshopDir, r + ".json")).ToList()
            : Directory.GetFiles(cfg.WorkshopDir, "*.json").OrderBy(f => f).ToList();
        var kinds = new Dictionary<string, (int count, HashSet<string> modules, string example)>();
        int scripts = 0, parsed = 0, hostile = 0, direct = 0, compat = 0;
        var ctx = new TranslateContext { ResolveImage = (_, _, _) => "marker", ResolveAsset = _ => "asset" };
        var gctx = new TranslateContext { ResolveImage = (_, _, _) => "marker", ResolveAsset = _ => "asset", IsGlobal = true };
        foreach (var f in files)
        {
            TtsSave save;
            try { save = TtsSave.Load(cfg, f); }
            catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}: cannot load ({e.Message})"); continue; }
            var name = $"{save.SaveName} ({Path.GetFileNameWithoutExtension(f)})";
            var all = new List<(string lua, string where, bool global)> { (save.Lua, "Global script", true) };
            foreach (var o in save.Objects.SelectMany(o => o.Walk()))
                if (o.Lua.Trim().Length > 0) all.Add((o.Lua, $"{o.Name} '{o.Nickname}' {o.Guid}", false));
            foreach (var (lua, where, global) in all)
            {
                if (lua.Trim().Length == 0) continue;
                scripts++;
                var verdict = ScriptSafety.Inspect(lua);
                if (verdict.Hostile) hostile++;
                if (verdict.Script.Trim().Length == 0) continue;
                var tr = TtsTranslator.Translate(verdict.Script, global ? gctx : ctx);
                if (tr.Parsed) parsed++;
                direct += tr.DirectCalls; compat += tr.CompatCalls;
                foreach (var u in tr.Untranslated)
                {
                    var what = u.What == "event" ? "event " + u.Text.Split(' ')[0] : u.What;
                    if (!kinds.TryGetValue(what, out var k)) k = (0, new HashSet<string>(), $"{name}, {where}, line {u.Line}: {OneLine(u.Text)}");
                    k.count++; k.modules.Add(name);
                    kinds[what] = k;
                }
            }
        }
        Console.WriteLine($"{files.Count} modules, {scripts} scripts ({parsed} parsed, {hostile} hostile), {direct} direct and {compat} compat rewrites");
        foreach (var (what, k) in kinds.OrderByDescending(x => x.Value.modules.Count).ThenByDescending(x => x.Value.count))
            Console.WriteLine($"{k.count,6}× {what,-36} in {k.modules.Count,2} module(s)   e.g. {k.example}");
        Console.WriteLine($"Untranslated kinds: {kinds.Count}");
        return 0;
    }

    static string OneLine(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length > 110 ? s[..110] + "…" : s;
    }
}

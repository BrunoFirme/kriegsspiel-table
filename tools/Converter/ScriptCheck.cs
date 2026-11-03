using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Converter;

public static class ScriptCheck
{
    public static int Run(Config cfg, List<string> rest)
    {
        var root = rest.Count > 0 && Directory.Exists(rest[0]) ? rest[0] : cfg.Output;
        var scenarios = rest.Where(r => r.EndsWith("scenario.json", StringComparison.OrdinalIgnoreCase) && File.Exists(r)).ToList();
        var ids = rest.Where(r => !Directory.Exists(r) && !scenarios.Contains(r)).ToList();
        var store = new PackageStore(root);
        foreach (var scPath in scenarios) RunScenario(store, scPath);
        if (scenarios.Count > 0 && ids.Count == 0) return 0;
        if (ids.Count == 0) ids = store.ListPackIds().Where(id => !id.StartsWith("scenarios")).ToList();
        int grandDefs = 0, grandErrors = 0;
        foreach (var id in ids)
        {
            var pack = store.Pack(id);
            if (pack == null) { Console.WriteLine($"{id}: no such pack"); continue; }
            var scripted = pack.Pieces.Where(d => d.Script != null).ToList();
            if (scripted.Count == 0) { Console.WriteLine($"{id}: no scripts"); continue; }
            store.LoadPacks(new[] { id });
            var g = new GameState { ScenarioId = "check", Name = "check", Packs = new() { id } };
            var layer = g.Layer(Layers.Umpire);
            int n = 0;
            foreach (var d in scripted) { n++; layer.Add(new PieceInstance { Id = $"p-{n:D6}", Def = d.Id, Pos = new Vec2((n % 40) * 5, (n / 40) * 5), Label = d.Name, ScriptState = d.ScriptState ?? "" }); }
            g.EnsureNextId();
            var server = new GameServer(g, store);
            var errors = new List<(string piece, string message)>();
            void Collect(IEnumerable<Outgoing> outs)
            {
                foreach (var o in outs)
                    if (o.Message is CommandMessage cm)
                        foreach (var c in Flat(cm.Command))
                            if (c is Say s && s.Text.StartsWith("Script error in "))
                            {
                                var rest2 = s.Text["Script error in ".Length..];
                                var colon = rest2.IndexOf(": ", StringComparison.Ordinal);
                                errors.Add(colon > 0 ? (rest2[..colon], rest2[(colon + 2)..]) : ("?", rest2));
                            }
            }
            Collect(server.Join(1, new JoinRequest { Name = "Check" }));
            Collect(server.Scripts.Load());
            int loadErrors = errors.Count;
            int widgets = 0, clicks = 0;
            foreach (var p in layer.ToList())
            {
                widgets += p.Widgets.Count;
                for (int w = 0; w < p.Widgets.Count; w++)
                    if (p.Widgets[w].Kind == "button" && p.Widgets[w].OnClick != null)
                    {
                        clicks++;
                        Collect(server.Handle(1, new ClickWidget { Layer = Layers.Umpire, Id = p.Id, Index = w + 1 }));
                    }
            }
            for (double t = 0.1; t < 3.05; t += 0.1) Collect(server.Scripts.Tick(t));
            Collect(server.Scripts.Save());
            var failing = errors.Select(e => e.piece).Distinct().Count();
            var unsupported = server.Scripts.Console.Where(l => l.StartsWith("TTS call not supported here: ")).Select(l => l["TTS call not supported here: ".Length..]).Distinct().OrderBy(x => x).ToList();
            Console.WriteLine($"{id}: {scripted.Count} scripted kinds, {widgets} widgets, {clicks} buttons clicked; {scripted.Count - failing} kinds clean, {failing} with errors ({loadErrors} on load, {errors.Count - loadErrors} on clicks/timers/save)");
            foreach (var grp in errors.GroupBy(e => Normalize(e.message)).OrderByDescending(x => x.Count()).Take(12))
                Console.WriteLine($"   {grp.Count(),4}× {grp.Key}  (e.g. {grp.First().piece}: {grp.First().message})");
            if (unsupported.Count > 0) Console.WriteLine($"   unsupported TTS calls noted: {string.Join(", ", unsupported)}");
            grandDefs += scripted.Count; grandErrors += failing;
        }
        Console.WriteLine($"TOTAL: {grandDefs} scripted kinds, {grandErrors} with errors");
        return 0;
    }

    static void RunScenario(PackageStore store, string scPath)
    {
        var sc = Json.Load<Scenario>(scPath);
        var server = GameServer.FromScenario(sc, store);
        static void Collect(IEnumerable<Outgoing> _) { }
        List<(string piece, string message)> Errors() => server.Scripts.Console.Where(l => l.StartsWith("Script error in ")).Select(l =>
        {
            var rest2 = l["Script error in ".Length..];
            var colon = rest2.IndexOf(": ", StringComparison.Ordinal);
            return colon > 0 ? (rest2[..colon], rest2[(colon + 2)..]) : ("?", rest2);
        }).ToList();
        Collect(server.Scripts.Load());
        Collect(server.Join(1, new JoinRequest { Name = "Check" }));
        int loadErrors = Errors().Count, clicks = 0, uiClicks = 0;
        var layer = server.State.Layer(Layers.Umpire);
        foreach (var p in layer.ToList())
            for (int w = 0; w < p.Widgets.Count; w++)
                if (p.Widgets[w].Kind == "button" && p.Widgets[w].OnClick != null) { clicks++; Collect(server.Handle(1, new ClickWidget { Layer = Layers.Umpire, Id = p.Id, Index = w + 1 })); }
        foreach (var id in UiIds(server.State.Ui).ToList())
            if (server.State.UiNode(id) is { } n && n.Tag == "Button" && n.Attributes.ContainsKey("onClick")) { uiClicks++; Collect(server.Handle(1, new UiEvent { Id = id, Event = "click", Value = "-1" })); }
        int keys = 0, rows = 0;
        if (layer.Count > 0)
            for (int i = 0; i < server.State.Hotkeys.Count; i++)
            {
                var first = layer[0];
                keys++;
                Collect(server.Handle(1, new Hotkey { Index = i + 1, Layer = Layers.Umpire, Piece = first.Id, Pos = first.Pos, Selected = new() { first.Id } }));
                Collect(server.Handle(1, new Hotkey { Index = i + 1, Up = true, Layer = Layers.Umpire, Piece = first.Id, Pos = first.Pos, Selected = new() { first.Id } }));
                for (double t = 0.1; t < 1.55; t += 0.1) Collect(server.Scripts.Tick(t));
            }
        for (int i = 0; i < server.State.MenuItems.Count; i++) { rows++; Collect(server.Handle(1, new PickGlobalMenuItem { Index = i + 1, Pos = new Vec2(0, 0) })); }
        for (double t = 0.1; t < 3.05; t += 0.1) Collect(server.Scripts.Tick(t));
        Collect(server.Scripts.Save());
        var errors = Errors();
        var failing = errors.Select(e => e.piece).Distinct().Count();
        var unsupported = server.Scripts.Console.Where(l => l.StartsWith("TTS call not supported here: ")).Select(l => l["TTS call not supported here: ".Length..]).Distinct().OrderBy(x => x).ToList();
        Console.WriteLine($"{sc.Id}: global script {(sc.Script?.Length ?? 0) / 1024} KB ({server.Scripts.GlobalStatus}; on_load {(server.Scripts.GlobalDefines("on_load") ? "defined" : "missing")}, on_save {(server.Scripts.GlobalDefines("on_save") ? "defined" : "missing")}), {layer.Count} pieces ({server.Scripts.Loaded} scripts loaded), {UiIds(server.State.Ui).Count()} panel elements, {PieceUiStats(server, store)}, {server.State.Hotkeys.Count} hotkeys ({string.Join(", ", server.State.Hotkeys.Take(3).Select(h => h.Label))}{(server.State.Hotkeys.Count > 3 ? ", …" : "")}), {server.State.MenuItems.Count} table menu rows; {clicks} buttons and {uiClicks} panel buttons clicked, {keys} hotkeys pressed, {rows} table rows picked; {errors.Count} errors ({loadErrors} on load) in {failing} scripts");
        foreach (var grp in errors.GroupBy(e => Normalize(e.message)).OrderByDescending(x => x.Count()).Take(12))
            Console.WriteLine($"   {grp.Count(),4}× {grp.Key}  (e.g. {grp.First().piece}: {grp.First().message})");
        if (unsupported.Count > 0) Console.WriteLine($"   unsupported TTS calls noted: {string.Join(", ", unsupported)}");
        var console = server.Scripts.Console.Where(l => !l.StartsWith("TTS call not supported") && !l.StartsWith("Script error")).ToList();
        if (console.Count > 0) Console.WriteLine($"   console ({console.Count} lines): {string.Join(" | ", console.Take(6).Select(l => l.Length > 80 ? l[..80] : l))}");
    }

    static string PieceUiStats(GameServer server, PackageStore store)
    {
        var pieces = server.State.Layer(Layers.Umpire).Where(p => p.Ui.Count > 0).ToList();
        var images = pieces.SelectMany(p => p.Ui).SelectMany(AllNodes).Where(n => n.Tag == "Image" && n.Attributes.GetValueOrDefault("image") is { Length: > 0 }).Select(n => n.Attributes["image"]).ToList();
        var missing = images.Distinct().Where(id => !store.LoadedPacks.Any(pk => pk.Assets.ContainsKey(id))).ToList();
        var hint = missing.Count > 0 ? $"; e.g. {(missing[0].Length > 12 ? missing[0][..12] : missing[0])} not among the {store.LoadedPacks.Count()} loaded packs' assets" : "";
        return $"{pieces.Count} pieces with panels of their own ({images.Distinct().Count()} pictures, {missing.Count} not in a pack{hint})";
    }

    static IEnumerable<UiNode> AllNodes(UiNode n)
    {
        yield return n;
        foreach (var c in n.Children) foreach (var x in AllNodes(c)) yield return x;
    }

    static IEnumerable<string> UiIds(IEnumerable<UiNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.Id != null) yield return n.Id;
            foreach (var c in UiIds(n.Children)) yield return c;
        }
    }

    static IEnumerable<Command> Flat(Command c) => c is Batch b ? b.Items.SelectMany(Flat) : new[] { c };

    static string Normalize(string m)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(m, @"^[^:]*:\(\d+,\d+(?:-\d+)?\): ", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"p-\d{6}", "p-…");
        return s.Length > 160 ? s[..160] : s;
    }
}

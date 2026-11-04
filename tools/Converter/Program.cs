using KTable.Converter;

if (args.Length > 0 && args[0] == "agent") return await AgentBridge.Run(args.Skip(1).ToList());
if (Console.IsOutputRedirected) Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var argList = args.ToList();
string? cfgPath = Take(argList, "--config");
var outOverride = Take(argList, "--out");
var cfg = Config.Load(cfgPath);
if (outOverride != null) cfg.OutputPath = outOverride;
var reportPath = Path.Combine(FindToolDir(), "report.md");
var cmd = argList.Count > 0 ? argList[0] : "all";
var rest = argList.Skip(1).ToList();
Console.WriteLine($"Output: {cfg.Output}\nCache:  {cfg.Cache}");

var report = new Report();
int unmappedElsewhere = 0;
var assets = new AssetStore(cfg, report);
var way = new MakeWay(cfg);
try
{
    switch (cmd)
    {
        case "all":
            foreach (var pc in cfg.Packs) await new PackBuilder(cfg, assets, report, way).Build(pc);
            await new ScenarioBuilder(cfg, assets, report, way).Build(cfg.Scenario.Master);
            report.Write(reportPath, "all");
            break;
        case "pack":
            if (rest.Count == 0) { Console.Error.WriteLine("pack <packId|workshopId> ..."); return 1; }
            foreach (var id in rest)
            {
                var pc = cfg.Packs.FirstOrDefault(p => p.Id == id || p.WorkshopId == id)
                         ?? new PackCfg { WorkshopId = id, Id = "workshop." + id, Short = "w" + id };
                var r1 = new Report();
                await new PackBuilder(cfg, new AssetStore(cfg, r1), r1, way).Build(pc);
                r1.Write(reportPath, "pack " + pc.Id);
                unmappedElsewhere += r1.UnknownTypes;
            }
            break;
        case "scenario":
        {
            var masterId = rest.FirstOrDefault(a => !a.StartsWith("--") && !a.Contains('=')) ?? cfg.Scenario.Master;
            await new ScenarioBuilder(cfg, assets, report, way).Build(masterId);
            report.Write(reportPath, "scenario " + cfg.Scenario.Id);
            break;
        }
        case "fetch":
        {
            var ids = cfg.Packs.Select(p => p.WorkshopId).Append(cfg.Scenario.Master).Distinct();
            foreach (var id in ids)
            {
                Console.WriteLine($"fetch {id}");
                await assets.FetchAll(TtsSave.Load(cfg, id));
            }
            break;
        }
        case "plan":
        case "import":
        {
            if (rest.Count == 0) { Console.Error.WriteLine($"{cmd} <file|workshopId|link> [--name N] [--id slug] [--skip 3DText,Notecard]"); return 1; }
            var name = Take(rest, "--name");
            var slugArg = Take(rest, "--id");
            foreach (var s in (Take(rest, "--skip") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!cfg.IgnoreTypes.Contains(s)) cfg.IgnoreTypes.Add(s);
            var file = await Workshop.Resolve(cfg, rest[0]);
            if (cmd == "import") Progress.Stage("read");
            var save = TtsSave.Load(cfg, file);
            var plan = ImportPlan.Of(save, name, slugArg);
            if (cmd == "plan")
            {
                foreach (var p in plan.Packs) Console.WriteLine($"PLAN pack {p}");
                if (plan.Scenario != null) Console.WriteLine($"PLAN scenario {plan.Scenario}");
                Console.WriteLine($"PLAN name {plan.Name}");
                Console.WriteLine($"PLAN file {file}");
                return 0;
            }
            way.What = $"the import of '{plan.Name}'";
            if (plan.Restore && KtMarker.Find(save) is { } shared)
            {
                var sid = plan.Packs[0];
                Console.WriteLine($"Restoring Kriegsspiel Table pack '{shared.Name}' ({sid})");
                var spc = new PackCfg { WorkshopId = file, Id = sid, Short = "kt", Name = shared.Name };
                var built = await new PackBuilder(cfg, assets, report, way).Build(spc);
                built.M.Source.Authors = shared.Authors;
                if (shared.License.Length > 0) built.M.Source.License = shared.License;
                built.Save();
                Progress.Stage("finish");
                var pr = Path.Combine(cfg.Output, sid, "import-report.md");
                report.Write(pr, "restore " + sid);
                Console.WriteLine($"IMPORTED pack={sid} pieces={built.M.Pieces.Count}");
                Console.WriteLine($"SIZES pack={FolderBytes(built.Dir)}");
                Console.WriteLine($"REPORT {pr}");
                break;
            }
            name = plan.Name;
            var slug = plan.Scenario!;
            var packId = plan.Packs[0];
            var shortName = slug.Length > 12 ? slug[..12].Trim('-') : slug;
            Console.WriteLine($"Importing '{name}' from {file}");
            var pc = new PackCfg { WorkshopId = file, Id = packId, Short = shortName, Name = name };
            await new PackBuilder(cfg, assets, report, way).Build(pc);
            cfg.Packs.Insert(0, pc);
            cfg.Scenario = new ScenarioCfg
            {
                Id = packId, Dir = slug, Name = name, Master = file,
                ExtrasPack = plan.Packs[1], ExtrasShort = shortName + "x",
                Clock = new ClockCfg(),
            };
            var scenario = await new ScenarioBuilder(cfg, assets, report, way).Build(file);
            if (!scenario.Packs.Contains(packId))
            {
                scenario.Packs.Insert(0, packId);
                KTable.Core.Json.Save(Path.Combine(cfg.Output, "scenarios", slug, "scenario.json"), scenario);
            }
            Progress.Stage("finish");
            var importReport = Path.Combine(cfg.Output, "scenarios", slug, "import-report.md");
            report.Write(importReport, "import " + packId);
            Console.WriteLine($"IMPORTED scenario={slug} pack={packId} sheets={scenario.Sheets.Count} pieces={scenario.UmpireBoard.Count}");
            Console.WriteLine($"REPORT {importReport}");
            Console.WriteLine($"SIZES pack={plan.Packs.Sum(p => FolderBytes(Path.Combine(cfg.Output, p)))} scenario={FolderBytes(Path.Combine(cfg.Output, "scenarios", slug))}");
            break;
        }
        case "export-tts":
        {
            if (rest.Count == 0) { Console.Error.WriteLine("export-tts <scenario.json|game.kstable> [out.json]"); return 1; }
            var ex = new TtsExport(cfg);
            var outFile = ex.Export(rest[0], rest.Count > 1 ? rest[1] : null);
            Console.WriteLine($"EXPORTED {outFile}");
            if (ex.LocalOnly > 0) Console.WriteLine($"NOTE {ex.LocalOnly} image(s) have no web URL (your own or imported files): they load only on this PC. Upload them (e.g. to Steam Cloud via TTS) before sharing.");
            break;
        }
        case "export-pack-tts":
        {
            if (rest.Count == 0) { Console.Error.WriteLine("export-pack-tts <packId> [out.json]"); return 1; }
            var ex = new TtsExport(cfg);
            var outFile = ex.ExportPack(rest[0], rest.Count > 1 ? rest[1] : null);
            Console.WriteLine($"EXPORTED {outFile}");
            if (ex.LocalOnly > 0) Console.WriteLine($"LOCAL {ex.LocalOnly}");
            break;
        }
                case "list-tts":
        {
            var listed = new List<string>();
            foreach (var (dir, kind) in new[] { (cfg.WorkshopDir, "workshop"), (cfg.SavesDir, "save") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTime))
                {
                    if (Path.GetFileName(f) is var fn && (fn.StartsWith("SaveFileInfos") || fn.StartsWith("WorkshopFileInfos"))) continue;
                    string title = Path.GetFileNameWithoutExtension(f);
                    try
                    {
                        using var fs = File.OpenRead(f);
                        var buf = new byte[Math.Min(65536, fs.Length)];
                        fs.ReadExactly(buf);
                        var m = System.Text.RegularExpressions.Regex.Match(System.Text.Encoding.UTF8.GetString(buf), """
                            "SaveName"\s*:\s*"((?:[^"\\]|\\.)*)"
                            """);
                        if (m.Success) title = System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { title = Path.GetFileNameWithoutExtension(f); }
                    var thumb = Path.ChangeExtension(f, ".png");
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { kind, id = Path.GetFileNameWithoutExtension(f), name = title, path = f, thumb = File.Exists(thumb) ? thumb : null,
                        size = new FileInfo(f).Length }));
                    listed.Add(f);
                }
            }
            foreach (var f in listed)
            {
                var measured =
                    KTable.Core.BestEffort.Read<(long Bytes, int ToFetch)?>(() => { var save = TtsSave.Load(cfg, f); return assets.Footprint(PackBuilder.PieceUrls(save), PackBuilder.Urls(save)); },
                        fallback: null);
                if (measured is var (bytes, toFetch))
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { kind = "size", path = f, size = bytes, fetch = toFetch }));
            }
            return 0;
        }
        case "api-reference":
        {
            var docs = rest.FirstOrDefault() ?? Path.Combine(FindToolDir(), "..", "..", "docs");
            File.WriteAllText(Path.Combine(docs, "scripting-api.md"), KTable.Server.Scripting.ApiDoc.Markdown());
            File.WriteAllText(Path.Combine(docs, "ktable-api.lua"), KTable.Server.Scripting.ApiDoc.Stubs());
            Console.WriteLine("written: " + Path.GetFullPath(Path.Combine(docs, "scripting-api.md")) + ", ktable-api.lua");
            return 0;
        }
        case "check-scripts":
            return ScriptCheck.Run(cfg, rest);
        case "tally-scripts":
            return ScriptTally.Run(cfg, rest);
        case "report":
            Console.WriteLine(File.Exists(reportPath) ? reportPath : "no report yet; run a conversion");
            break;
        case "sanity":
            Sanity.Render(cfg, rest.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "ktable_sanity.png"), rest.Skip(1).FirstOrDefault(), rest.Count > 2 ? int.Parse(rest[2]) : 4000);
            break;
        default:
            Console.Error.WriteLine($"unknown command {cmd}");
            return 1;
    }
    way.Done();
}
catch
{
    way.PutBack();
    throw;
}
finally { assets.Save(); Progress.End(); }

if (report.UnknownTypes + unmappedElsewhere > 0)
{
    Console.Error.WriteLine($"{report.UnknownTypes + unmappedElsewhere} unmapped object type(s); see {reportPath}");
    return 2;
}
return 0;

static string? Take(List<string> a, string name)
{
    int i = a.IndexOf(name);
    if (i < 0 || i + 1 >= a.Count) return null;
    var v = a[i + 1];
    a.RemoveRange(i, 2);
    return v;
}

static long FolderBytes(string dir) => KTable.Core.BestEffort.Read(() =>
    Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0L, fallback: 0L);

static string FindToolDir()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Converter.csproj"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}

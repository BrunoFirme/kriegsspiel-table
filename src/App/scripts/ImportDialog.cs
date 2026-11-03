using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

public partial class ImportDialog : AcceptDialog
{
    readonly string _packagesRoot;
    readonly Action<string, string> _done;
    ItemList _list = null!;
    LineEdit _filter = null!, _source = null!, _name = null!;
    CheckBox _skipText = null!;
    RichTextLabel _log = null!;
    FindBar _find = null!;
    readonly System.Text.StringBuilder _logText = new();
    Button _go = null!, _stop = null!;
    Label _status = null!;
    ProgressBar _bar = null!;
    readonly List<TtsEntry> _entries = new();
    bool _running;
    ConverterRun? _run;

    public ImportDialog(string packagesRoot, Action<string, string> done)
    {
        _packagesRoot = packagesRoot;
        _done = done;
        Title = "Import a Tabletop Simulator module";
        OkButtonText = "Close";
        Size = new Vector2I(900, 640);
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        root.AddChild(new Label
        {
            Text = "Turns any TTS module or save into a pack (all its pieces, bags flattened) and a scenario (its table layout; big locked boards become map sheets). " +
                   "Pieces, images, models, alternate states and scripts convert: a module's Lua is translated into this app's own scripting, and the report lists any line it could not translate. Unity asset bundles cannot run outside TTS.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f),
        });

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(cols);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f };
        cols.AddChild(left);
        left.AddChild(new Label { Text = "On this PC (your TTS Workshop subscriptions and saves)" });
        _filter = new LineEdit { PlaceholderText = "filter…", ClearButtonEnabled = true };
        _filter.TextChanged += _ => Fill();
        left.AddChild(_filter);
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedIconSize = new Vector2I(48, 48), IconMode = ItemList.IconModeEnum.Left };
        _list.ItemSelected += i => Pick((int)i);
        left.AddChild(_list);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cols.AddChild(right);
        var g = new GridContainer { Columns = 2 };
        right.AddChild(g);
        g.AddChild(new Label { Text = "Source" });
        var srcRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _source = new LineEdit { PlaceholderText = "file, Workshop id or steamcommunity.com link", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        srcRow.AddChild(_source);
        var browse = new Button { Text = "Browse…" };
        browse.Pressed += Browse;
        srcRow.AddChild(browse);
        g.AddChild(srcRow);
        g.AddChild(new Label { Text = "Name" });
        _name = new LineEdit { PlaceholderText = "(from the module)" };
        g.AddChild(_name);
        g.AddChild(new Label { Text = "Leave out" });
        _skipText = new CheckBox
        {
            Text = "Floating 3D text",
            TooltipText = "Modules often letter their table with floating text: titles over an area, labels beside a chart. They come in as flat label pieces and a big module can hold dozens. The notecards come in either way.",
        };
        g.AddChild(_skipText);
        var goRow = new HBoxContainer();
        goRow.AddThemeConstantOverride("separation", 8);
        _go = new Button { Text = "Import", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _go.Pressed += Go;
        goRow.AddChild(_go);
        _stop = new Button
        {
            Text = "Stop", Visible = false,
            TooltipText = "Stops the import. Anything it had already moved aside comes back: the pack and scenario you had stay as they were.",
        };
        _stop.Pressed += Stop;
        goRow.AddChild(_stop);
        right.AddChild(goRow);
        var st = new HBoxContainer();
        st.AddThemeConstantOverride("separation", 8);
        _status = new Label { Modulate = new Color(1, 1, 1, 0.8f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = 0, ShowPercentage = false, CustomMinimumSize = new Vector2(150, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        _bar.AddThemeStyleboxOverride("background", Ui.Box(Ui.Line, 2, 0, 0));
        _bar.AddThemeStyleboxOverride("fill", Ui.Box(Ui.Accent, 2, 0, 0));
        st.AddChild(_status); st.AddChild(_bar);
        right.AddChild(st);
        _log = new RichTextLabel { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = true, CustomMinimumSize = new Vector2(0, 160) };
        _find = FindBar.ForLabel(_log, () => _logText.ToString());
        right.AddChild(_find);
        right.AddChild(_log);
        WindowInput += e => { if (_find.Handles(e)) SetInputAsHandled(); };

        if (ConverterTool.Writer is { } earlier)
        {
            _run = earlier;
            _go.Disabled = true;
            _stop.Visible = true;
            SetStatus("An import started earlier is still running.", null);
        }

        Log("Looking for TTS modules…");
        ConverterTool.Run(new[] { "list-tts" }, line =>
        {
            if (!IsInstanceValid(this) || !line.StartsWith("{")) return;
            var e = BestEffort.Read<TtsEntry?>(() => JsonSerializer.Deserialize<TtsEntry>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }), fallback: null);
            if (e != null) _entries.Add(e);
        }, code =>
        {
            if (!IsInstanceValid(this)) return;
            Log(code == 0 ? $"{_entries.Count} modules and saves found." : "Could not list TTS modules (is TTS installed?). You can still browse to a file or paste a Workshop link.");
            Fill();
        });
    }

    static Texture2D? SmallIcon(string file)
    {
        if (Image.LoadFromFile(file) is not { } img || img.IsEmpty()) return null;
        img.Resize(48, 48);
        return ImageTexture.CreateFromImage(img);
    }

    void Fill()
    {
        _list.Clear();
        var q = _filter.Text.Trim();
        foreach (var e in _entries.Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Id.Contains(q)))
        {
            var icon = e.Thumb is { } thumb ? BestEffort.Read<Texture2D?>(() => SmallIcon(thumb), fallback: null) : null;
            int i = _list.AddItem($"{e.Name.Trim()}\n{(e.Kind == "save" ? "save" : "workshop " + e.Id)} · {e.Size / 1024 / 1024.0:0.#} MB", icon);
            _list.SetItemMetadata(i, _entries.IndexOf(e));
        }
    }

    void Pick(int i)
    {
        var e = _entries[_list.GetItemMetadata(i).AsInt32()];
        _source.Text = e.Path;
        _name.Text = e.Name.Trim();
    }

    void Browse()
    {
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.json ; TTS module or save" }, Size = new Vector2I(820, 520) };
        Ui.RememberFolder(fd);
        AddChild(fd);
        fd.FileSelected += p => { _source.Text = p; if (_name.Text.Length == 0) _name.Text = System.IO.Path.GetFileNameWithoutExtension(p); };
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    public void Prefill(string source) { _source.Text = source; _name.Text = System.IO.Path.GetFileNameWithoutExtension(source); }
    public void StartImport() => Go();
    public string? StopOn { get; set; }

    void SetStatus(string text, double? progress)
    {
        _status.Text = text;
        _bar.Visible = progress != null;
        if (progress is { } p) _bar.Value = p;
    }

    public override void _Process(double delta)
    {
        if (_running || _run is not { Finished: true }) return;
        _run = null;
        _go.Disabled = false;
        _stop.Visible = false; _stop.Disabled = false;
        SetStatus("The import started earlier has ended.", null);
    }

    void Stop()
    {
        if (_run == null) return;
        _run.Cancel();
        _stop.Disabled = true;
        SetStatus("Stopping, and putting back what the import had moved aside…", null);
    }

    static string Entry(string path)
    {
        try { return SaveStore.Default.TrashEntries().FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))?.Describe() ?? System.IO.Path.GetFileName(path); }
        catch { return System.IO.Path.GetFileName(path); }
    }

    void Go()
    {
        if (_running || _source.Text.Trim().Length == 0) return;
        _running = true;
        _go.Disabled = true;
        _go.Text = "Importing…";
        var source = _source.Text.Trim();
        var name = _name.Text.Trim();
        bool skipText = _skipText.ButtonPressed;
        SetStatus("Reading the module, to see what it would replace…", null);
        var args = new List<string> { "plan", source, "--out", _packagesRoot };
        if (name.Length > 0) args.AddRange(new[] { "--name", name });
        var plan = new ImportPlan();
        var said = new List<string>();
        ConverterTool.Run(args, line =>
        {
            if (plan.Read(line)) said.Add(line);
        }, code =>
        {
            if (!IsInstanceValid(this)) return;
            var packs = plan.Packs;
            var scenario = plan.Scenario;
            source = plan.File ?? source;
            if (code != 0 || packs.Count == 0)
            {
                foreach (var l in said) Log(Fmt.Bb(l));
                Log($"[color=#f87]The module could not be read, so nothing was imported (exit {code}).[/color]");
                Reset("Failed, see below.");
                return;
            }
            var worked = WorkedOn(_packagesRoot, packs);
            if (worked.Count == 0) { Import(source, name, skipText, packs, scenario); return; }
            AskFirst(worked, () => Import(source, name, skipText, packs, scenario), () =>
            {
                Log("Not imported: nothing was changed.");
                Reset("Not imported.");
            });
        });
    }

    void Reset(string status)
    {
        _running = false;
        _go.Disabled = false;
        _go.Text = "Import";
        _stop.Visible = false; _stop.Disabled = false;
        SetStatus(status, null);
    }

    void AskFirst(List<(string id, string name, PackChange change)> worked, Action go, Action cancel) =>
        AskBeforeReplacing(this, worked, "Importing this module again", "Import anyway", go, cancel);

    public static List<(string id, string name, PackChange change)> WorkedOn(string packagesRoot, IEnumerable<string> packIds)
    {
        var worked = new List<(string id, string name, PackChange change)>();
        foreach (var id in packIds.Distinct())
        {
            var dir = System.IO.Path.Combine(packagesRoot, id);
            PackChange change;
            try { change = PackChanges.Check(dir).change; } catch { change = PackChange.Unknown; }
            if (change is PackChange.Changed or PackChange.Unknown) worked.Add((id, WorkshopContent.NameIn(System.IO.Path.Combine(dir, "pack.json"), id), change));
        }
        return worked;
    }

    public static void AskBeforeReplacing(Node parent, List<(string id, string name, PackChange change)> worked, string doing, string okText, Action go, Action? cancel)
    {
        var lines = worked.Select(w => w.change == PackChange.Changed
            ? $"·  '{w.name}' ({w.id}): pieces in it were made or changed in the piece studio."
            : $"·  '{w.name}' ({w.id}): whether pieces in it were changed in the piece studio cannot be told (it came here before the app kept a record of what each conversion wrote).");
        bool one = worked.Count == 1;
        var text = $"{doing} replaces {(one ? "a pack you already have" : "packs you already have")}:\n\n{string.Join("\n", lines)}\n\n"
            + $"The pack{(one ? "" : "s")} you have go{(one ? "es" : "")} to the trash whole, with every piece changed in the piece studio, and stay{(one ? "s" : "")} there {SaveStore.TrashDays} days: "
            + "restore from Packs and Pieces → Trash, at the foot of the page. The new copy does not have those changes.";
        bool answered = false;
        var dlg = Ui.Confirm(parent, one ? "Replace a pack you changed?" : "Replace packs you changed?", okText, () => { if (!answered) { answered = true; go(); } }, text, 460);
        dlg.CancelButtonText = "Cancel";
        dlg.Canceled += () => { if (!answered) { answered = true; cancel?.Invoke(); } };
    }

    void Import(string source, string name, bool skipText, List<string> plannedPacks, string? plannedScenario)
    {
        if (!IsInstanceValid(this)) return;
        InUse.Hold hold;
        try
        {
            var folders = plannedPacks.Select(p => (InUse.Pack(_packagesRoot, p), $"the pack {p}")).ToList();
            if (plannedScenario != null) folders.Add((InUse.Scenario(_packagesRoot, plannedScenario), $"the scenario {plannedScenario}"));
            hold = InUse.Claim(folders, "being imported from Tabletop Simulator");
        }
        catch (InUseException e) { Log($"[color=#f87]Not imported: {Fmt.Bb(e.Message)}[/color]"); Reset("Not imported."); return; }
        SetStatus("Starting the converter…", null);
        var args = new List<string> { "import", source, "--out", _packagesRoot };
        if (name.Length > 0) args.AddRange(new[] { "--name", name });
        if (skipText) args.AddRange(new[] { "--skip", "3DText" });
        string? scenario = null, pack = null, report = null, hostile = null;
        Log($"[b]Importing {Fmt.Bb(source)}[/b] — first imports download missing images from Steam and can take a few minutes.");
        int movedAside = 0, putBack = 0;
        string Left() => movedAside == 0 ? "" : putBack >= movedAside
            ? " What you had before is back in place."
            : " What you had before is in the trash: restore it from there (Packs and Pieces or Host a game, at the foot of the page).";
        _run = ConverterTool.Run(args, line =>
        {
            if (!IsInstanceValid(this)) return;
            if (StopOn != null && line.Contains(StopOn)) { StopOn = null; Stop(); }
            if (line.StartsWith("TRASHED "))
            {
                movedAside++;
                Log($"[color=#ffd75e]The {Fmt.Bb(Entry(line[8..].Trim()))} you had went to the trash first, whole. If the new one is not what you wanted, restore it from the trash at the foot of Packs and Pieces (a pack) or Host a game (a scenario) within {SaveStore.TrashDays} days.[/color]");
                return;
            }
            if (line.StartsWith("PUTBACK ")) { putBack++; Log("[color=#8f8]Put back as it was:[/color] " + Fmt.Bb(line[8..])); return; }
            if (line.StartsWith("IMPORTED "))
            {
                foreach (var part in line[9..].Split(' '))
                {
                    var kv = part.Split('=', 2);
                    if (kv[0] == "scenario") scenario = kv[1];
                    if (kv[0] == "pack") pack = kv[1];
                }
            }
            if (line.StartsWith("REPORT ")) report = line[7..];
            if (line.StartsWith("HOSTILE "))
            {
                hostile = line[8..];
                Log($"[color=#f87][b]Hostile script removed[/b] — {Fmt.Bb(hostile)}. The module carried a script that copies itself into objects or reaches the web; it was cut out before anything was read from it, and nothing of it is in the pack. Details in the report.[/color]");
                return;
            }
            if (line.StartsWith("PROGRESS "))
            {
                var ab = line[9..].Split('/');
                if (ab.Length == 2 && int.TryParse(ab[0], out var a) && int.TryParse(ab[1], out var b) && b > 0) SetStatus($"Downloading images from Steam: {a} of {b}", a / (double)b);
                return;
            }
            if (line.Contains("downloading") && line.Contains("assets")) SetStatus("Downloading images from Steam…", 0);
            else if (line.Contains("downloads done")) SetStatus("Building the pack…", null);
            else if (line.Contains("building tiles")) SetStatus("Slicing the map into tiles…", null);
            else if (line.StartsWith("Scenario ")) SetStatus("Laying out the scenario…", null);
            else if (line.StartsWith("Pack ")) SetStatus("Building the pack…", null);
            if (line.StartsWith("Output") || line.StartsWith("Cache")) return;
            Log(Fmt.Bb(line));
        }, code =>
        {
            bool stopped = code == ConverterTool.StoppedCode;
            if (IsInstanceValid(this))
            {
                _running = false;
                _run = null;
                _go.Disabled = false;
                _go.Text = "Import";
                _stop.Visible = false; _stop.Disabled = false;
                SetStatus(stopped ? "Stopped." : pack != null ? "Done." : "Failed, see below.", null);
                if (stopped)
                    Log(movedAside > 0
                        ? "[color=#ffd75e]Stopped. Nothing of this import was kept." + Left() + "[/color]"
                        : "[color=#ffd75e]Stopped before anything was changed.[/color]");
                else if (pack != null)
                {
                    Log(scenario != null
                        ? $"[color=#8f8]Done.[/color] Scenario '{Fmt.Bb(scenario)}' and pack '{Fmt.Bb(pack)}' are ready." + (report != null ? $" Report: {Fmt.Bb(report)}" : "")
                        : $"[color=#8f8]Done.[/color] Pack '{Fmt.Bb(pack)}' restored exactly as it was shared.");
                    if (hostile != null) Log("[color=#f87]A hostile script was removed from this module; see the report.[/color]");
                }
                else Log($"[color=#f87]Import failed (exit {code}).[/color]" + Left());
            }
            if (!stopped && pack != null) _done(scenario ?? "", pack);
        }, writesTo: _packagesRoot, hold: hold);
        if (_run != null) { _stop.Visible = true; _stop.Disabled = false; }
    }

    void Log(string s)
    {
        _logText.Append(s).Append('\n');
        if (_find.Terms.Count > 0) _find.Refresh();
        else _log.AppendText(s + "\n");
    }
}

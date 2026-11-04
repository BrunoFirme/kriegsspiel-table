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
    ImportProgress _progress = null!;
    readonly List<TtsEntry> _entries = new();
    readonly Dictionary<TtsEntry, int> _shown = new();
    bool _running;
    ConverterRun? _run, _listing;
    static ImportWatch? _watch;

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
        root.AddChild(Ui.Hint("A TTS module or save becomes a pack (every piece, bags opened up) and a scenario (its table laid out; big locked boards become map sheets). Scripts are translated; the report beside the scenario lists what did not convert."));

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
        var browse = new Button { Text = "Browse…", TooltipText = "Each module shows its own picture, as in TTS" };
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
            TooltipText = "Titles and labels a module floats over its table come in as flat label pieces; a big module can hold dozens. Notecards come in either way.",
        };
        g.AddChild(_skipText);
        var goRow = new HBoxContainer();
        goRow.AddThemeConstantOverride("separation", 8);
        _go = new Button { Text = "Import", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _go.Pressed += Go;
        goRow.AddChild(_go);
        right.AddChild(goRow);
        _stop = new Button
        {
            Text = "Stop", Visible = false, FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Stops the import and puts back what it had moved aside.",
        };
        _stop.Pressed += Stop;
        _progress = new ImportProgress(_stop);
        right.AddChild(_progress);
        _status = new Label { Modulate = new Color(1, 1, 1, 0.8f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, Visible = false };
        right.AddChild(_status);
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
            if (_watch is { Ended: null } watch) _progress.Follow(watch, "Importing");
            else SetStatus("An import started earlier is still running.");
        }

        Log("Looking for TTS modules…");
        bool listed = false;
        _listing = ConverterTool.Run(new[] { "list-tts" }, line =>
        {
            if (!IsInstanceValid(this) || !line.StartsWith("{")) return;
            var e = TtsEntryOf(line);
            if (e == null) return;
            if (e.Kind != MeasuredKind) { _entries.Add(e); return; }
            if (!listed) { listed = true; Fill(); }
            Measured(e);
        }, code =>
        {
            if (!IsInstanceValid(this)) return;
            _listing = null;
            if (code == ConverterTool.StoppedCode) return;
            Log(code == 0 ? $"{_entries.Count} modules and saves found." : "Could not list TTS modules (is TTS installed?). You can still browse to a file or paste a Workshop link.");
            if (!listed) Fill();
        });
    }

    const string MeasuredKind = "size";

    public override void _ExitTree() => _listing?.Cancel();

    void Measured(TtsEntry m)
    {
        var e = _entries.FirstOrDefault(x => string.Equals(x.Path, m.Path, StringComparison.OrdinalIgnoreCase));
        if (e == null) return;
        e.Size = m.Size;
        e.Fetch = m.Fetch;
        e.Measured = true;
        if (_shown.TryGetValue(e, out var i) && i < _list.ItemCount) Describe(i, e);
    }

    void Describe(int i, TtsEntry e)
    {
        var where = e.Kind == "save" ? "save" : "workshop " + e.Id;
        var size = e.Measured && e.Size > 0 ? " · " + ContentLimits.Describe(e.Size) : "";
        var fetch = e.Measured && e.Fetch > 0 ? $" · {e.Fetch} not on this PC" : "";
        _list.SetItemText(i, $"{e.Name.Trim()}   ·   {where}{size}{fetch}");
        if (e.Measured)
            _list.SetItemTooltip(i, "Pictures and models of its pieces and maps that TTS keeps on this PC" + (e.Fetch > 0 ? $"; {e.Fetch} more come from the web during the import" : ""));
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
        _shown.Clear();
        var q = _filter.Text.Trim();
        foreach (var e in _entries.Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Id.Contains(q)))
        {
            var icon = e.Thumb is { } thumb ? BestEffort.Read<Texture2D?>(() => SmallIcon(thumb), fallback: null) : null;
            int i = _list.AddItem("", icon);
            _list.SetItemMetadata(i, _entries.IndexOf(e));
            _shown[e] = i;
            Describe(i, e);
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
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.json ; TTS module or save" }, Size = new Vector2I(900, 600) };
        Ui.RememberFolder(fd);
        if (_entries.FirstOrDefault(e => e.Kind == "workshop") is { } listed && System.IO.Path.GetDirectoryName(listed.Path) is { } workshop) fd.CurrentDir = workshop;
        AddChild(fd);
        FileThumbs.Show(fd);
        fd.FileSelected += p =>
        {
            _source.Text = p;
            var known = _entries.FirstOrDefault(e => string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase));
            _name.Text = known?.Name.Trim() ?? System.IO.Path.GetFileNameWithoutExtension(p);
        };
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    public void Prefill(string source) { _source.Text = source; _name.Text = System.IO.Path.GetFileNameWithoutExtension(source); }
    public void StartImport() => Go();
    public string? StopOn { get; set; }

    void SetStatus(string text)
    {
        _status.Text = text;
        _status.Visible = text.Length > 0;
    }

    public override void _Process(double delta)
    {
        if (_running || _run is not { Finished: true }) return;
        _run = null;
        _go.Disabled = false;
        _stop.Visible = false;
        _stop.Disabled = false;
        if (!_progress.Visible) SetStatus("The import started earlier has ended.");
    }

    void Stop()
    {
        if (_run == null) return;
        _run.Cancel();
        _stop.Disabled = true;
        SetStatus("Stopping, and putting back what the import had moved aside…");
    }

    static readonly JsonSerializerOptions ListedOptions = new() { PropertyNameCaseInsensitive = true };

    static TtsEntry? TtsEntryOf(string line)
    {
        try { return JsonSerializer.Deserialize<TtsEntry>(line, ListedOptions); }
        catch (JsonException) { return null; }
    }

    static string Entry(string path)
    {
        try { return SaveStore.Default.TrashEntries().FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))?.Describe() ?? System.IO.Path.GetFileName(path); }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return System.IO.Path.GetFileName(path); }
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
        var watch = _watch = new ImportWatch();
        _progress.Follow(watch, "Reading the module");
        SetStatus("");
        var args = new List<string> { "plan", source, "--out", _packagesRoot };
        if (name.Length > 0) args.AddRange(new[] { "--name", name });
        var plan = new ImportPlan();
        var said = new List<string>();
        _run = ConverterTool.Run(args, line =>
        {
            if (!watch.Read(line) && plan.Read(line)) said.Add(line);
        }, code =>
        {
            if (!IsInstanceValid(this)) { watch.End(NotImported); return; }
            _run = null;
            watch.Between();
            var packs = plan.Packs;
            var scenario = plan.Scenario;
            source = plan.File ?? source;
            if (code == ConverterTool.StoppedCode)
            {
                Log("[color=#ffd75e]Stopped before anything was changed.[/color]");
                Reset(Stopped);
                return;
            }
            if (code != 0 || packs.Count == 0)
            {
                foreach (var l in said) Log(Fmt.Bb(l));
                Log($"[color=#f87]The module could not be read, so nothing was imported (exit {code}).[/color]");
                Reset(Failed);
                return;
            }
            var worked = WorkedOn(_packagesRoot, packs);
            if (worked.Count == 0) { Import(source, name, skipText, packs, scenario, watch); return; }
            _stop.Visible = false;
            _progress.Waiting("Waiting for your answer");
            AskFirst(worked, () => Import(source, name, skipText, packs, scenario, watch), () =>
            {
                Log("Not imported: nothing was changed.");
                Reset(NotImported);
            });
        });
        if (_run != null) { _stop.Visible = true; _stop.Disabled = false; }
    }

    const string Done = "Done", Stopped = "Stopped", Failed = "Failed", NotImported = "Not imported";

    void Reset(string outcome)
    {
        _running = false;
        _go.Disabled = false;
        _go.Text = "Import";
        _stop.Visible = false;
        _stop.Disabled = false;
        _watch?.End(outcome);
        SetStatus("");
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
            try { change = PackChanges.Check(dir).change; }
            catch (Exception e) when (SaveStore.IsFileTrouble(e)) { change = PackChange.Unknown; }
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
            + "restore from Piece studio → Trash, at the foot of the page. The new copy does not have those changes.";
        bool answered = false;
        var dlg = Ui.Confirm(parent, one ? "Replace a pack you changed?" : "Replace packs you changed?", okText, () => { if (!answered) { answered = true; go(); } }, text, 460);
        dlg.CancelButtonText = "Cancel";
        dlg.Canceled += () => { if (!answered) { answered = true; cancel?.Invoke(); } };
    }

    InUse.Hold? ClaimForImport(List<string> plannedPacks, string? plannedScenario)
    {
        try
        {
            var folders = plannedPacks.Select(p => (InUse.Pack(_packagesRoot, p), $"the pack {p}")).ToList();
            if (plannedScenario != null) folders.Add((InUse.Scenario(_packagesRoot, plannedScenario), $"the scenario {plannedScenario}"));
            return InUse.Claim(folders, "being imported from Tabletop Simulator");
        }
        catch (InUseException e)
        {
            Log($"[color=#f87]Not imported: {Fmt.Bb(e.Message)}[/color]");
            Reset(NotImported);
            return null;
        }
    }

    void Import(string source, string name, bool skipText, List<string> plannedPacks, string? plannedScenario, ImportWatch watch)
    {
        if (!IsInstanceValid(this)) return;
        if (ClaimForImport(plannedPacks, plannedScenario) is not { } hold) return;
        _progress.Waiting("Starting the converter");
        var args = new List<string> { "import", source, "--out", _packagesRoot };
        if (name.Length > 0) args.AddRange(new[] { "--name", name });
        if (skipText) args.AddRange(new[] { "--skip", "3DText" });
        string? scenario = null, pack = null, report = null, hostile = null, leftOut = null;
        Log($"[b]Importing {Fmt.Bb(source)}[/b]");
        int movedAside = 0, putBack = 0;
        string Left() => movedAside == 0 ? "" : putBack >= movedAside
            ? " What you had before is back in place."
            : " What you had before is in the trash: restore it from there (Piece studio or Host a game, at the foot of the page).";
        _run = ConverterTool.Run(args, line =>
        {
            if (IsInstanceValid(this) && StopOn != null && line.Contains(StopOn)) { StopOn = null; Stop(); }
            if (watch.Read(line)) return;
            if (line.StartsWith("SIZES ")) { watch.Sizes = Sizes(line[6..]); return; }
            if (!IsInstanceValid(this)) return;
            if (line.StartsWith("TRASHED "))
            {
                movedAside++;
                Log($"[color=#ffd75e]The {Fmt.Bb(Entry(line[8..].Trim()))} you had went to the trash first, whole. If the new one is not what you wanted, restore it from the trash at the foot of Piece studio (a pack) or Host a game (a scenario) within {SaveStore.TrashDays} days.[/color]");
                return;
            }
            if (line.StartsWith("PUTBACK "))
            {
                putBack++;
                Log("[color=#8f8]Put back as it was:[/color] " + Fmt.Bb(line[8..]));
                return;
            }
            if (line.StartsWith("IMPORTED "))
            {
                foreach (var part in line[9..].Split(' '))
                {
                    var kv = part.Split('=', 2);
                    if (kv[0] == "scenario") scenario = kv[1];
                    if (kv[0] == "pack") pack = kv[1];
                }
                return;
            }
            if (line.StartsWith("REPORT ")) { report = line[7..]; return; }
            if (line.StartsWith("HOSTILE "))
            {
                hostile = line[8..];
                Log($"[color=#f87][b]Hostile script removed[/b] — {Fmt.Bb(hostile)}. The module carried a script that copies itself into objects or reaches the web; it was cut out before anything was read from it, and nothing of it is in the pack. Details in the report.[/color]");
                return;
            }
            if (line.StartsWith("LEFTOUT "))
            {
                var parts = line[8..].Split(' ', 2);
                leftOut = parts[0] == "1" ? "1 piece left out" : $"{parts[0]} pieces left out";
                Log($"[color=#ff5f5f][b]{leftOut}:[/b] {Fmt.Bb(parts.Length > 1 ? parts[1] : "")}. Their pictures or models are gone from the web and TTS has no copy on this computer, so TTS cannot show them either. Everything else came in; the report lists each one with its link.[/color]");
                return;
            }
            if (line.StartsWith("Output") || line.StartsWith("Cache")) return;
            Log(Fmt.Bb(line));
        }, code =>
        {
            bool stopped = code == ConverterTool.StoppedCode;
            watch.End(stopped ? Stopped : pack != null ? Done : Failed);
            if (IsInstanceValid(this))
            {
                _running = false;
                _run = null;
                _go.Disabled = false;
                _go.Text = "Import";
                _stop.Visible = false;
                _stop.Disabled = false;
                SetStatus("");
                if (stopped)
                    Log(movedAside > 0
                        ? "[color=#ffd75e]Stopped. Nothing of this import was kept." + Left() + "[/color]"
                        : "[color=#ffd75e]Stopped before anything was changed.[/color]");
                else if (pack != null)
                {
                    Log(scenario != null
                        ? $"[color=#8f8]Done.[/color] Scenario '{Fmt.Bb(scenario)}' and pack '{Fmt.Bb(pack)}' are ready." + OnDisk(watch) + (report != null ? $" Report: {Fmt.Bb(report)}" : "")
                        : $"[color=#8f8]Done.[/color] Pack '{Fmt.Bb(pack)}' restored exactly as it was shared." + OnDisk(watch));
                    if (hostile != null) Log("[color=#f87]A hostile script was removed from this module; see the report.[/color]");
                    if (leftOut != null) Log($"[color=#ff5f5f][font_size=20][b]{leftOut}: broken in TTS too[/b][/font_size][/color]");
                }
                else Log($"[color=#f87]Import failed (exit {code}).[/color]" + Left());
            }
            if (!stopped && pack != null) _done(scenario ?? "", pack);
        }, writesTo: _packagesRoot, hold: hold);
        if (_run != null) { _stop.Visible = true; _stop.Disabled = false; }
    }

    static string OnDisk(ImportWatch watch) => watch.Sizes.Length > 0 ? $" {watch.Sizes}." : "";

    static string Sizes(string said)
    {
        var parts = new List<string>();
        foreach (var kv in said.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)))
            if (kv.Length == 2 && kv[0].Length > 0 && long.TryParse(kv[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var bytes) && bytes > 0)
                parts.Add($"{(parts.Count == 0 ? char.ToUpperInvariant(kv[0][0]) + kv[0][1..] : kv[0])} {ContentLimits.Describe(bytes)}");
        return parts.Count == 0 ? "" : string.Join(", ", parts) + " on disk";
    }

    void Log(string s)
    {
        _logText.Append(s).Append('\n');
        if (_find.Terms.Count > 0) _find.Refresh();
        else _log.AppendText(s + "\n");
    }
}

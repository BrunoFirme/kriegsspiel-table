using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Core.Model;
using Godot;
using KTable.Client.Agent;
using KTable.Core;

namespace KTable.App;

public partial class AgentEndpoint : Node
{
    const int PortsToTry = 20;
    public const string LiveFile = "agent.json", CacheFile = "agent-tools.json";

    const string Instructions = "Kriegsspiel Table, a tabletop for umpired wargames. You act as the person at this PC, with their seat's rights; your changes are logged as agent. Call status first. Steps that do not wait on each other's answers go together in one batch call. " +
        "Go where the work is: open_studio (a pack's pieces, or a new pack), host_game (a scenario, or a new blank one), close_studio, leave_game. " +
        "For a unit, make_unit does it in one call (a block with its counters, text lines, looks and script); make_units makes many. " +
        "For anything else in the piece studio: start a kind (studio_make_block, studio_new_piece, studio_pieces_from_pictures, studio_import_models, studio_new_effect); " +
        "give it native data before any script (studio_set_piece, studio_set_counter, studio_set_text_line, studio_add_look, studio_set_markers, studio_set_effect); " +
        "draw its faces as layers (studio_read_face, then studio_write_face); add behaviour with studio_write_script after reading api_reference with find. " +
        "For a 3D figure: studio_import_models (a model file), then studio_model_view from each side, studio_model_paint (whole, by material or by a box) and studio_model_decal; studio_model_add_part builds from shapes or a model file (file), also onto a block, where it sits centred on top. " +
        "Look at what you made (studio_view_piece, studio_model_view, the pictures the tools return) and correct it until it matches what was asked; studio_undo takes your last step back. " +
        "At the table: view_map shows the map from above with a grid in the coordinates pieces are placed by, so find places on it before placing; add_pack, list_kinds, place_piece or place_pieces (many at once, in a formation) and form_up (pieces already there, into a line, column, square, wedge…, or strung along a path), add_map_sheets (pictures, 3D terrain and heightmaps) and set_map_sheet (also a sheet's relief, its height, its picture and a crop), shape_ground (raise, lower, smooth, flatten; paint ground types by stroke, fill or shape, sparse at a density, each with its own details: plots and buildings, trees, road class, houses and walls; a road over water is a bridge, through a town its street; the map needs a scale first, which set_map_scale or a blank sheet made with one gives) and ground_at, set_map_scale, set_piece_values, set_piece_options, press_button and pick_menu_row (as a player), " +
        "copy_pieces and paste_pieces carry pieces as they stand from one scenario into another: the clipboard outlives the game, so copy before leave_game and paste after host_game, and the packs they need come into that game with them. " +
        "stow_pieces puts pieces away inside another piece, off the table, as a bag holds them in Tabletop Simulator, and unstow_piece takes one out again; read_piece on the holder lists what it holds. " +
        "end_turn, set_clock, roll_dice, say, draw (on a named drawing board if asked), list_drawings, erase_drawings, set_zone, set_grid, set_light (the light, the backdrop round the table, the table's shade, the map style painted ground is drawn in, contour lines, a snow line, streams from the relief, the wind), set_briefing (the scenario's written situation), save_game, load_save, write_script, run_lua, read_console, snapshot, show_view (turns the person's own view), undo. " +
        "Effects (smoke, fire, volleys, mist) are pieces of the standard pack " + FirmeEffects.PackName + ": add_pack it if list_packs shows it is not at the table, then place them. " +
        "A pack's name, authors and description: studio_set_pack. This PC's settings (graphics, sound, interface size, captures…): read_settings, and set_settings only when the person asks.";

    readonly Settings _cfg;
    readonly ConcurrentQueue<(string body, TaskCompletionSource<string?> answer)> _calls = new();
    bool _busy;
    ulong _busySince;
    const ulong StuckMs = 180_000;
    McpServer? _server;
    Mcp? _mcp;
    int _seen;

    public Func<Session?> TableSession = () => null;
    public Func<Hud?> TableHud = () => null;
    public PieceStudio? Studio;
    public Func<bool> AtTitle = () => false;
    public Func<string, string?> OpenStudio = _ => "not ready";
    public Func<string, bool, string?> Host = (_, _) => "not ready";
    public Func<string?> CloseStudio = () => "not ready";
    public Func<string?> LeaveGame = () => "not ready";

    public event Action? Changed;

    public static AgentEndpoint? Current { get; private set; }

    public AgentEndpoint(Settings cfg) { _cfg = cfg; Name = "Agent"; }

    public override void _EnterTree() => Current = this;

    public bool On => _server != null;
    public string PlayerName => _cfg.LastName;
    public string? Problem { get; private set; }
    public string Address => _server?.Address ?? $"http://127.0.0.1:{_cfg.AgentPort}/mcp";
    public int Calls => _server?.Calls ?? 0;
    public string? Working { get; private set; }
    public DateTime? LastCallAt { get; private set; }
    public IReadOnlyList<(DateTime At, string What)> Recent => _recent;
    readonly List<(DateTime At, string What)> _recent = new();
    const int RecentKept = 6;
    public int Port => _server?.Port ?? _cfg.AgentPort;

    public string Token
    {
        get
        {
            if (_cfg.AgentToken.Length < 32) { _cfg.AgentToken = NewToken(); _cfg.Save(); }
            return _cfg.AgentToken;
        }
    }

    public string Header => "Authorization: Bearer " + Token;

    public string Config => new JsonObject
    {
        ["mcpServers"] = new JsonObject
        {
            ["kriegsspiel-table"] = Helper is { } h
                ? new JsonObject { ["type"] = "stdio", ["command"] = h, ["args"] = new JsonArray("agent", DataPaths.Local) }
                : new JsonObject { ["type"] = "http", ["url"] = Address, ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + Token } },
        },
    }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public string HttpConfig => new JsonObject
    {
        ["mcpServers"] = new JsonObject
        {
            ["kriegsspiel-table"] = new JsonObject { ["type"] = "http", ["url"] = Address, ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + Token } },
        },
    }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public static string? Helper => ConverterTool.Find() is { } exe ? Path.GetFullPath(exe) : null;

    public string HelperCommand => Helper is { } h ? $"\"{h}\" agent \"{DataPaths.Local}\"" : "";

    public string Note => Helper != null
        ? "Please connect yourself to Kriegsspiel Table, the wargame table app on this PC, so you can help me build pieces and set up games in it.\n" +
          "Add an MCP server named kriegsspiel-table that runs this command (stdio):\n" +
          HelperCommand + "\n" +
          "Then call its status tool and tell me you are ready. From then on, when I ask for pieces, units, scripts or a game, use its tools; it opens the app by itself when it is needed."
        : "Please connect yourself to Kriegsspiel Table, the wargame table app on this PC, so you can help me build pieces and set up games in it.\n" +
          $"Add an MCP server named kriegsspiel-table at {Address} (HTTP) that sends the header {Header}\n" +
          "Then call its status tool and tell me you are ready. From then on, when I ask for pieces, units, scripts or a game, use its tools while the app is open.";

    static string NewToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public void TurnOn(bool remember = true)
    {
        if (_server == null)
        {
            _mcp ??= BuildMcp();
            McpServer? started = null;
            SocketException? last = null;
            for (int i = 0; i < PortsToTry && started == null; i++)
            {
                int port = _cfg.AgentPort + i;
                if (port > 65535) break;
                var server = new McpServer(port, Token, Enqueue);
                try { server.Start(); started = server; }
                catch (SocketException e) { server.Dispose(); last = e; }
            }
            if (started == null)
            {
                Problem = $"No port from {_cfg.AgentPort} on could be opened ({last?.SocketErrorCode}): choose another in the technical settings.";
                Changed?.Invoke();
                return;
            }
            _server = started;
            if (started.Port != _cfg.AgentPort) { _cfg.AgentPort = started.Port; _cfg.Save(); }
            Problem = null;
            Diagnostics.Write(LogLevel.Info, $"agent endpoint on at {started.Address}");
        }
        if (remember && !_cfg.AgentOn) { _cfg.AgentOn = true; _cfg.Save(); }
        WriteFiles();
        Changed?.Invoke();
    }

    public void TurnOff(bool remember = true)
    {
        if (remember && _cfg.AgentOn) { _cfg.AgentOn = false; _cfg.Save(); }
        if (_server == null) return;
        _server.Dispose();
        _server = null;
        while (_calls.TryDequeue(out var c)) c.answer.TrySetResult(null);
        BestEffort.Try(() => File.Delete(Path.Combine(DataPaths.Local, LiveFile)));
        Diagnostics.Write(LogLevel.Info, "agent endpoint off");
        Changed?.Invoke();
    }

    public void RenewToken()
    {
        _cfg.AgentToken = NewToken();
        _cfg.Save();
        if (_server != null) { TurnOff(remember: false); TurnOn(); }
        Changed?.Invoke();
    }

    public void SetPort(int port)
    {
        if (port == _cfg.AgentPort) return;
        _cfg.AgentPort = port;
        _cfg.Save();
        if (_server != null) { TurnOff(remember: false); TurnOn(); }
        Changed?.Invoke();
    }

    void WriteFiles()
    {
        if (_server == null || _mcp == null) return;
        try
        {
            Directory.CreateDirectory(DataPaths.Local);
            var user = OS.GetCmdlineArgs().TakeWhile(a => a != "--" && a != "++").ToList();
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            KTable.Core.Json.WriteAtomic(Path.Combine(DataPaths.Local, CacheFile), new JsonObject
            {
                ["version"] = ProjectSettings.GetSetting("application/config/version", "dev").AsString(),
                ["instructions"] = _mcp.Instructions,
                ["tools"] = _mcp.ToolList(),
                ["prompts"] = _mcp.PromptList(),
                ["launch"] = new JsonObject
                {
                    ["exe"] = OS.GetExecutablePath(),
                    ["args"] = new JsonArray(user.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                    ["cwd"] = System.IO.Directory.GetCurrentDirectory(),
                },
                ["pid"] = OS.GetProcessId(),
                ["process"] = me.ProcessName,
            }.ToJsonString());
            KTable.Core.Json.WriteAtomic(Path.Combine(DataPaths.Local, LiveFile), new JsonObject { ["url"] = _server.Address, ["token"] = Token, ["pid"] = OS.GetProcessId() }.ToJsonString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Diagnostics.Record("agent", "could not write the helper's files: " + e.Message); }
    }

    public override void _ExitTree()
    {
        TurnOff(remember: false);
        if (Current == this) Current = null;
    }

    Task<string?> Enqueue(string body)
    {
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _calls.Enqueue((body, answer));
        return answer.Task;
    }

    public override void _Process(double delta)
    {
        if (_busy && Time.GetTicksMsec() - _busySince > StuckMs) { Diagnostics.Record("agent", "a call ran past three minutes; the next one goes ahead"); _busy = false; }
        if (!_busy && _calls.TryDequeue(out var c)) _ = Answer(c.body, c.answer);
        if (_server != null && _server.Calls != _seen) { _seen = _server.Calls; Changed?.Invoke(); }
    }

    async Task Answer(string body, TaskCompletionSource<string?> answer)
    {
        _busy = true;
        _busySince = Time.GetTicksMsec();
        Working = ToolOf(body);
        if (Working != null) Changed?.Invoke();
        try
        {
            if (DisplayServer.GetName() != "headless" && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized)
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            answer.TrySetResult(await _mcp!.Handle(body));
        }
        catch (Exception e) { Diagnostics.Record("agent", e.Message); answer.TrySetResult(null); }
        finally
        {
            _busy = false;
            if (Working != null)
            {
                Working = null;
                LastCallAt = DateTime.UtcNow;
                Changed?.Invoke();
            }
        }
    }

    static string? ToolOf(string body)
    {
        try
        {
            var o = JsonNode.Parse(body);
            return (string?)o?["method"] == "tools/call" && (string?)o["params"]?["name"] is { Length: > 0 } name ? name.Replace('_', ' ') : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    internal Session? Session => Studio != null && IsInstanceValid(Studio) && !Studio.Gone ? Studio.S : TableSession();

    internal bool InStudio => Studio != null && IsInstanceValid(Studio) && !Studio.Gone;

    internal async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), Timer.SignalName.Timeout);

    internal Task<bool> Landed(Session s) => Waits.Landed(s, Delay);

    internal async Task FreshTurns(Session s, Hud hud)
    {
        bool came = false;
        void Came() => came = true;
        s.TurnsChanged += Came;
        try
        {
            hud.N.RequestTurns();
            await Waits.Until(() => came, Waits.LandingSeconds, Delay);
        }
        finally { s.TurnsChanged -= Came; }
    }

    internal async Task Drawn(int frames)
    {
        if (DisplayServer.GetName() == "headless") return;
        async Task Frame() => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        for (int i = 0; i < frames; i++)
        {
            var drawn = Frame();
            if (await Task.WhenAny(drawn, Delay(DrawWaitSeconds)) != drawn) return;
        }
    }

    const double DrawWaitSeconds = 2;
    internal const int SettleFrames = 4;

    internal async Task<byte[]?> Snapshot(int max = 1280)
    {
        if (DisplayServer.GetName() == "headless") return null;
        async Task Drawn() => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var drawn = Drawn();
        if (await Task.WhenAny(drawn, Delay(2)) != drawn) return null;
        var img = GetViewport().GetTexture().GetImage();
        if (img == null || img.IsEmpty()) return null;
        int w = img.GetWidth(), h = img.GetHeight();
        if (w > max) img.Resize(max, Math.Max(1, h * max / w), Image.Interpolation.Lanczos);
        return img.SavePngToBuffer();
    }

    internal void Log(string what)
    {
        Diagnostics.Write(LogLevel.Info, "agent " + what);
        _recent.Insert(0, (DateTime.UtcNow, what));
        if (_recent.Count > RecentKept) _recent.RemoveAt(_recent.Count - 1);
        Changed?.Invoke();
        if (InStudio) { Studio!.Script.LogLine("agent " + what, "#c9a35a"); return; }
        if (TableHud() is { } hud && IsInstanceValid(hud)) hud.Log($"[color=#c9a35a]agent[/color] {Fmt.Bb(what)}");
    }

    Mcp BuildMcp()
    {
        var tools = new List<AgentTool>();
        tools.AddRange(new AppTools(this).Tools());
        tools.AddRange(new TableTools(() => Session, () => InStudio, Delay, Log, (p, text) => Session is { } s ? s.Ground.NameAt(p, text).Baseline : new List<Vec2> { p }).Tools());
        tools.AddRange(new StudioTools(this).Tools());
        tools.AddRange(new AgentTableTools(this).Tools());
        tools.AddRange(new AgentSettingsTools(this, _cfg).Tools());
        var version = ProjectSettings.GetSetting("application/config/version", "dev").AsString();
        return new Mcp(tools, version, Instructions, AgentRecipes.All);
    }

    internal Task<string?> Handle(string body) => (_mcp ??= BuildMcp()).Handle(body);
}

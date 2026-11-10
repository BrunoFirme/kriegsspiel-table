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
    public Func<string, int?, string, string?> Join = (_, _, _) => "not ready";
    public Func<string?> CloseStudio = () => "not ready";
    public Func<string?> LeaveGame = () => "not ready";
    public Action Refresh = () => { };

    public event Action? Changed;

    public static AgentEndpoint? Current { get; private set; }

    public AgentEndpoint(Settings cfg) { _cfg = cfg; Name = "Agent"; }

    internal Settings Cfg => _cfg;

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
                DisplayServer.WindowSetMode(Graphics.ModeOf(_cfg));
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

    internal const int ShotUsual = 1280, ShotLeast = 256, ShotMost = 3840;

    internal async Task<byte[]?> Snapshot(int max = ShotUsual, double[]? region = null)
    {
        if (DisplayServer.GetName() == "headless") return null;
        async Task Drawn() => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var drawn = Drawn();
        if (await Task.WhenAny(drawn, Delay(2)) != drawn) return null;
        var img = GetViewport().GetTexture().GetImage();
        if (img == null || img.IsEmpty()) return null;
        if (region is { Length: 4 })
        {
            int iw = img.GetWidth(), ih = img.GetHeight();
            var box = new Rect2I((int)(region[0] * iw), (int)(region[1] * ih), Math.Max(1, (int)((region[2] - region[0]) * iw)), Math.Max(1, (int)((region[3] - region[1]) * ih)));
            img = img.GetRegion(box);
        }
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
        tools.AddRange(new TableTools(() => Session, () => InStudio, Delay, Log, new TableHooks
        {
            PlaceName = (p, text) => Session is { } s ? s.Ground.NameAt(p, text).Baseline : new List<Vec2> { p },
            KeepPicture = KeepPicture,
            SendWithFiles = (files, c) =>
            {
                if (TableHud() is { } hud) hud.N.ShareThen(files, c);
                else Session?.Do(c);
            },
            Favourite = (kind, set) =>
            {
                if (TableHud() is not { } hud) return false;
                if (set is { } on && on != hud.Pieces.IsFavourite(kind)) hud.Pieces.ToggleFavourite(kind);
                return hud.Pieces.IsFavourite(kind);
            },
        }).Tools());
        tools.AddRange(new StudioTools(this).Tools());
        tools.AddRange(new AgentTableTools(this).Tools());
        tools.AddRange(new AgentSettingsTools(this, _cfg).Tools());
        tools.AddRange(new AgentImport(this).Tools());
        tools.AddRange(new AgentHelpTools().Tools());
        tools.AddRange(new AgentUiTools(this).Tools());
        var version = ProjectSettings.GetSetting("application/config/version", "dev").AsString();
        return new Mcp(tools, version, AgentGuide.Instructions, AgentRecipes.All);
    }

    internal async Task<string?> KeepPicture(string pathOrLink, bool backdrop)
    {
        if (Session is not { } s) return null;
        var root = s.Store.Root;
        var (files, problem) = await PictureLinks.Local(new[] { pathOrLink }, root);
        if (problem != null || files.Count == 0 || !File.Exists(files[0])) return null;
        var file = files[0];
        if (!backdrop) return PictureLinks.Keep(root, file);
        try { return await Task.Run(() => ScenarioDialog.KeepBackdrop(root, file)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Diagnostics.Record("agent", $"backdrop {file}: {e.Message}"); return null; }
    }

    internal Task<string?> Handle(string body) => (_mcp ??= BuildMcp()).Handle(body);
}

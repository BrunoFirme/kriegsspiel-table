#if KTABLE_STEAM
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;

namespace KTable.App;

public partial class SteamService : Node
{
    public static SteamService? Instance { get; private set; }
    public uint AppId { get; private set; }
    public bool Running { get; private set; }
    public string Status { get; private set; } = "";
    public string UserName => Running ? SteamFriends.GetPersonaName() : "";
    public AppId_t App => new(AppId);

    static bool _resolverSet;

    public override void _Ready()
    {
        Instance = this;
        AppId = ReadAppId();
        SetResolver();
        if (AppId == 0) { Status = "Steam features are off (no Steam app id configured)."; return; }
        try
        {
            if (!OS.IsDebugBuild() && SteamAPI.RestartAppIfNecessary(App))
            {
                Diagnostics.Closing();
                GetTree().Quit();
                return;
            }
            if (!SteamAPI.IsSteamRunning()) { Status = "Steam is not running. Start Steam to use the Workshop."; return; }
            System.Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            System.Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
            Running = SteamAPI.Init();
            Status = Running ? $"Connected to Steam as {SteamFriends.GetPersonaName()}." : "Steam refused to start this app (is it owned on this account?).";
        }
        catch (Exception e) { Status = "Steam library could not be loaded: " + e.Message; Running = false; }
        GD.Print("[steam] " + Status);
    }

    public override void _Process(double delta)
    {
        if (Running) SteamAPI.RunCallbacks();
    }

    public override void _ExitTree()
    {
        if (Running) { SteamAPI.Shutdown(); Running = false; }
    }

    static uint ReadAppId()
    {
        if (uint.TryParse(System.Environment.GetEnvironmentVariable("KTABLE_STEAM_APP_ID"), out var env) && env > 0) return env;
        var v = ProjectSettings.GetSetting("steam/app_id", 0);
        return v.VariantType == Variant.Type.Int ? (uint)Math.Max(0, v.AsInt64()) : 0;
    }

    static void SetResolver()
    {
        if (_resolverSet) return;
        _resolverSet = true;
        NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, (name, asm, path) =>
        {
            if (!name.Contains("steam_api", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
            var lib = NativePath();
            return lib != null && NativeLibrary.TryLoad(lib, out var h) ? h : IntPtr.Zero;
        });
    }

    public static string? NativePath()
    {
        var (dir, file) = OS.GetName() switch
        {
            "Windows" => ("win-x64", "steam_api64.dll"),
            "macOS" => ("osx", "libsteam_api.dylib"),
            _ => ("linux-x64", "libsteam_api.so"),
        };
        foreach (var root in new[] { ProjectSettings.GlobalizePath("res://native"), Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "native") })
        {
            var p = Path.Combine(root, dir, file);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public void OpenUrl(string url)
    {
        if (Running && SteamUtils.IsOverlayEnabled()) SteamFriends.ActivateGameOverlayToWebPage(url);
        else OS.ShellOpen(url);
    }

    public static string ItemUrl(ulong id) => $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}";
}
#endif

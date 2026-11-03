using System;
using System.Collections.Generic;
using Godot;
using KTable.Client.Scripting;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting;

namespace KTable.App;

public partial class ScriptFileWatch : Node
{
    const double Every = 0.25;

    public readonly ScriptFiles Files;
    readonly Session _s;
    readonly Dictionary<string, Action<string?>> _apply = new();
    double _since;

    public event Action? LinksChanged;
    public event Action<string, bool>? Noted;

    public ScriptFileWatch(Session s, string folder, string title)
    {
        _s = s;
        Name = "ScriptFiles";
        Files = new ScriptFiles(folder, title, ApiDoc.Stubs());
        Files.Console(_s.ConsoleLines, DefOf, NameOf);
        _s.ConsoleChanged += OnConsole;
        TreeExiting += () => { _s.ConsoleChanged -= OnConsole; Files.StopAll(); };
    }

    public static string TableFolder(Session s) => ScriptFiles.ScenarioFolder(ScriptFiles.Root, s.ScenarioDir ?? (s.View.Name.Length > 0 ? s.View.Name : "game"));

    public static string PackFolder(string packId) => ScriptFiles.PackFolder(ScriptFiles.Root, packId);

    public ScriptLink? Of(string key) => Files.Of(key);

    public void Link(Node parent, string key, string file, Func<string?> current, Func<bool> alive, Action<string?> apply, Keep keep = Keep.Ask)
    {
        var started = Files.Start(key, file, current, alive, keep, DateTime.UtcNow);
        if (started.Problem != null) { Note($"{file}: could not be linked: {started.Problem}", true); return; }
        if (started.Conflict)
        {
            var dlg = Ui.Confirm(parent, "The file holds another script", "Use the file", () => Link(parent, key, file, current, alive, apply, Keep.File),
                $"{file} already holds a script that differs from this one. Use the file runs it here; the other keeps this one and writes it over the file.", 440);
            dlg.Also("Write this one over it", () => Link(parent, key, file, current, alive, apply, Keep.Script));
            return;
        }
        _apply[key] = apply;
        Note($"{started.Link!.File}: edited as a file: {started.Link.Path}", false);
        LinksChanged?.Invoke();
    }

    public void LinkTable(Node parent, string key)
    {
        if (key.Length == 0)
        {
            Link(parent, key, ScriptFiles.GlobalFile, () => _s.View.Script, () => true, text => { _s.Do(new SetGlobalScript { Text = text }); _s.Request(new GetScripts()); });
            return;
        }
        if (_s.View.Find(Layers.Umpire, key) is not { } piece) return;
        Link(parent, key, ScriptFiles.PieceFile(key, piece.Label ?? _s.Def(piece.Def)?.Name),
            () => _s.View.Find(Layers.Umpire, key) is { } p ? p.Script ?? _s.Def(p.Def)?.Script : null,
            () => _s.View.Find(Layers.Umpire, key) != null,
            text => { _s.Do(new SetScript { Layer = Layers.Umpire, Id = key, Text = text }); _s.Request(new GetScripts()); });
    }

    public void Unlink(string key)
    {
        if (Files.Of(key) is not { } link) return;
        Files.Stop(key);
        _apply.Remove(key);
        Note($"{link.File}: no longer linked; the file is kept", false);
        LinksChanged?.Invoke();
    }

    public void OpenFolder() { System.IO.Directory.CreateDirectory(Files.Folder); OS.ShellOpen(Files.Folder); }

    public override void _Process(double delta)
    {
        _since += delta;
        if (_since < Every || Files.Links.Count == 0) return;
        _since = 0;
        List<ScriptEvent> said;
        try { said = Files.Poll(DateTime.UtcNow); }
        catch (Exception e) { Diagnostics.Record("script files", e.Message); return; }
        bool ended = false;
        foreach (var e in said)
        {
            if (e.Kind == ScriptEventKind.Changed && _apply.TryGetValue(e.Link.Key, out var apply))
            {
                try { apply(e.Text.Trim().Length == 0 ? null : e.Text); }
                catch (Exception x) { Diagnostics.Record("script files", $"script file {e.Link.File}: {x.Message}"); }
            }
            if (e.Kind == ScriptEventKind.Ended) { _apply.Remove(e.Link.Key); ended = true; }
            Noted?.Invoke($"{e.Link.File}: {ScriptFiles.Words(e)}", e.Kind is ScriptEventKind.Refused or ScriptEventKind.NotTaken or ScriptEventKind.Unreadable);
        }
        if (ended) LinksChanged?.Invoke();
    }

    void Note(string text, bool bad)
    {
        Files.Note(text, DateTime.UtcNow);
        Noted?.Invoke(text, bad);
    }

    void OnConsole(List<ConsoleLine>? _) => Files.Console(_s.ConsoleLines, DefOf, NameOf);

    string? DefOf(string source) => _s.View.Find(Layers.Umpire, source)?.Def;

    static string NameOf(ConsoleLine l) => l.Source.Length == 0 ? "global" : l.Name ?? l.Source;
}

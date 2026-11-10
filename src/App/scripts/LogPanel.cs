using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class LogPanel : HudPart
{
    public readonly FloatPanel Panel;
    public readonly RichTextLabel Text;

    public LogPanel(HudContext c, FloatPanel panel) : base(c)
    {
        Panel = panel;
        Panel.Resizable = true;
        Panel.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Panel.CustomMinimumSize = Ctx.PanelSize("log", 420, 200);
        Panel.AddThemeStyleboxOverride("panel", Ui.Panel());
        Text = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Text.AddThemeColorOverride("default_color", Ui.Ink);
        Text.AddThemeConstantOverride("line_separation", 4);
        Text.MetaClicked += meta => { if (meta.AsString() is var m && m.StartsWith("folder:") && int.TryParse(m[7..], out var i) && i < Folders.Count) OS.ShellOpen(Folders[i]); };
        Panel.Body.AddChild(Text);
        Panel.AddToBar(SizeButton("A−", -1, "Smaller text"));
        Panel.AddToBar(SizeButton("A+", +1, "Larger text"));
        ShowSize();
    }

    Button SizeButton(string text, int step, string tip)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(24, 18), TooltipText = tip };
        b.AddThemeFontSizeOverride("font_size", 11);
        b.AddThemeColorOverride("font_color", Ui.Muted);
        b.Pressed += () =>
        {
            int now = Math.Clamp(Cfg.LogFontSize + step, KTable.Client.Prefs.Settings.LogFontMin, KTable.Client.Prefs.Settings.LogFontMax);
            if (now == Cfg.LogFontSize) return;
            Cfg.LogFontSize = now;
            Cfg.Save();
            ShowSize();
        };
        return b;
    }

    void ShowSize()
    {
        foreach (var kind in new[] { "normal", "bold", "italics", "bold_italics", "mono" }) Text.AddThemeFontSizeOverride(kind + "_font_size", Cfg.LogFontSize);
    }

    static readonly List<string> Folders = new();

    public static string FolderLink(string folder, string words)
    {
        int i = Folders.IndexOf(folder);
        if (i < 0) { Folders.Add(folder); i = Folders.Count - 1; }
        return $"[url=folder:{i}]{Fmt.Bb(words)}[/url]";
    }

    public const int MaxLines = 5000;

    public long Written { get; private set; }

    public void Append(string bb)
    {
        Written++;
        Text.AppendText($"[color=#a09c94]{DateTime.Now:HH:mm}[/color]  {bb}\n");
        while (Text.GetParagraphCount() > MaxLines + 1 && Text.RemoveParagraph(0)) { }
    }
}

sealed class Transcript : HudPart
{
    readonly Dictionary<long, PeerInfo> _knownPeers = new();

    public const string NameColor = "#e4c074";

    public Transcript(HudContext c) : base(c) { }

    public void RememberPeers()
    {
        foreach (var p in S.Peers) _knownPeers[p.PeerId] = p;
    }

    string SheetName(string id) => S.View.Sheets.FirstOrDefault(s => s.Id == id)?.Name ?? "a map sheet";

    string Who(Command c)
    {
        if (c.Actor == S.Me.PeerId) return "You";
        var p = S.Peers.FirstOrDefault(x => x.PeerId == c.Actor) ?? _knownPeers.GetValueOrDefault(c.Actor);
        return $"[color={NameColor}]{Fmt.Bb(p?.Name ?? (c.Actor == 1 ? "the host" : "someone"))}[/color]";
    }

    static string RoleWord(PeerInfo p) => Fmt.Role(p.Role);

    public void LogPeers()
    {
        var now = S.Peers.ToDictionary(p => p.PeerId);
        foreach (var (id, p) in now)
            if (!_knownPeers.ContainsKey(id) && id != S.Me.PeerId) Log($"[color={NameColor}]{Fmt.Bb(p.Name)}[/color] joined as {RoleWord(p)}");
        foreach (var (id, p) in _knownPeers)
            if (!now.ContainsKey(id) && id != S.Me.PeerId) Log($"[color={NameColor}]{Fmt.Bb(p.Name)}[/color] left");
        _knownPeers.Clear();
        foreach (var (id, p) in now) _knownPeers[id] = p;
    }

    public void Write(Command c)
    {
        switch (c)
        {
            case AdvanceClock:
                Log($"[b]Turn {S.View.Clock.Turn}[/b] · {Fmt.Clock(S.View.Clock)[(Fmt.Clock(S.View.Clock).IndexOf('·') + 1)..].Trim()}  ({Who(c)} ended the turn)");
                break;
            case SetClock:
                Log($"{Who(c)} set the clock: {Fmt.Clock(S.View.Clock)}");
                break;
            case RestoreTurn:
                Log($"[b]{Who(c)} put the table back to the end of turn {S.View.Clock.Turn}[/b] · {Fmt.Clock(S.View.Clock)}{(S.IsUmpire ? " · the turns after it left the history" : "")}");
                break;
            case LoadSave ls:
                Log($"[b]{Who(c)} loaded {Fmt.Bb(ls.Name)}[/b] · {Fmt.Clock(S.View.Clock)}");
                break;
            case AddSheet a:
                Log($"{Who(c)} added the map sheet [b]{Fmt.Bb(a.Sheet.Name)}[/b]");
                break;
            case RemoveSheet:
                Log($"{Who(c)} removed a map sheet");
                break;
            case CropSheet cr:
                Log($"{Who(c)} {(cr.Crop == null ? "uncropped" : "cropped")} a map sheet");
                break;
            case SetSheetRelief sr:
                Log($"{Who(c)} {(sr.Relief == null ? "took the relief off" : "shaped the relief of")} [b]{Fmt.Bb(SheetName(sr.Id))}[/b]");
                break;
            case SetSheetPicture sp:
                Log($"{Who(c)} laid a new picture on [b]{Fmt.Bb(SheetName(sp.Id))}[/b]");
                break;
            case SetSheetOpacity so:
                Log($"{Who(c)} set [b]{Fmt.Bb(SheetName(so.Id))}[/b] to {so.Opacity * 100:0}% opacity");
                break;
            case SetLighting:
                Log($"{Who(c)} changed the lighting");
                break;
            case SetWind sw:
                Log($"{Who(c)} set the wind: {Wind.Words(sw.Wind.Strength)}{(sw.Wind.Strength >= 0.25 ? $" from the {Wind.Point(sw.Wind.From)}" : "")}");
                break;
            case SetBriefing:
                Log($"{Who(c)} changed the briefing (Scenario properties → Briefing)");
                break;
            case SetPointerRule pr:
                Log($"{Who(c)} {(pr.AlwaysShown ? "showed every umpire's pointer for good" : "let umpires hide their pointers")}");
                break;
            case SetVisionRadius sv:
                Log($"{Who(c)} set the vision radius: {Fmt.Distance(sv.Radius, S.View.Calibration)}");
                break;
            case SetZone z:
                Log($"{Who(c)} set the zone [b]{Fmt.Bb(z.Zone.Name)}[/b] for {Fmt.Bb(z.Zone.Owner)}");
                break;
            case RemoveZone:
                Log($"{Who(c)} removed a zone");
                break;
            case AddPack ap:
                Log($"Pack [b]{Fmt.Bb(S.Store.Pack(ap.Id)?.Name ?? ap.Id)}[/b] is now in the game");
                break;
            case SetCalibration sc when c.Actor != S.Me.PeerId:
                Log($"{Who(c)} set the map scale");
                break;
            case SpawnPiece sp when c.Actor != S.Me.PeerId:
                Log($"{Who(c)} placed [b]{Fmt.Bb(S.Def(sp.Def)?.Name ?? sp.Def)}[/b]");
                break;
            case DeletePiece when c.Actor != S.Me.PeerId:
                Log($"{Who(c)} removed a piece");
                break;
            case Say s:
                Log($"{(s.Actor == 0 ? "" : Who(s) + ": ")}[color={Fmt.BbColor(s.Color, "#e8e4dc")}]{Fmt.Bb(s.Text)}[/color]");
                break;
        }
    }

    static string DiceWhat(RollDice d) => d.PieceIds.Count > 0 ? (d.Label.Length > 0 ? d.Label : $"{d.Count} dice") + " on the table" : $"{d.Count}d{d.Sides}" + (d.Label.Length > 0 ? " for " + d.Label : "");

    public void Rolled(RollDice rd)
    {
        LogDice(rd);
        Say($"{rd.ActorName} rolled {DiceWhat(rd)}: {string.Join(", ", rd.Results)}{(rd.Results.Count > 1 ? $"  (sum {rd.Results.Sum()})" : "")}");
    }

    void LogDice(RollDice d)
    {
        var who = d.ActorName;
        var res = string.Join(", ", d.Results);
        var sum = d.Results.Sum();
        Log($"[color={NameColor}]{Fmt.Bb(who)}[/color] rolled {Fmt.Bb(DiceWhat(d))}: [b]{res}[/b]{(d.Results.Count > 1 ? $" (sum {sum})" : "")}");
    }
}

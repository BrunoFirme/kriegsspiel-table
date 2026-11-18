using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public enum LogKind
{
    Talk,
    Dice,
    Table,
    World,
    Speech,
    Hotline,
    Notes,
}

sealed record LogLine(LogKind Kind, string Bb, DateTime At, long GameSeconds, int Turn, Vec2? Pos = null, string? Who = null, string? Piece = null);

sealed class LogPanel : HudPart
{
    public const string Title = "Chat log";
    public const int MaxLines = 5000;
    const string StampInk = "#a09c94", Everyone = "", Umpires = "u:";
    const float WideBy = 220;

    static readonly (LogKind Kind, string Name, string Tip)[] Kinds =
    {
        (LogKind.Talk, "Talk", "What people wrote"), (LogKind.Speech, "Speech", "What pieces said within earshot"), (LogKind.Dice, "Dice", "Rolls"),
        (LogKind.Table, "Table", "Who changed what on the table"), (LogKind.World, "World", "Turns, timers, who came and went"),
        (LogKind.Hotline, "Hotline", "Lines to the one who wears a piece"), (LogKind.Notes, "Notes", "Umpires' notes, pinned to a place"),
    };

    const string SpeakAs = "speak:", WhisperTo = "whisper:", HotlineTo = "hot:", NoteHere = "note";
    const char Between = '\n';
    static readonly string[] Loudly = { "Whisper", "Say", "Shout" };

    public Func<(string Id, string Name)?>? Speaker;
    public Func<(string Id, string Name)?>? Listener;
    public Func<Vec2>? Looking;
    public Func<double>? Reach;
    public Action<Vec2>? Fly;
    public Func<IReadOnlySet<string>>? Picked;
    const string SpotLink = "at:";
    bool _near, _heard;
    readonly HashSet<(long At, string Kind, string? Who, string Text)> _taken = new();
    readonly long _openedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    readonly LogPickers _pickers = new();

    public readonly FloatPanel Panel;
    public readonly RichTextLabel Text;
    public event Action? UnreadChanged;

    readonly List<LogLine> _lines = new();
    readonly HashSet<LogKind> _shown = new(Kinds.Select(k => k.Kind));
    readonly FindBar _find;
    readonly HFlowContainer _filters = new();
    readonly HBoxContainer _foot = new();
    readonly OptionButton _to = new() { FitToLongestItem = false, TooltipText = "Who reads the line" };
    readonly LineEdit _write = new() { MaxLength = KTable.Core.Protocol.Say.MostLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    readonly List<string> _targets = new();
    readonly Button _state;
    int _lastTurn = -1;
    string _toNow = Everyone;

    public long Written { get; private set; }

    public int Unread { get; private set; }

    public LogPanel(HudContext c, FloatPanel panel) : base(c)
    {
        Panel = panel;
        Panel.Resizable = true;
        Panel.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Panel.CustomMinimumSize = Ctx.PanelSize("log", 420, 200);
        Panel.AddThemeStyleboxOverride("panel", Ui.Panel());
        Text = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Text.AddThemeColorOverride("default_color", Ui.Ink);
        Text.AddThemeConstantOverride("line_separation", Ui.Px(4));
        Text.MetaClicked += meta => Followed(meta.AsString());
        _find = FindBar.ForLabel(Text, Bb);
        _filters.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        _filters.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        foreach (var (kind, name, tip) in Kinds.Where(k => S.IsUmpire || k.Kind is not (LogKind.Hotline or LogKind.Notes))) _filters.AddChild(KindChip(kind, name, tip));
        _pickers.Changed += Restream;
        _filters.AddChild(_pickers.Who);
        _filters.AddChild(_pickers.When);
        var near = new Button { Text = "Near view", ToggleMode = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Only what happened where the view looks" };
        near.Toggled += on =>
        {
            _near = on;
            Render();
        };
        _filters.AddChild(near);
        Panel.Body.AddChild(_filters);
        Panel.Body.AddChild(_find);
        Panel.Body.AddChild(Text);
        BuildFoot();
        _state = BarButton("", "", () =>
        {
            Cfg.LogWide = !Cfg.LogWide;
            Cfg.Save();
            ShowState(resize: true);
        });
        Panel.AddToBar(_state);
        Panel.AddToBar(SizeButton("A−", -1, "Smaller text"));
        Panel.AddToBar(SizeButton("A+", +1, "Larger text"));
        Panel.VisibilityChanged += () => { if (Panel.Visible) Read(); };
        ShowSize();
        ShowState(resize: false);
    }

    void Followed(string link)
    {
        if (link.StartsWith("folder:", StringComparison.Ordinal) && int.TryParse(link[7..], out var i) && i < Folders.Count) OS.ShellOpen(Folders[i]);
        if (!link.StartsWith(SpotLink, StringComparison.Ordinal) || link[SpotLink.Length..].Split(';') is not [var x, var z]) return;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        if (double.TryParse(x, System.Globalization.NumberStyles.Float, c, out double px)
            && double.TryParse(z, System.Globalization.NumberStyles.Float, c, out double pz)) Fly?.Invoke(new Vec2(px, pz));
    }

    Button KindChip(LogKind kind, string name, string tip)
    {
        var chip = new Button { Text = name, ToggleMode = true, ButtonPressed = true, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
        chip.Toggled += on =>
        {
            if (on) _shown.Add(kind);
            else _shown.Remove(kind);
            Render();
        };
        return chip;
    }

    void BuildFoot()
    {
        _foot.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        _to.ItemSelected += at => _toNow = _targets[(int)at];
        _write.TextSubmitted += Send;
        _write.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
            _write.ReleaseFocus();
            _write.AcceptEvent();
        };
        _foot.AddChild(_to);
        _foot.AddChild(_write);
        Panel.Body.AddChild(_foot);
    }

    bool MayWrite => S.IsUmpire || S.Me.Role == Role.Player && (S.View.RightsOf(S.Me.Name).Write || S.View.RightsOf(S.Me.Name).Speak);

    public void ShowPeople()
    {
        _foot.Visible = MayWrite;
        _targets.Clear();
        _to.Clear();
        void Offer(string target, string name)
        {
            _targets.Add(target);
            _to.AddItem(name);
        }
        Offer(Everyone, "Everyone");
        Offer(Umpires, "Umpires");
        foreach (var p in S.Peers.Where(p => p.PeerId != S.Me.PeerId).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) Offer($"p:{p.PeerId}", p.Name);
        if (Speaker?.Invoke() is var (id, piece) && (S.IsUmpire || S.View.RightsOf(S.Me.Name).Speak))
        {
            for (int loud = 0; loud < Loudly.Length; loud++) Offer($"{SpeakAs}{loud}:{id}", $"{Loudly[loud]} as {piece}");
            if (Listener?.Invoke() is var (toId, toName)) Offer($"{WhisperTo}{id}{Between}{toId}", $"Whisper to {toName}");
            if (S.IsUmpire) Offer(HotlineTo + id, $"Hotline to {piece}");
        }
        if (S.IsUmpire) Offer(NoteHere, "Note here");
        int at = _targets.IndexOf(_toNow);
        if (at < 0) (_toNow, at) = (Everyone, 0);
        _to.Selected = at;
        if (_pickers.ByPickedPiece && Cfg.LogWide) Restream();
    }

    void Send(string text)
    {
        if (text.Trim().Length == 0) return;
        if (_toNow.StartsWith(SpeakAs, StringComparison.Ordinal) && _toNow.Split(':', 3) is [_, var loud, var speaker])
            S.Do(new Speak { Id = speaker, Text = text.Trim(), Loud = int.Parse(loud, System.Globalization.CultureInfo.InvariantCulture) });
        else if (_toNow.StartsWith(WhisperTo, StringComparison.Ordinal) && _toNow[WhisperTo.Length..].Split(Between) is [var from, var to])
            S.Do(new Speak { Id = from, Text = text.Trim(), Loud = Earshot.Whisper, To = to });
        else if (_toNow.StartsWith(HotlineTo, StringComparison.Ordinal)) S.Do(new Hotline { Id = _toNow[HotlineTo.Length..], Text = text.Trim() });
        else if (_toNow == NoteHere) S.Do(new Note { Text = text.Trim(), Pos = Looking?.Invoke() ?? default });
        else S.Do(new Say { Text = text.Trim(), To = _toNow.Length == 0 ? null : _toNow });
        _write.Text = "";
    }

    public void Write()
    {
        if (!Panel.Visible || !_foot.Visible) return;
        _write.GrabFocus();
    }

    Button BarButton(string text, string tip, Action press)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(24, 18), TooltipText = tip };
        b.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        b.AddThemeColorOverride("font_color", Ui.Muted);
        b.Pressed += press;
        return b;
    }

    Button SizeButton(string text, int step, string tip) => BarButton(text, tip, () =>
    {
        int now = Math.Clamp(Cfg.LogFontSize + step, KTable.Client.Prefs.Settings.LogFontMin, KTable.Client.Prefs.Settings.LogFontMax);
        if (now == Cfg.LogFontSize) return;
        Cfg.LogFontSize = now;
        Cfg.Save();
        ShowSize();
    });

    void ShowSize()
    {
        foreach (var kind in new[] { "normal", "bold", "italics", "bold_italics", "mono" }) Text.AddThemeFontSizeOverride(kind + "_font_size", Ui.Px(Cfg.LogFontSize));
    }

    void ShowState(bool resize)
    {
        bool wide = Cfg.LogWide;
        _filters.Visible = wide;
        _state.Text = wide ? "◂" : "▸";
        _state.TooltipText = wide ? "Show the chat alone" : "Open the whole log: kinds, who, when and game time";
        if (resize) Panel.Size = new Vector2(Math.Max(Panel.CustomMinimumSize.X, Panel.Size.X + (wide ? Ui.Px(WideBy) : -Ui.Px(WideBy))), Panel.Size.Y);
        Render();
    }

    static readonly List<string> Folders = new();

    public static string FolderLink(string folder, string words)
    {
        int i = Folders.IndexOf(folder);
        if (i < 0)
        {
            Folders.Add(folder);
            i = Folders.Count - 1;
        }
        return $"[url=folder:{i}]{Fmt.Bb(words)}[/url]";
    }

    bool Shown(LogLine line)
    {
        if (!Cfg.LogWide) return true;
        if (!_shown.Contains(line.Kind) || !_pickers.Passes(line, Picked?.Invoke(), S.View.Clock.Turn)) return false;
        return !_near || line.Pos is { } at && Looking != null && Reach != null && (at - Looking()).Length <= Reach();
    }

    string? TurnName(int turn) => turn == S.View.Clock.Turn ? S.View.Clock.Name : S.Turns.LastOrDefault(t => t.Turn == turn)?.Name;

    string Heading(int turn)
    {
        string? name = TurnName(turn);
        return $"[color={StampInk}]—  Turn {turn}{(name != null ? " · " + Fmt.Bb(name) : "")}  —[/color]\n";
    }

    string LineBb(LogLine line, ref int lastTurn)
    {
        string heading = line.Turn != lastTurn && lastTurn >= 0 ? Heading(line.Turn) : "";
        lastTurn = line.Turn;
        string stamp = Cfg.LogWide ? S.View.Clock.Cal.TimeOfDay(line.GameSeconds) : line.At.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (line.Pos is { } at) stamp = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[url={SpotLink}{at.X:R};{at.Z:R}]{stamp}[/url]");
        return $"{heading}[color={StampInk}]{stamp}[/color]  {line.Bb}\n";
    }

    string Bb()
    {
        var sb = new System.Text.StringBuilder();
        int lastTurn = -1;
        foreach (var line in _lines.Where(Shown)) sb.Append(LineBb(line, ref lastTurn));
        _lastTurn = lastTurn;
        return sb.ToString();
    }

    void Render()
    {
        _pickers.Forget();
        foreach (var line in _lines) _pickers.Note(line);
        _pickers.Offer(TurnName);
        Restream();
    }

    void Restream()
    {
        if (_find.Terms.Count > 0) _find.Refresh();
        else Text.Text = Bb();
    }

    void Read()
    {
        if (Unread == 0) return;
        Unread = 0;
        UnreadChanged?.Invoke();
    }

    internal static string SaidBy(string name, int loud, string? to) =>
        $"[color={Transcript.NameColor}]{Fmt.Bb(name)}[/color] {Earshot.Verb(loud)}" + (to != null ? $" to [color={Transcript.NameColor}]{Fmt.Bb(to)}[/color]" : "");

    static string EntryBb(RecordEntry e)
    {
        string who = e.Who != null ? $"[color={Transcript.NameColor}]{Fmt.Bb(e.Who)}[/color]" : "";
        string piece = $"[color={Transcript.NameColor}]{Fmt.Bb(e.PieceName ?? "a piece")}[/color]";
        if (e.Kind == RecordEntry.Speech) return $"{SaidBy(e.PieceName ?? "a piece", e.Loud, e.ToPieceName)}: {Fmt.Bb(e.Text)}";
        if (e.Kind == RecordEntry.Direct) return $"{who} → {piece} (hotline): {Fmt.Bb(e.Text)}";
        if (e.Kind == RecordEntry.Noted) return $"{(who.Length > 0 ? who + " noted: " : "")}{Fmt.Bb(e.Text)}";
        if (e.Kind != RecordEntry.Talk) return who.Length > 0 ? $"{who} {Fmt.Bb(e.Text)}" : Fmt.Bb(e.Text);
        string whom = e.Reach == RecordEntry.ToUmpires ? " → Umpires" : e.ToName is { } name ? " → " + Fmt.Bb(name) : "";
        return $"{(who.Length > 0 ? who + whom + ": " : "")}[color={Fmt.BbColor(e.Color, "#e8e4dc")}]{Fmt.Bb(e.Text)}[/color]";
    }

    static LogKind KindOf(string kind) => kind switch
    {
        RecordEntry.Talk => LogKind.Talk,
        RecordEntry.Dice => LogKind.Dice,
        RecordEntry.Speech => LogKind.Speech,
        RecordEntry.Direct => LogKind.Hotline,
        RecordEntry.Noted => LogKind.Notes,
        RecordEntry.Table => LogKind.Table,
        _ => LogKind.World,
    };

    public void Earlier(IReadOnlyList<RecordEntry> entries)
    {
        bool first = !_heard;
        _heard = true;
        var fresh = entries.Where(e => (first || e.At < _openedAt) && _taken.Add((e.At, e.Kind, e.Who, e.Text))).ToList();
        if (fresh.Count == 0) return;
        var earlier = fresh.Select(e => new LogLine(KindOf(e.Kind), EntryBb(e),
            DateTimeOffset.FromUnixTimeMilliseconds(e.At).LocalDateTime, e.Seconds, e.Turn, e.Pos, e.Who, e.Piece));
        _lines.InsertRange(0, earlier);
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        Render();
    }

    public void Append(string bb, LogKind kind = LogKind.Table) => Append(bb, kind, null);

    public void Append(string bb, LogKind kind, Vec2? at, string? who = null, string? piece = null)
    {
        Written++;
        var line = new LogLine(kind, bb, DateTime.Now, S.View.Clock.Now, S.View.Clock.Turn, at, who, piece);
        _lines.Add(line);
        bool trimmed = _lines.Count > MaxLines;
        if (trimmed) _lines.RemoveAt(0);
        if (!Panel.Visible && kind is LogKind.Talk or LogKind.Speech or LogKind.Hotline)
        {
            Unread++;
            UnreadChanged?.Invoke();
        }
        if (_pickers.Note(line) || trimmed)
        {
            Render();
            return;
        }
        if (!Shown(line)) return;
        if (_find.Terms.Count > 0) Render();
        else Text.AppendText(LineBb(line, ref _lastTurn));
    }
}

sealed class Transcript : HudPart
{
    readonly Dictionary<long, PeerInfo> _knownPeers = new();

    public const string NameColor = "#e4c074";

    public Transcript(HudContext c) : base(c) { }

    public LogPanel? Out;

    const string Me = "\u0001";

    public Action<string>? Kept;

    static string Plain(string bb) => System.Text.RegularExpressions.Regex.Replace(bb, @"\[/?(b|i|u|color|url)(=[^\]]*)?\]", "").Replace("[lb]", "[");

    new void Log(string bb) => Tell(bb, LogKind.Table);

    void Tell(string bb, LogKind kind, Vec2? at = null)
    {
        string shown = bb.Replace(Me, "You");
        if (Out != null) Out.Append(shown, kind, at, _by, _about);
        else base.Log(shown);
        if (kind == LogKind.Table) Kept?.Invoke(Plain(bb.Replace(Me, S.Me.Name)));
    }

    string Whom(Say s) => s.To switch
    {
        null => "",
        "u:" => " → Umpires",
        _ when s.To == $"p:{S.Me.PeerId}" => " → you",
        _ => " → " + Fmt.Bb(S.Peers.FirstOrDefault(p => $"p:{p.PeerId}" == s.To)?.Name ?? "someone"),
    };

    public void RememberPeers()
    {
        foreach (var p in S.Peers) _knownPeers[p.PeerId] = p;
    }

    string SheetName(string id) => S.View.Sheets.FirstOrDefault(s => s.Id == id)?.Name ?? "a map sheet";

    string Who(Command c)
    {
        if (c.Actor == S.Me.PeerId) return Me;
        var p = S.Peers.FirstOrDefault(x => x.PeerId == c.Actor) ?? _knownPeers.GetValueOrDefault(c.Actor);
        return $"[color={NameColor}]{Fmt.Bb(p?.Name ?? (c.Actor == 1 ? "the host" : "someone"))}[/color]";
    }

    static string RoleWord(PeerInfo p) => Fmt.Role(p.Role);

    public void LogPeers()
    {
        var now = S.Peers.ToDictionary(p => p.PeerId);
        foreach (var (id, p) in now)
            if (!_knownPeers.ContainsKey(id) && id != S.Me.PeerId) Tell($"[color={NameColor}]{Fmt.Bb(p.Name)}[/color] joined as {RoleWord(p)}", LogKind.World);
        foreach (var (id, p) in _knownPeers)
            if (!now.ContainsKey(id) && id != S.Me.PeerId) Tell($"[color={NameColor}]{Fmt.Bb(p.Name)}[/color] left", LogKind.World);
        _knownPeers.Clear();
        foreach (var (id, p) in now) _knownPeers[id] = p;
    }

    string Now => S.View.Clock.Cal.Words(S.View.Clock.Now);

    void Spoken(Command c)
    {
        switch (c)
        {
            case Speak sp:
                Tell($"{LogPanel.SaidBy(sp.Name ?? "A piece", sp.Loud, sp.ToName)}: {Fmt.Bb(sp.Text)}", LogKind.Speech, sp.Pos);
                break;
            case Note n:
                Tell($"{Who(n)} noted: {Fmt.Bb(n.Text)}", LogKind.Notes, n.Pos);
                break;
            case Hotline h:
                Tell($"{Who(h)} → [color={NameColor}]{Fmt.Bb(h.Name ?? "a piece")}[/color] (hotline): {Fmt.Bb(h.Text)}", LogKind.Hotline);
                if (h.Actor != S.Me.PeerId) Say($"{h.Name}: {Fmt.OneLine(h.Text)}");
                break;
        }
    }

    string? _by, _about;

    string? By(Command c) => c switch
    {
        Say { By: { } said } => said,
        Speak { By: { } spoke } => spoke,
        Hotline { By: { } told } => told,
        Note { By: { } noted } => noted,
        _ when c.Actor == S.Me.PeerId => S.Me.Name,
        _ => (S.Peers.FirstOrDefault(x => x.PeerId == c.Actor) ?? _knownPeers.GetValueOrDefault(c.Actor))?.Name,
    };

    static string? About(Command c) => c switch
    {
        Speak sp => sp.Id,
        Hotline h => h.Id,
        LayerCommand lc => KTable.Client.TouchedPiece.IdOf(lc),
        _ => null,
    };

    public void Write(Command c)
    {
        (_by, _about) = (By(c), About(c));
        if (c.Actor == KTable.Server.GameServer.NoOne) return;
        switch (c)
        {
            case AdvanceClock { Back: true }:
                Log($"{Who(c)} took the turn back: {Fmt.Clock(S.View.Clock)}");
                break;
            case AdvanceClock:
                Tell($"[b]Turn {S.View.Clock.Turn}[/b] · {Fmt.Clock(S.View.Clock)[(Fmt.Clock(S.View.Clock).IndexOf('·') + 1)..].Trim()}  ({Who(c)} ended the turn)", LogKind.World);
                break;
            case SetClock { Place: null }:
                Log($"{Who(c)} set the clock: {Fmt.Clock(S.View.Clock)}");
                break;
            case StepClock { Place: null } st:
                Log($"{Who(c)} moved the clock {(st.Seconds < 0 ? "back" : "on")} {GameClock.StepText((int)Math.Min(Math.Abs(st.Seconds), int.MaxValue))}: {Now}");
                break;
            case RunClock { Place: null } rc:
                Log(rc.Rate > 0 ? $"{Who(c)} let the clock run {ClockWords.Rate(rc.Rate)}" : $"{Who(c)} paused the clock: {Now}");
                break;
            case SetTimer { Place: null } st:
                Log($"{Who(c)} set the timer [b]{Fmt.Bb(st.Name)}[/b] for {S.View.Clock.Cal.Words(st.Due)}");
                break;
            case ClearTimer { Place: null } ct:
                Log($"{Who(c)} removed the timer [b]{Fmt.Bb(ct.Name)}[/b]");
                break;
            case SetCalendar:
                Log($"{Who(c)} set the calendar: {S.View.Clock.Cal.Name}");
                break;
            case Speak or Note or Hotline:
                Spoken(c);
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
            case SetPointerRule { ToPlayers: { } toPlayers }:
                Log($"{Who(c)} {(toPlayers ? "showed the umpires' pointers to the players" : "hid the umpires' pointers from the players")}");
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
                Log(sc.North is { } north ? FormattableString.Invariant($"{Who(c)} turned north to {north:0}° from the top of the map") : $"{Who(c)} set the map scale");
                break;
            case SpawnPiece sp when c.Actor != S.Me.PeerId:
                Log($"{Who(c)} placed [b]{Fmt.Bb(S.Def(sp.Def)?.Name ?? sp.Def)}[/b]");
                break;
            case DeletePiece when c.Actor != S.Me.PeerId:
                Log($"{Who(c)} removed a piece");
                break;
            case Say s:
                Tell($"{(s.Actor == 0 ? "" : Who(s) + Whom(s) + ": ")}[color={Fmt.BbColor(s.Color, "#e8e4dc")}]{Fmt.Bb(s.Text)}[/color]", LogKind.Talk);
                if (Out is { Panel.Visible: false } && s.Actor != S.Me.PeerId && s.Actor != 0) Say($"{S.Peers.FirstOrDefault(p => p.PeerId == s.Actor)?.Name ?? "Someone"}: {Fmt.OneLine(s.Text)}");
                break;
        }
    }

    static string DiceWhat(RollDice d) => d.PieceIds.Count > 0 ? (d.Label.Length > 0 ? d.Label : $"{d.Count} dice") + " on the table" : $"{d.Count}d{d.Sides}" + (d.Label.Length > 0 ? " for " + d.Label : "");

    public void Rolled(RollDice rd)
    {
        LogDice(rd);
        Say($"{rd.ActorName} rolled {DiceWhat(rd)}: {string.Join(", ", rd.Results)}{(rd.Results.Count > 1 ? $"  (sum {rd.Results.Sum()})" : "")}");
    }

    string Secretly(RollDice d) => d.Layer == null && !d.Open && S.Peers.Any(p => p.Name == d.ActorName && p.Role == KTable.Core.Model.Role.Player) ? " · secret" : "";

    void LogDice(RollDice d)
    {
        var who = d.ActorName;
        var res = string.Join(", ", d.Results);
        var sum = d.Results.Sum();
        Tell($"[color={NameColor}]{Fmt.Bb(who)}[/color] rolled {Fmt.Bb(DiceWhat(d))}: [b]{res}[/b]{(d.Results.Count > 1 ? $" (sum {sum})" : "")}{Secretly(d)}", LogKind.Dice);
    }
}

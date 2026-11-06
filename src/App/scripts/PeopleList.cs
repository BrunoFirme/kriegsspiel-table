using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PeopleList : HudPart
{
    const int DotSize = 10;
    const int ListWidth = 320;
    const int PillPadding = 20, PillHeight = 24;
    const float HiddenPointerAlpha = 0.35f;

    public const string PointerShownByHost = "Every umpire's pointer is shown at this table (Scenario properties)";

    Button _pill = null!;
    HBoxContainer _dots = null!, _row = null!;
    Label _more = null!, _door = null!, _status = null!, _chevron = null!;
    PopupPanel? _pop;
    VBoxContainer? _list;
    string _netStatus = "";

    public PeopleList(HudContext c) : base(c) { }

    public Button Button => _pill;
    public string StatusText => _netStatus;

    bool Others => S.IsUmpire && (S.Peers.Count > 1 || (S.IsLead && S.Waiting.Count > 0));
    bool OtherUmpires => S.Peers.Any(p => Visibility.IsUmpire(p.Role) && p.PeerId != S.Me.PeerId);
    bool Opens => S.IsUmpire && !S.Reviewing;
    const string AloneWord = "Invite";
    bool PointerShared => Cfg.ShareCursor || S.View.PointersAlwaysShown;

    public Button Pill()
    {
        _pill = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        var row = _row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Ui.Gap.Row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        row.OffsetLeft = PillPadding / 2;
        row.OffsetRight = -PillPadding / 2;
        _dots = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _dots.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        row.AddChild(_dots);
        _more = Small(Ui.Muted);
        row.AddChild(_more);
        _door = Small(Ui.AccentInk);
        row.AddChild(_door);
        _status = Small(Ui.Muted);
        row.AddChild(_status);
        _chevron = Small(Ui.Faint);
        _chevron.Text = "▾";
        row.AddChild(_chevron);
        _pill.AddChild(row);
        _pill.Pressed += Open;
        _pill.MouseEntered += () => _status.AddThemeColorOverride("font_color", Opens ? Ui.Ink : Ui.Muted);
        _pill.MouseExited += () => _status.AddThemeColorOverride("font_color", Ui.Muted);
        return _pill;
    }

    static Label Small(Color color)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    static TextureRect Dot(Color? hue) => new()
    {
        Texture = Ui.Dot, CustomMinimumSize = new Vector2(DotSize, DotSize), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        Modulate = hue ?? Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    IEnumerable<PeerInfo> Seated() =>
        S.Peers.OrderBy(p => p.Role switch { Role.HostUmpire => 0, Role.LeadUmpire => 1, Role.Umpire => 2, _ => 3 }).ThenBy(p => p.Name);

    Color Hue(PeerInfo p)
    {
        var hue = UmpireHue.Of(S, p) ?? UmpireHue.Unassigned;
        return p.PeerId == S.Me.PeerId && !PointerShared ? hue with { A = HiddenPointerAlpha } : hue;
    }

    public void SetStatus(string text) { _netStatus = text; Sync(); }

    public void Sync()
    {
        if (_pill == null || !GodotObject.IsInstanceValid(_pill)) return;
        var others = Others;
        _pill.Visible = others || _netStatus.Length > 0;
        if (!_pill.Visible) return;
        var opens = Opens;
        _pill.MouseFilter = opens ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        foreach (var st in new[] { "normal", "pressed", "hover", "disabled" }) _pill.AddThemeStyleboxOverride(st, Ui.Pill());
        if (opens) _pill.AddThemeStyleboxOverride("hover", Ui.Pill(0.12f));
        _chevron.Visible = opens;
        _status.Text = others || !S.IsHost ? _netStatus : AloneWord;
        _status.Visible = _netStatus.Length > 0;
        foreach (var c in _dots.GetChildren()) { _dots.RemoveChild(c); c.QueueFree(); }
        _dots.Visible = others;
        if (others) foreach (var p in Seated().Where(p => Visibility.IsUmpire(p.Role))) _dots.AddChild(Dot(Hue(p)));
        var watching = S.Peers.Count(p => !Visibility.IsUmpire(p.Role));
        _more.Visible = others && watching > 0;
        _more.Text = $"+{watching}";
        var knocking = S.IsLead ? S.Waiting.Count : 0;
        _door.Visible = knocking > 0;
        _door.Text = $"{knocking} at the door";
        _pill.TooltipText = Tooltip(others, watching);
        Callable.From(FitPill).CallDeferred();
        if (_pop != null && GodotObject.IsInstanceValid(_pop) && _pop.Visible) Fill();
    }

    string Tooltip(bool others, int watching)
    {
        if (!Opens) return "";
        var who = others ? $"{S.Peers.Count} at the table{(watching > 0 ? $", {watching} watching" : "")}" : "Invite people";
        return OtherUmpires && !PointerShared ? who + "\nYour pointer is hidden from the other umpires" : who;
    }

    void FitPill()
    {
        if (GodotObject.IsInstanceValid(_pill)) _pill.CustomMinimumSize = new Vector2(_row.GetCombinedMinimumSize().X + PillPadding, PillHeight);
    }

    public void Open()
    {
        if (!Opens) return;
        if (_pop == null || !GodotObject.IsInstanceValid(_pop))
        {
            _pop = new PopupPanel();
            _list = new VBoxContainer { CustomMinimumSize = new Vector2(ListWidth, 0) };
            _list.AddThemeConstantOverride("separation", Ui.Gap.Row);
            _pop.AddChild(_list);
            HudNode.AddChild(_pop);
        }
        Fill();
        var at = _pill.GetGlobalRect();
        _pop.Popup(new Rect2I((int)(at.End.X - ListWidth - 20), (int)(at.End.Y + 4), 0, 0));
    }

    void Fill()
    {
        foreach (var c in _list!.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        if (S.IsLead && S.Waiting.Count > 0)
        {
            _list.AddChild(Header("At the door"));
            foreach (var w in S.Waiting)
            {
                _list.AddChild(new Label { Text = w.Name });
                var row = new HFlowContainer();
                foreach (var b in AdmitButtons(S, N, w)) row.AddChild(b);
                _list.AddChild(row);
            }
        }
        if (Others)
        {
            _list.AddChild(Header("At the table"));
            foreach (var p in Seated()) _list.AddChild(Row(p));
            _list.AddChild(new HSeparator());
        }
        _list.AddChild(Btn("Invite…", () => { _pop!.Hide(); InviteDialog.Open(Ctx); }, "The address and code others join with"));
        if (OtherUmpires)
        {
            var forced = S.View.PointersAlwaysShown;
            var share = new CheckBox { Text = "Share my pointer", ButtonPressed = PointerShared, Disabled = forced, TooltipText = forced ? PointerShownByHost : "The other umpires see where you point; observers never do" };
            share.Toggled += on =>
            {
                Cfg.ShareCursor = on;
                Cfg.Save();
                Sync();
            };
            _list.AddChild(share);
            var show = new CheckBox { Text = "Show the others' pointers", ButtonPressed = Cfg.ShowCursors };
            show.Toggled += on =>
            {
                Cfg.ShowCursors = on;
                Cfg.Save();
                T.ShowCursors = on;
            };
            _list.AddChild(show);
        }
        _pop!.ResetSize();
    }

    Control Row(PeerInfo p)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(Dot(Visibility.IsUmpire(p.Role) ? Hue(p) : null));
        row.AddChild(new Label { Text = p.PeerId == S.Me.PeerId ? $"{p.Name} (you)" : p.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        var role = new Label { Text = RoleWord(p.Role), Modulate = Ui.Muted };
        role.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(role);
        if (PeerActions(p) is { } mb) row.AddChild(mb);
        return row;
    }

    static string RoleWord(Role r) => r switch { Role.HostUmpire => "host", Role.LeadUmpire => "lead umpire", Role.Umpire => "umpire", _ => "observer" };

    public static IEnumerable<Button> AdmitButtons(Session S, Net N, PeerInfo w)
    {
        var id = w.PeerId;
        if (S.IsHost) yield return Btn("Lead", () => N.Door(new Admit { PeerId = id, Role = Role.LeadUmpire }), "Let them in as a lead umpire: they also let people in, set roles and draw zones");
        yield return Ui.Primary("Umpire", () => N.Door(new Admit { PeerId = id, Role = Role.Umpire }), "Let them in as an umpire: they change the table");
        yield return Btn("Observer", () => N.Door(new Admit { PeerId = id, Role = Role.Observer }), "Let them in to watch");
        yield return Btn("Refuse", () => N.Door(new Refuse { PeerId = id }), "Turn them away");
    }

    MenuButton? PeerActions(PeerInfo p)
    {
        if (!S.IsLead || p.PeerId == S.Me.PeerId || p.Role == Role.HostUmpire) return null;
        if (!S.IsHost && p.Role == Role.LeadUmpire) return null;
        var mb = new MenuButton { Text = "⋯", Flat = true, FocusMode = Control.FocusModeEnum.None };
        var pm = mb.GetPopup();
        var id = p.PeerId;
        if (S.IsHost && p.Role != Role.LeadUmpire) pm.AddItem("Make lead umpire", 1);
        if (S.IsHost && p.Role == Role.LeadUmpire) pm.AddItem("Make umpire (no longer lead)", 2);
        if (p.Role != Role.Umpire && p.Role != Role.LeadUmpire) pm.AddItem("Make umpire", 2);
        if (p.Role != Role.Observer) pm.AddItem("Make observer", 3);
        pm.AddSeparator();
        pm.AddItem("Put out of the table", 9);
        pm.IdPressed += which =>
        {
            switch (which)
            {
                case 1:
                    N.Door(new SetRole { PeerId = id, Role = Role.LeadUmpire });
                    break;
                case 2:
                    N.Door(new SetRole { PeerId = id, Role = Role.Umpire });
                    break;
                case 3:
                    N.Door(new SetRole { PeerId = id, Role = Role.Observer });
                    break;
                case 9:
                    Ui.Confirm(HudNode, $"Put {p.Name} out", "Put out", () => N.Door(new Revoke { PeerId = id }), "They leave the table now and would have to knock again to come back.");
                    break;
            }
        };
        return mb;
    }
}

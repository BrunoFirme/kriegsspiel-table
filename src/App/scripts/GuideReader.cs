using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client.Docs;
using KTable.Server.Scripting;

namespace KTable.App;

public static class GuideReader
{
    static Reader? _open;
    static string _last = GuideLibrary.Docs[0].File;

    public static bool Show(Node parent, string? file = null, string? anchor = null, string? find = null)
    {
        file ??= _last;
        if (_open != null && GodotObject.IsInstanceValid(_open.Window))
        {
            _open.Window.GrabFocus();
            _open.Go(file, anchor, true, find);
            return true;
        }
        _open = new Reader(parent);
        _open.Window.TreeExiting += () => _open = null;
        _open.Go(file, anchor, false, find);
        return true;
    }

    public static bool ShowEntry(Node parent, ApiEntry? e) => Show(parent, ApiDoc.File, e == null ? null : ApiDoc.AnchorOf(e));

    public static void Search(string query) => _open?.SearchFor(query);

    public static void Spec(Node parent, string spec)
    {
        var find = spec.Contains('~') ? spec[(spec.IndexOf('~') + 1)..] : null;
        if (find != null) spec = spec[..spec.IndexOf('~')];
        var anchor = spec.Contains('#') ? spec[(spec.IndexOf('#') + 1)..] : null;
        if (anchor != null) spec = spec[..spec.IndexOf('#')];
        if (spec.StartsWith('?')) { Show(parent); Search(spec[1..]); }
        else Show(parent, spec.Length > 0 ? spec : null, anchor, find);
    }

    internal static void Remember(string file) => _last = file;

    sealed class Reader
    {
        public readonly Window Window;
        readonly LineEdit _search = new() { PlaceholderText = "Search help  (Ctrl+K)", ClearButtonEnabled = true };
        readonly VBoxContainer _nav = new();
        readonly Tree _toc = new() { HideRoot = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Single };
        readonly Button _back, _forward;
        readonly Label _crumb = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, Modulate = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
        readonly HFlowContainer _links = new();
        readonly Label _linksHead = new() { Text = Ui.Spaced("This page as a file") };
        readonly FindBar _find = new();
        readonly ScrollContainer _scroll =
            new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        readonly MarginContainer _pad = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        readonly VBoxContainer _column = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        readonly Label _status = new() { Modulate = Ui.Muted, Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        readonly Dictionary<string, Button> _navButtons = new();
        readonly List<(string file, int scroll)> _history = new(), _ahead = new();

        string _file = "";
        List<RenderedBlock> _blocks = new();
        readonly List<(int block, int para)> _hits = new();
        bool _tocQuiet;
        bool _searching;

        public Reader(Node parent)
        {
            Window = new Window { Title = "Help", Transient = true, Exclusive = false, WrapControls = false, MinSize = new Vector2I(760, 420) };
            Window.CloseRequested += () => Window.QueueFree();
            var bg = new PanelContainer();
            bg.AddThemeStyleboxOverride("panel", Ui.WindowFill(Ui.Surface));
            bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            Window.AddChild(bg);
            var split = new HBoxContainer();
            split.AddThemeConstantOverride("separation", 0);
            bg.AddChild(split);

            var side = new PanelContainer { CustomMinimumSize = new Vector2(264, 0) };
            side.AddThemeStyleboxOverride("panel",
                new StyleBoxFlat { BgColor = Ui.Bar, CornerRadiusBottomLeft = 5, ContentMarginLeft = 12, ContentMarginRight = 10, ContentMarginTop = 12, ContentMarginBottom = 10 });
            split.AddChild(side);
            var sideBox = new VBoxContainer();
            sideBox.AddThemeConstantOverride("separation", 8);
            side.AddChild(sideBox);
            sideBox.AddChild(_search);
            _nav.AddThemeConstantOverride("separation", 1);
            sideBox.AddChild(_nav);
            BuildNav();
            var contents = new Label { Text = Ui.Spaced("On this page") };
            Ui.HeadStyle(contents);
            sideBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
            sideBox.AddChild(contents);
            _toc.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            _toc.AddThemeConstantOverride("v_separation", 3);
            _toc.AddThemeConstantOverride("draw_guides", 0);
            _toc.AddThemeConstantOverride("draw_relationship_lines", 0);
            _toc.AddThemeColorOverride("font_color", Ui.Muted);
            _toc.AddThemeColorOverride("font_hovered_color", Ui.Ink);
            _toc.AddThemeColorOverride("font_selected_color", Colors.White);
            _toc.AddThemeStyleboxOverride("selected", Ui.Box(new Color(1, 1, 1, 0.10f), 3, 4, 2));
            _toc.AddThemeStyleboxOverride("selected_focus", Ui.Box(new Color(1, 1, 1, 0.12f), 3, 4, 2));
            _toc.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            sideBox.AddChild(_toc);
            Ui.HeadStyle(_linksHead);
            sideBox.AddChild(_linksHead);
            _links.AddThemeConstantOverride("h_separation", 0);
            sideBox.AddChild(_links);

            var main = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            main.AddThemeConstantOverride("separation", 0);
            split.AddChild(main);
            var top = new PanelContainer();
            top.AddThemeStyleboxOverride("panel", Ui.TopBar());
            main.AddChild(top);
            var bar = new HBoxContainer();
            bar.AddThemeConstantOverride("separation", 4);
            top.AddChild(bar);
            _back = Ui.Bare(Ui.Btn("‹", Back, "Back (Alt+Left)"));
            _forward = Ui.Bare(Ui.Btn("›", Forward, "Forward (Alt+Right)"));
            bar.AddChild(_back);
            bar.AddChild(_forward);
            bar.AddChild(_crumb);
            var gap = new Control { CustomMinimumSize = new Vector2(10, 0) };
            bar.AddChild(gap);
            bar.AddChild(_find);

            main.AddChild(_scroll);
            _scroll.AddChild(_pad);
            _column.AddThemeConstantOverride("separation", 12);
            _pad.AddChild(_column);
            _scroll.Resized += Fit;
            _scroll.GetVScrollBar().ValueChanged += _ => Track();
            var statusPad = new MarginContainer();
            statusPad.AddThemeConstantOverride("margin_left", 14);
            statusPad.AddThemeConstantOverride("margin_bottom", 6);
            statusPad.AddChild(_status);
            main.AddChild(statusPad);

            _search.TextChanged += q => Search(q);
            _search.GuiInput += e =>
            {
                if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
                {
                    _search.Text = "";
                    Search("");
                    _search.AcceptEvent();
                }
                else if (e is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter } && _firstHit != null) { OpenHit(_firstHit); _search.AcceptEvent(); }
            };
            _toc.ItemSelected += () =>
            {
                if (_tocQuiet || _toc.GetSelected()?.GetMetadata(0).AsString() is not { Length: > 0 } a) return;
                ScrollTo(a);
            };
            _find.Changed += () => Mark(true);
            _find.Moved += () => Mark(true);
            Window.WindowInput += Keys;

            parent.AddChild(Window);
            var screen = DisplayServer.ScreenGetUsableRect(Window.CurrentScreen).Size;
            Window.PopupCentered(new Vector2I(Math.Min(1240, screen.X - 80), Math.Min(820, screen.Y - 80)));
        }

        void Keys(InputEvent e)
        {
            if (_find.Handles(e)) { Window.SetInputAsHandled(); return; }
            if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
            if (k.Keycode == Key.K && k.CtrlPressed)
            {
                _search.GrabFocus();
                _search.SelectAll();
                Window.SetInputAsHandled();
            }
            else if (k.Keycode == Key.Left && k.AltPressed) { Back(); Window.SetInputAsHandled(); }
            else if (k.Keycode == Key.Right && k.AltPressed) { Forward(); Window.SetInputAsHandled(); }
            else if (k.Keycode == Key.Escape && _find.Terms.Count == 0 && !_searching) { Window.QueueFree(); Window.SetInputAsHandled(); }
        }

        void BuildNav()
        {
            string? group = null;
            foreach (var d in GuideLibrary.Docs)
            {
                if (d.Group != group && d.Group.Length > 0)
                {
                    group = d.Group;
                    var h = new Label { Text = Ui.Spaced(group) };
                    Ui.HeadStyle(h);
                    var pad = new MarginContainer();
                    pad.AddThemeConstantOverride("margin_top", _nav.GetChildCount() == 0 ? 6 : 12);
                    pad.AddThemeConstantOverride("margin_bottom", 2);
                    pad.AddChild(h);
                    _nav.AddChild(pad);
                }
                var file = d.File;
                var b = new Button { Text = d.Short, ToggleMode = true, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 28) };
                StyleBoxFlat Box(float a) => Ui.Box(new Color(1, 1, 1, a), 3, 10, 3);
                b.AddThemeStyleboxOverride("normal", Box(0));
                b.AddThemeStyleboxOverride("hover", Box(0.06f));
                b.AddThemeStyleboxOverride("pressed", Box(0.11f));
                b.AddThemeStyleboxOverride("hover_pressed", Box(0.13f));
                b.AddThemeColorOverride("font_color", Ui.Muted);
                b.AddThemeColorOverride("font_hover_color", Ui.Ink);
                b.AddThemeColorOverride("font_pressed_color", Colors.White);
                b.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
                b.Pressed += () => { _search.Text = ""; Go(file, null, true); };
                _nav.AddChild(b);
                _navButtons[file] = b;
            }
        }

        public void Go(string file, string? anchor, bool remember, string? find = null)
        {
            if (GuideLibrary.Doc(file) == null && GuideLibrary.Blocks(file) == null) { Say($"{file} is not one of the guides."); return; }
            if (_searching) { _searching = false; _search.Text = ""; }
            if (file != _file || _blocks.Count == 0)
            {
                if (remember && _file.Length > 0) { _history.Add((_file, (int)_scroll.ScrollVertical)); _ahead.Clear(); }
                Load(file);
            }
            else if (remember && anchor != null) { _history.Add((_file, (int)_scroll.ScrollVertical)); _ahead.Clear(); }
            if (find != null) _find.Open(find);
            else if (_find.Terms.Count > 0) Mark(false);
            Later(() =>
            {
                if (anchor != null) ScrollTo(anchor);
                else _scroll.ScrollVertical = 0;
                if (_find.Terms.Count > 0) FirstInView();
            });
            UpdateHistory();
        }

        void Load(string file, int scroll = -1)
        {
            _file = file;
            Remember(file);
            foreach (var c in _column.GetChildren()) { _column.RemoveChild(c); c.QueueFree(); }
            _hits.Clear();
            var blocks = GuideLibrary.Blocks(file);
            if (blocks == null)
            {
                Say($"{file} could not be read.");
                _blocks = new();
                return;
            }
            _blocks = MarkdownView.Build(blocks, _column, Link);
            _column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 80) });
            foreach (var (f, b) in _navButtons) b.SetPressedNoSignal(f == file);

            _crumb.Text = GuideLibrary.Doc(file)?.Short ?? file;
            BuildToc(blocks);
            BuildLinks();
            Say("");
            if (scroll >= 0) Later(() => _scroll.ScrollVertical = scroll);
        }

        void BuildToc(List<MdBlock> blocks)
        {
            _toc.Clear();
            var root = _toc.CreateItem();
            TreeItem? section = null;
            bool many = blocks.Count(b => b.Kind == BlockKind.Heading && b.Level == 3) > 40;
            foreach (var b in blocks.Where(b => b.Kind == BlockKind.Heading && b.Level is 2 or 3))
            {
                var parent = b.Level == 3 && section != null ? section : root;
                var it = _toc.CreateItem(parent);
                it.SetText(0, Markdown.Plain(b.Text));
                it.SetMetadata(0, b.Anchor);
                it.SetTooltipText(0, Markdown.Plain(b.Text));
                if (b.Level == 2) { section = it; it.Collapsed = many; }
            }
        }

        void BuildLinks()
        {
            foreach (var c in _links.GetChildren()) { _links.RemoveChild(c); c.QueueFree(); }
            Button Quiet(string text, Action a, string tip)
            {
                var b = Ui.Bare(Ui.Btn(text, a, tip));
                b.AddThemeColorOverride("font_color", Ui.Muted);
                b.AddThemeFontSizeOverride("font_size", 12);
                _links.AddChild(b);
                return b;
            }
            _linksHead.Visible = GuideLibrary.OnDisk(_file) != null || _file is "scripting.md" or ApiDoc.File;
            if (_file is "scripting.md" or ApiDoc.File)
                Quiet("API file for VS Code…", () => Guides.SaveStubs(Window, Say), "ktable-api.lua: completion and help for these calls in VS Code's Lua extension");
            if (GuideLibrary.OnDisk(_file) is { } path)
            {
                Quiet("Copy path", () => { AppClipboard.SetText(path); Say("Copied: " + path); }, "For pointing an AI agent or an editor at this file");
                Quiet("Open file", () => OS.ShellOpen(path), path);
                Quiet("Show in folder", () =>
                {
                    if (OS.GetName() == "Windows") OS.CreateProcess("explorer.exe", new[] { "/select," + path });
                    else OS.ShellOpen(Path.GetDirectoryName(path) ?? path);
                }, Path.GetDirectoryName(path) ?? path);
            }
        }

        void Link(string href)
        {
            if (href.StartsWith("http://", StringComparison.Ordinal) || href.StartsWith("https://", StringComparison.Ordinal)) { OS.ShellOpen(href); return; }
            var parts = href.Split('#', 2);
            var file = parts[0].Length == 0 ? _file : parts[0];
            var anchor = parts.Length == 2 ? parts[1] : null;
            if (GuideLibrary.Doc(file) != null) { Go(file, anchor, true); return; }
            if (GuideLibrary.OnDisk(file) is { } path) { OS.ShellOpen(path); return; }
            Say($"{file} is not beside the game.");
        }

        void Fit()
        {
            int w = (int)_scroll.Size.X;
            int side = Math.Max(28, (w - MarkdownView.Column) / 2);
            _pad.AddThemeConstantOverride("margin_left", side);
            _pad.AddThemeConstantOverride("margin_right", side + 8);
            _pad.AddThemeConstantOverride("margin_top", 22);
        }

        void Later(Action a)
        {
            if (!Window.IsInsideTree()) return;
            var tree = Window.GetTree();
            int frames = 0;
            void Tick()
            {
                if (++frames < 2) return;
                tree.ProcessFrame -= Tick;
                if (GodotObject.IsInstanceValid(Window)) a();
            }
            tree.ProcessFrame += Tick;
        }

        float Top(Control c) => c.GlobalPosition.Y - _column.GlobalPosition.Y;

        void ScrollTo(string anchor)
        {
            var hit = _blocks.FirstOrDefault(b => b.Block.Kind == BlockKind.Heading && b.Block.Anchor == anchor);
            if (hit == null) return;
            _scroll.ScrollVertical = (int)Math.Max(0, Top(hit.Control) - 6);
            Track();
        }

        void Track()
        {
            float y = _scroll.ScrollVertical + _scroll.Size.Y / 3;
            RenderedBlock? at = null;
            foreach (var b in _blocks)
            {
                if (b.Block.Kind != BlockKind.Heading || b.Block.Level is not (2 or 3)) continue;
                if (Top(b.Control) > y) break;
                at = b;
            }
            var doc = GuideLibrary.Doc(_file)?.Short ?? _file;
            _crumb.Text = at == null ? doc : doc + "  ›  " + Markdown.Plain(at.Block.Text);
            Select(at?.Block.Anchor);
        }

        void Select(string? anchor)
        {
            _tocQuiet = true;
            try
            {
                if (anchor == null) { _toc.DeselectAll(); return; }
                for (var it = _toc.GetRoot()?.GetFirstChild(); it != null; it = it.GetNext())
                {
                    if (it.GetMetadata(0).AsString() == anchor)
                    {
                        if (!it.IsSelected(0)) { it.Select(0); _toc.ScrollToItem(it); } return;
                    }
                    for (var ch = it.GetFirstChild(); ch != null; ch = ch.GetNext())
                        if (ch.GetMetadata(0).AsString() == anchor)
                        {
                            if (!ch.IsSelected(0))
                            {
                                if (it.Collapsed) it.Collapsed = false;
                                ch.Select(0);
                                _toc.ScrollToItem(ch);
                            }
                            return;
                        }
                }
            }
            finally { _tocQuiet = false; }
        }

        void Mark(bool scroll)
        {
            var terms = _find.Terms;
            _hits.Clear();
            int index = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                var b = _blocks[i];
                if (b.Label == null) continue;
                if (terms.Count == 0)
                {
                    if (b.Marked) { b.Label.Text = b.Bbcode; b.Marked = false; }
                    continue;
                }
                var paras = new List<int>();
                int before = index;
                var text = TextFind.Mark(b.Bbcode, terms, _find.Current, ref index, paras);
                if (index > before || b.Marked) { b.Label.Text = text; b.Marked = index > before; }
                foreach (var p in paras) _hits.Add((i, p));
            }
            _find.SetTotal(index);
            if (scroll && _hits.Count > 0) Later(() => Reveal(_find.Current));
        }

        void FirstInView()
        {
            int first = _hits.FindIndex(x => Top(_blocks[x.block].Control) >= _scroll.ScrollVertical - 2);
            if (first < 0) return;
            _find.SetCurrent(first);
            Mark(false);
            Later(() => Reveal(first));
        }

        void Reveal(int n)
        {
            if (n < 0 || n >= _hits.Count) return;
            var (bi, para) = _hits[n];
            var b = _blocks[bi];
            float y = Top(b.Control);
            if (b.Label != null && para > 0 && para < b.Label.GetParagraphCount()) y += b.Label.GetParagraphOffset(para);
            float view = _scroll.Size.Y;
            if (y < _scroll.ScrollVertical + 20 || y > _scroll.ScrollVertical + view - 60) _scroll.ScrollVertical = (int)Math.Max(0, y - view / 3);
        }

        SearchHit? _firstHit;

        public void SearchFor(string q) { _search.Text = q; Search(q); }

        void Search(string q)
        {
            q = q.Trim();
            if (q.Length == 0)
            {
                if (!_searching) return;
                _searching = false;
                _firstHit = null;
                var keep = _file;
                _file = "";
                Load(keep);
                return;
            }
            _searching = true;
            foreach (var c in _column.GetChildren()) { _column.RemoveChild(c); c.QueueFree(); }
            _blocks = new();
            _toc.Clear();
            foreach (var b in _navButtons.Values) b.SetPressedNoSignal(false);
            foreach (var c in _links.GetChildren()) { _links.RemoveChild(c); c.QueueFree(); }
            _linksHead.Visible = false;
            var hits = DocSearch.Find(GuideLibrary.Sections(), q);
            _firstHit = hits.FirstOrDefault();
            _crumb.Text = hits.Count == 0 ? $"Nothing found for “{q}”" : hits.Count == 1 ? "1 place" : $"{hits.Count} places";
            var head = MarkdownView.Label(20, true);
            head.Text = MarkdownView.Esc(q);
            _column.AddChild(head);
            var terms = DocSearch.Terms(q);
            foreach (var h in hits) _column.AddChild(Result(h, terms, q));
            _scroll.ScrollVertical = 0;
        }

        Control Result(SearchHit h, IReadOnlyList<string> terms, string q)
        {
            var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            var rest = Ui.Box(new Color(1, 1, 1, 0.035f), 4, 14, 10);
            var hover = Ui.Box(new Color(1, 1, 1, 0.08f), 4, 14, 10);
            panel.AddThemeStyleboxOverride("panel", rest);
            panel.MouseEntered += () => panel.AddThemeStyleboxOverride("panel", hover);
            panel.MouseExited += () => panel.AddThemeStyleboxOverride("panel", rest);
            var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 3);
            panel.AddChild(box);
            int ignore = 0;
            var where = MarkdownView.Label(13);
            where.MouseFilter = Control.MouseFilterEnum.Ignore;
            where.AddThemeColorOverride("default_color", Ui.Muted);
            where.Text = MarkdownView.Esc(h.Section.DocTitle);
            box.AddChild(where);
            var title = MarkdownView.Label(16, true);
            title.MouseFilter = Control.MouseFilterEnum.Ignore;
            title.Text = TextFind.Mark(MarkdownView.Esc(h.Section.Heading), terms, -1, ref ignore, new List<int>());
            box.AddChild(title);
            if (h.Snippet.Length > 0)
            {
                var snip = MarkdownView.Label(14);
                snip.MouseFilter = Control.MouseFilterEnum.Ignore;
                snip.AddThemeColorOverride("default_color", new Color(Ui.Ink, 0.82f));
                snip.Text = TextFind.Mark(MarkdownView.Esc(h.Snippet), terms, -1, ref ignore, new List<int>());
                box.AddChild(snip);
            }
            panel.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OpenHit(h, q);
            };
            return panel;
        }

        void OpenHit(SearchHit h, string? q = null)
        {
            q ??= _search.Text.Trim();
            _searching = false;
            _search.Text = "";
            var file = h.Section.Doc;
            _file = "";
            Load(file);
            _find.Open(q);
            Later(() =>
            {
                if (h.Section.Anchor.Length > 0) ScrollTo(h.Section.Anchor);
                FirstInView();
            });
            UpdateHistory();
        }

        void Back()
        {
            if (_history.Count == 0) return;
            var (f, s) = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            _ahead.Add((_file, (int)_scroll.ScrollVertical));
            if (f != _file) Load(f, s);
            else _scroll.ScrollVertical = s;
            UpdateHistory();
        }

        void Forward()
        {
            if (_ahead.Count == 0) return;
            var (f, s) = _ahead[^1];
            _ahead.RemoveAt(_ahead.Count - 1);
            _history.Add((_file, (int)_scroll.ScrollVertical));
            if (f != _file) Load(f, s);
            else _scroll.ScrollVertical = s;
            UpdateHistory();
        }

        void UpdateHistory()
        {
            _back.Disabled = _history.Count == 0;
            _forward.Disabled = _ahead.Count == 0;
        }

        void Say(string text)
        {
            _status.Text = text;
            _status.Visible = text.Length > 0;
        }
    }
}

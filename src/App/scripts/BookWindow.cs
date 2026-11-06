using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class BookWindow
{
    const int PageTexSize = 2048;

    public static bool Is(PieceDef? def) => def is { } d && d.Tags.Contains("book") && d.States.Count > 1;

    static readonly Dictionary<string, Window> Open_ = new();

    public static void Open(Node parent, Session s, PieceNode node)
    {
        var def = node.Def;
        if (!Is(def)) return;
        var id = node.Piece.Id;
        if (Open_.TryGetValue(id, out var already) && GodotObject.IsInstanceValid(already)) { already.GrabFocus(); return; }

        var pack = s.Store.PackOfDef(node.Piece.Def);
        if (pack == null) return;
        var pages = def!.States.Select(st => st.Image).Where(i => i is { Length: > 0 }).Select(i => i!).ToList();
        if (pages.Count == 0) return;
        int page = Mathf.Clamp(node.Piece.State, 0, pages.Count - 1);

        var title = node.Piece.Label is { Length: > 0 } label ? label : def.Name;
        var w = new Window { Title = title, Transient = true, Exclusive = false, WrapControls = false, MinSize = new Vector2I(420, 360) };
        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", Ui.WindowFill(Ui.Surface));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        w.AddChild(bg);
        var pad = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) pad.AddThemeConstantOverride("margin_" + side, 10);
        bg.AddChild(pad);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Group);
        pad.AddChild(box);

        var sheet = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        box.AddChild(sheet);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", Ui.Gap.Group);
        box.AddChild(bar);
        var back = Ui.Btn("◀", () => { }, "The page before");
        var on = Ui.Btn("▶", () => { }, "The next page");
        var where = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center, Modulate = Ui.Muted };
        bar.AddChild(back);
        bar.AddChild(where);
        bar.AddChild(on);

        void Draw()
        {
            where.Text = $"Page {page + 1} of {pages.Count}";
            back.Disabled = page == 0;
            on.Disabled = page == pages.Count - 1;
            if (!pack.Assets.TryGetValue(pages[page], out var a)) { sheet.Texture = null; return; }
            int want = page;
            sheet.Texture = s.Assets.Texture(pack.Id, a.File, tex => { if (GodotObject.IsInstanceValid(sheet) && want == page) sheet.Texture = tex; }, maxSize: PageTexSize);
        }

        void Turn(int to)
        {
            int next = Mathf.Clamp(to, 0, pages.Count - 1);
            if (next == page) return;
            page = next;
            Draw();
        }

        back.Pressed += () => Turn(page - 1);
        on.Pressed += () => Turn(page + 1);

        if (s.CanTouch(node.Layer, node.Piece))
            bar.AddChild(Ui.Btn("Show on the table", () => s.Do(new SetState { Layer = node.Layer, Id = id, State = page }), "Turns the piece itself to this page, for everyone"));

        Draw();
        w.WindowInput += e =>
        {
            if (e is not InputEventKey { Pressed: true } k) return;
            switch (k.Keycode)
            {
                case Key.Left or Key.Pageup or Key.Up:
                    Turn(page - 1);
                    break;
                case Key.Right or Key.Pagedown or Key.Down or Key.Space:
                    Turn(page + 1);
                    break;
                case Key.Home:
                    Turn(0);
                    break;
                case Key.End:
                    Turn(pages.Count - 1);
                    break;
                default: return;
            }
            w.SetInputAsHandled();
        };
        w.CloseRequested += w.QueueFree;
        w.TreeExiting += () => Open_.Remove(id);
        Open_[id] = w;
        parent.AddChild(w);
        w.PopupCentered(new Vector2I(760, 900));
    }
}

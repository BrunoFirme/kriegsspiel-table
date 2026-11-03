using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class DicePopover : HudPart
{
    public PopupPanel? Pop;

    public DicePopover(HudContext c) : base(c) { }

    public System.Action? ShowTableDice;
    public System.Func<Rect2?>? Beside;

    public void Show(Control anchor)
    {
        if (Pop == null) Build();
        var at = anchor.GetGlobalRect();
        float x = at.End.X;
        if (Beside?.Invoke() is { } drawer) x = Mathf.Max(x, drawer.End.X);
        Pop!.Popup(new Rect2I((int)(x + 6), (int)at.Position.Y, 0, 0));
    }

    void Build()
    {
        Pop = new PopupPanel();
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(176, 0) };
        box.AddThemeConstantOverride("separation", 4);
        Pop.AddChild(box);
        var pool = new System.Collections.Generic.Dictionary<int, int>();
        var line = new Label { HorizontalAlignment = HorizontalAlignment.Center, Text = "—", CustomMinimumSize = new Vector2(0, 22) };
        string Spell() => pool.Count == 0 ? "—" : string.Join(" + ", pool.Where(e => e.Value > 0).OrderBy(e => e.Key).Select(e => $"{e.Value}d{e.Key}"));
        var faces = new GridContainer { Columns = 3 };
        faces.AddThemeConstantOverride("h_separation", 4);
        faces.AddThemeConstantOverride("v_separation", 4);
        foreach (var sides in new[] { 4, 6, 8, 10, 12, 20 })
        {
            int n = sides;
            var b = Btn($"d{n}", () => { pool[n] = pool.GetValueOrDefault(n) + 1; line.Text = Spell(); }, $"add a d{n} · click again for another");
            b.CustomMinimumSize = new Vector2(52, 30);
            faces.AddChild(b);
        }
        box.AddChild(faces);
        box.AddChild(line);
        void Roll()
        {
            var items = pool.Where(e => e.Value > 0).OrderBy(e => e.Key)
                .Select(e => (Command)new RollDice { Count = e.Value, Sides = e.Key }).ToList();
            if (items.Count == 0) items.Add(new RollDice { Count = 1, Sides = 6 });
            if (items.Count == 1) S.Do(items[0]); else S.Do(new Batch { Items = items });
            pool.Clear(); line.Text = Spell();
            Pop!.Hide();
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        var clear = Btn("Clear", () => { pool.Clear(); line.Text = Spell(); }, "empty the hand");
        clear.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var roll = Ui.Primary("Roll", Roll, "the server rolls; every umpire sees it in the Log");
        roll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(clear); row.AddChild(roll);
        box.AddChild(row);
        var table = Btn("Dice to throw on the table…", () => { Pop!.Hide(); ShowTableDice?.Invoke(); }, "The dice pieces: drag them onto the table, pick them up and throw them");
        Ui.MakeQuiet(table);
        table.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(table);
        Ctx.Root.AddChild(Pop);
    }
}

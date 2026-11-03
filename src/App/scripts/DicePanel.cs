using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class DicePanel : HudPart
{
    public readonly FloatPanel Panel;
    public System.Action? ShowTableDice;
    readonly Label _results;
    bool _fresh;
    const int ResultSize = 15;

    public DicePanel(HudContext c, IHudShell shell) : base(c)
    {
        Panel = shell.MakePanel("dice", "Dice");
        _results = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _results.AddThemeFontSizeOverride("font_size", ResultSize);
        _results.AddThemeColorOverride("font_color", Ui.AccentInk);
        Build(Panel.Body);
    }

    public void Rolled(RollDice rd)
    {
        if (rd.Actor != S.Me.PeerId || rd.PieceIds.Count > 0 || rd.Results.Count == 0) return;
        var line = $"{rd.Count}d{rd.Sides}   {string.Join(" · ", rd.Results)}" + (rd.Results.Count > 1 ? $"   = {rd.Results.Sum()}" : "");
        _results.Text = _fresh || _results.Text.Length == 0 ? line : _results.Text + "\n" + line;
        _fresh = false;
        _results.Visible = true;
        Panel.Fit();
    }

    void Build(VBoxContainer box)
    {
        box.CustomMinimumSize = new Vector2(176, 0);
        var pool = new Dictionary<int, int>();
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
            _fresh = true;
            if (items.Count == 1) S.Do(items[0]); else S.Do(new Batch { Items = items });
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        var clear = Btn("Clear", () => { pool.Clear(); line.Text = Spell(); _results.Visible = false; Panel.Fit(); }, "empty the hand");
        clear.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var roll = Ui.Primary("Roll", Roll, "the server rolls; every umpire sees it in the Log");
        roll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(clear); row.AddChild(roll);
        box.AddChild(row);
        box.AddChild(_results);
        var table = Btn("Dice to throw on the table…", () => ShowTableDice?.Invoke(), "The dice pieces: drag them onto the table, pick them up and throw them");
        Ui.MakeQuiet(table);
        table.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(table);
    }
}

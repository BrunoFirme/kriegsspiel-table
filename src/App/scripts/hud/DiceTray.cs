using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class DiceTray : HudPart
{
    const int MostDice = 20, Lift = 8;
    const float FanTurn = 0.18f;
    static readonly int[] Sides = [4, 6, 8, 10, 12, 20, 100];

    readonly Dictionary<int, int> _counts = new();
    readonly Dictionary<int, Button> _dice = new();
    readonly PopupPanel _pop = new();
    readonly CheckBox _secret = new() { Text = "Only the umpire sees it", FocusMode = Control.FocusModeEnum.None };
    readonly Button _throw;

    public DiceTray(HudContext ctx) : base(ctx)
    {
        _pop.AddThemeStyleboxOverride("panel", Ui.Panel());
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        foreach (int sides in Sides)
        {
            var die = Btn($"d{sides}", () => Count(sides, 1), "Click adds one, right-click takes one away");
            die.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) Count(sides, -1); };
            _dice[sides] = die;
            row.AddChild(die);
        }
        column.AddChild(row);
        column.AddChild(_secret);
        var roll = Btn("Roll", () => Roll(false));
        Ui.MakePrimary(roll);
        _throw = Btn("Throw", () => Roll(true), "Throw them in front of you");
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        (roll.SizeFlagsHorizontal, _throw.SizeFlagsHorizontal) = (Control.SizeFlags.ExpandFill, Control.SizeFlags.ExpandFill);
        actions.AddChild(_throw);
        actions.AddChild(roll);
        column.AddChild(actions);
        _pop.AddChild(column);
        Ctx.Root.AddChild(_pop);
    }

    public void Open(Control under)
    {
        _throw.Visible = T.Walk.Walking;
        _pop.ResetSize();
        var size = _pop.GetContentsMinimumSize();
        _pop.Popup(new Rect2I((Vector2I)(under.GlobalPosition - new Vector2(0, size.Y + Ui.Px(Lift))), (Vector2I)size));
    }

    void Count(int sides, int by)
    {
        int now = Math.Clamp(_counts.GetValueOrDefault(sides) + by, 0, MostDice);
        _counts[sides] = now;
        var die = _dice[sides];
        die.Text = now > 0 ? $"{now}d{sides}" : $"d{sides}";
        if (now > 0) die.AddThemeStyleboxOverride("normal", Ui.Box(new Color(Ui.Accent, 0.30f), 3, 10, 4));
        else die.RemoveThemeStyleboxOverride("normal");
    }

    void Roll(bool thrown)
    {
        var picked = _counts.Where(p => p.Value > 0).ToList();
        if (picked.Count == 0)
        {
            Say("Pick dice first");
            return;
        }
        for (int i = 0; i < picked.Count; i++)
        {
            var (sides, count) = picked[i];
            var path = thrown && ThrownDice.Throwable.Contains(sides) ? T.Walk.ThrowPath((i - (picked.Count - 1) / 2f) * FanTurn) : null;
            S.Do(new RollDice { Sides = sides, Count = count, Open = !_secret.ButtonPressed, Thrown = path });
        }
        foreach (var (sides, _) in picked) Count(sides, -MostDice);
        _pop.Hide();
    }
}

using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ZoneDialog : HudPart
{
    ZoneDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c, List<Vec2> poly) => new ZoneDialog(c).Show(poly);

    void Show(List<Vec2> poly)
    {
        var dlg = new ConfirmationDialog { Title = "Assign zone to an umpire", OkButtonText = "Assign" };
        var grid = Ui.Form(Ui.DialogLabels);
        grid.AddChild(new Label { Text = "Zone name" });
        var name = new LineEdit { Text = $"Zone {S.View.Zones.Count + 1}", CustomMinimumSize = new Vector2(220, 0) };
        grid.AddChild(name);
        grid.AddChild(new Label { Text = "Umpire" });
        var owner = new OptionButton();
        var others = S.Peers.Where(p => Visibility.IsUmpire(p.Role) && p.PeerId != S.Me.PeerId).Select(p => p.Name).Distinct().ToList();
        foreach (var o in others) owner.AddItem(o);
        owner.AddItem("(type a name below)");
        grid.AddChild(owner);
        grid.AddChild(new Label { Text = "…or name" });
        var typed = new LineEdit { PlaceholderText = "umpire name as they were let in" };
        grid.AddChild(typed);
        Ui.AlignForm(grid);
        var box = new VBoxContainer();
        box.AddChild(grid);
        box.AddChild(Hint("Once any zone exists, umpires can only change umpire-board pieces inside their own zones. The host and the lead umpires stay unrestricted. Observers never see zones."));
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            var who = typed.Text.Trim().Length > 0 ? typed.Text.Trim() : owner.Selected >= 0 && owner.Selected < others.Count ? others[owner.Selected] : "";
            if (who.Length == 0) { Log("[color=#ff7060]Zone needs an umpire name.[/color]"); return; }
            var hue = UmpireHue.Of(S, who);
            S.Do(new SetZone { Zone = new Zone { Name = name.Text, Owner = who, Color = hue is { } h ? "#" + h.ToHtml(false) : "", Polygon = poly } });
        };
        Ui.Open(HudNode, dlg, new Vector2I(420, 260));
        Ui.FreeOnClose(dlg);
    }
}

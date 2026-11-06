using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class UmpiresPage : HudPart
{
    VBoxContainer _box = null!;

    public UmpiresPage(HudContext c) : base(c) { }

    public Control Build()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
        _box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_box);
        return scroll;
    }

    public void Rebuild()
    {
        foreach (var c in _box.GetChildren()) c.QueueFree();
        if (!S.IsUmpire)
        {
            _box.AddChild(Header("Watching"));
            _box.AddChild(Hint("You see the umpire board as it is. The umpires' zones, dice and pointers stay with them."));
            return;
        }
        AddZones();
    }

    void AddZones()
    {
        _box.AddChild(Header("Umpire zones"));
        if (S.IsLead)
        {
            var split = Btn("Split the board…", () => T.SetTool(Tool.Zone), "Drag a rectangle on the umpire board to give it to an umpire");
            _box.AddChild(split);
        }
        if (S.View.Zones.Count == 0)
            _box.AddChild(Hint(S.IsLead ? "None yet" : "None"));
        foreach (var z in S.View.Zones)
        {
            var zr = new HBoxContainer();
            var mine = string.Equals(z.Owner, S.Me.Name, StringComparison.OrdinalIgnoreCase);
            var zl = new Label { Text = $"{z.Name} — {z.Owner}{(mine ? " (you)" : "")}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
            zl.AddThemeColorOverride("font_color", (UmpireHue.Of(S, z.Owner) ?? UmpireHue.Unassigned).Lightened(0.2f));
            zr.AddChild(zl);
            var zid = z.Id;
            if (S.IsLead) zr.AddChild(Btn("Remove", () => S.Do(new RemoveZone { Id = zid }), "remove zone"));
            _box.AddChild(zr);
        }
        if (!S.IsLead && S.View.Zones.Count > 0 && !S.View.Zones.Any(z => string.Equals(z.Owner, S.Me.Name, StringComparison.OrdinalIgnoreCase)))
            _box.AddChild(Hint("No zone for you: the umpire board is read-only"));
    }

    internal static string ShortClockText(string gameTime) =>
        DateTime.TryParse(gameTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) ? t.ToString("d MMM HH:mm") : gameTime;
    internal static string LocalTime(string iso) =>
        DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("HH:mm") : "";
}

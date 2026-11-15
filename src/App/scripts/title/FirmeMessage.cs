using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Updates;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class FirmeMessage
{
    const string Shipped = "res://" + FirmeNotes.ShippedAt;
    const int RuleWidth = 2;
    const int RuleGap = 12;
    const int RuleRise = 2;
    static Task<(bool Reached, FirmeNote? Note)>? _live;
    static string? _litThisRun;

    readonly Settings _cfg;
    internal readonly VBoxContainer Slot = new();

    FirmeMessage(Settings cfg) { _cfg = cfg; }

    internal static FirmeMessage Put(VBoxContainer at, Settings cfg)
    {
        var m = new FirmeMessage(cfg);
        at.AddChild(m.Slot);
        m.Show(Known());
        if (!OS.HasFeature(UpdateOffer.ExportedBuild) || !cfg.CheckUpdates || _live is { IsCompleted: true }) return m;
        _live ??= AskLive();
        _live.ContinueWith(_ => Callable.From(() => m.Show(Known())).CallDeferred(), TaskScheduler.Default);
        return m;
    }

    static FirmeNote? Known() => _live is { IsCompletedSuccessfully: true, Result: (true, var heard) } ? heard : ShippedNote();

    static FirmeNote? ShippedNote() => Godot.FileAccess.FileExists(Shipped) ? FirmeNotes.Read(Godot.FileAccess.GetFileAsString(Shipped)) : null;

    static Task<(bool Reached, FirmeNote? Note)> AskLive() => Task.Run(async () =>
    {
        try
        {
            using var http = Releases.NewClient(UpdateOffer.UserAgent);
            var heard = await FirmeNotes.Fetch(http, CancellationToken.None);
            GD.Print(heard is { } n ? $"[note] Firme's message: {n.Text}" : "[note] no message from Firme");
            return (true, heard);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            GD.PrintErr("[note] reading Firme's message: " + e.Message);
            return (false, (FirmeNote?)null);
        }
    });

    internal void Show(FirmeNote? note)
    {
        if (!GodotObject.IsInstanceValid(Slot)) return;
        foreach (var c in Slot.GetChildren()) c.QueueFree();
        if (note == null) return;
        if (_cfg.FirstSight(note.Text))
        {
            _cfg.Save();
            _litThisRun = note.Text;
        }
        Slot.AddChild(Card(note, note.Text == _litThisRun));
    }

    internal static StyleBoxFlat Rule(bool fresh) =>
        new() { BgColor = Colors.Transparent, BorderWidthLeft = RuleWidth, BorderColor = fresh ? Brass : Ui.Faint,
            ContentMarginLeft = RuleGap, ContentMarginTop = RuleRise, ContentMarginBottom = RuleRise };

    static PanelContainer Card(FirmeNote note, bool fresh)
    {
        var rule = Rule(fresh);
        var rest = rule.BorderColor;
        var card =
            new PanelContainer { CustomMinimumSize = new Vector2(ColumnWidth, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                MouseFilter = note.Link == null ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", rule);
        card.AddChild(ItemName(new Label { Text = note.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart }));
        if (note.Link is { } link)
        {
            card.TooltipText = link.AbsoluteUri;
            card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            card.MouseEntered += () => rule.BorderColor = Ink;
            card.MouseExited += () => rule.BorderColor = rest;
            card.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OS.ShellOpen(link.AbsoluteUri);
            };
        }
        return card;
    }
}

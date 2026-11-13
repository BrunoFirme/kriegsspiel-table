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
    const string From = "From Firme";
    const int Width = 260;
    const float HoverLift = 0.05f;
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
        if (note.Text != _cfg.SeenNote)
        {
            _cfg.SeenNote = note.Text;
            _cfg.Save();
            _litThisRun = note.Text;
        }
        Slot.AddChild(Card(note, note.Text == _litThisRun));
    }

    static PanelContainer Card(FirmeNote note, bool fresh)
    {
        var fill = CardStyle(fresh);
        var rest = fill.BgColor;
        var card =
            new PanelContainer { CustomMinimumSize = new Vector2(Width, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                MouseFilter = note.Link == null ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", fill);
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        card.AddChild(lines);
        lines.AddChild(Caps(From));
        lines.AddChild(ItemName(new Label { Text = note.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart }));
        if (note.Link is { } link)
        {
            card.TooltipText = link.AbsoluteUri;
            card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            card.MouseEntered += () => fill.BgColor = new Color(rest, rest.A + HoverLift);
            card.MouseExited += () => fill.BgColor = rest;
            card.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OS.ShellOpen(link.AbsoluteUri);
            };
        }
        return card;
    }
}

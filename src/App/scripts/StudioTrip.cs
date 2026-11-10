using System.Linq;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed record StudioTrip(string Save, string Name, string Pack, string Kind)
{
    static StudioTrip? _pending;

    internal static StudioTrip? Take()
    {
        var trip = _pending;
        _pending = null;
        return trip;
    }

    internal static string? PackToEdit(Session s, PieceDef kind) =>
        s.IsHost && s.SavePath != null && s.Store.PackOfDef(kind.Id) is { } pack && !StandardPacks.IsStandard(pack.Id) ? pack.Id : null;

    internal static void Leave(HudContext c, string pack, string kind) => new Going(c).Ask(pack, kind);

    internal static string? SaveForTrip(HudContext c, string pack, string kind) => new Going(c).Save(pack, kind);

    sealed class Going : HudPart
    {
        public Going(HudContext c) : base(c) { }

        public void Ask(string pack, string kind)
        {
            var seated = S.Peers.Where(p => p.PeerId != S.Me.PeerId).Select(p => p.Name).ToList();
            if (seated.Count == 0) { Go(pack, kind); return; }
            var who = seated.Count == 1 ? $"{seated[0]} is" : $"{string.Join(", ", seated)} are";
            Ui.Confirm(HudNode, "Edit in the piece studio", "Leave the table", () => Go(pack, kind),
                $"The table closes while you are in the piece studio: {who} dropped, and can join again once Done there hosts this game again.", 420).CancelButtonText = "Stay";
        }

        void Go(string pack, string kind)
        {
            if (Save(pack, kind) is { } why)
            {
                Log($"[color=#ff7060]{Fmt.Bb(why)}[/color]");
                return;
            }
            Cfg.Save();
            N.Shutdown();
            HudNode.GetTree().ReloadCurrentScene();
        }

        public string? Save(string pack, string kind)
        {
            if (S.SavePath is not { } save) return "This table is not kept in a save, so it stays open.";
            if (InUse.BeingReplaced(InUse.Pack(S.Store.Root, pack)) is { } busy) return $"The pack {pack} is {busy} right now, so the table stays open.";
            T.Cam.KeepNow();
            N.SaveNow(closing: true);
            if (N.LastSaveProblem is { } problem) return $"The game could not be saved, so the table stays open: {problem}";
            _pending = new StudioTrip(save, S.Me.Name, pack, kind);
            return null;
        }
    }
}

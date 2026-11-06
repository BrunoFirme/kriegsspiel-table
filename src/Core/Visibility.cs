using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core;

public static class Visibility
{
    public static bool IsUmpire(Role r) => r is Role.Umpire or Role.LeadUmpire or Role.HostUmpire;
    public static bool IsLead(Role r) => r is Role.LeadUmpire or Role.HostUmpire;

    public static GameState Filter(GameState g, PeerInfo peer)
    {
        var v = g.DeepClone();
        if (IsUmpire(peer.Role)) return v;
        switch (peer.Role)
        {
            case Role.Observer:
                v.Zones = new();
                v.Dice = new();
                v.Briefing = null;
                return v;
            default:
                return Empty(g);
        }
    }

    static GameState Empty(GameState g) => new()
    {
        Format = g.Format, ScenarioId = g.ScenarioId, Name = g.Name, Packs = new(g.Packs),
        Sheets = g.Sheets.Select(Json.Clone).ToList(),
        Grid = Json.Clone(g.Grid),
        Calibration = new() { UnitsPerMeter = g.Calibration.UnitsPerMeter },
        Clock = new() { Turn = g.Clock.Turn, GameTime = g.Clock.GameTime, IncrementMinutes = g.Clock.IncrementMinutes },
        Lighting = Json.Clone(g.Lighting), Wind = g.Wind.Clone(), VisionRadius = g.VisionRadius,
        NextId = g.NextId, LastSeq = g.LastSeq,
    };

    public static Command? ForPeer(Command c, PeerInfo peer, GameState g)
    {
        if (IsUmpire(peer.Role)) return c;
        if (peer.Role != Role.Observer) return null;
        switch (c?.ShownToObservers())
        {
            case Shown.Parts when c is Batch b:
            {
                var items = b.Items.Select(i => ForPeer(i, peer, g)).Where(i => i != null).Cast<Command>().ToList();
                return items.Count == 0 ? null : new Batch { Seq = b.Seq, Actor = b.Actor, At = b.At, Items = items };
            }
            case Shown.UmpiresOnly:
                return null;
            case Shown.Addressee when c is Say s:
                return s.To == null || s.To == $"p:{peer.PeerId}" ? c : null;
            case Shown.Addressee when c is SelectPiece sp:
                return sp.To == $"p:{peer.PeerId}" ? c : null;
            case Shown.DiceOnBoard when c is RollDice rd:
                return rd.Layer != null && rd.PieceIds.Count > 0 ? c : null;
            case Shown.WholeTable when c is RestoreTurn rt:
                return new RestoreTurn { Seq = rt.Seq, Actor = rt.Actor, At = rt.At, Index = rt.Index, State = rt.State == null ? null : Filter(rt.State, peer) };
            case Shown.WholeTable when c is LoadSave ls:
                return new LoadSave { Seq = ls.Seq, Actor = ls.Actor, At = ls.At, Name = ls.Name, State = ls.State == null ? null : Filter(ls.State, peer) };
            default:
                return c;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Prefs;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class SoundTests
{
    const string U = Layers.Umpire;

    static readonly Sound[] Landings = { Sound.Wood, Sound.WoodHeavy, Sound.Card, Sound.Plastic, Sound.Die };

    static List<TableSounds.Heard> Listen(OfflineTable t) => t.Table.Sound.Kept = new();

    static async Task Carry(TestRunner r, OfflineTable t, Vec2 from, Vec2 to)
    {
        t.Table.Hand.Start(from);
        t.Table.Hand.Move(to);
        await r.Seconds(0.25);
        t.Table.Hand.Drop();
        await r.Frames(5);
    }

    static Action<Command> SecondUmpire(OfflineTable t)
    {
        t.Server.Join(2, new JoinRequest { Name = "Other", Protocol = ProtocolVersion.Current });
        t.Server.Control(1, new Admit { PeerId = 2, Role = Role.Umpire });
        return c =>
        {
            foreach (var o in t.Server.Handle(2, c))
                if (o.PeerId == 1 && o.Message is CommandMessage cm) t.S.OnCommand(cm.Command);
        };
    }

    [NodeTest]
    static async Task A_piece_picked_up_and_set_down_is_heard_once_each_where_it_lands(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        var heard = Listen(t);
        t.Table.Selection.Set(new[] { "p1" });
        await Carry(r, t, new Vec2(1, 0), new Vec2(4, 2));
        Check.Equal(1, heard.Count(h => h.Sound == Sound.Lift), "picked up");
        var land = heard.Where(h => Landings.Contains(h.Sound)).ToList();
        Check.Equal(1, land.Count, "set down (its own move coming back is not heard again)");
        Check.That(land[0].At.DistanceTo(t.Node("p1").GlobalPosition) < 0.5f, $"where it lands: {land[0].At} vs {t.Node("p1").GlobalPosition}");
        Check.That(land[0].After > 0, "after the fall, not as it is let go of");
    }

    [NodeTest]
    static async Task Six_hundred_set_down_at_once_are_one_sound_and_a_few_more(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 600);
        await r.Frame();
        var heard = Listen(t);
        t.Table.Selection.Set(Enumerable.Range(1, 600).Select(i => "p" + i));
        await Carry(r, t, new Vec2(1, 0), new Vec2(1, 5));
        var land = heard.Where(h => Landings.Contains(h.Sound)).ToList();
        Check.Equal(1, land.Count(h => h.Sound == Sound.WoodHeavy), "one heavy set-down");
        Check.That(land.Count <= 4, $"no more than four landings heard: {land.Count}");
    }

    [NodeTest]
    static async Task Another_umpire_s_move_is_heard_landing_and_a_turn_in_place_is_heard_turning(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        var send = SecondUmpire(t);
        var heard = Listen(t);
        send(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(3, 3) });
        await r.Frame();
        var land = heard.Where(h => Landings.Contains(h.Sound)).ToList();
        Check.Equal(1, land.Count, "the move heard");
        Check.That(land[0].At.DistanceTo(t.Node("p1").GlobalPosition) < 0.5f, "where it landed");
        heard.Clear();
        await r.Seconds(0.1);
        send(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(2, 0), RotDeg = 90 });
        await r.Frame();
        Check.Equal("Tick", string.Join(",", heard.Select(h => h.Sound)), "a turn in place");
    }

    [NodeTest]
    static async Task A_piece_not_drawn_here_or_far_off_the_screen_is_not_heard(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        var send = SecondUmpire(t);
        var heard = Listen(t);
        t.Node("p1").Visible = false;
        send(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(3, 3) });
        await r.Frame();
        Check.Equal(0, heard.Count, "a hidden piece's move");
        send(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(100000, 0) });
        await r.Frame();
        Check.Equal(0, heard.Count, "a move far off the screen");
    }

    [NodeTest]
    static async Task The_player_reads_every_sound_and_a_table_with_no_audio_device_plays_on(TestRunner r)
    {
        Sfx.Install(r, new Settings(), quiet: false);
        await r.Frame();
        foreach (Sound s in Enum.GetValues<Sound>()) Check.That(Sfx.Takes(s) >= 2, $"takes of {s}: {Sfx.Takes(s)}");
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1", "p2" });
        await Carry(r, t, new Vec2(1, 0), new Vec2(5, 5));
        await r.Seconds(0.5);
        Check.Equal(new Vec2(5, 5), t.S.View.Find(U, "p1")!.Pos, "set down as usual");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }
}

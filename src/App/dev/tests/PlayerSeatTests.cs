using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class PlayerSeatTests
{
    [NodeTest]
    static async Task A_player_waits_until_an_umpire_places_them_and_then_walks_from_that_place_facing_the_given_way(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        var walk = t.Table.Walk;
        try
        {
            walk.PlacedAt(new Vector3(3, 0, 2), 0.5f, null);
            Check.That(!walk.Walking, "an umpire is never placed as a player");
            t.S.Me.Role = Role.Player;
            walk.FollowRole();
            await r.Frames(2);
            Check.That(!walk.Walking, "a player nobody has placed waits");
            walk.PlacedAt(new Vector3(3, 0, 2), 0.5f, null);
            await r.Frames(3);
            Check.That(walk.Walking, "a placed player walks");
            Check.That(new Vector2(walk.Feet.X - 3, walk.Feet.Z - 2).Length() < 0.05f, $"they start where they were placed: {walk.Feet}");
            Check.That(Mathf.IsEqualApprox(t.Table.Rig.Yaw, 0.5f), $"facing the way given: {t.Table.Rig.Yaw}");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_player_put_into_a_piece_asks_only_for_that_piece_and_walks_in_it_once_it_is_theirs(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        var walk = t.Table.Walk;
        var asked = new List<string>();
        walk.BodyWanted += (spec, _, _) => asked.Add(spec);
        string taking = AvatarBody.OfTaking("p1");
        try
        {
            t.S.Me.Role = Role.Player;
            walk.FollowRole();
            await r.Frames(2);
            walk.PlacedAt(t.Node("p1").TablePosition(), 0, "p1");
            await r.Seconds(1.5);
            Check.That(asked.Count > 0 && asked.All(s => s == taking), $"they ask only for the piece they were put in: {string.Join(", ", asked)}");
            t.Table.BodiesArrived([new BodyOf("p1", t.S.Me.PeerId, t.S.Me.Name, AvatarPose.NoColour)]);
            await r.Seconds(3.5);
            Check.That(walk.BodyPiece == "p1" && asked.All(s => s == taking), $"once it is theirs they walk in it and ask for no other body: {walk.BodyPiece}, {string.Join(", ", asked)}");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task An_umpire_whose_takeover_gets_no_answer_is_let_go_and_never_given_a_body_of_their_own(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        var walk = t.Table.Walk;
        var asked = new List<string>();
        walk.BodyWanted += (spec, _, _) => asked.Add(spec);
        try
        {
            walk.TakeControl("p1", t.Node("p1").TablePosition());
            await r.Seconds(1);
            Check.That(walk.InControl, "the umpire walks as the piece at once");
            await r.Seconds(3);
            Check.That(!walk.Walking, "with no answer the umpire is let go");
            Check.That(asked.All(s => s == AvatarBody.OfTaking("p1")), $"and never asks for a body of their own: {string.Join(", ", asked)}");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task An_umpire_in_control_looks_from_above_and_back_and_finds_the_camera_where_they_left_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        var walk = t.Table.Walk;
        var rig = t.Table.Rig;
        try
        {
            walk.TakeControl("p1", t.Node("p1").TablePosition());
            t.Table.BodiesArrived([new BodyOf("p1", t.S.Me.PeerId, t.S.Me.Name, AvatarPose.NoColour)]);
            await r.Frames(3);
            float facing = walk.Pose.Yaw;
            walk.SeeAsGod(true);
            await r.Frames(2);
            Check.That(walk.GodView && walk.InControl && rig.Eye == null, "from above the umpire keeps the piece and the camera is the table's again");
            Check.That(Mathf.IsEqualApprox(rig.Yaw, facing) && Mathf.IsEqualApprox(rig.Pitch, CameraRig.LookingDownRadians), "it starts behind the piece at the table's usual angle");
            rig.Yaw += 1.2f;
            var left = rig.Now;
            await r.Frames(3);
            Check.That(Mathf.IsEqualApprox(walk.Pose.Yaw, facing), $"turning the table camera leaves the piece facing its way: {walk.Pose.Yaw} against {facing}");
            walk.SeeAsGod(false);
            await r.Frames(3);
            Check.That(!walk.GodView && rig.Eye != null && Mathf.IsEqualApprox(rig.Yaw, facing), $"back through the avatar's eyes, facing its way: {rig.Yaw} against {facing}");
            walk.SeeAsGod(true);
            await r.Frames(2);
            float moved = new Vector2(rig.Target.X - left.Target.X, rig.Target.Z - left.Target.Z).Length();
            Check.That(Mathf.IsEqualApprox(rig.Yaw, left.Yaw) && moved < walk.Height * 0.1f, $"from above again the camera is where it was left: {rig.Yaw} against {left.Yaw}, {moved}");
            walk.FindAvatar();
            await r.Frames(2);
            Check.That(Mathf.IsEqualApprox(rig.Yaw, facing), "finding the avatar puts the camera behind it again");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_player_without_the_right_to_fly_comes_down_and_flies_once_the_umpire_allows_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        var walk = t.Table.Walk;
        try
        {
            t.S.Me.Role = Role.Player;
            walk.PlacedAt(new Vector3(3, 0, 2), 0, null);
            await r.Frames(2);
            walk.Fly(true);
            await r.Frames(2);
            Check.That(!walk.Flying, "flying is off for players until an umpire allows it");
            t.S.View.Everyone = new PlayerRights { Fly = true };
            walk.Fly(true);
            await r.Frames(2);
            Check.That(walk.Flying, "a player the umpire lets fly stays up");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_walker_measures_and_pings_where_they_look_until_the_umpire_takes_those_away(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        var walk = t.Table.Walk;
        try
        {
            walk.Enter(new Vector3(3, 0, 2));
            t.Table.Rig.Pitch = -0.4f;
            walk.Measuring = true;
            await r.Frames(3);
            Check.That(t.Table.Measure.Reading is { Length: > 0 }, $"the distance shows by the aim dot: {t.Table.Measure.Reading}");
            Vector3? pinged = null;
            walk.Pinged += at => pinged = at;
            walk.PingAimed();
            Check.That(pinged is { } spot && new Vector2(spot.X - walk.Feet.X, spot.Z - walk.Feet.Z).Length() > walk.Height, $"the ping lands ahead, where they look: {pinged}");
            t.S.Me.Role = Role.Player;
            t.S.View.Everyone = new PlayerRights { Measure = false, Ping = false };
            await r.Frames(2);
            Check.That(!walk.Measuring && t.Table.Measure.Reading == null, "measuring stops when the umpire takes it away");
            pinged = null;
            walk.PingAimed();
            Check.That(pinged == null, "no ping without the right");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_walker_is_never_pulled_back_or_turned_by_its_own_body_writes_coming_back_late(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        var walk = t.Table.Walk;
        try
        {
            walk.Enter(new Vector3(1, 0, 0));
            walk.BodyIs("p1");
            await r.Frames(3);
            var written = new List<(Vec2 Pos, double Rot)>();
            float yaw = 0;
            walk.Steer = () => new Steering(yaw += 0.01f, 0, new Vector2(0, 1), false);
            for (int i = 0; i < 150; i++)
            {
                await r.Seconds(0.02);
                if (t.S.View.Find(Layers.Umpire, "p1") is { } body && (written.Count == 0 || written[^1].Pos != body.Pos)) written.Add((body.Pos, body.RotDeg));
            }
            Check.That(written.Count >= 2, $"the walker wrote its body more than once: {written.Count}");
            var was = walk.Feet;
            float turnedTo = t.Table.Rig.Yaw;
            (t.S.View.Find(Layers.Umpire, "p1")!.Pos, t.S.View.Find(Layers.Umpire, "p1")!.RotDeg) = written[^2];
            walk.Steer = () => new Steering(turnedTo, 0, Vector2.Zero, false);
            await r.Seconds(0.15);
            Check.That(new Vector2(walk.Feet.X - was.X, walk.Feet.Z - was.Z).Length() < walk.Height * 0.2f, $"a late echo of its own write leaves the walker where it is: {was} -> {walk.Feet}");
            Check.That(Mathf.Abs(Mathf.AngleDifference(t.Table.Rig.Yaw, turnedTo)) < 0.01f, $"and facing where it faces: {turnedTo} -> {t.Table.Rig.Yaw}");
        }
        finally
        {
            walk.Steer = null;
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task Flying_lifts_off_and_slides_along_the_ground_at_speed_instead_of_grinding_into_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        var walk = t.Table.Walk;
        try
        {
            walk.Enter(new Vector3(3, 0, 2));
            await r.Seconds(0.2);
            float ground = walk.Feet.Y;
            walk.Fly(true);
            await r.Seconds(0.8);
            Check.That(walk.Feet.Y - ground > walk.Height * 0.3f, $"flying lifts off the ground: {walk.Feet.Y - ground} against {walk.Height}");
            var from = walk.Feet;
            walk.Steer = () => new Steering(0, -0.3f, new Vector2(0, 1), false);
            await r.Seconds(1.5);
            float went = new Vector2(walk.Feet.X - from.X, walk.Feet.Z - from.Z).Length();
            Check.That(went > walk.Height * 3, $"flying down into the ground slides on along it at speed: {went / walk.Height:0.0} heights in 1.5 s");
        }
        finally
        {
            walk.Steer = null;
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task Thrown_dice_land_showing_the_rolled_faces_and_are_gone_a_few_seconds_later(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { StandardPacks.Dice }, prepare: StandardPacks.Ensure);
        await r.Frames(3);
        var walk = t.Table.Walk;
        try
        {
            walk.Enter(new Vector3(3, 0, 2));
            await r.Seconds(0.2);
            var path = walk.ThrowPath();
            Check.That(path is { Count: > 5 }, "the throw leaves the hand and comes down");
            double lands = t.Table.ThrowDice(new KTable.Core.Protocol.RollDice { Sides = 6, Count = 2, Results = [2, 6], Seq = 3, Thrown = path, ActorName = "Tester" });
            Check.That(lands > 0 && t.Table.Thrown.Flying == 2, $"two dice fly for {lands:0.00} s");
            await r.Seconds(lands + 0.3);
            Check.Equal("2 6", string.Join(" ", t.Table.Thrown.Showing()), "each die rests on the face the roll gave");
            await r.Seconds(4.5);
            Check.Equal(0, t.Table.Thrown.Flying, "the dice are gone a few seconds after they land");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_table_with_walking_figures_readies_their_walk_and_the_dice_before_anyone_needs_them(TestRunner r)
    {
        const int Looks = 150;
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { StandardPacks.Dice, AvatarBody.StandardPack }, prepare: StandardPacks.Ensure);
        try
        {
            for (int i = 0; i < Looks && (!t.Table.Preload.Started || t.Table.Preload.Busy); i++) await r.Seconds(0.1);
            var townsman = t.Table.S.Store.Def(AvatarBody.StandardPack + ".townsman");
            Check.That(townsman != null && t.Table.Preload.Holds(townsman), "the Townsman's walk is built before anyone walks");
            Check.That(ThrownDice.Throwable.All(t.Table.Thrown.Ready), "every die that can be thrown is loaded with its picture");
        }
        finally
        {
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_table_without_walking_figures_loads_nothing_for_them(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { StandardPacks.Dice }, prepare: StandardPacks.Ensure);
        try
        {
            await r.Seconds(1.5);
            Check.That(!t.Table.Preload.Started && !t.Table.Thrown.Ready(6), "a table nobody walks on loads no dice or figures for walking");
        }
        finally
        {
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_walk_whose_frames_come_late_from_the_host_is_built_once_they_arrive(TestRunner r)
    {
        const int Looks = 60;
        const string Late = ".later";
        var later = new List<(string Pack, string Rel, string Path)>();
        void HoldFramesBack(KTable.Core.PackageStore store)
        {
            StandardPacks.Ensure(store);
            var pack = store.Pack(AvatarBody.StandardPack)!;
            var def = store.Def(AvatarBody.StandardPack + ".townsman")!;
            foreach (var rel in (def.Walk ?? []).Concat(def.Run ?? []).Concat(def.Idle ?? []).Select(key => pack.Assets[key].File).Distinct())
            {
                var path = Path.Combine(store.Root, pack.Id, rel);
                File.Move(path, path + Late);
                later.Add((pack.Id, rel, path));
            }
        }
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { AvatarBody.StandardPack }, prepare: HoldFramesBack);
        var asked = new List<string>();
        t.S.Assets.RequestRemote = (_, rel) => asked.Add(rel);
        try
        {
            var townsman = t.S.Store.Def(AvatarBody.StandardPack + ".townsman")!;
            await r.Seconds(2.5);
            Check.That(!t.Table.Preload.Holds(townsman), "nothing is built while the frames are on their way");
            Check.That(asked.Count == later.Count && asked.ToHashSet().SetEquals(later.Select(l => l.Rel)), $"each missing frame is asked of the host once (asked {asked.Count} of {later.Count})");
            foreach (var (pack, rel, path) in later)
            {
                File.Move(path + Late, path);
                t.S.Assets.FileArrived(pack, rel);
            }
            for (int i = 0; i < Looks && !t.Table.Preload.Holds(townsman); i++) await r.Seconds(0.1);
            Check.That(t.Table.Preload.Holds(townsman), "the walk is built once its frames arrive");
        }
        finally
        {
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_player_coat_takes_their_colour_deep_and_the_rest_of_the_picture_stays(TestRunner r)
    {
        await r.Frames(1);
        var picture = Image.CreateEmpty(2, 1, false, Image.Format.Rgba8);
        picture.SetPixel(0, 0, Color.Color8(92, 58, 38));
        picture.SetPixel(1, 0, Color.Color8(238, 236, 228));
        var mask = Image.CreateEmpty(2, 1, false, Image.Format.L8);
        mask.SetPixel(0, 0, Colors.White);
        mask.SetPixel(1, 0, Colors.Black);
        var colour = new Color(0.087f, 0.464f, 0.673f);
        var dyed = Livery.Dyed(picture, mask, colour)?.GetImage() ?? throw new TestFailure("no dyed picture");
        var coat = dyed.GetPixel(0, 0);
        Check.That(Mathf.Abs(coat.H - colour.H) < 0.02f && Mathf.Abs(coat.S - colour.S) < 0.05f, $"the coat takes the player's hue: {coat}");
        Check.That(coat.V > 0.55f && coat.V < 0.75f, $"deep, not bright: {coat.V}");
        Check.That(dyed.GetPixel(1, 0).IsEqualApprox(picture.GetPixel(1, 0)), $"the linen stays: {dyed.GetPixel(1, 0)}");
    }
}

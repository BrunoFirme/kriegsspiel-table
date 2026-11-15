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

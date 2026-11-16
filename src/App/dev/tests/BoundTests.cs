using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class BoundTests
{
    const long Guest = 7;

    static string Square(OfflineTable t, double x0, double x1, BoundRule everyone, string? on = null)
    {
        var layer = new BoundLayer
        {
            Name = "Yard",
            Shapes = { new BoundShape { Polygon = { new(x0, 0), new(x1, 0), new(x1, 4), new(x0, 4) }, Floor = -2, Ceiling = 6, On = on } },
        };
        var set = new SetBoundLayer { Bound = layer };
        t.S.Do(set);
        var id = t.S.View.BoundLayers!.Last().Id;
        if (everyone != BoundRule.Off) t.S.Do(new SetBoundRule { LayerId = id, Rule = everyone });
        return id;
    }

    static async Task<Walker> Walking(TestRunner r, OfflineTable t, Vector3 at, float yaw)
    {
        var walk = t.Table.Walk;
        t.S.Me.Role = Role.Player;
        walk.FollowRole();
        await r.Frames(2);
        walk.PlacedAt(at, yaw, null);
        await r.Frames(3);
        walk.Steer = () => new Steering(yaw, 0, new Vector2(0, 1), true);
        return walk;
    }

    [NodeTest]
    static async Task A_player_kept_in_a_yard_slides_along_its_edge_and_never_leaves(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        Square(t, 0, 4, BoundRule.In);
        var walk = await Walking(r, t, new Vector3(2, 0, -2), Mathf.Pi / 2);
        try
        {
            await r.Seconds(2);
            Check.That(walk.Feet.X < 4.05f && walk.Feet.X > 3, $"they stop at the yard's edge: {walk.Feet}");
            var space = t.Table.Fences.For(t.S.Me.Name);
            Check.That(t.Table.Fences.Allows(space, walk.Feet), "and stay inside it");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_player_kept_out_of_a_room_stops_at_its_wall_and_walks_freely_elsewhere(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        Square(t, 4, 7, BoundRule.Out);
        var walk = await Walking(r, t, new Vector3(1, 0, -2), Mathf.Pi / 2);
        try
        {
            await r.Seconds(2);
            Check.That(walk.Feet.X < 4.01f && walk.Feet.X > 3, $"they stop at the room's wall: {walk.Feet}");
            walk.Steer = () => new Steering(0, 0, new Vector2(0, 1), true);
            await r.Seconds(0.6);
            Check.That(walk.Feet.Z > -1.5f, $"and walk away along it: {walk.Feet}");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task The_host_sends_back_a_player_who_strays_and_moves_in_those_left_outside_when_bounds_change(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        try
        {
            var yard = Square(t, 0, 4, BoundRule.Off);
            float h = AvatarPose.Usual;
            Check.That(t.Table.Fence(Guest, "Ana", new AvatarPose(new Vector3(9, 0, -2), 0, h)) == null, "an unbound player goes anywhere");
            t.S.Do(new SetBoundRule { LayerId = yard, Rule = BoundRule.In });
            await r.Frames(2);
            var strays = t.Table.Strays();
            Check.That(strays.Count == 1 && strays[0].Peer == Guest && strays[0].Spot.X < 4 && strays[0].Spot.X > 3.5f, $"the one left outside is moved to the nearest spot: {string.Join(",", strays)}");
            Check.That(t.Table.Fence(Guest, "Ana", new AvatarPose(new Vector3(3, 0, -2), 0, h)) == null, "moving inside is fine");
            Check.That(t.Table.Fence(Guest, "Ana", new AvatarPose(new Vector3(7, 0, -2), 0, h)) is { } back && back.X < 4, "stepping out is sent back");
            t.Table.PlayerPlaced(Guest, "Ana", new Vector3(9, 0, -2), 0);
            Check.That(t.Table.Fence(Guest, "Ana", new AvatarPose(new Vector3(8.5f, 0, -2), 0, h)) == null, "a player the umpire put outside may walk back in");
            Check.That(t.Table.Fence(Guest, "Ana", new AvatarPose(new Vector3(10, 0, -2), 0, h)) != null, "but not farther out");
        }
        finally
        {
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_bound_drawn_on_a_piece_travels_with_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        try
        {
            var piece = t.S.View.Layer(Layers.Umpire)[0];
            Square(t, -1, 1, BoundRule.In, piece.Id);
            float Middle() => t.Table.Fences.For("Ana").In[0].Ring.Average(v => v.X);
            float before = Middle();
            t.S.Do(new MovePiece { Layer = Layers.Umpire, Id = piece.Id, Pos = piece.Pos + new Vec2(5, 0), RotDeg = piece.RotDeg });
            await r.Seconds(1);
            Check.That(Mathf.Abs(Middle() - before - 5) < 0.2f, $"the bound moved with the piece: {before} then {Middle()}");
        }
        finally
        {
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task The_bounds_panel_lists_layers_with_everyone_and_each_player(TestRunner r)
    {
        var h = await HudTests.Host(r);
        try
        {
            h.Table.Bounding.Open = true;
            h.S.Do(new SetBoundLayer { Bound = new BoundLayer { Name = "Cellar" } });
            h.Hud.Fencing!.Panel.Visible = true;
            h.Hud.Fencing.Rebuild();
            await r.Frames(2);
            var texts = h.Hud.Fencing.Panel.FindChildren("*", "Button", true, false).OfType<Button>().Select(b => b.Text).ToList();
            Check.That(texts.Contains("Cellar") && texts.Contains("Off"), $"the layer and Everyone's Off show: {string.Join("|", texts)}");
        }
        finally
        {
            h.Hud.QueueFree();
            h.Table.QueueFree();
            h.Net.QueueFree();
        }
    }
}

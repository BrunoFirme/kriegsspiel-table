using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class BoundDemo(Main main, Table table, Hud hud, string shots)
{
    const float SideMetres = 8f;

    SceneTree Tree => main.GetTree();

    public async Task Run()
    {
        await Wait(3);
        var s = main.Session;
        float metre = AvatarPose.UnitsPerMetre(s.View), half = SideMetres * metre / 2;
        var middle = Coords.FromWorld(table.Rig.Target);
        var square = new BoundLayer { Name = "Yard" };
        double ground = s.Ground.At(middle);
        square.Shapes.Add(new BoundShape
        {
            Polygon = { middle + new Vec2(-half, -half), middle + new Vec2(half, -half), middle + new Vec2(half, half), middle + new Vec2(-half, half) },
            Floor = ground - metre, Ceiling = ground + 4 * metre,
        });
        s.Do(new SetBoundLayer { Bound = square });
        await Wait(0.5);
        var id = s.View.BoundLayers![^1].Id;
        s.Do(new SetBoundRule { LayerId = id, Rule = BoundRule.In });
        hud.Fencing!.Panel.Visible = true;
        table.Bounding.Pick(id);
        table.Bounding.Select(s.View.BoundLayers![^1].Shapes[0].Id);
        table.Rig.Show(new CameraRig.View(table.Rig.Target, SideMetres * metre * 2.2f, table.Rig.Yaw, -0.7f, false));
        await Wait(1.5);
        Shot("umpire");
        hud.Fencing.Panel.Visible = false;
        s.Me.Role = Role.Player;
        table.Walk.FollowRole();
        await Wait(0.3);
        table.Walk.PlacedAt(Coords.ToWorld(middle, (float)ground), Mathf.Pi / 2, null);
        table.Walk.Steer = () => new Steering(Mathf.Pi / 2 + 0.35f, -0.05f, new Vector2(0, 1), true);
        await Wait(2.2);
        GD.Print($"[bounds] feet {table.Walk.Feet} middle {Coords.ToWorld(middle)} half {half}");
        Shot("player");
        table.Walk.ThirdPerson = true;
        await Wait(0.6);
        Shot("player-third");
        table.Walk.Leave();
        s.Me.Role = Role.HostUmpire;
        s.Do(new SpawnPiece { Layer = Layers.Umpire, Id = "demo-inn", Def = "std.adventuring.tavern", Pos = middle + new Vec2(half * 4, 0) });
        await Wait(4);
        if (table.Pieces.NodeOf(Layers.Umpire, "demo-inn") is { } inn && AvatarSolids.Where(inn) is { } frame)
        {
            float unit = metre / Mathf.Max(frame.Basis.Y.Length(), 1e-6f);
            WallSlice.Snap(inn, 1.6f * unit, Vector2.Zero, unit);
            await Wait(1.5);
            foreach (var probe in new[] { new Vector2(0, 0), new Vector2(3, 2), new Vector2(-4, 1) })
                GD.Print($"[bounds] snap near {probe * unit} -> {WallSlice.Snap(inn, 1.6f * unit, probe * unit, 2f * unit)}");
        }
        GD.Print("[bounds] done");
    }

    void Shot(string name)
    {
        var path = System.IO.Path.Combine(shots, $"bounds-{name}.png");
        main.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[bounds] shot {path}");
    }

    async Task Wait(double s) => await main.ToSignal(Tree.CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}

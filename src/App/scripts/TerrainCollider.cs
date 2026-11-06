using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class TerrainCollider
{
    public const float Span = 80;
    const int Cells = 64;
    const float FloorSize = 100000;

    public static void AddGround(Node3D world, Ground ground, Vector3 centre, float span = Span)
    {
        float lowest = 0;
        if (ground.Any) world.AddChild(Relief(ground, centre, span, out lowest));
        var floor = new StaticBody3D { Position = new Vector3(0, Mathf.Min(0, lowest) - 0.5f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(FloorSize, 1, FloorSize) } });
        world.AddChild(floor);
    }

    static StaticBody3D Relief(Ground g, Vector3 centre, float span, out float lowest)
    {
        float step = span / Cells, x0 = centre.X - span / 2, z0 = centre.Z - span / 2;
        var heights = new float[Cells + 1, Cells + 1];
        lowest = float.MaxValue;
        for (int j = 0; j <= Cells; j++)
            for (int i = 0; i <= Cells; i++)
            {
                heights[i, j] = (float)g.At(Coords.FromWorld(new Vector3(x0 + i * step, 0, z0 + j * step)));
                lowest = Mathf.Min(lowest, heights[i, j]);
            }
        Vector3 P(int i, int j) => new(x0 + i * step, heights[i, j], z0 + j * step);
        var faces = new Vector3[Cells * Cells * 6];
        int k = 0;
        for (int j = 0; j < Cells; j++)
            for (int i = 0; i < Cells; i++)
            {
                faces[k++] = P(i, j);
                faces[k++] = P(i + 1, j);
                faces[k++] = P(i, j + 1);
                faces[k++] = P(i + 1, j);
                faces[k++] = P(i + 1, j + 1);
                faces[k++] = P(i, j + 1);
            }
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true } });
        return body;
    }
}

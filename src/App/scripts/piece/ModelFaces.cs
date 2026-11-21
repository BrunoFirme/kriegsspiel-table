using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace KTable.App;

static class ModelFaces
{
    static readonly ConditionalWeakTable<Mesh, Vector3[]> Faces = new();

    public static Vector3[] Within(MeshInstance3D model, Rect2 ground, int mostTriangles)
    {
        var mesh = model.Mesh;
        var xf = model.TableTransform();
        var bounds = xf * mesh.GetAabb();
        if (!ground.Intersects(new Rect2(bounds.Position.X, bounds.Position.Z, bounds.Size.X, bounds.Size.Z), includeBorders: true)) return [];
        var faces = Faces.GetValue(mesh, m => m.GetFaces());
        var kept = new List<Vector3>();
        float x0 = ground.Position.X, z0 = ground.Position.Y, x1 = ground.End.X, z1 = ground.End.Y;
        for (int i = 0; i + 2 < faces.Length; i += 3)
        {
            Vector3 a = xf * faces[i], b = xf * faces[i + 1], c = xf * faces[i + 2];
            if (Mathf.Max(a.X, Mathf.Max(b.X, c.X)) < x0 || Mathf.Min(a.X, Mathf.Min(b.X, c.X)) > x1
                || Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) < z0 || Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) > z1) continue;
            if (kept.Count / 3 >= mostTriangles) return [];
            kept.Add(a);
            kept.Add(b);
            kept.Add(c);
        }
        return kept.ToArray();
    }
}

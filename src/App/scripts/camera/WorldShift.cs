using Godot;

namespace KTable.App;

static class WorldShift
{
    const string ShaderShift = "table_shift";

    static Node3D? _world;

    public static Vector3 By { get; private set; }

    public static Vector3 ToGlobal(Vector3 table) => new(table.X - By.X, table.Y, table.Z - By.Z);

    public static Vector3 ToTable(Vector3 global) => new(global.X + By.X, global.Y, global.Z + By.Z);

    public static Transform3D ToTable(Transform3D global) => new(global.Basis, ToTable(global.Origin));

    public static Transform3D ToGlobal(Transform3D table) => new(table.Basis, ToGlobal(table.Origin));

    public static Plane ToTable(Plane global) => new(global.Normal, global.D + global.Normal.X * By.X + global.Normal.Z * By.Z);

    public static Godot.Collections.Array<Plane> ToTable(Godot.Collections.Array<Plane> global)
    {
        var table = new Godot.Collections.Array<Plane>();
        foreach (var plane in global) table.Add(ToTable(plane));
        return table;
    }

    public static Vector3 TablePosition(this Node3D node) => ToTable(node.GlobalPosition);

    public static Transform3D TableTransform(this Node3D node) => ToTable(node.GlobalTransform);

    public static bool Moves(Node3D world) => _world == world;

    public static void Shifts(Node3D world)
    {
        _world = world;
        Set(Vector3.Zero);
    }

    public static void Leave(Node3D world)
    {
        if (_world != world) return;
        Set(Vector3.Zero);
        _world = null;
    }

    public static void Set(Vector3 by)
    {
        by = new Vector3(by.X, 0, by.Z);
        if (by == By && _world?.Position == -by) return;
        By = by;
        if (_world != null && GodotObject.IsInstanceValid(_world)) _world.Position = -by;
        RenderingServer.GlobalShaderParameterSet(ShaderShift, by);
    }
}

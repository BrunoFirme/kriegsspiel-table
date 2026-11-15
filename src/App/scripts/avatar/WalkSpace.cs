using Godot;

namespace KTable.App;

sealed class WalkSpace(float scale)
{
    public float Scale { get; } = scale;
    public Vector3 Anchor { get; set; }

    public Vector3 In(Vector3 table) => (table - Anchor) * Scale;

    public Vector3 Out(Vector3 physics) => physics / Scale + Anchor;

    public Transform3D In(Transform3D table) => new(new Basis(table.Basis.X * Scale, table.Basis.Y * Scale, table.Basis.Z * Scale), In(table.Origin));
}

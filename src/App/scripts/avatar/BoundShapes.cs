using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class BoundShapes
{
    const float Fill = 0.07f, FillPicked = 0.14f, Line = 0.55f, LinePicked = 0.9f;

    static readonly Color[] Tints =
    [
        new(0.50f, 0.70f, 0.84f), new(0.90f, 0.71f, 0.40f), new(0.64f, 0.77f, 0.53f),
        new(0.85f, 0.53f, 0.50f), new(0.73f, 0.56f, 0.81f), new(0.46f, 0.84f, 0.77f),
    ];

    readonly TableContext _c;
    readonly BoundEditor _edit;
    readonly System.Func<string, PieceNode?> _node;
    readonly List<(Node3D Holder, string Piece)> _riding = new();
    Node3D? _root;
    long _seq = -1;
    int _version, _drawnVersion = -1;

    public BoundShapes(TableContext c, BoundEditor edit, System.Func<string, PieceNode?> node)
    {
        (_c, _edit, _node) = (c, edit, node);
        edit.Changed += () => _version++;
    }

    public static Color TintOf(int index) => Tints[index % Tints.Length];

    public void Tick()
    {
        if (_c.S.View.LastSeq != _seq || _version != _drawnVersion) Rebuild();
        foreach (var (holder, piece) in _riding)
        {
            var frame = _node(piece) is { } n ? AvatarSolids.Where(n) : null;
            holder.Visible = frame != null;
            if (frame is { } f) holder.Transform = f;
        }
    }

    void Rebuild()
    {
        (_seq, _drawnVersion) = (_c.S.View.LastSeq, _version);
        if (_root == null)
        {
            _root = new Node3D { Name = "Bounds" };
            _c.Scene.AddChild(_root);
        }
        foreach (var child in _root.GetChildren()) child.QueueFree();
        _riding.Clear();
        if (!_c.S.IsUmpire || _c.S.View.BoundLayers is not { } layers) return;
        for (int i = 0; i < layers.Count; i++)
        {
            if (!_edit.Shown(layers[i].Id)) continue;
            bool picked = layers[i].Id == _edit.Layer;
            foreach (var shape in layers[i].Shapes)
            {
                var holder = new Node3D();
                holder.AddChild(Prism(shape, TintOf(i), picked || shape.Id == _edit.Selected));
                _root.AddChild(holder);
                if (shape.On != null) _riding.Add((holder, shape.On));
            }
        }
    }

    static MeshInstance3D Prism(BoundShape shape, Color tint, bool picked)
    {
        var ring = new List<Vector2>();
        foreach (var p in shape.Polygon) ring.Add(shape.On == null ? new Vector2((float)p.X, (float)-p.Z) : new Vector2((float)p.X, (float)p.Z));
        float floor = (float)shape.Floor, ceiling = (float)shape.Ceiling;
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, Look(tint with { A = picked ? FillPicked : Fill }));
        for (int i = 0; i < ring.Count; i++)
        {
            var (a, b) = (ring[i], ring[(i + 1) % ring.Count]);
            foreach (var v in new[] { (a, floor), (b, floor), (b, ceiling), (a, floor), (b, ceiling), (a, ceiling) })
                mesh.SurfaceAddVertex(new Vector3(v.Item1.X, v.Item2, v.Item1.Y));
        }
        mesh.SurfaceEnd();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, Look(tint with { A = picked ? LinePicked : Line }));
        for (int i = 0; i < ring.Count; i++)
        {
            var (a, b) = (ring[i], ring[(i + 1) % ring.Count]);
            foreach (var y in new[] { floor, ceiling })
            {
                mesh.SurfaceAddVertex(new Vector3(a.X, y, a.Y));
                mesh.SurfaceAddVertex(new Vector3(b.X, y, b.Y));
            }
            mesh.SurfaceAddVertex(new Vector3(a.X, floor, a.Y));
            mesh.SurfaceAddVertex(new Vector3(a.X, ceiling, a.Y));
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer };
    }

    static StandardMaterial3D Look(Color colour) => new()
    {
        AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };
}

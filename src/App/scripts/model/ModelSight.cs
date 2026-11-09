using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class ModelSight
{
    const float DepthSlack = 0.0015f, NearSlack = 0.05f, EdgeSlack = 1e-4f;
    const int Around = 1;

    public readonly struct Lens
    {
        readonly Vector2 _size;
        readonly Projection _proj;
        readonly Transform3D _view;
        readonly float _near;

        public Lens(Vector2 size, Camera3D cam)
        {
            _size = size;
            _proj = cam.GetCameraProjection();
            _view = cam.GlobalTransform.AffineInverse();
            _near = cam.Near;
        }

        public float Near => _near;

        public bool Project(Vector3 w, out Vector2 s, out float z)
        {
            var v = _view * w;
            z = -v.Z;
            s = default;
            if (z <= _near * 0.5f) return false;
            var c = _proj * new Vector4(v.X, v.Y, v.Z, 1);
            s = new Vector2((c.X / c.W * 0.5f + 0.5f) * _size.X, (1 - (c.Y / c.W * 0.5f + 0.5f)) * _size.Y);
            return true;
        }
    }

    public sealed record Seen(ModelPartView View, int Place, Vector2[] S, float[] Z, bool[] Ok, Vector3[] W);

    public readonly Lens Eye;
    readonly List<Seen> _slots = new();
    readonly Dictionary<(string Part, int Place), int> _slotOf = new();
    readonly Transform3D _camAt;
    readonly float _fov;
    readonly Vector2 _size;
    Rect2I _box;
    float[] _depth = Array.Empty<float>();
    int[] _slot = Array.Empty<int>(), _tri = Array.Empty<int>();

    public ModelSight(Vector2 size, Camera3D cam, Transform3D stand, IEnumerable<ModelPartView> parts)
    {
        Eye = new Lens(size, cam);
        _camAt = cam.GlobalTransform;
        _fov = cam.Fov;
        _size = size;
        foreach (var v in parts)
        {
            var places = v.Places;
            for (int p = 0; p < places.Length; p++)
            {
                var xf = stand * places[p];
                int n = v.Data.V.Count;
                var s = new Vector2[n];
                var z = new float[n];
                var ok = new bool[n];
                var w = new Vector3[n];
                for (int i = 0; i < n; i++) { w[i] = xf * v.Data.V[i]; ok[i] = Eye.Project(w[i], out s[i], out z[i]); }
                _slotOf[(v.Part.Id, p)] = _slots.Count;
                _slots.Add(new Seen(v, p, s, z, ok, w));
            }
        }
    }

    public bool Fits(Camera3D cam, Vector2 size) => cam.GlobalTransform.IsEqualApprox(_camAt) && Mathf.IsEqualApprox(cam.Fov, _fov) && size == _size;

    public int Slot(ModelPartView v, int place) => _slotOf.TryGetValue((v.Part.Id, place), out var s) ? s : -1;

    public Seen this[int slot] => _slots[slot];

    public void Look(Rect2I box)
    {
        _box = box;
        int x0 = box.Position.X, y0 = box.Position.Y, x1 = box.End.X - 1, y1 = box.End.Y - 1, rw = box.Size.X;
        int cells = Math.Max(0, box.Size.X * box.Size.Y);
        if (_depth.Length != cells)
        {
            _depth = new float[cells];
            _slot = new int[cells];
            _tri = new int[cells];
        }
        Array.Fill(_depth, float.MaxValue);
        Array.Fill(_slot, -1);
        for (int si = 0; si < _slots.Count; si++)
        {
            var (_, _, s, z, ok, _) = _slots[si];
            int tris = s.Length / 3;
            for (int t = 0; t < tris; t++)
            {
                int a = t * 3, b = a + 1, c = a + 2;
                if (!ok[a] || !ok[b] || !ok[c]) continue;
                float bx0 = Mathf.Min(s[a].X, Mathf.Min(s[b].X, s[c].X)), bx1 = Mathf.Max(s[a].X, Mathf.Max(s[b].X, s[c].X));
                float by0 = Mathf.Min(s[a].Y, Mathf.Min(s[b].Y, s[c].Y)), by1 = Mathf.Max(s[a].Y, Mathf.Max(s[b].Y, s[c].Y));
                if (bx1 < x0 || bx0 > x1 + 1 || by1 < y0 || by0 > y1 + 1) continue;
                float den = (s[b].Y - s[c].Y) * (s[a].X - s[c].X) + (s[c].X - s[b].X) * (s[a].Y - s[c].Y);
                if (Mathf.Abs(den) < 1e-9f) continue;
                int px0 = Math.Max(x0, (int)bx0), px1 = Math.Min(x1, (int)bx1), py0 = Math.Max(y0, (int)by0), py1 = Math.Min(y1, (int)by1);
                for (int py = py0; py <= py1; py++)
                    for (int px = px0; px <= px1; px++)
                    {
                        float qx = px + 0.5f, qy = py + 0.5f;
                        float w0 = ((s[b].Y - s[c].Y) * (qx - s[c].X) + (s[c].X - s[b].X) * (qy - s[c].Y)) / den;
                        float w1 = ((s[c].Y - s[a].Y) * (qx - s[c].X) + (s[a].X - s[c].X) * (qy - s[c].Y)) / den;
                        float w2 = 1 - w0 - w1;
                        if (w0 < -EdgeSlack || w1 < -EdgeSlack || w2 < -EdgeSlack) continue;
                        float zz = 1 / (w0 / z[a] + w1 / z[b] + w2 / z[c]);
                        int k = (py - y0) * rw + (px - x0);
                        if (zz >= _depth[k]) continue;
                        _depth[k] = zz;
                        _slot[k] = si;
                        _tri[k] = t;
                    }
            }
        }
    }

    public bool Sees(int slot, int tri, Vector3 world) => Eye.Project(world, out var sp, out var zp) && Sees(slot, tri, sp, zp);

    public bool Sees(int slot, int tri, Vector2 sp, float zp)
    {
        int px = (int)sp.X, py = (int)sp.Y;
        int x0 = _box.Position.X, y0 = _box.Position.Y, x1 = _box.End.X - 1, y1 = _box.End.Y - 1, rw = _box.Size.X;
        if (px < x0 || px > x1 || py < y0 || py > y1) return false;
        for (int dy = -Around; dy <= Around; dy++)
            for (int dx = -Around; dx <= Around; dx++)
            {
                int qx = px + dx, qy = py + dy;
                if (qx < x0 || qx > x1 || qy < y0 || qy > y1) continue;
                int k = (qy - y0) * rw + (qx - x0);
                if (_slot[k] == slot && _tri[k] == tri) return true;
            }
        float zb = _depth[(py - y0) * rw + (px - x0)];
        return zp <= zb * (1 + DepthSlack) + Eye.Near * NearSlack;
    }
}

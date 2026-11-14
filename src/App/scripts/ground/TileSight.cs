using System;
using Godot;

namespace KTable.App;

public readonly record struct TileSight
{
    public const float MostStretch = 4, Sharper = 0.25f, MostRelaxed = 4, RelaxStep = 0.25f;
    const int Across = 5;
    const float LeastSpan = 1e-12f;
    readonly Transform3D _toEye;
    readonly Projection _lens;
    readonly Vector2 _screen;
    readonly float _near;

    public enum Call { Unseen, Met, Finer }

    public TileSight(Camera3D cam)
    {
        var view = cam.GetViewport();
        _toEye = cam.GlobalTransform.AffineInverse();
        _lens = cam.GetCameraProjection();
        _screen = view.GetVisibleRect().Size * view.Scaling3DScale;
        _near = cam.Near;
    }

    public TileSight(Transform3D eye, Projection lens, Vector2 screen, float near)
    {
        _toEye = eye.AffineInverse();
        _lens = lens;
        _screen = screen;
        _near = near;
    }

    public static float Called(Vector2 perPixelAcross, Vector2 perPixelDown, float relaxed) => Level(perPixelAcross, perPixelDown, MathF.Pow(2, -relaxed));

    static float Level(Vector2 perPixelAcross, Vector2 perPixelDown, float kept)
    {
        float across = perPixelAcross.Length(), down = perPixelDown.Length();
        float major = Math.Max(Math.Max(across, down), LeastSpan), ratio = major / Math.Max(Math.Min(across, down), LeastSpan);
        float stretch = Math.Min(Math.Min(ratio, MostStretch), MathF.Sqrt(MostStretch * ratio) * kept);
        return Sharper - MathF.Log2(major / stretch * (GroundTiles.TilePx - 2 * GroundTiles.GutterPx));
    }

    public Ground Of(Func<Vector2, float, Vector3> world)
    {
        Vector3 origin = _toEye * world(Vector2.Zero, 0), up = _toEye * world(Vector2.Zero, 1) - origin;
        Vector3 east = _toEye * world(Vector2.Right, 0) - origin, south = _toEye * world(Vector2.Down, 0) - origin;
        return new Ground(Clip(origin, 1), Clip(east, 0), Clip(south, 0), Clip(up, 0), new Vector4(origin.Z, east.Z, south.Z, up.Z), _screen / 2, _near);
    }

    Vector4 Clip(Vector3 eye, float w) => _lens * new Vector4(eye.X, eye.Y, eye.Z, w);

    public readonly struct Ground
    {
        readonly Vector4 _origin, _east, _south, _up, _depth;
        readonly Vector2 _half;
        readonly float _near;

        internal Ground(Vector4 origin, Vector4 east, Vector4 south, Vector4 up, Vector4 depth, Vector2 half, float near) =>
            (_origin, _east, _south, _up, _depth, _half, _near) = (origin, east, south, up, depth, half, near);

        public bool At(float u, float v, float height, out Vector2 at)
        {
            var clip = _origin + _east * u + _south * v + _up * height;
            at = new Vector2((clip.X / clip.W + 1) * _half.X, (1 - clip.Y / clip.W) * _half.Y);
            return _depth.X + _depth.Y * u + _depth.Z * v + _depth.W * height <= -_near;
        }

        public bool Outside(Rect2 uv, float low, float high)
        {
            bool left = true, right = true, under = true, over = true, behind = true;
            for (int c = 0; c < 8; c++)
            {
                float u = (c & 1) == 0 ? uv.Position.X : uv.End.X, v = (c & 2) == 0 ? uv.Position.Y : uv.End.Y, h = (c & 4) == 0 ? low : high;
                var clip = _origin + _east * u + _south * v + _up * h;
                (left, right, under, over) = (left && clip.X < -clip.W, right && clip.X > clip.W, under && clip.Y < -clip.W, over && clip.Y > clip.W);
                behind &= _depth.X + _depth.Y * u + _depth.Z * v + _depth.W * h > -_near;
            }
            return left || right || under || over || behind;
        }

        public float? Called(Vector2 uv, float height, Vector2 slope, float relaxed) => Level(uv, height, slope, MathF.Pow(2, -relaxed));

        float? Level(Vector2 uv, float height, Vector2 slope, float kept)
        {
            if (_depth.X + _depth.Y * uv.X + _depth.Z * uv.Y + _depth.W * height > -_near) return null;
            var clip = _origin + _east * uv.X + _south * uv.Y + _up * height;
            if (Math.Abs(clip.X) > Math.Abs(clip.W) || Math.Abs(clip.Y) > Math.Abs(clip.W)) return null;
            Vector4 east = _east + _up * slope.X, south = _south + _up * slope.Y;
            float w2 = clip.W * clip.W;
            var a = new Vector2((east.X * clip.W - clip.X * east.W) * _half.X, (east.Y * clip.W - clip.Y * east.W) * -_half.Y) / w2;
            var b = new Vector2((south.X * clip.W - clip.X * south.W) * _half.X, (south.Y * clip.W - clip.Y * south.W) * -_half.Y) / w2;
            float det = a.X * b.Y - a.Y * b.X;
            if (Math.Abs(det) < LeastSpan) return null;
            return TileSight.Level(new Vector2(b.Y, -a.Y) / det, new Vector2(-b.X, a.X) / det, kept);
        }

        static float Steeper(float before, float after) => Math.Abs(before) > Math.Abs(after) ? before : after;

        public Call Calls(Rect2 uv, Func<Vector2, float> rise, float relaxed, int level)
        {
            Span<float> high = stackalloc float[Across * Across];
            var step = uv.Size / (Across - 1);
            for (int j = 0; j < Across; j++)
                for (int i = 0; i < Across; i++) high[j * Across + i] = rise(uv.Position + step * new Vector2(i, j));
            float kept = MathF.Pow(2, -relaxed);
            bool seen = false;
            for (int j = 0; j < Across; j++)
                for (int i = 0; i < Across; i++)
                {
                    float here = high[j * Across + i];
                    var slope = new Vector2(Steeper(here - high[j * Across + Math.Max(i - 1, 0)], high[j * Across + Math.Min(i + 1, Across - 1)] - here) / step.X,
                        Steeper(here - high[Math.Max(j - 1, 0) * Across + i], high[Math.Min(j + 1, Across - 1) * Across + i] - here) / step.Y);
                    if (Level(uv.Position + step * new Vector2(i, j), here, slope, kept) is not { } called) continue;
                    if (called > level) return Call.Finer;
                    seen = true;
                }
            return seen ? Call.Met : Call.Unseen;
        }
    }
}

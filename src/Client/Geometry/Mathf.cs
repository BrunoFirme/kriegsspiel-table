namespace KTable.Client.Geometry;

static class Mathf
{
    public const float Tau = (float)Math.PI * 2f;
    public const float Pi = (float)Math.PI;

    public static float Abs(float s) => Math.Abs(s);
    public static int Abs(int s) => Math.Abs(s);
    public static float Sin(float s) => MathF.Sin(s);
    public static float Cos(float s) => MathF.Cos(s);
    public static (float Sin, float Cos) SinCos(float s) => MathF.SinCos(s);
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    public static float Sqrt(float s) => MathF.Sqrt(s);
    public static float Pow(float x, float y) => MathF.Pow(x, y);
    public static float Floor(float s) => MathF.Floor(s);
    public static float Ceil(float s) => MathF.Ceiling(s);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    public static int Sign(float s) => Math.Sign(s);
    public static float Lerp(float from, float to, float weight) => from + (to - from) * weight;
    public static float DegToRad(float deg) => deg * ((float)Math.PI / 180f);
    public static float RadToDeg(float rad) => rad * 57.29578f;

    public static float Wrap(float value, float min, float max)
    {
        float range = max - min;
        if (Math.Abs(range) < 1E-06f) return min;
        return min + ((value - min) % range + range) % range;
    }
}

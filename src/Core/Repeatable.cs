using System.Globalization;

namespace KTable.Core;

public static class Repeatable
{
    public const string Variable = "KTABLE_REPEATABLE";

    static readonly Settings? S = Read(Environment.GetEnvironmentVariable(Variable));
    static long _reads = -1, _made;
    static readonly Random? IdSource = S == null ? null : new Random(Mix(S.Seed, -1));
    static readonly Random? SharedSeeded = S == null ? null : new Locked(new Random(Mix(S.Seed, 0)));

    sealed record Settings(int Seed, DateTime Start);

    public static bool On => S != null;

    public static int? Seed => S?.Seed;

    public static Random NewRandom() =>
        S == null ? new Random() : new Random(Mix(S.Seed, Interlocked.Increment(ref _made)));

    public static Random Shared => SharedSeeded ?? Random.Shared;

    public static DateTime UtcNow => S == null ? DateTime.UtcNow : Tick();

    public static DateTime Now => S == null ? DateTime.Now : Tick().ToLocalTime();

    public static Guid NewGuid()
    {
        if (IdSource == null) return Guid.NewGuid();
        var b = new byte[16];
        lock (IdSource) IdSource.NextBytes(b);
        const int VersionByte = 7, VariantByte = 8;
        const int Version4 = 0x40, Rfc4122Variant = 0x80;
        b[VersionByte] = (byte)((b[VersionByte] & 0x0F) | Version4);
        b[VariantByte] = (byte)((b[VariantByte] & 0x3F) | Rfc4122Variant);
        return new Guid(b);
    }

    static DateTime Tick() => S!.Start.AddMilliseconds(Interlocked.Increment(ref _reads));

    static Settings? Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        int seed = 1;
        var start = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq < 0 ? part : part[..eq].Trim();
            var v = eq < 0 ? "" : part[(eq + 1)..].Trim();
            if (key.Equals("seed", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) seed = n;
            else if (key.Equals("clock", StringComparison.OrdinalIgnoreCase)
                     && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                         out var t)) start = DateTime.SpecifyKind(t, DateTimeKind.Utc);
            else
            {
                BestEffort.Try(() => Console.Error.WriteLine($"{Variable}: cannot read '{part}' (expected seed=<integer>,clock=<ISO 8601 instant>); runs are not repeatable."));
                return null;
            }
        }
        return new Settings(seed, start);
    }

    static int Mix(int seed, long n)
    {
        unchecked
        {
            var z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)n * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z & 0x7FFFFFFF);
        }
    }

    sealed class Locked : Random
    {
        readonly Random _r;
        public Locked(Random r) { _r = r; }
        public override int Next() { lock (_r) return _r.Next(); }
        public override int Next(int maxValue) { lock (_r) return _r.Next(maxValue); }
        public override int Next(int minValue, int maxValue) { lock (_r) return _r.Next(minValue, maxValue); }
        public override long NextInt64() { lock (_r) return _r.NextInt64(); }
        public override long NextInt64(long maxValue) { lock (_r) return _r.NextInt64(maxValue); }
        public override long NextInt64(long minValue, long maxValue) { lock (_r) return _r.NextInt64(minValue, maxValue); }
        public override double NextDouble() { lock (_r) return _r.NextDouble(); }
        public override float NextSingle() { lock (_r) return _r.NextSingle(); }
        public override void NextBytes(byte[] buffer) { lock (_r) _r.NextBytes(buffer); }
        public override void NextBytes(Span<byte> buffer) { lock (_r) _r.NextBytes(buffer); }
        protected override double Sample() { lock (_r) return _r.NextDouble(); }
    }
}

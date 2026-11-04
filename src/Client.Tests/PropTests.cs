using System.Security.Cryptography;
using System.Text;
using KTable.Client.Scenery;
using N = System.Numerics;

namespace KTable.Client.Tests;

public class PropTests
{
    sealed class LatticeNoise(int seed, float freq) : IPropNoise
    {
        public float GetNoise2D(float x, float y) => Get(x, y, 0);
        public float GetNoise3D(float x, float y, float z) => Get(x, y, z);

        float Get(float x, float y, float z)
        {
            int ix = (int)MathF.Floor(x * freq * 8), iy = (int)MathF.Floor(y * freq * 8), iz = (int)MathF.Floor(z * freq * 8);
            uint h = unchecked((uint)(ix * 73856093) ^ (uint)(iy * 19349663) ^ (uint)(iz * 83492791) ^ (uint)seed * 2654435761u);
            h ^= h >> 13;
            h = unchecked(h * 0x5bd1e995u);
            h ^= h >> 15;
            return (h & 0xFFFF) / 32767.5f - 1;
        }
    }

    static readonly PropMaker Maker = new((seed, freq) => new LatticeNoise(seed, freq));

    static PropMade Make(string name) => name switch
    {
        "Bivouac" => Maker.Bivouac(),
        "Picket post" => Maker.PicketPost(),
        "Bivouac fire" => Maker.CampFire(),
        "Czech hedgehog" => Maker.Hedgehog(),
        "Shell crater" => Maker.Crater(0.35f, 3),
        "Large crater" => Maker.Crater(0.8f, 7),
        "Trench" => Maker.Trench(new[] { new N.Vector2(-1.5f, 0), new N.Vector2(1.5f, 0) }),
        "Barricade" => Maker.Barricade(),
        "Breastwork" => Maker.Breastwork(),
        "Earthwork rampart" => Maker.Earthwork(),
        "Chevaux de frise" => Maker.ChevauxDeFrise(),
        "Stakes" => Maker.Stakes(),
        "Palisade" => Maker.Palisade(),
        "Moat" => Maker.Moat(),
        "Trench with traverse" =>
            Maker.Trench(new[] { new N.Vector2(-1.6f, 0.35f), new N.Vector2(-0.55f, 0.35f), new N.Vector2(-0.55f, -0.35f), new N.Vector2(0.55f, -0.35f), new N.Vector2(0.55f, 0.35f),
                new N.Vector2(1.6f, 0.35f) }),
        _ => throw new ArgumentException(name),
    };

    public static TheoryData<string, int, int, ulong, string, string> Props => new()
    {
        { "Bivouac", 32574, 11952, 0xFCFE483FB69AF0B1UL, "aee63a14b869026f", "411a27feeae65252" },
        { "Picket post", 10407, 4680, 0x49C30E7502C7DF9AUL, "cf58afa663898984", "46411b8bb4fb9ffc" },
        { "Bivouac fire", 6354, 3000, 0x749376AE13BA77E0UL, "53152af49ba2b965", "e74eb5b51c18604a" },
        { "Czech hedgehog", 456, 0, 0x1A21EE510A64C829UL, "9cd72d2e9e7433fe", "9ab4fcca88f0b778" },
        { "Shell crater", 11424, 0, 0x5EA72B14C995092EUL, "97b8312c7a77b5ad", "1d6d7ad0f9b4c68c" },
        { "Large crater", 13824, 0, 0xCC5499DE37A0A1AFUL, "07242e38d274a3de", "ae49de5c4938524c" },
        { "Trench", 58056, 21888, 0xE534899216BCB2C4UL, "87e47fb0aeb8247d", "928d09f21c648c9c" },
        { "Barricade", 54828, 0, 0x4CB5B1C11745FA7CUL, "5d7e9bb03bd08e29", "500275575ec1f54e" },
        { "Breastwork", 27963, 0, 0xB09526A591BF11D3UL, "f02f01d5f9f67c98", "d7c2ecaac7d33107" },
        { "Earthwork rampart", 38211, 0, 0xAC73C87E45E8A99BUL, "f8412e2076f5f737", "f0df0591671526cd" },
        { "Chevaux de frise", 3198, 0, 0x7653758926439C84UL, "38f952c29eb60f65", "c3ea904ae34afe94" },
        { "Stakes", 13110, 0, 0xB94F478F311D880BUL, "a464784f95ff5f0d", "66fd7f8d35421d59" },
        { "Palisade", 12666, 0, 0x2FDF4AED79A01D74UL, "469dac1667fef1b0", "0bd9192381478483" },
        { "Moat", 51831, 0, 0x24BB4C9AAB9D84FAUL, "ed049e0ac81b744d", "6c77a0be96865592" },
        { "Trench with traverse", 75804, 33432, 0x66EBAD74BB4B4EC8UL, "26c638eb05eeb2c2", "439941a1c140bb5d" },
    };

    [Theory, MemberData(nameof(Props))]
    public void A_prop_is_made_as_it_always_has_been(string name, int vertices, int ground, ulong checksum, string obj, string thumb)
    {
        var made = Make(name);
        Assert.Equal(vertices, made.Mesh.V.Count);
        Assert.Equal(ground, made.Mesh.Ground);
        Assert.Equal(0, made.Mesh.V.Count % 3);
        Assert.Equal(made.Mesh.V.Count, made.Mesh.Shade.Length);
        Assert.Equal(checksum, Sum(made));
        Assert.Equal(obj, Sha(Encoding.UTF8.GetBytes(made.Mesh.Obj())));
        var pic = PropThumb.Render(made.Mesh, StandInAtlas());
        Assert.Equal(PropThumb.Out * PropThumb.Out * 4, pic.Length);
        Assert.Equal(thumb, Sha(pic));
    }

    [Fact]
    public void A_prop_stands_centred_on_the_table()
    {
        var b = Make("Barricade").Mesh.Bounds();
        Assert.Equal(0, b.Position.Y);
        Assert.Equal(0, b.Position.X + b.Size.X / 2, 4);
        Assert.Equal(0, b.Position.Z + b.Size.Z / 2, 4);
    }

    [Fact]
    public void A_camp_keeps_its_fires_moved_with_it()
    {
        Assert.Equal(3, Make("Bivouac").Fires.Count);
        Assert.Single(Make("Bivouac fire").Fires);
        Assert.Empty(Make("Moat").Fires);
    }

    static byte[] StandInAtlas()
    {
        var a = new byte[PropAtlas.AtlasW * PropAtlas.AtlasH * 4];
        for (int i = 0; i < a.Length; i++) a[i] = (byte)(i % 4 == 3 ? (i / 4 * 37) % 256 : (i * 7919 >> 3) % 256);
        return a;
    }

    static ulong Sum(PropMade m)
    {
        ulong h = 14695981039346656037UL;
        void Add(float f) { h ^= (uint)BitConverter.SingleToInt32Bits(f); h *= 1099511628211UL; }
        for (int i = 0; i < m.Mesh.V.Count; i++)
        {
            var (p, n, t) = m.Mesh.V[i];
            Add(p.X);
            Add(p.Y);
            Add(p.Z);
            Add(n.X);
            Add(n.Y);
            Add(n.Z);
            Add(t.X);
            Add(t.Y);
            Add(m.Mesh.Shade[i]);
        }
        foreach (var f in m.Fires) { Add(f.X); Add(f.Y); }
        return h;
    }

    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16].ToLowerInvariant();
}

using System.Numerics;
using KTable.Client.Geometry;
using KTable.Core.Model;

namespace KTable.Client.Effects;

public sealed class EffectLayer
{
    public string Sprite = "puff";
    public bool Add;
    public bool Glow;
    public int Amount = 16;
    public float Life = 4;
    public float Explosive;
    public Vector3 Box;
    public Vector3 At;
    public Vector3 Dir = Axis.Up;
    public float Spread = 15;
    public float VMin, VMax;
    public float Lift;
    public float Fall;
    public float Damp;
    public float Brake;
    public float Drift;
    public float SMin = 1, SMax = 1;
    public (float x, float y)[] Curve = { (0, 1), (1, 1) };
    public (float at, Rgba c)[] Ramp = { (0, Rgba.White), (1, Rgba.White) };
    public float Spin = 20;
    public bool Flat;
    public bool Soft = true;
    public bool Picture;
    public float Offset;
    public Vector2 Cross;
    public float Turb;
    public Vector3[]? Points;
    public bool Fixed;
    public float Start = -1;
    public int Cycles = 1;
    public float Reach;
    public SmokeHeat Heat;
    public float Rise;
    public bool Lying;

    public EffectLayer Copy() => (EffectLayer)MemberwiseClone();
}

public sealed class EffectLightSpec
{
    public Rgba Color;
    public float Range, Attenuation;
    public Vector3 Position;
    public float Base = 1;
    public int Mode;
    public float? Every;
    public float Phase, Speed = 1;
}

public sealed record LampsSpec(Vector3[] At, float Size, Rgba Color, int Lights, float Range, float Energy, float Speed);

public sealed class ShotSpec
{
    public Vector3 From, To;
    public float Phase, Wobble;
    public float HeadSize;
    public Rgba HeadColor;
    public readonly List<EffectLayer> Trail = new();
    public readonly List<EffectLayer> Burst = new();
    public Rgba LightColor;
    public float? LightRange;
}

public sealed class ProjectilesSpec
{
    public readonly List<ShotSpec> Shots = new();
    public bool Flare;
    public float Every = 5, Flight = 1.5f, Arc = 2, Speed = 1, LightBase = 3;
}

public sealed class EffectPlan
{
    public readonly List<EffectLayer> Layers = new();
    public readonly List<EffectLightSpec> Lights = new();
    public LampsSpec? Lamps;
    public ProjectilesSpec? Projectiles;
    public EffectLightSpec? Light;
    public float Span, Rise;
    public float TrailSpan;
    public double Speed = 1;
}

public static class EffectRecipe
{
    public static EffectPlan Plan(EffectDef e, float w, float h, float phase, Rgba color, bool ownPicture, SmokeAir? air = null)
    {
        var recipe = new Recipe(e, w, h, phase, color, air);
        recipe.Lay();
        return recipe.Finish(ownPicture);
    }

    sealed class Recipe
    {
        readonly EffectDef e;
        readonly float w, h, phase, s, k, A, H, P, D, every, clarity, thicker, finer;
        readonly Plume plume;
        readonly double wind;
        readonly SmokeAir air;
        readonly Rgba c;
        readonly Vector3 ex;
        readonly EffectPlan plan = new();
        readonly List<EffectLayer> layers;

        public Recipe(EffectDef e, float w, float h, float phase, Rgba c, SmokeAir? air)
        {
            this.e = e;
            this.phase = phase;
            this.c = c with { A = 1 };
            float density = EffectKinds.HasSmokeSetting(e) ? (float)Math.Clamp(e.Density, SmokeDensity.VeryFine, SmokeDensity.VeryDense) : 0;
            thicker = Mathf.Max(density, 0);
            finer = Mathf.Max(-density, 0);
            bool lingers = EffectKinds.Lingers(e.Kind);
            float denser = (float)SmokeDensity.DenseAmount(e.Kind);
            double amount = e.Amount * (lingers ? Notched(denser, denser * denser, thicker) : 1);
            clarity = Mathf.Clamp(c.A, 0, 1);
            layers = plan.Layers;
            w = Mathf.Max(w, 0.2f);
            h = Mathf.Max(h, 0.2f);
            this.w = w;
            this.h = h;
            s = Mathf.Clamp(Mathf.Sqrt(w * h), 0.4f, 40f);
            k = Mathf.Clamp(Mathf.Max(w, h) / Mathf.Min(w, h), 1, 10);
            A = (float)Math.Clamp(amount, 0.05, 6);
            H = (float)Math.Clamp(e.Height, 0.1, 6);
            P = (float)Math.Clamp(e.Puff, 0.1, 6);
            D = (float)Math.Clamp(e.Wind * (lingers ? Mathf.Lerp(1, (float)SmokeDensity.DenseWind, Mathf.Min(thicker, 1)) : 1), 0, 5);
            every = (float)Math.Clamp(EffectKinds.Interval(e), 0.5, 120);
            ex = new Vector3(w / 2, 0, h / 2);
            var full = thicker > 0 && air is { } a && lingers ? SmokeDrift.Of(e.Kind, w, amount, a) : default;
            plume = new Plume(full.Reach * Mathf.Min(thicker, 1), full.Spread * Mathf.Min(thicker, 1));
            this.air = air ?? new SmokeAir(SmokeDrift.DefaultWindStrength, 1 / SmokeDrift.UncalibratedMetresPerUnit);
            wind = SmokeDrift.WindMetresPerSecond(this.air.WindStrength);
        }

        const float BlastBrake = 0.12f, BlastPart = 0.15f, RideAfter = 0.08f;
        const float FineClarity = 0.4f, FineAmount = 0.6f, FinePuff = 0.75f, DenseSmoke = 1.5f;
        const float VeryFineClarity = 0.15f, VeryFineAmount = 0.35f, VeryFinePuff = 0.6f, VeryDensePuff = 1.15f;

        static float Notched(float atOne, float atTwo, float t) => t <= 1 ? Mathf.Lerp(1, atOne, t) : Mathf.Lerp(atOne, atTwo, t - 1);

        bool Smoky(EffectLayer l) => l.Sprite == "puff" && !l.Add && !l.Glow || e.Kind == "fog" && l.Sprite == "mist";

        void Thin()
        {
            if (thicker == 0 && finer == 0) return;
            float more = EffectKinds.Lingers(e.Kind) ? 1 : Notched(DenseSmoke, DenseSmoke * DenseSmoke, thicker);
            float amount = Notched(FineAmount, VeryFineAmount, finer) * more;
            float size = Notched(FinePuff, VeryFinePuff, finer) * Notched(1, VeryDensePuff, thicker), see = Notched(FineClarity, VeryFineClarity, finer);
            var shots = plan.Projectiles?.Shots.SelectMany(s => s.Trail.Concat(s.Burst)) ?? Enumerable.Empty<EffectLayer>();
            foreach (var l in layers.Concat(shots).Where(Smoky))
            {
                l.Amount = Math.Max(1, (int)Math.Round(l.Amount * amount));
                l.SMin *= size;
                l.SMax *= size;
                l.Ramp = l.Ramp.Select(r => (r.at, r.c with { A = r.c.A * see })).ToArray();
            }
        }

        const int Linger = 2;
        const float MostWidening = 8;

        float Ride => SmokeDrift.Ride(air, D);

        int Turns(EffectLayer smoke, int sections)
        {
            if (plume.Reach <= 0) return Linger;
            int emitters = smoke.Fixed ? Math.Max(1, sections) : 1;
            int most = Math.Min(SmokeDrift.MostParticles / Math.Max(1, smoke.Amount * emitters), (int)(SmokeDrift.MostSeconds / smoke.Life));
            if (smoke.Fixed) most = Math.Min(most, SmokeDrift.MostEmitters / emitters);
            return Math.Clamp((int)Math.Ceiling(plume.Reach / (Ride * smoke.Life)), Linger, Math.Max(Linger, most));
        }

        void AddLingering(EffectLayer smoke, int sections = 1)
        {
            int turns = Turns(smoke, sections);
            var carried = plume.Reach > 0 ? SmokeDrift.Over(Mathf.Min(plume.Reach, Ride * smoke.Life * (turns - smoke.Brake)), air) : plume;
            (float, T)[] Stretched<T>((float at, T v)[] keys) => keys.Select(k => (k.at <= BlastPart ? k.at / turns : k.at, k.v)).ToArray();
            (float x, float y)[] Widened((float x, float y)[] keys)
            {
                if (carried.Reach <= 0) return keys;
                var last = keys[^1];
                float size = Mathf.Max(smoke.SMax * last.y, 0.01f);
                float widen = Mathf.Clamp((size + carried.Spread) / size, 1, MostWidening);
                return keys[..^1].Append((last.x, last.y * widen)).ToArray();
            }
            EffectLayer Longer(float offset, int amount)
            {
                var l = smoke.Copy();
                l.Amount = amount;
                l.Offset = offset;
                l.Life = smoke.Life * turns;
                l.Cycles = turns;
                l.Brake = smoke.Brake / turns;
                if (smoke.Fixed) l.Explosive = 1 - (1 - smoke.Explosive) / turns;
                l.Curve = Widened(Stretched(smoke.Curve));
                l.Ramp = Stretched(smoke.Ramp);
                l.Reach = carried.Reach;
                l.Heat = SmokeHeat.Cold;
                return l;
            }
            if (!smoke.Fixed) { layers.Add(Longer(smoke.Offset, smoke.Amount * turns)); return; }
            for (int turn = 0; turn < turns; turn++) layers.Add(Longer((smoke.Offset + turn) / turns, smoke.Amount));
        }

        int N(float baseCount, bool line = true) => Math.Clamp((int)Math.Round(baseCount * A * (line ? k : 1)), 1, 900);
        Rgba Fade(Rgba col, float a) => col with { A = a * clarity };

        public void Lay()
        {
            switch (e.Kind)
            {
                case "powder" when EffectKinds.Mode(e) == "random":
                    Musketry();
                    break;
                case "powder":
                case "cannon":
                    Volleys();
                    break;
                case "burst":
                    Burst(Vector3.Zero, s, every, 0, 1);
                    break;
                case "barrage":
                    Barrage();
                    break;
                case "fire":
                    Fire();
                    break;
                case "dust":
                    Dust();
                    break;
                case "fog":
                    Fog();
                    break;
                case "spray":
                    Spray();
                    break;
                case "sparks":
                    Sparks();
                    break;
                case "rain":
                    Rain();
                    break;
                case "snow":
                    Snow();
                    break;
                case "camp":
                    Camp();
                    break;
                case "lamps":
                    Lamps();
                    break;
                case "rocket":
                case "flare":
                    Projectiles();
                    break;
                case "tracer":
                    Tracer();
                    break;
                case "column":
                    Columns();
                    break;
                case "broadside":
                    Broadside();
                    break;
                case "screen":
                    Screen();
                    break;
                case "oil":
                    Oil();
                    break;
                case "wake":
                    Wake();
                    break;
                case "arrows":
                case "firearrows":
                    Missiles(Arrow, e.Kind == "firearrows");
                    break;
                case "javelins":
                    Missiles(Javelin, false);
                    break;
                case "slingstones":
                    Missiles(SlingStone, false);
                    break;
                case "rockfall":
                    Rockfall();
                    break;
                case "melee":
                    Melee();
                    break;
                case "stones":
                    Stones();
                    break;
                default:
                    Smoke();
                    break;
            }
            Thin();
        }

        void Musketry()
        {
            AddLingering(new EffectLayer
            {
                Sprite = "puff", Amount = N(17), Life = 7, Box = new Vector3(ex.X * 0.95f, 0.14f * s, ex.Z * 0.6f), At = new Vector3(0, 0.2f * s, 0),
                Dir = new Vector3(0, 0.55f, -1).Normalized(), Spread = 30, VMin = 0.25f * s * H, VMax = 0.7f * s * H, Damp = 0.12f * s * H, Brake = 0.2f, Lift = 0.06f * s * H, Drift = 0.9f * D,
                SMin = 1.1f * s * P, SMax = 1.7f * s * P, Curve = new[] { (0f, 0.55f), (0.12f, 1f), (1f, 2.1f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.05f, Fade(c, 0.9f)), (0.3f, Fade(c, 0.8f)), (0.65f, Fade(c.Lerp(Rgba.White, 0.1f), 0.45f)), (1f, Fade(c.Lerp(Rgba.White, 0.2f), 0)) },
            });
            layers.Add(MuzzleFlash(N(5), 1.1f, false, 0, new Vector3(ex.X * 0.95f, 0.02f, 0.05f), new Vector3(0, 0.12f * s, -ex.Z * 0.8f), 0.07f));
        }

        const float GunGap = 0.9f;
        const int FewestGuns = 2, MostGuns = 8;

        void Volleys()
        {
            bool musket = e.Kind == "powder", rolling = EffectKinds.Mode(e) == "rolling";
            int sections = musket
                ? rolling ? Math.Clamp((int)Math.Round(w / GunGap), 3, 10) : 1
                : Math.Clamp((int)Math.Round(w / GunGap), FewestGuns, MostGuns);
            float front = ex.X * (musket ? 0.95f : 0.85f), hw = front / sections;
            for (int i = 0; i < sections; i++)
            {
                float xc = -front + hw * (2 * i + 1);
                float off = rolling ? i / (float)sections * (musket ? 0.5f : 0.6f) : 0;
                if (musket)
                {
                    var line = new Vector3(xc, 0.12f * s, -ex.Z * 0.8f);
                    AddLingering(sections: sections, smoke: new EffectLayer
                    {
                        Sprite = "puff", Amount = Math.Max(2, N(12) / sections), Fixed = true, Life = every, Explosive = 0.85f, Offset = off,
                        Box = new Vector3(hw * 0.95f, 0.08f * s, 0.08f * s), At = line + new Vector3(0, 0.06f * s, 0),
                        Dir = new Vector3(0, 0.55f, -1)
                            .Normalized(), Spread = 30, VMin = 0.9f * s * H, VMax = 1.8f * s * H, Damp = 1.6f * s * H, Brake = BlastBrake, Lift = 0.08f * s * H, Drift = 0.9f * D,
                        SMin = 1.1f * s * P, SMax = 1.7f * s * P, Curve = new[] { (0f, 0.55f), (0.05f, 1f), (1f, 2.2f) },
                        Ramp = new[] { (0f, Fade(c, 0)), (0.02f, Fade(c, 0.95f)), (0.3f, Fade(c, 0.85f)), (0.65f, Fade(c.Lerp(Rgba.White, 0.1f), 0.45f)), (1f, Fade(c.Lerp(Rgba.White, 0.2f), 0)) },
                    });
                    layers.Add(MuzzleFlash(Math.Max(1, (int)Math.Round(hw * 2 / 0.45f)), every, true, off, new Vector3(hw * 0.95f, 0.02f, 0.02f), line, 0.08f / every));
                }
                else
                {
                    var mouth = new Vector3(xc, 0.15f * s, -ex.Z * 0.85f);
                    float bore = Mathf.Min(hw * 0.3f, 0.1f * s);
                    if (e.Smoke)
                        AddLingering(sections: sections, smoke: new EffectLayer
                        {
                            Sprite = "puff", Amount = Math.Max(3, N(12) / sections), Fixed = true, Life = every, Explosive = 0.92f, Offset = off,
                            Box = new Vector3(bore, 0.06f * s, 0.06f * s), At = mouth,
                            Dir = new Vector3(0, 0.35f, -1)
                                .Normalized(), Spread = 22, VMin = 2.6f * s * H, VMax = 4.2f * s * H, Damp = 3.6f * s * H, Brake = BlastBrake, Lift = 0.09f * s * H, Drift = 0.9f * D,
                            SMin = 1.0f * s * P, SMax = 1.6f * s * P, Curve = new[] { (0f, 0.5f), (0.06f, 1f), (1f, 2.3f) },
                            Ramp = new[] { (0f, Fade(c, 0)), (0.02f, Fade(c, 0.97f)), (0.3f, Fade(c, 0.85f)), (0.6f, Fade(c, 0.5f)), (1f, Fade(c.Lerp(Rgba.White, 0.2f), 0)) },
                        });
                    layers.Add(MuzzleFlash(1, every, true, off, new Vector3(bore * 0.2f, 0.01f, 0.01f), mouth, 0.12f / every, gun: true));
                }
            }
        }

        EffectLayer MuzzleFlash(int amount, float life, bool fixedTimes, float offset, Vector3 box, Vector3 at, float lasts, bool gun = false)
        {
            float len = (gun ? 1.8f : 0.55f) * s, wide = len * (gun ? 0.62f : 0.72f);
            return new EffectLayer
            {
                Sprite = "muzzle", Cross = new Vector2(wide, len), Add = true, Amount = amount, Fixed = fixedTimes, Life = life, Explosive = fixedTimes ? 1 : 0, Offset = offset,
                Box = box, At = at + new Vector3(0, 0, -len * 0.5f), Dir = new Vector3(0, 0, -1), Spread = gun ? 3 : 7, VMin = 0.05f * s, VMax = 0.1f * s,
                SMin = 0.8f * P, SMax = 1.15f * P, Spin = 0, Soft = false,
                Ramp =
                    new[] { (0f, new Rgba(1, 0.86f, 0.55f, 0.95f)), (lasts * 0.35f, new Rgba(1, 0.6f, 0.2f, 0.8f)), (lasts, new Rgba(0.9f, 0.32f, 0.05f, 0)), (1f, new Rgba(0.9f, 0.32f, 0.05f, 0)) },
            };
        }

        void Barrage()
        {
            var rng = new Random(EffectKinds.Seed(phase));
            int shells = Math.Clamp((int)Math.Round(6 * A), 1, 16);
            float sb = Mathf.Clamp(s / 3.2f, 0.3f, 20f);
            for (int i = 0; i < shells; i++)
            {
                var at = new Vector3(((float)rng.NextDouble() * 2 - 1) * ex.X * 0.85f, 0, ((float)rng.NextDouble() * 2 - 1) * ex.Z * 0.85f);
                Burst(at, sb, every * (0.85f + 0.3f * (float)rng.NextDouble()), (float)rng.NextDouble(), 1 / Mathf.Max(0.3f, A));
            }
        }

        void Fire()
        {
            layers.Add(new EffectLayer
            {
                Sprite = "flame", Amount = N(26), Life = 1.0f, Box = new Vector3(ex.X * 0.7f, 0.02f, ex.Z * 0.7f), At = new Vector3(0, 0.04f, 0),
                Spread = 10, VMin = 0.3f * s * Mathf.Sqrt(H), VMax = 0.65f * s * Mathf.Sqrt(H), Lift = 0.5f * s * Mathf.Sqrt(H), SMin = 0.45f * s * P, SMax = 0.8f * s * P, Spin = 25, Soft = false,
                Curve = new[] { (0f, 0.6f), (0.3f, 1f), (1f, 0.2f) },
                Ramp =
                    new[] { (0f, new Rgba(1, 0.9f, 0.55f, 0)), (0.08f, new Rgba(1, 0.86f, 0.4f, 0.95f)), (0.35f, new Rgba(1, 0.5f, 0.08f, 0.9f)), (0.7f, new Rgba(0.85f, 0.2f, 0.02f, 0.6f)),
                        (1f, new Rgba(0.3f, 0.05f, 0, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "spark", Add = true, Amount = N(6), Life = 0.8f, Box = new Vector3(ex.X * 0.6f, 0.02f, ex.Z * 0.6f), At = new Vector3(0, 0.2f * s * H, 0),
                Spread = 10, VMin = 0.3f * s * H, VMax = 0.6f * s * H, SMin = 0.5f * s * P, SMax = 0.8f * s * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, new Rgba(1, 0.6f, 0.2f, 0)), (0.3f, new Rgba(1, 0.6f, 0.2f, 0.35f)), (1f, new Rgba(1, 0.4f, 0.1f, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "spark", Add = true, Amount = N(10), Life = 2.4f, Box = new Vector3(ex.X * 0.6f, 0.1f * s, ex.Z * 0.6f), At = new Vector3(0, 0.3f * s * H, 0),
                Spread = 30, VMin = 0.6f * s * H, VMax = 1.4f * s * H, Lift = 0.15f * s * H, Drift = 0.25f * D, SMin = 0.04f * s, SMax = 0.08f * s, Spin = 0, Soft = false,
                Ramp = new[] { (0f, new Rgba(1, 0.8f, 0.4f, 0)), (0.1f, new Rgba(1, 0.75f, 0.35f, 1)), (0.7f, new Rgba(1, 0.4f, 0.1f, 0.8f)), (1f, new Rgba(0.6f, 0.1f, 0, 0)) },
            });
            if (e.Smoke)
                layers.Add(new EffectLayer
                {
                    Sprite = "puff", Heat = SmokeHeat.Hot, Amount = N(22), Life = 6f * Mathf.Sqrt(H), Box = new Vector3(ex.X * 0.55f, 0.05f * s, ex.Z * 0.55f), At =
                        new Vector3(0, 0.2f * s * Mathf.Sqrt(H), 0),
                    Spread = 14, VMin = 0.45f * s * H, VMax = 0.8f * s * H, Lift = 0.1f * s * H, Drift = 0.35f * D, SMin = 0.85f * s * P, SMax = 1.4f * s * P,
                    Curve = new[] { (0f, 0.3f), (1f, 2.6f) },
                    Ramp = new[] { (0f, Fade(c, 0)), (0.04f, Fade(c, 0.6f)), (0.15f, Fade(c, 0.65f)), (0.65f, Fade(c.Lerp(Rgba.White, 0.25f), 0.32f)), (1f, Fade(c.Lerp(Rgba.White, 0.4f), 0)) },
                });
        }

        void Dust()
        {
            layers.Add(new EffectLayer
            {
                Sprite = "dust", Amount = N(18), Life = 5.5f, Box = new Vector3(ex.X * 0.95f, 0.02f, ex.Z * 0.95f), At = new Vector3(0, 0.15f * s, 0),
                Spread = 70, VMin = 0.12f * s * H, VMax = 0.35f * s * H, Damp = 0.05f * s, Lift = 0.03f * s * H, Drift = 0.3f * D,
                SMin = 0.6f * s * P, SMax = 1.1f * s * P, Curve = new[] { (0f, 0.4f), (1f, 1.7f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.2f, Fade(c, 0.72f)), (0.7f, Fade(c.Lerp(Rgba.White, 0.12f), 0.45f)), (1f, Fade(c, 0)) },
            });
        }

        void Fog()
        {
            layers.Add(new EffectLayer
            {
                Sprite = "mist", Amount = N(22), Life = 11, Box = new Vector3(ex.X * 0.85f, 0.03f * s * H, ex.Z * 0.85f), At = new Vector3(0, 0.08f * s * H, 0),
                Dir = Axis.Right, Spread = 180, Flat = true, VMin = 0.02f * s, VMax = 0.06f * s, Drift = 0.12f * D,
                SMin = 0.45f * s * P, SMax = 0.75f * s * P, Spin = 6, Curve = new[] { (0f, 0.75f), (1f, 1.25f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.3f, Fade(c, 0.3f)), (0.7f, Fade(c, 0.3f)), (1f, Fade(c, 0)) },
            });
        }

        void Spray()
        {
            layers.Add(new EffectLayer
            {
                Sprite = "drop", Amount = N(40, false), Life = every, Explosive = 0.95f, Box = new Vector3(0.1f * s, 0.02f, 0.1f * s), At = new Vector3(0, 0.02f, 0),
                Spread = 14, VMin = 3.2f * s * H, VMax = 4.8f * s * H, Fall = 9f * s * H, SMin = 0.1f * s * P, SMax = 0.22f * s * P, Spin = 0, Soft = false,
                Curve = new[] { (0f, 0.6f), (0.2f, 1f), (1f, 1.2f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.01f, Fade(c, 0.9f)), (0.22f, Fade(c, 0.6f)), (0.32f, Fade(c, 0)), (1f, Fade(c, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "mist", Amount = N(8, false), Life = every, Explosive = 0.9f, Box = new Vector3(0.2f * s, 0.05f, 0.2f * s), At = new Vector3(0, 0.2f * s, 0),
                Spread = 60, VMin = 0.4f * s * H, VMax = 0.9f * s * H, Damp = 0.6f * s, Drift = 0.2f * D, SMin = 0.5f * s * P, SMax = 0.9f * s * P,
                Curve = new[] { (0f, 0.4f), (0.2f, 1f), (1f, 1.6f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.03f, Fade(c, 0.6f)), (0.45f, Fade(c, 0.25f)), (0.7f, Fade(c, 0)), (1f, Fade(c, 0)) },
            });
        }

        void Sparks()
        {
            layers.Add(new EffectLayer
            {
                Sprite = "spark", Add = true, Amount = N(30), Life = 2.4f, Box = new Vector3(ex.X * 0.8f, 0.1f * s, ex.Z * 0.8f), At = new Vector3(0, 0.15f * s, 0),
                Spread = 70, VMin = 0.15f * s * H, VMax = 0.6f * s * H, Lift = 0.3f * s * H, Drift = 0.2f * D, SMin = 0.06f * s * P, SMax = 0.14f * s * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(c, 0)), (0.25f, Fade(c, 1)), (0.7f, Fade(c, 0.9f)), (1f, Fade(c, 0)) },
            });
        }

        void Rain()
        {
            float top = 0.55f * s * H, fall = top / 0.6f;
            layers.Add(new EffectLayer
            {
                Sprite = "streak", Cross = new Vector2(0.012f * s, 0.07f * s), Amount = Math.Clamp((int)Math.Round(260 * A), 10, 1500), Fixed = true, Life = 0.6f,
                Box = new Vector3(ex.X, 0.1f * s, ex.Z), At = new Vector3(0, top, 0), Dir = Axis.Down, Spread = 2, VMin = fall * 0.9f, VMax = fall * 1.1f,
                Drift = 6f * D, SMin = 0.8f * P, SMax = 1.2f * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(c, 0)), (0.1f, Fade(c, 0.5f)), (0.9f, Fade(c, 0.5f)), (1f, Fade(c, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "mist", Amount = Math.Clamp((int)Math.Round(14 * A), 4, 60), Fixed = true, Life = 8, Box = new Vector3(ex.X * 0.85f, 0.02f, ex.Z * 0.85f), At = new Vector3(0, 0.05f * s, 0),
                Dir = Axis.Right, Spread = 180, Flat = true, VMin = 0.01f * s, VMax = 0.03f * s, Drift = 0.2f * D, SMin = 0.35f * s, SMax = 0.6f * s, Spin = 4,
                Ramp = new[] { (0f, Fade(c, 0)), (0.3f, Fade(c, 0.16f)), (0.7f, Fade(c, 0.16f)), (1f, Fade(c, 0)) },
            });
        }

        void Snow()
        {
            float top = 0.5f * s * H, fall = 0.11f * s;
            layers.Add(new EffectLayer
            {
                Sprite = "drop", Amount = Math.Clamp((int)Math.Round(320 * A), 10, 1500), Fixed = true, Life = top / fall,
                Box = new Vector3(ex.X, 0.1f * s, ex.Z), At = new Vector3(0, top, 0), Dir = Axis.Down, Spread = 12, VMin = fall * 0.8f, VMax = fall * 1.2f,
                Drift = 0.5f * D, Turb = 0.6f, SMin = 0.012f * s * P, SMax = 0.022f * s * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(c, 0)), (0.08f, Fade(c, 0.95f)), (0.9f, Fade(c, 0.95f)), (1f, Fade(c, 0)) },
            });
        }

        const float CampReach = 7, CampFade = 1.4f, CampGlow = 1.1f, CampFlame = 0.2f;

        void Camp()
        {
            var rng = new Random(EffectKinds.Seed(phase));
            var pts = e.At is { Count: >= 2 } at
                ? Enumerable.Range(0, at.Count / 2).Select(i => new Vector3((float)at[i * 2], 0, (float)at[i * 2 + 1])).ToArray()
                : EffectKinds.Scatter(rng, Math.Clamp((int)Math.Round(6 * A), 1, 60), ex.X * 0.85f, ex.Z * 0.85f, 0);
            int fires = pts.Length;
            float fs = 0.05f * Mathf.Sqrt(w * h) * P;
            layers.Add(new EffectLayer
            {
                Sprite = "flame", Points = pts, Amount = Math.Min(2400, (int)Math.Round(fires * 7 * Mathf.Max(1, e.At != null ? A : 1))), Fixed = true, Life = 0.8f, At = new Vector3(0, 0.02f, 0),
                Spread = 10, VMin = 0.6f * fs * Mathf.Sqrt(H), VMax = 1.2f * fs * Mathf.Sqrt(H), Lift = 1.2f * fs, SMin = 0.8f * fs, SMax = 1.2f * fs, Spin = 25, Soft = false,
                Curve = new[] { (0f, 0.6f), (0.3f, 1f), (1f, 0.2f) },
                Ramp =
                    new[] { (0f, new Rgba(1, 0.9f, 0.55f, 0)), (0.08f, new Rgba(1, 0.86f, 0.4f, 0.95f)), (0.35f, new Rgba(1, 0.5f, 0.08f, 0.9f)), (0.7f, new Rgba(0.85f, 0.2f, 0.02f, 0.6f)),
                        (1f, new Rgba(0.3f, 0.05f, 0, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "spark", Add = true, Points = pts, Amount = Math.Min(2400, fires * 2), Fixed = true, Life = 1f, At = new Vector3(0, 0.03f, 0),
                Spread = 0, SMin = 3f * fs, SMax = 4f * fs, Spin = 0, Soft = false,
                Ramp = new[] { (0f, new Rgba(1, 0.6f, 0.25f, 0)), (0.3f, new Rgba(1, 0.6f, 0.25f, 0.22f)), (0.7f, new Rgba(1, 0.55f, 0.2f, 0.22f)), (1f, new Rgba(1, 0.5f, 0.2f, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "spark", Add = true, Points = pts, Amount = Math.Min(2400, fires * 2), Fixed = true, Life = 1.6f, At = new Vector3(0, 0.3f * fs, 0),
                Spread = 25, VMin = 1f * fs * H, VMax = 2f * fs * H, Lift = 0.3f * fs, Drift = 0.25f * D, SMin = 0.08f * fs, SMax = 0.15f * fs, Spin = 0, Soft = false,
                Ramp = new[] { (0f, new Rgba(1, 0.8f, 0.4f, 0)), (0.1f, new Rgba(1, 0.75f, 0.35f, 1)), (0.7f, new Rgba(1, 0.4f, 0.1f, 0.8f)), (1f, new Rgba(0.6f, 0.1f, 0, 0)) },
            });
            if (e.Smoke)
                layers.Add(new EffectLayer
                {
                    Sprite = "puff", Heat = SmokeHeat.Hot, Points = pts, Amount = Math.Min(2400, fires * 4), Fixed = true, Life = 5f * Mathf.Sqrt(H), At = new Vector3(0, 0.3f * fs, 0),
                    Spread = 10, VMin = 0.9f * fs * H, VMax = 1.5f * fs * H, Lift = 0.1f * fs, Drift = 0.35f * D, SMin = 1.0f * fs, SMax = 1.7f * fs,
                    Curve = new[] { (0f, 0.35f), (1f, 3f) },
                    Ramp = new[] { (0f, Fade(c, 0)), (0.04f, Fade(c, 0.35f)), (0.15f, Fade(c, 0.4f)), (0.65f, Fade(c.Lerp(Rgba.White, 0.25f), 0.2f)), (1f, Fade(c.Lerp(Rgba.White, 0.4f), 0)) },
                });
            if (e.Light)
                for (int i = 0; i < Math.Min(4, fires); i++)
                    plan.Lights.Add(new EffectLightSpec
                    {
                        Color = new Rgba(1, 0.6f, 0.28f), Range = CampReach * fs, Attenuation = CampFade,
                        Position = pts[i * fires / Math.Min(4, fires)] + new Vector3(0, fs, 0), Base = CampGlow * Mathf.Pow(fs / CampFlame, CampFade), Mode = 1,
                        Phase = i * 1.7f, Speed = (float)Math.Clamp(e.Speed, 0.1, 5),
                    });
        }

        void Lamps()
        {
            var rng = new Random(EffectKinds.Seed(phase));
            int lamps = Math.Clamp((int)Math.Round(8 * A), 1, 80);
            var pts = EffectKinds.Scatter(rng, lamps, ex.X * 0.9f, ex.Z * 0.9f, 0.03f * s * H);
            plan.Lamps = new LampsSpec(pts, 0.09f * s * P, c, e.Light ? Math.Min(4, lamps) : 0, Mathf.Max(0.8f, 0.4f * s), 0.7f, (float)Math.Clamp(e.Speed, 0.1, 5));
        }

        void Projectiles()
        {
            var rng = new Random(EffectKinds.Seed(phase));
            bool flare = e.Kind == "flare";
            int count = flare ? Math.Clamp((int)Math.Round(A), 1, 6) : Math.Clamp((int)Math.Round(3 * A), 1, 12);
            var shots = new ProjectilesSpec
            {
                Flare = flare, Every = every, Speed = (float)Math.Clamp(e.Speed, 0.1, 5),
                Flight = flare ? 0.8f : Mathf.Clamp(h / 9f, 0.5f, 3f), Arc = flare ? 0.9f * s * H : 0.16f * h * H, LightBase = 3f * Mathf.Sqrt(A),
            };
            plan.Projectiles = shots;
            for (int i = 0; i < count; i++)
            {
                var shot = new ShotSpec { Phase = (phase + i * (flare ? 0.37f : 0.23f) + (float)rng.NextDouble() * 0.1f) % 1, Wobble = (float)rng.NextDouble() * 6 };
                float x = count == 1 ? 0 : -ex.X * 0.8f + ex.X * 1.6f * i / (count - 1);
                if (flare)
                {
                    shot.From = new Vector3(((float)rng.NextDouble() * 2 - 1) * ex.X * 0.6f, 0.05f, ((float)rng.NextDouble() * 2 - 1) * ex.Z * 0.6f);
                    shot.To = shot.From with { Y = shots.Arc };
                }
                else
                {
                    shot.From = new Vector3(x, 0.15f, ex.Z * 0.9f);
                    shot.To = new Vector3(x + ((float)rng.NextDouble() * 2 - 1) * 0.15f * w, 0.05f, -ex.Z * 0.9f);
                }
                float hs = flare ? 0.06f * s * P : 0.02f * h * P;
                shot.HeadSize = hs * (flare ? 3 : 2.5f);
                shot.HeadColor = flare ? new Rgba(1, 1, 0.95f) : new Rgba(1, 0.8f, 0.45f);
                shot.Trail.Add(new EffectLayer
                {
                    Sprite = "spark", Add = true, Amount = flare ? 10 : 60, Fixed = true, Life = flare ? 0.8f : 0.6f, Box = Vector3.One * 0.01f,
                    Spread = 180, VMin = 0.05f * hs * 10, VMax = 0.2f * hs * 10, Fall = flare ? 0 : 2 * hs * 10, SMin = 0.5f * hs, SMax = 0.9f * hs, Spin = 0, Soft = false,
                    Ramp = new[] { (0f, new Rgba(1, 0.85f, 0.5f, 1)), (0.5f, new Rgba(1, 0.5f, 0.15f, 0.8f)), (1f, new Rgba(0.6f, 0.1f, 0, 0)) },
                });
                if (e.Smoke || flare)
                    shot.Trail.Add(new EffectLayer
                    {
                        Sprite = "puff", Amount = flare ? 30 : 80, Fixed = true, Life = flare ? 6 : 4, Box = Vector3.One * 0.01f, Spread = 180, VMin = 0, VMax = 0.05f * hs * 10,
                        Lift = 0.02f * hs * 10, Drift = 0.3f * D, SMin = (flare ? 1.2f : 1.5f) * hs, SMax = (flare ? 2f : 2.5f) * hs, Curve = new[] { (0f, 0.6f), (1f, 3.5f) },
                        Ramp = new[] { (0f, Fade(c, 0)), (0.05f, Fade(c, flare ? 0.35f : 0.6f)), (1f, Fade(c.Lerp(Rgba.White, 0.3f), 0)) },
                    });
                if (flare)
                {
                    shot.LightColor = c;
                    shot.LightRange = Mathf.Max(3, 1.6f * s);
                }
                else
                {
                    shot.Burst.Add(new EffectLayer
                    {
                        Sprite = "flash", Add = true, Amount = 2, Fixed = true, Life = 0.5f, Explosive = 1, Box = Vector3.One * 0.01f, Spread = 0, SMin = 6 * hs, SMax = 8 * hs, Spin = 0, Soft = false,
                        Ramp = new[] { (0f, new Rgba(1, 1, 0.9f, 1)), (0.25f, new Rgba(1, 0.6f, 0.2f, 0.7f)), (1f, new Rgba(1, 0.4f, 0.1f, 0)) },
                    });
                    shot.Burst.Add(new EffectLayer
                    {
                        Sprite = "puff", Amount = 6, Fixed = true, Life = 3f, Explosive = 1, Box = Vector3.One * 0.05f, Spread = 60, VMin = 0.3f * hs * 10, VMax = 0.8f * hs * 10, Damp = 0.5f * hs * 10,
                        Drift = 0.3f * D, SMin = 3 * hs, SMax = 5 * hs, Curve = new[] { (0f, 0.4f), (1f, 2.2f) },
                        Ramp = new[] { (0f, Fade(c, 0)), (0.05f, Fade(c, 0.7f)), (1f, Fade(c.Lerp(Rgba.White, 0.3f), 0)) },
                    });
                }
                shots.Shots.Add(shot);
            }
        }

        void Tracer()
        {
            float speed = h / 0.5f;
            layers.Add(new EffectLayer
            {
                Sprite = "streak", Cross = new Vector2(0.006f * h, 0.05f * h), Add = true, Amount = Math.Clamp((int)Math.Round(14 * A), 2, 200), Fixed = true, Life = 0.5f,
                Box = new Vector3(ex.X * 0.8f, 0.01f, 0.01f), At = new Vector3(0, 0.08f * h * H * 0.2f + 0.05f, ex.Z * 0.95f),
                Dir = new Vector3(0, 0.01f * H, -1).Normalized(), Spread = 1.2f, VMin = speed * 0.95f, VMax = speed * 1.05f, SMin = P, SMax = P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(c, 1)), (0.85f, Fade(c, 1)), (1f, Fade(c, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "flash", Add = true, Amount = Math.Clamp((int)Math.Round(6 * A), 1, 60), Fixed = true, Life = 0.15f, Box = new Vector3(ex.X * 0.8f, 0.01f, 0.01f),
                At = new Vector3(0, 0.05f, ex.Z * 0.95f), Spread = 0, SMin = 0.02f * h * P, SMax = 0.035f * h * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, new Rgba(1, 0.95f, 0.8f, 1)), (1f, new Rgba(1, 0.6f, 0.2f, 0)) },
            });
        }

        void Columns()
        {
            var rng = new Random(EffectKinds.Seed(phase));
            int n = Math.Clamp((int)Math.Round(3 * A), 1, 16);
            bool rolling = EffectKinds.Mode(e) == "rolling";
            float sb = Mathf.Clamp(Mathf.Min(w / n, h) * 0.7f, 0.15f, 20f);
            for (int i = 0; i < n; i++)
            {
                float jx = n == 1 ? 0 : ((float)rng.NextDouble() - 0.5f) * ex.X * 0.6f / n;
                float z = n == 1 ? 0 : ((float)rng.NextDouble() * 2 - 1) * ex.Z * 0.55f;
                var at = new Vector3(-ex.X * 0.85f + ex.X * 1.7f * (i + 0.5f) / n + jx, 0, z);
                float off = rolling ? i / (float)n * 0.5f : (float)rng.NextDouble() * 0.03f;
                Column(at, sb * (0.85f + 0.3f * (float)rng.NextDouble()), off);
            }
        }

        void Column(Vector3 at, float sb, float off)
        {
            float top = 3.4f * sb * H, v = 2.86f * top, fall = Mathf.Min(1 / every, 0.45f);
            var tops = new[] { (0f, Fade(c, 0)), (0.004f, Fade(c, 0.95f)), (0.3f, Fade(c, 0.85f)), (0.65f, Fade(c.Lerp(Rgba.White, 0.1f), 0.35f)), (1f, Fade(c, 0)) };
            layers.Add(new EffectLayer
            {
                Sprite = "puff", Amount = 60, Fixed = true, Life = every, Explosive = 1, Offset = off,
                Box = new Vector3(0.16f * sb, 0.02f, 0.16f * sb), At = at + new Vector3(0, 0.05f, 0),
                Spread = 4, VMin = 0.3f * v, VMax = v, Damp = v / 0.7f, Drift = 0.05f * D,
                SMin = 0.5f * sb * P, SMax = 0.8f * sb * P, Curve = new[] { (0f, 0.45f), (0.12f, 1f), (1f, 1.9f) }, Ramp = tops,
            });
            layers.Add(new EffectLayer
            {
                Sprite = "puff", Amount = 12, Fixed = true, Life = every, Explosive = 1, Offset = off,
                Box = new Vector3(0.1f * sb, 0.02f, 0.1f * sb), At = at + new Vector3(0, 0.05f, 0),
                Spread = 9, VMin = 0.85f * v, VMax = 1.08f * v, Damp = v / 0.7f, Drift = 0.05f * D,
                SMin = 0.3f * sb * P, SMax = 0.5f * sb * P, Curve = new[] { (0f, 0.45f), (0.12f, 1f), (1f, 1.7f) }, Ramp = tops,
            });
            layers.Add(new EffectLayer
            {
                Sprite = "drop", Amount = 24, Fixed = true, Life = every, Explosive = 1, Offset = off,
                Box = new Vector3(0.15f * sb, 0.02f, 0.15f * sb), At = at + new Vector3(0, 0.05f, 0),
                Spread = 16, VMin = 0.35f * v, VMax = 0.75f * v, Fall = 0.5625f * v * v / (1.1f * top), SMin = 0.05f * sb * P, SMax = 0.11f * sb * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(c, 0)), (0.001f, Fade(c, 0.85f)), (0.55f * fall, Fade(c, 0.7f)), (fall, Fade(c, 0)), (1f, Fade(c, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "mist", Amount = 12, Fixed = true, Life = every, Explosive = 1, Offset = off,
                Box = new Vector3(0.1f * sb, 0.01f, 0.1f * sb), At = at + new Vector3(0, 0.04f, 0),
                Dir = Axis.Right, Spread = 180, Flat = true, VMin = 0.5f * sb, VMax = 1.1f * sb, Damp = 0.9f * sb,
                SMin = 0.45f * sb * P, SMax = 0.75f * sb * P, Curve = new[] { (0f, 0.5f), (0.3f, 1.2f), (1f, 1.6f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.01f, Fade(c, 0.75f)), (0.4f, Fade(c, 0.35f)), (0.8f, Fade(c, 0)), (1f, Fade(c, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "mist", Amount = 8, Fixed = true, Life = every, Explosive = 0.85f, Offset = off,
                Box = new Vector3(0.2f * sb, 0.3f * top, 0.2f * sb), At = at + new Vector3(0, 0.35f * top, 0),
                Spread = 50, VMin = 0.05f * sb, VMax = 0.25f * sb, Drift = 0.4f * D,
                SMin = 0.7f * sb * P, SMax = 1.1f * sb * P, Curve = new[] { (0f, 0.6f), (1f, 1.5f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.12f, Fade(c, 0.4f)), (0.55f, Fade(c, 0.25f)), (0.95f, Fade(c, 0)), (1f, Fade(c, 0)) },
            });
        }

        void Broadside()
        {
            bool rolling = EffectKinds.Mode(e) == "rolling";
            int n = Math.Clamp((int)Math.Round(w / 1.4f * A), 1, 8);
            float gap = ex.X * 1.7f / n, sb = Mathf.Clamp(Mathf.Min(gap, h) * 0.9f, 0.15f, 20f);
            float lasts = Mathf.Min(1f / every, 0.35f);
            var lit = new Rgba(0.9f, 0.3f, 0.05f);
            var ring = new Rgba(0.86f, 0.9f, 0.93f);
            for (int i = 0; i < n; i++)
            {
                float xc = -ex.X * 0.85f + gap * (i + 0.5f), off = rolling ? i / (float)n * 0.6f : 0;
                var muzzle = new Vector3(xc, 0.25f * sb, -ex.Z * 0.6f);
                layers.Add(new EffectLayer
                {
                    Sprite = "puff", Glow = true, Amount = 24, Fixed = true, Life = every, Explosive = 1, Offset = off,
                    Box = new Vector3(0.2f * sb, 0.1f * sb, 0.1f * sb), At = muzzle, Dir = new Vector3(0, 0.25f, -1).Normalized(), Spread = 32,
                    VMin = 1.4f * sb, VMax = 4f * sb, Damp = 5f * sb, Lift = 0.3f * sb, SMin = 1f * sb * P, SMax = 1.6f * sb * P,
                    Curve = new[] { (0f, 0.35f), (lasts * 0.15f, 1f), (lasts, 1.5f), (1f, 1.5f) },
                    Ramp =
                        new[] { (0f, new Rgba(1, 0.9f, 0.6f, 1)), (lasts * 0.15f, new Rgba(1, 0.55f, 0.12f, 1)), (lasts * 0.5f, new Rgba(0.9f, 0.25f, 0.03f, 0.95f)),
                            (lasts, new Rgba(0.25f, 0.05f, 0.01f, 0)), (1f, new Rgba(0.25f, 0.05f, 0.01f, 0)) },
                });
                layers.Add(new EffectLayer
                {
                    Sprite = "flash", Add = true, Amount = 2, Fixed = true, Life = every, Explosive = 1, Offset = off,
                    Box = Vector3.One * 0.01f, At = muzzle + new Vector3(0, 0, -0.3f * sb), Spread = 0, SMin = 2.2f * sb * P, SMax = 2.6f * sb * P, Spin = 0, Soft = false,
                    Ramp = new[] { (0f, new Rgba(1, 1, 0.9f, 1)), (lasts * 0.3f, new Rgba(1, 0.7f, 0.3f, 0.6f)), (lasts * 0.6f, new Rgba(1, 0.5f, 0.2f, 0)), (1f, new Rgba(1, 0.5f, 0.2f, 0)) },
                });
                if (e.Smoke)
                    AddLingering(sections: n, smoke: new EffectLayer
                    {
                        Sprite = "puff", Amount = 18, Fixed = true, Life = every, Explosive = 0.9f, Offset = off,
                        Box = new Vector3(0.25f * sb, 0.1f * sb, 0.15f * sb), At = muzzle + new Vector3(0, 0.1f * sb, -0.4f * sb),
                        Dir = new Vector3(0, 0.3f, -1)
                            .Normalized(), Spread = 35, VMin = 1.2f * sb * H, VMax = 2.8f * sb * H, Damp = 2.2f * sb * H, Brake = BlastBrake, Lift = 0.07f * sb * H, Drift = 1.05f * D,
                        SMin = 0.8f * sb * P, SMax = 1.35f * sb * P, Curve = new[] { (0f, 0.3f), (0.06f, 1f), (1f, 2.6f) },
                        Ramp =
                            new[] { (0f, Fade(c.Lerp(lit, 0.7f), 0)), (lasts * 0.3f, Fade(c.Lerp(lit, 0.6f), 0.9f)), (lasts * 1.2f, Fade(c.Lerp(lit, 0.15f), 0.9f)),
                                (0.5f, Fade(c.Lerp(Rgba.White, 0.1f), 0.55f)), (1f, Fade(c.Lerp(Rgba.White, 0.2f), 0)) },
                    });
                layers.Add(new EffectLayer
                {
                    Sprite = "mist", Amount = 20, Fixed = true, Life = every, Explosive = 1, Offset = off,
                    Box = new Vector3(0.1f * sb, 0.01f, 0.1f * sb), At = new Vector3(xc, 0.03f, -ex.Z * 0.2f),
                    Dir = Axis.Right, Spread = 180, Flat = true, VMin = 2.6f * sb, VMax = 3.4f * sb, Damp = 4f * sb, Spin = 10,
                    SMin = 0.35f * sb * P, SMax = 0.6f * sb * P, Curve = new[] { (0f, 0.6f), (lasts, 1.3f), (1f, 1.3f) },
                    Ramp = new[] { (0f, Fade(ring, 0)), (lasts * 0.1f, Fade(ring, 0.55f)), (lasts * 1.5f, Fade(ring, 0)), (1f, Fade(ring, 0)) },
                });
            }
        }

        void Screen()
        {
            float bw = Mathf.Clamp(w, 0.2f, 20f);
            int n = h > w * 1.5f ? Math.Clamp((int)Math.Round(h / (w * 1.5f)), 2, 10) : 1;
            float seg = ex.Z / n;
            var aged = new Rgba(0.55f, 0.55f, 0.53f);
            for (int i = 0; i < n; i++)
            {
                float age = n == 1 ? 0.5f : i / (float)(n - 1), peak = 0.97f - 0.3f * age;
                var col = c.Lerp(aged, 0.04f * age);
                layers.Add(new EffectLayer
                {
                    Sprite = "puff", Heat = SmokeHeat.Cold, Amount = Math.Clamp((int)Math.Round((14 + 30 * seg / bw) * A), 3, 500), Fixed = true, Life = 10f * Mathf.Sqrt(H),
                    Box = new Vector3(ex.X * (0.45f + 0.35f * age), 0.05f * bw, seg), At = new Vector3(0, (0.25f + 0.45f * age) * bw * H, -ex.Z + seg * (2 * i + 1)),
                    Spread = 70, VMin = 0.02f * bw * H, VMax = 0.1f * bw * H, Lift = 0.012f * bw * H, Drift = 0.3f * D,
                    SMin = (0.7f + 0.6f * age) * bw * P, SMax = (1.1f + 0.9f * age) * bw * P, Spin = 8,
                    Curve = new[] { (0f, 0.55f), (0.3f, 1f), (1f, 1.45f) },
                    Ramp = new[] { (0f, Fade(col, 0)), (0.18f, Fade(col, peak)), (0.7f, Fade(col, peak * 0.8f)), (1f, Fade(col, 0)) },
                });
            }
            if (n > 1)
                layers.Add(new EffectLayer
                {
                    Sprite = "puff", Heat = SmokeHeat.Cold, Amount = N(14, false), Life = 3.5f, Box = new Vector3(0.15f * bw, 0.05f * bw, 0.1f * bw), At = new Vector3(0, 0.2f * bw * H, -ex.Z + 0.1f * bw),
                    Dir = new Vector3(0, 0.35f, 1).Normalized(), Spread = 20, VMin = 0.25f * bw, VMax = 0.5f * bw, Damp = 0.1f * bw, Lift = 0.03f * bw * H, Drift = 0.3f * D,
                    SMin = 0.5f * bw * P, SMax = 0.8f * bw * P, Curve = new[] { (0f, 0.4f), (1f, 1.6f) },
                    Ramp = new[] { (0f, Fade(c, 0)), (0.08f, Fade(c, 0.95f)), (0.7f, Fade(c, 0.8f)), (1f, Fade(c, 0)) },
                });
        }

        void Oil()
        {
            float fs = Mathf.Clamp(Mathf.Sqrt(Mathf.Min(w, h) * s) * 0.5f, 0.12f, 20f), rh = Mathf.Sqrt(H);
            var lit = new Rgba(0.75f, 0.3f, 0.08f);
            var aged = new Rgba(0.35f, 0.33f, 0.31f);
            layers.Add(new EffectLayer
            {
                Sprite = "flame", Amount = N(30), Life = 1.1f, Box = new Vector3(ex.X * 0.85f, 0.02f, ex.Z * 0.85f), At = new Vector3(0, 0.03f, 0),
                Spread = 10, VMin = 0.5f * fs * rh, VMax = 1.0f * fs * rh, Lift = 0.8f * fs * rh, SMin = 0.7f * fs * P, SMax = 1.25f * fs * P, Spin = 25, Soft = false,
                Curve = new[] { (0f, 0.6f), (0.3f, 1f), (1f, 0.2f) },
                Ramp =
                    new[] { (0f, new Rgba(1, 0.9f, 0.55f, 0)), (0.08f, new Rgba(1, 0.86f, 0.4f, 0.95f)), (0.35f, new Rgba(1, 0.5f, 0.08f, 0.9f)), (0.7f, new Rgba(0.85f, 0.2f, 0.02f, 0.6f)),
                        (1f, new Rgba(0.3f, 0.05f, 0, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "spark", Add = true, Amount = N(8), Life = 2f, Box = new Vector3(ex.X * 0.7f, 0.1f * fs, ex.Z * 0.7f), At = new Vector3(0, 0.6f * fs, 0),
                Spread = 30, VMin = 0.8f * fs * H, VMax = 1.8f * fs * H, Lift = 0.2f * fs * H, Drift = 0.25f * D, SMin = 0.06f * fs, SMax = 0.12f * fs, Spin = 0, Soft = false,
                Ramp = new[] { (0f, new Rgba(1, 0.8f, 0.4f, 0)), (0.1f, new Rgba(1, 0.75f, 0.35f, 1)), (0.7f, new Rgba(1, 0.4f, 0.1f, 0.8f)), (1f, new Rgba(0.6f, 0.1f, 0, 0)) },
            });
            if (e.Smoke)
                layers.Add(new EffectLayer
                {
                    Sprite = "puff", Heat = SmokeHeat.Hot, Amount = N(36), Life = 10f * rh, Box = new Vector3(ex.X * 0.7f, 0.08f * fs, ex.Z * 0.7f), At = new Vector3(0, 0.4f * fs, 0),
                    Spread = 10, VMin = 0.55f * fs * H, VMax = 0.95f * fs * H, Lift = 0.12f * fs * H, Drift = 0.45f * D,
                    SMin = 1.1f * fs * P, SMax = 1.8f * fs * P, Curve = new[] { (0f, 0.55f), (0.15f, 1f), (1f, 3.6f) },
                    Ramp =
                        new[] { (0f, Fade(c.Lerp(lit, 0.6f), 0)), (0.03f, Fade(c.Lerp(lit, 0.45f), 0.9f)), (0.12f, Fade(c, 0.97f)), (0.6f, Fade(c.Lerp(aged, 0.05f), 0.8f)),
                            (1f, Fade(c.Lerp(aged, 0.1f), 0)) },
                });
        }

        void Wake()
        {
            int n = Math.Clamp((int)Math.Round(A), 1, 8);
            float ww = Mathf.Clamp(h * 0.025f, 0.03f, 1.5f) * P;
            for (int j = 0; j < n; j++)
            {
                float fx = n == 1 ? 0 : -1 + 2f * j / (n - 1);
                var tail = new Vector3(fx * ex.X * 0.15f, 0.02f, ex.Z * 0.95f);
                var head = new Vector3(fx * ex.X * 0.9f, 0.02f, -ex.Z * 0.95f);
                int dots = Math.Clamp((int)Math.Round(Vector3.Distance(head, tail) / ww * 3), 8, 600);
                var track = Enumerable.Range(0, dots).Select(i => Vector3.Lerp(tail, head, i / (float)(dots - 1))).ToArray();
                var near = track.Skip(dots * 3 / 4).ToArray();
                layers.Add(new EffectLayer
                {
                    Sprite = "mist", Points = track, Amount = Math.Min(1200, dots * 4), Fixed = true, Life = 3.5f,
                    Dir = Axis.Right, Spread = 180, Flat = true, VMin = 0, VMax = 0.05f * ww, SMin = 1.6f * ww, SMax = 2.6f * ww, Spin = 10,
                    Curve = new[] { (0f, 0.5f), (0.3f, 1f), (1f, 1.6f) },
                    Ramp = new[] { (0f, Fade(c, 0)), (0.15f, Fade(c, 0.45f)), (0.6f, Fade(c, 0.25f)), (1f, Fade(c, 0)) },
                });
                layers.Add(new EffectLayer
                {
                    Sprite = "mist", Points = near, Amount = Math.Min(600, near.Length * 2), Fixed = true, Life = 1.6f,
                    Dir = Axis.Right, Spread = 180, Flat = true, VMin = 0, VMax = 0.1f * ww, SMin = 1.4f * ww, SMax = 2.2f * ww, Spin = 10,
                    Ramp = new[] { (0f, Fade(c, 0)), (0.15f, Fade(c, 0.7f)), (1f, Fade(c, 0)) },
                });
                layers.Add(new EffectLayer
                {
                    Sprite = "mist", Amount = 14, Life = 1.4f, Box = new Vector3(1.2f * ww, 0.01f, 1.2f * ww), At = head,
                    Spread = 60, VMin = 0.2f * ww, VMax = 0.6f * ww, Damp = 0.3f * ww, SMin = 2.5f * ww, SMax = 4f * ww,
                    Ramp = new[] { (0f, Fade(c, 0)), (0.1f, Fade(c, 0.85f)), (0.6f, Fade(c, 0.45f)), (1f, Fade(c, 0)) },
                });
                layers.Add(new EffectLayer
                {
                    Sprite = "drop", Amount = 10, Life = 0.8f, Box = new Vector3(0.8f * ww, 0.01f, 0.8f * ww), At = head,
                    Spread = 25, VMin = 2 * ww, VMax = 4 * ww, Fall = 10 * ww, SMin = 0.4f * ww, SMax = 0.7f * ww, Spin = 0, Soft = false,
                    Ramp = new[] { (0f, Fade(c, 0)), (0.1f, Fade(c, 0.8f)), (1f, Fade(c, 0)) },
                });
            }
        }

        const float FireBurns = 2.2f, LeastLead = 0.8f, MostLead = 1.6f;

        sealed record Missile(float Many, float Wide, float Long, float StuckWide, float StuckLong, float Pitch, float Top, float LeastTop, float Fall, float Stays, bool Shadow, float Dust);

        static readonly Missile Arrow = new(60, 0.024f, 0.17f, 0.022f, 0.11f, 0.95f, 0.55f, 1.2f, 0.5f, 2.4f, true, 0.25f);
        static readonly Missile Javelin = new(14, 0.036f, 0.4f, 0.033f, 0.27f, 0.62f, 0.3f, 0.6f, 0.45f, 3.6f, false, 1f);
        static readonly Missile SlingStone = new(70, 0.022f, 0.06f, 0, 0, 0.42f, 0.22f, 0.45f, 0.32f, 0, false, 0.6f);
        static readonly Rgba Shadow = new(0.03f, 0.03f, 0.04f, 1);
        static readonly Rgba Flame = new(1, 0.62f, 0.22f, 1);
        static readonly Rgba Earth = new(0.52f, 0.45f, 0.35f, 1);

        static float Wrap01(float v) => v - Mathf.Floor(v);

        void Missiles(Missile m, bool fire)
        {
            string mode = EffectKinds.Mode(e);
            if (mode == "random") { MissilesAtWill(m, fire); return; }
            int sections = mode == "rolling" ? Math.Clamp((int)Math.Round(w / Mathf.Max(1.5f, h)), 2, 6) : 1;
            float hw = ex.X / sections;
            for (int i = 0; i < sections; i++)
                MissileVolley(m, new Vector3(-ex.X + hw * (2 * i + 1), 0, 0), new Vector3(hw, 0, ex.Z), sections > 1 ? i / (float)sections * 0.6f : 0, fire);
        }

        (float top, Vector3 dir, float speed, float ahead) Flight(Missile m)
        {
            float top = Mathf.Max(m.LeastTop, m.Top * s * H), down = top / m.Fall, ahead = down / MathF.Tan(m.Pitch);
            var v = new Vector3(0, -down, -ahead);
            return (top, v.Normalized(), v.Length(), ahead);
        }

        int MissileCount(Missile m, Vector3 half) =>
            Math.Clamp((int)Math.Round(m.Many * A * Mathf.Sqrt(Mathf.Max(1, half.X * half.Z * 4 / 6))), Math.Max(2, (int)(m.Many / 10)), 500);

        EffectLayer FallingMissiles(Missile m, Vector3 at, Vector3 half, bool fire, int amount, float life, (float top, Vector3 dir, float speed, float ahead) f) => new()
        {
            Sprite = "streak", Cross = new Vector2(m.Wide * s, m.Long * s), Add = fire, Amount = amount, Life = life,
            Box = new Vector3(half.X * 0.9f, f.top * 0.04f, half.Z * 0.9f), At = at + new Vector3(0, f.top, f.ahead * m.Fall),
            Dir = f.dir, Spread = 2, VMin = f.speed * 0.97f, VMax = f.speed * 1.03f, SMin = P, SMax = P, Spin = 0, Soft = false,
        };

        EffectLayer StuckMissiles(Missile m, Vector3 at, Vector3 half, bool fire, int amount, float life, Vector3 dir) => new()
        {
            Sprite = "streak", Cross = new Vector2(m.StuckWide * s, m.StuckLong * s), Amount = amount, Life = life,
            Box = new Vector3(half.X * 0.9f, 0.001f, half.Z * 0.9f), At = at + new Vector3(0, 0.01f, 0),
            Dir = dir, Spread = 3, VMin = 0.001f, VMax = 0.002f, SMin = P, SMax = P, Spin = 0, Soft = false,
            Ramp = fire
                ? new[] { (0f, Fade(Flame, 0.9f)), (0.25f, Fade(new Rgba(0.25f, 0.2f, 0.16f, 1), 0.9f)), (0.85f, Fade(new Rgba(0.2f, 0.17f, 0.14f, 1), 0.9f)), (1f, Fade(c, 0)) }
                : new[] { (0f, Fade(c, 0.9f)), (0.85f, Fade(c, 0.9f)), (1f, Fade(c, 0)) },
        };

        EffectLayer ArrowDust(Vector3 at, Vector3 half, int amount, float life) => new()
        {
            Sprite = "dust", Amount = amount, Life = life, Box = new Vector3(half.X * 0.9f, 0.01f, half.Z * 0.9f), At = at + new Vector3(0, 0.03f, 0),
            Spread = 60, VMin = 0.1f * s, VMax = 0.3f * s, Damp = 0.3f * s, SMin = 0.08f * s * P, SMax = 0.16f * s * P, Soft = false,
            Curve = new[] { (0f, 0.5f), (1f, 1.6f) },
        };

        void MissileVolley(Missile m, Vector3 at, Vector3 half, float off, bool fire)
        {
            var f = Flight(m);
            float lead = Mathf.Clamp(every * 0.3f, LeastLead, MostLead), life = every, sweep = f.ahead * lead;
            float arrowsOff = Wrap01(off - (lead - m.Fall) / life), landOff = Wrap01(off - lead / life);
            float shadow = Mathf.Max(half.X, half.Z) * 0.7f * P, landed = lead / life;
            int arrows = MissileCount(m, half);
            if (m.Shadow)
                layers.Add(new EffectLayer
            {
                Sprite = "mist", Lying = true, Amount = 28, Fixed = true, Life = life, Explosive = 1, Offset = off,
                Box = new Vector3(half.X * 0.6f, 0, half.Z * 0.6f), At = at + new Vector3(0, 0.04f, sweep),
                Dir = new Vector3(0, 0, -1), Spread = 0, VMin = f.ahead, VMax = f.ahead, SMin = shadow * 0.8f, SMax = shadow * 1.15f, Spin = 4, Soft = false,
                Ramp =
                    new[] { (0f, Fade(Shadow, 0)), (landed * 0.3f, Fade(Shadow, 0.16f)), (landed + 0.15f / life, Fade(Shadow, 0.16f)), (landed + 0.6f / life, Fade(Shadow, 0)), (1f, Fade(Shadow, 0)) },
            });
            var falling = FallingMissiles(m, at, half, fire, arrows, life, f);
            falling.Fixed = true;
            falling.Explosive = 0.9f;
            falling.Offset = arrowsOff;
            float down = m.Fall / life;
            var arrowInk = fire ? Flame : c;
            falling.Ramp = new[] { (0f, Fade(arrowInk, 0)), (down * 0.05f, Fade(arrowInk, 0.95f)), (down * 0.97f, Fade(arrowInk, 0.95f)), (down, Fade(arrowInk, 0)), (1f, Fade(arrowInk, 0)) };
            layers.Add(falling);
            float stays = Mathf.Min(fire ? FireBurns : m.Stays, life * 0.6f) / life;
            if (m.StuckLong > 0)
            {
                var stuck = StuckMissiles(m, at, half, fire, Math.Max(3, arrows * 3 / 5), life, f.dir);
                stuck.Fixed = true;
                stuck.Explosive = 0.9f;
                stuck.Offset = landOff;
                stuck.Ramp = stuck.Ramp.Select(k => (k.at * stays, k.c)).Append((1f, stuck.Ramp[^1].c)).ToArray();
                layers.Add(stuck);
            }
            var dust = ArrowDust(at, half, Math.Max(2, (int)(arrows * m.Dust)), life);
            dust.Fixed = true;
            dust.Explosive = 0.9f;
            dust.Offset = landOff;
            float puff = 0.9f / life;
            dust.Ramp = new[] { (0f, Fade(Earth, 0)), (puff * 0.1f, Fade(Earth, 0.5f)), (puff, Fade(Earth, 0)), (1f, Fade(Earth, 0)) };
            layers.Add(dust);
            if (fire) BurningSpots(at, half, landOff, stays, life, Math.Max(2, arrows / 5));
        }

        void BurningSpots(Vector3 at, Vector3 half, float off, float burns, float life, int spots)
        {
            float fs = 0.06f * s * P;
            layers.Add(new EffectLayer
            {
                Sprite = "flame", Amount = spots, Fixed = true, Life = life, Explosive = 0.9f, Offset = off,
                Box = new Vector3(half.X * 0.9f, 0.01f, half.Z * 0.9f), At = at + new Vector3(0, 0.03f, 0),
                Spread = 10, VMin = 0.05f * fs, VMax = 0.1f * fs, SMin = 1.6f * fs, SMax = 2.4f * fs, Spin = 25, Soft = false,
                Curve = new[] { (0f, 0.4f), (0.1f * burns, 1f), (burns, 0.3f), (1f, 0.3f) },
                Ramp =
                    new[] { (0f, new Rgba(1, 0.9f, 0.55f, 0)), (0.05f * burns, new Rgba(1, 0.8f, 0.35f, 0.95f)), (0.6f * burns, new Rgba(1, 0.45f, 0.08f, 0.8f)), (burns, new Rgba(0.4f, 0.08f, 0, 0)),
                        (1f, new Rgba(0.4f, 0.08f, 0, 0)) },
            });
            if (!e.Smoke) return;
            layers.Add(new EffectLayer
            {
                Sprite = "puff", Amount = Math.Max(2, spots / 2), Fixed = true, Life = life, Explosive = 0.85f, Offset = off,
                Box = new Vector3(half.X * 0.8f, 0.02f, half.Z * 0.8f), At = at + new Vector3(0, 0.1f * s, 0),
                Spread = 15, VMin = 0.15f * s * H, VMax = 0.3f * s * H, Lift = 0.03f * s * H, Drift = 0.4f * D, SMin = 0.25f * s * P, SMax = 0.45f * s * P,
                Curve = new[] { (0f, 0.4f), (1f, 2f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.1f * burns, Fade(c, 0.45f)), (1.4f * burns, Fade(c.Lerp(Rgba.White, 0.2f), 0)), (1f, Fade(c, 0)) },
            });
        }

        void MissilesAtWill(Missile m, bool fire)
        {
            var f = Flight(m);
            var half = new Vector3(ex.X, 0, ex.Z);
            int arrows = Math.Max(3, MissileCount(m, half) / 4);
            var falling = FallingMissiles(m, Vector3.Zero, half, fire, arrows, m.Fall, f);
            var ink = fire ? Flame : c;
            falling.Ramp = new[] { (0f, Fade(ink, 0)), (0.05f, Fade(ink, 0.95f)), (0.97f, Fade(ink, 0.95f)), (1f, Fade(ink, 0)) };
            layers.Add(falling);
            if (m.StuckLong > 0) layers.Add(StuckMissiles(m, Vector3.Zero, half, fire, Math.Max(3, (int)(arrows * m.Stays / m.Fall * 0.5f)), m.Stays, f.dir));
            var dust = ArrowDust(Vector3.Zero, half, Math.Max(2, (int)(arrows * m.Dust * 2)), 0.9f);
            dust.Ramp = new[] { (0f, Fade(Earth, 0)), (0.1f, Fade(Earth, 0.45f)), (1f, Fade(Earth, 0)) };
            layers.Add(dust);
        }

        void Melee()
        {
            layers.Add(new EffectLayer
            {
                Sprite = "dust", Amount = N(20), Life = 3.5f, Box = new Vector3(ex.X * 0.85f, 0.02f, ex.Z * 0.85f), At = new Vector3(0, 0.08f * s, 0),
                Spread = 80, VMin = 0.1f * s * H, VMax = 0.3f * s * H, Damp = 0.1f * s, Lift = 0.02f * s * H, Drift = 0.3f * D,
                SMin = 0.45f * s * P, SMax = 0.85f * s * P, Curve = new[] { (0f, 0.5f), (1f, 1.6f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.2f, Fade(c, 0.75f)), (0.7f, Fade(c.Lerp(Rgba.White, 0.1f), 0.45f)), (1f, Fade(c, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "dust", Amount = N(8), Life = 1.4f, Box = new Vector3(ex.X * 0.75f, 0.02f, ex.Z * 0.75f), At = new Vector3(0, 0.04f * s, 0),
                Spread = 50, VMin = 0.4f * s * H, VMax = 0.8f * s * H, Fall = 1.2f * s * H, SMin = 0.12f * s * P, SMax = 0.22f * s * P, Soft = false,
                Ramp = new[] { (0f, Fade(c.Lerp(Earth, 0.5f), 0)), (0.1f, Fade(c.Lerp(Earth, 0.5f), 0.7f)), (1f, Fade(c, 0)) },
            });
        }

        void Rockfall()
        {
            var rng = new Random(EffectKinds.Seed(phase));
            int n = Math.Clamp((int)Math.Round(4 * A), 1, 16);
            bool rolling = EffectKinds.Mode(e) == "rolling";
            float sb = Mathf.Clamp(s / 3f, 0.25f, 20f), pull = 4f * s * H, start = 1.2f * s * H;
            float flight = Mathf.Clamp(Mathf.Sqrt(2 * start / pull), 0.3f, 2f);
            for (int i = 0; i < n; i++)
            {
                var at = new Vector3(((float)rng.NextDouble() * 2 - 1) * ex.X * 0.8f, 0, ((float)rng.NextDouble() * 2 - 1) * ex.Z * 0.6f);
                float off = rolling ? i / (float)n : (float)rng.NextDouble() * 0.08f;
                float size = sb * (0.6f + 0.7f * (float)rng.NextDouble());
                Stone(at, size, off, 0.3f * s, start, flight);
                RollingOn(at, size, off + flight / every);
            }
        }

        void RollingOn(Vector3 at, float sb, float off)
        {
            var rock = new Rgba(0.3f, 0.28f, 0.25f, 1);
            float rolls = 1.2f / every;
            layers.Add(new EffectLayer
            {
                Sprite = "drop", Amount = 4, Fixed = true, Life = every, Explosive = 1, Offset = Wrap01(off), Box = new Vector3(0.1f * sb, 0.02f, 0.05f * sb), At = at + new Vector3(0, 0.04f, 0),
                Dir = new Vector3(0, 0.15f, -1), Spread = 30, VMin = 0.5f * sb, VMax = 1.3f * sb, Damp = 0.6f * sb, SMin = 0.1f * sb * P, SMax = 0.2f * sb * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(rock, 0)), (0.03f * rolls, Fade(rock, 1)), (0.8f * rolls, Fade(rock, 1)), (rolls, Fade(rock, 0)), (1f, Fade(rock, 0)) },
            });
        }

        void Stones()
        {
            var rng = new Random(EffectKinds.Seed(phase));
            int n = Math.Clamp((int)Math.Round(2 * A), 1, 8);
            bool rolling = EffectKinds.Mode(e) == "rolling";
            float sb = Mathf.Clamp(s / 2.2f, 0.4f, 20f);
            for (int i = 0; i < n; i++)
            {
                var at = new Vector3(((float)rng.NextDouble() * 2 - 1) * ex.X * 0.7f, 0, ((float)rng.NextDouble() * 2 - 1) * ex.Z * 0.7f);
                float off = rolling ? i / (float)n * 0.7f : (float)rng.NextDouble() * 0.08f;
                Stone(at, sb * (0.85f + 0.3f * (float)rng.NextDouble()), off, 2.4f * s, 0.45f * s * H, Mathf.Clamp(1.1f * Mathf.Sqrt(H), 0.7f, 2f));
            }
        }

        void Stone(Vector3 at, float sb, float off, float reach, float start, float flight)
        {
            float pull = 4f * s * H;
            float up = (pull * flight * flight / 2 - start) / flight, ahead = reach / flight;
            var v = new Vector3(0, up, -ahead);
            float life = every, landed = flight / life, landOff = Wrap01(off - landed);
            var rock = new Rgba(0.3f, 0.28f, 0.25f, 1);
            layers.Add(new EffectLayer
            {
                Sprite = "drop", Amount = 1, Fixed = true, Life = life, Explosive = 1, Offset = off, Box = Vector3.One * 0.001f,
                At = at + new Vector3(0, start, reach), Dir = v.Normalized(), Spread = 0, VMin = v.Length(), VMax = v.Length(), Fall = pull,
                SMin = 0.45f * sb * P, SMax = 0.45f * sb * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(rock, 0)), (landed * 0.05f, Fade(rock, 1)), (landed * 0.98f, Fade(rock, 1)), (landed, Fade(rock, 0)), (1f, Fade(rock, 0)) },
            });
            float burst = 0.6f / life, hangs = Mathf.Min(4.5f, life * 0.7f) / life;
            layers.Add(new EffectLayer
            {
                Sprite = "dust", Amount = 26, Fixed = true, Life = life, Explosive = 1, Offset = landOff, Box = new Vector3(0.12f * sb, 0.02f, 0.12f * sb), At = at + new Vector3(0, 0.05f, 0),
                Spread = 40, VMin = 1.2f * sb * H, VMax = 2.4f * sb * H, Fall = 4f * sb * H, Damp = 0.8f * sb, SMin = 0.6f * sb * P, SMax = 1.1f * sb * P, Soft = false,
                Ramp = new[] { (0f, Fade(Earth, 0)), (0.02f * burst, Fade(Earth, 0.95f)), (burst, Fade(Earth, 0)), (1f, Fade(Earth, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "drop", Amount = 10, Fixed = true, Life = life, Explosive = 1, Offset = landOff, Box = new Vector3(0.08f * sb, 0.02f, 0.08f * sb), At = at + new Vector3(0, 0.05f, 0),
                Spread = 50, VMin = 1.5f * sb, VMax = 3f * sb, Fall = 9f * sb, SMin = 0.05f * sb * P, SMax = 0.09f * sb * P, Spin = 0, Soft = false,
                Ramp = new[] { (0f, Fade(rock, 0)), (0.02f * burst, Fade(rock, 1)), (burst, Fade(rock, 0)), (1f, Fade(rock, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "puff", Amount = 8, Fixed = true, Life = life, Explosive = 0.9f, Offset = landOff, Box = new Vector3(0.2f * sb, 0.05f * sb, 0.2f * sb), At = at + new Vector3(0, 0.15f * sb, 0),
                Spread = 50, VMin = 0.2f * sb * H, VMax = 0.5f * sb * H, Damp = 0.2f * sb, Lift = 0.05f * sb * H, Drift = 0.3f * D, SMin = 1.2f * sb * P, SMax = 2f * sb * P,
                Curve = new[] { (0f, 0.4f), (1f, 2f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.05f * hangs, Fade(c, 0.8f)), (0.5f * hangs, Fade(c.Lerp(Rgba.White, 0.15f), 0.4f)), (hangs, Fade(c, 0)), (1f, Fade(c, 0)) },
            });
        }

        void Smoke()
        {
            layers.Add(new EffectLayer
            {
                Sprite = "puff", Heat = SmokeHeat.Hot, Amount = N(16 * Mathf.Sqrt(H)), Life = 5f * Mathf.Sqrt(H), Box = new Vector3(ex.X * 0.6f, 0.05f * s, ex.Z * 0.6f), At = new Vector3(0, 0.1f * s, 0),
                Spread = 12, VMin = 0.35f * s * H, VMax = 0.6f * s * H, Lift = 0.05f * s * H, Drift = 0.35f * D, SMin = 0.6f * s * P, SMax = 1.0f * s * P,
                Curve = new[] { (0f, 0.35f), (0.3f, 0.8f), (1f, 1.9f) },
                Ramp = new[] { (0f, Fade(c, 0)), (0.12f, Fade(c, 0.6f)), (0.6f, Fade(c.Lerp(Rgba.White, 0.15f), 0.35f)), (1f, Fade(c.Lerp(Rgba.White, 0.3f), 0)) },
            });
        }

        void Burst(Vector3 at, float sb, float cycle, float offset, float count)
        {
            int Cnt(float n) => Math.Max(1, (int)Math.Round(n * A * count));
            layers.Add(new EffectLayer
            {
                Sprite = "flame", Add = true, Amount = Cnt(10), Fixed = true, Life = cycle, Explosive = 1, Offset = offset, Box = new Vector3(0.15f * sb, 0.05f * sb, 0.15f * sb), At = at
                    + new Vector3(0, 0.2f * sb, 0),
                Spread = 70, VMin = 0.8f * sb * H, VMax = 1.8f * sb * H, Damp = 2f * sb, SMin = 0.6f * sb * P, SMax = 1.1f * sb * P, Curve =
                    new[] { (0f, 0.4f), (0.03f, 1f), (0.12f, 1.2f), (1f, 1.2f) },
                Ramp =
                    new[] { (0f, new Rgba(1, 1, 0.9f, 1)), (0.03f, new Rgba(1, 0.75f, 0.3f, 1)), (0.1f, new Rgba(0.8f, 0.25f, 0.05f, 0.5f)), (0.18f, new Rgba(0.3f, 0.05f, 0, 0)),
                        (1f, new Rgba(0, 0, 0, 0)) },
            });
            layers.Add(new EffectLayer
            {
                Sprite = "dust", Amount = Cnt(22), Fixed = true, Life = cycle, Explosive = 1, Offset = offset, Box = new Vector3(0.2f * sb, 0.05f, 0.2f * sb), At = at + new Vector3(0, 0.05f, 0),
                Spread = 32, VMin = 2.5f * sb * H, VMax = 4.5f * sb * H, Fall = 6f * sb * H, Damp = 0.6f * sb, SMin = 0.18f * sb * P, SMax = 0.45f * sb * P, Soft = false,
                Ramp =
                    new[] { (0f, new Rgba(0.38f, 0.31f, 0.24f, 0)), (0.01f, new Rgba(0.38f, 0.31f, 0.24f, 1)), (0.18f, new Rgba(0.42f, 0.35f, 0.27f, 0.9f)), (0.3f, new Rgba(0.45f, 0.38f, 0.3f, 0)),
                        (1f, new Rgba(0.45f, 0.38f, 0.3f, 0)) },
            });
            if (e.Smoke)
                layers.Add(new EffectLayer
                {
                    Sprite = "puff", Heat = SmokeHeat.Cold, Amount = Cnt(12), Fixed = true, Life = cycle, Explosive = 0.85f, Offset = offset, Box = new Vector3(0.3f * sb, 0.1f * sb, 0.3f * sb), At = at
                        + new Vector3(0, 0.3f * sb, 0),
                    Spread = 45, VMin = 0.8f * sb * H, VMax = 1.6f * sb * H, Damp = 0.9f * sb * H, Lift = 0.12f * sb * H, Drift = 0.3f * D,
                    SMin = 0.6f * sb * P, SMax = 1.1f * sb * P, Curve = new[] { (0f, 0.3f), (0.12f, 1f), (1f, 2.2f) },
                    Ramp = new[] { (0f, Fade(c, 0)), (0.04f, Fade(c, 0.85f)), (0.55f, Fade(c.Lerp(Rgba.White, 0.2f), 0.4f)), (1f, Fade(c.Lerp(Rgba.White, 0.35f), 0)) },
                });
        }

        void Weather()
        {
            float thin = (float)SmokeDrift.ThinShare(wind), rise = (float)SmokeDrift.RiseShare(wind);
            foreach (var l in layers.Where(l => l.Heat != SmokeHeat.None))
            {
                l.Drift = D;
                if (l.Brake <= 0) l.Brake = RideAfter;
                if (l.Heat == SmokeHeat.Hot)
                {
                    l.VMin *= rise;
                    l.VMax *= rise;
                    l.Lift *= rise;
                    l.Rise = (l.VMin + l.VMax) / 2;
                }
                else if (l.Reach <= 0) l.Ramp = Thinned(l.Ramp, thin);
            }
        }

        static (float at, Rgba c)[] Thinned((float at, Rgba c)[] ramp, float share) => share >= 1
            ? ramp
            : ramp.Select(k => (k.at * share, k.c)).Append((1f, ramp[^1].c with { A = 0 })).ToArray();

        public EffectPlan Finish(bool ownPicture)
        {
            if (e.OwnPicture && ownPicture && layers.Count > 0)
            {
                var first = layers[0];
                first.Picture = true;
                first.Ramp = first.Ramp.Select(r => (r.at, Rgba.White with { A = r.c.A })).ToArray();
            }

            Weather();

            plan.Span = Mathf.Max(w, h) / 2;
            plan.Rise = s * H;
            plan.TrailSpan = Mathf.Max(w, h);
            bool firing = EffectKinds.Fires(e);
            foreach (var l in layers)
            {
                if (!l.Fixed) l.Amount = Math.Clamp(l.Amount, 1, l.Reach > 0 ? SmokeDrift.MostParticles : 900);
                if (firing && l.Explosive > 0) l.Start = ((phase / l.Cycles + l.Offset) % 1) * l.Life;
            }

            string mode = EffectKinds.Mode(e);
            if (e.Light && EffectKinds.Burns(e.Kind) && e.Kind is not ("camp" or "lamps" or "flare"))
            {
                bool pulse = e.Kind is "burst" || e.Kind is "cannon" or "powder" or "broadside" && mode == "together";
                plan.Light = new EffectLightSpec
                {
                    Color = e.Kind is "sparks" ? c : e.Kind is "cannon" or "powder" or "broadside" ? new Rgba(1, 0.78f, 0.5f) : new Rgba(1, 0.6f, 0.28f),
                    Range = Mathf.Clamp(plan.Span * 2.2f + s * 2.5f, 1.5f, 60), Attenuation = 1.3f,
                    Position = new Vector3(0, e.Kind == "fire" ? 0.6f * s * H : 0.3f * s, e.Kind is "cannon" or "powder" ? -ex.Z * 0.8f : e.Kind == "broadside" ? -ex.Z * 0.6f : 0),
                    Base = e.Kind switch
                    {
                        "fire" => 1.5f, "oil" => 1.8f, "burst" => 2.8f, "barrage" => 1.0f, "broadside" => pulse ? 3.2f : 1.6f, "firearrows" => 0.9f,
                        "cannon" => pulse ? 2.2f : 1.2f, "powder" => pulse ? 1.2f : mode == "rolling" ? 0.7f : 0.45f, _ => 0.5f,
                    } * Mathf.Clamp(Mathf.Sqrt(A), 0.5f, 1.6f),
                    Mode = pulse ? 2 : e.Kind is "fire" or "oil" or "powder" or "cannon" or "barrage" or "broadside" or "firearrows" ? 1 : 0,
                    Every = every, Phase = phase * every, Speed = (float)Math.Clamp(e.Speed, 0.1, 5),
                };
            }
            plan.Speed = Math.Clamp(e.Speed, 0.1, 5);
            return plan;
        }
    }
}

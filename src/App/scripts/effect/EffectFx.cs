using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.App;

public static class EffectFx
{
    public static Color DefaultColor(string kind) => new(EffectKinds.DefaultColor(kind));

    public static Color ColorOf(EffectDef e) => e.Color is { Length: > 0 } c ? Coords.ParseColor(c, DefaultColor(e.Kind)) : DefaultColor(e.Kind);

    public sealed class Emitter
    {
        public required GpuParticles3D Node;
        public Vector3 Lift;
        public float Drift;
        public float Brake, Blast;
        public Aabb Seen;
        public StandardMaterial3D? Lit;
        public double Speed = 1;
        public float Reach;
        public SmokeHeat Heat;
        public float Rise;
    }

    public static List<Emitter> Build(Node3D host, EffectDef e, float w, float h, float phase, Texture2D? own)
    {
        var c = ColorOf(e);
        var plan = EffectRecipe.Plan(e, w, h, phase, new Rgba(c.R, c.G, c.B, c.A), own != null, SmokeAirNow);
        foreach (var l in plan.Lights) host.AddChild(Light(l));
        if (plan.Lamps is { } lamps)
        {
            var node = new EffectLamps();
            host.AddChild(node);
            node.Setup(lamps.At.Select(G).ToArray(), lamps.Size, C(lamps.Color), lamps.Lights, lamps.Range, lamps.Energy, lamps.Speed);
        }
        var shots = plan.Projectiles is { } p ? Shots(host, p, plan.TrailSpan, plan.Rise) : new List<Emitter>();
        var made = new List<Emitter>();
        foreach (var l in plan.Layers)
        {
            made.Add(Emit(host, l, own, plan.Span, plan.Rise, l.Start));
        }
        if (plan.Light is { } light) host.AddChild(Light(light));
        made.AddRange(shots);
        foreach (var m in made)
        {
            m.Speed = plan.Speed;
            m.Node.SpeedScale = m.Speed;
        }
        return made;
    }

    static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    static Color C(Rgba c) => new(c.R, c.G, c.B, c.A);

    static EffectLight Light(EffectLightSpec l)
    {
        var light = new EffectLight
        {
            LightColor = C(l.Color), OmniRange = l.Range, OmniAttenuation = l.Attenuation, ShadowEnabled = false,
            Position = G(l.Position), Base = l.Base, Mode = l.Mode, Phase = l.Phase, Speed = l.Speed,
        };
        if (l.Every is { } every) light.Every = every;
        return light;
    }

    static List<Emitter> Shots(Node3D host, ProjectilesSpec p, float span, float rise)
    {
        var made = new List<Emitter>();
        var node = new EffectProjectiles { Flare = p.Flare, Every = p.Every, Speed = p.Speed, Flight = p.Flight, Arc = p.Arc, LightBase = p.LightBase };
        host.AddChild(node);
        foreach (var s in p.Shots)
        {
            var shot = new EffectProjectiles.Shot { Phase = s.Phase, Wobble = s.Wobble, From = G(s.From), To = G(s.To), Head = new Node3D() };
            node.AddChild(shot.Head);
            shot.Head.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = Vector2.One * s.HeadSize }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = Glow(C(s.HeadColor)),
            });
            foreach (var t in s.Trail)
            {
                var m = Emit(shot.Head, t, null, span, rise, 0);
                m.Node.Emitting = false;
                shot.Trail.Add(m.Node);
                made.Add(m);
            }
            if (s.LightRange is { } range)
            {
                shot.Light = new OmniLight3D { LightColor = C(s.LightColor), OmniRange = range, OmniAttenuation = 1.1f, ShadowEnabled = false, Visible = false };
                shot.Head.AddChild(shot.Light);
            }
            foreach (var b in s.Burst)
            {
                var m = Emit(node, b, null, span, rise, 0);
                m.Node.OneShot = true;
                m.Node.Emitting = false;
                shot.Burst.Add(m.Node);
                made.Add(m);
            }
            node.Shots.Add(shot);
        }
        return made;
    }

    public static StandardMaterial3D Glow(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, AlbedoTexture = Sprite("spark"), AlbedoColor = c,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    static readonly Dictionary<Vector2, ArrayMesh> CrossMeshes = new();

    static ArrayMesh CrossMesh(Vector2 size)
    {
        if (CrossMeshes.TryGetValue(size, out var have)) return have;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float hw = size.X / 2, hl = size.Y / 2;
        foreach (var side in new[] { Vector3.Right, Vector3.Back })
        {
            var a = -side * hw;
            var b = side * hw;
            var q =
                new[] { (a + Vector3.Down * hl, new Vector2(0, 1)), (b + Vector3.Down * hl, new Vector2(1, 1)), (b + Vector3.Up * hl, new Vector2(1, 0)), (a + Vector3.Up * hl, new Vector2(0, 0)) };
            foreach (var k in new[] { 0, 1, 2, 0, 2, 3 }) { st.SetUV(q[k].Item2); st.AddVertex(q[k].Item1); }
        }
        return CrossMeshes[size] = st.Commit();
    }

    static bool Upright(EffectLayer l) => l.Sprite is "puff" or "flame" && !l.Picture;

    static Emitter Emit(Node3D host, EffectLayer l, Texture2D? own, float span, float rise, float preprocess)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = G(l.Box),
            Direction = G(l.Dir), Spread = l.Spread, Flatness = l.Flat ? 1 : 0,
            InitialVelocityMin = l.VMin, InitialVelocityMax = l.VMax,
            Gravity = new Vector3(0, l.Lift - l.Fall, 0),
            DampingMin = l.Damp * 0.8f, DampingMax = l.Damp,
            ScaleMin = l.SMin, ScaleMax = l.SMax, ScaleCurve = CurveTex(l.Curve),
            AngleMin = Upright(l) ? -18 : 0, AngleMax = Upright(l) ? 18 : 360,
            AngularVelocityMin = Upright(l) ? -Mathf.Min(l.Spin, 8) : -l.Spin, AngularVelocityMax = Upright(l) ? Mathf.Min(l.Spin, 8) : l.Spin,
            ColorRamp = Ramp(l.Ramp.Select(r => (r.at, C(r.c))).ToArray()),
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            BlendMode = l.Add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            AlbedoTexture = l.Picture && own != null ? own : Sprite(l.Sprite),
            ProximityFadeEnabled = l.Soft, ProximityFadeDistance = Mathf.Clamp(l.SMax * 0.3f, 0.05f, 3f),
        };
        float reachY = l.At.Y + rise * 9 + l.SMax * 3 + 1, reachX = span + l.SMax * 3 + rise * 4 + 2;
        if (l.Points is { Length: > 0 } pts)
        {
            var img = Image.CreateEmpty(pts.Length, 1, false, Image.Format.Rgbf);
            for (int i = 0; i < pts.Length; i++) img.SetPixel(i, 0, new Color(pts[i].X, pts[i].Y, pts[i].Z));
            pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Points;
            pm.EmissionPointTexture = ImageTexture.CreateFromImage(img);
            pm.EmissionPointCount = pts.Length;
        }
        if (l.Cross != System.Numerics.Vector2.Zero)
        {
            mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;
            pm.ParticleFlagAlignY = true;
            pm.AngleMin = pm.AngleMax = 0;
            pm.AngularVelocityMin = pm.AngularVelocityMax = 0;
        }
        if (l.Lying)
        {
            mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;
            pm.ParticleFlagRotateY = true;
        }
        if (l.Turb > 0)
        {
            pm.TurbulenceEnabled = true;
            pm.TurbulenceNoiseStrength = 1;
            pm.TurbulenceNoiseScale = 6;
            pm.TurbulenceInfluenceMin = l.Turb * 0.05f;
            pm.TurbulenceInfluenceMax = l.Turb * 0.12f;
        }
        var em = new GpuParticles3D
        {
            ProcessMaterial = pm, MaterialOverride = mat,
            DrawPass1 = l.Cross != System.Numerics.Vector2.Zero ? CrossMesh(new Vector2(l.Cross.X, l.Cross.Y))
                : new QuadMesh { Size = Vector2.One, Orientation = l.Lying ? PlaneMesh.OrientationEnum.Y : PlaneMesh.OrientationEnum.Z },
            Amount = l.Amount, Lifetime = l.Life, Explosiveness = l.Explosive, Randomness = l.Explosive > 0 ? 0 : 0.4f,
            Preprocess = preprocess >= 0 ? preprocess : l.Life, Position = G(l.At), LocalCoords = false, FixedFps = 30, Interpolate = true,
            VisibilityAabb = new Aabb(new Vector3(-reachX, -1, -reachX), new Vector3(reachX * 2, reachY + 1, reachX * 2)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        bool glows = l.Add || l.Glow || l.Sprite is "flame" or "flash" or "muzzle" or "spark";
        if (!glows) mat.AlbedoColor = ShadeNow;
        host.AddChild(em);
        var made = new Emitter { Node = em, Lift = pm.Gravity, Drift = l.Drift, Lit = glows ? null : mat, Reach = l.Reach, Heat = l.Heat, Rise = l.Rise };
        if (l.Brake > 0)
        {
            made.Brake = l.Brake;
            made.Blast = Mathf.Max(l.VMax * 1.5f, 1f);
            made.Seen = em.VisibilityAabb;
            pm.DampingCurve = StepTex(l.Brake, 1, 0);
            FollowWind(made, pm);
        }
        return made;
    }

    static float RideSpeed(Emitter m) => Mathf.Max(StillDrift, Mathf.Max(
        SmokeDrift.ScreenWind(SmokeAirNow) * m.Drift,
        m.Reach / Mathf.Max((float)m.Node.Lifetime * (1 - m.Brake), MinDriftSeconds)));

    static float DriftSpeed(Emitter m) => m.Heat == SmokeHeat.None
        ? StillDrift + WindNow.Length() * m.Drift * WindCarrySeconds
        : new Vector2(RideSpeed(m), m.Rise).Length();

    static Vector3 Pushed(Emitter m) => m.Heat == SmokeHeat.None
        ? m.Lift + WindNow * m.Drift
        : new Vector3(0, m.Heat == SmokeHeat.Hot ? m.Rise / CatchUpSeconds : m.Lift.Y, 0) + WindHeading * RideSpeed(m) / CatchUpSeconds;

    static void FollowWind(Emitter m, ParticleProcessMaterial pm)
    {
        pm.VelocityLimitCurve = StepTex(m.Brake, m.Blast, DriftSpeed(m));
        float reach = (float)m.Node.Lifetime * DriftSpeed(m);
        m.Node.VisibilityAabb = m.Seen.Grow(reach);
    }

    const float WindCarrySeconds = 1.5f, StillDrift = SmokeDrift.StillDrift, BrakeEase = 0.08f, MinDriftSeconds = 0.5f, CatchUpSeconds = 1;

    static CurveTexture StepTex(float at, float before, float after)
    {
        var c = new Curve { MinValue = 0, MaxValue = Mathf.Max(before, after) };
        foreach (var (x, y) in new[] { (0f, before), (at, before), (Mathf.Min(1, at + BrakeEase), after), (1f, after) })
            c.AddPoint(new Vector2(x, y), 0, 0, Curve.TangentMode.Linear, Curve.TangentMode.Linear);
        return new CurveTexture { Curve = c };
    }

    public static Vector3 WindNow { get; private set; } = Pull(new Wind());

    public static Vector3 WindHeading { get; private set; } = Heading(new Wind());

    static Wind _wind = new();
    static Calibration? _scale;

    public static SmokeAir SmokeAirNow { get; private set; } = SmokeAir.Of(new Wind(), null);

    public static bool SetScale(Calibration c)
    {
        _scale = c;
        return SteerAir();
    }

    static bool SteerAir()
    {
        var air = SmokeAir.Of(_wind, _scale);
        if (air == SmokeAirNow) return false;
        SmokeAirNow = air;
        return true;
    }

    const double TopWind = 5, TopWindFelt = 1.5;

    public static Vector3 Pull(Wind w)
    {
        double s = Math.Clamp(w.Strength, 0, TopWind);
        double felt = s * (1 + (TopWindFelt - 1) * s / TopWind);
        return Heading(w) * (float)felt;
    }

    static Vector3 Heading(Wind w)
    {
        double to = (w.From + 180) * Math.PI / 180;
        return new Vector3((float)Math.Sin(to), 0, -(float)Math.Cos(to));
    }

    public static Color ShadeNow { get; private set; } = Colors.White;

    public static bool SetShade(Lighting l, Color tint)
    {
        float k = Mathf.Clamp((float)((l.Ambient + l.SunEnergy * 0.7) / (0.72 + 0.35 * 0.7)), 0.1f, 1.15f);
        var c = new Color(tint.R * k, tint.G * k, tint.B * k);
        if (c.IsEqualApprox(ShadeNow)) return false;
        ShadeNow = c;
        return true;
    }

    public static void Shade(List<Emitter> emitters)
    {
        foreach (var m in emitters) if (m.Lit != null && GodotObject.IsInstanceValid(m.Lit)) m.Lit.AlbedoColor = ShadeNow;
    }

    public static bool SetWind(Wind w)
    {
        var v = Pull(w);
        var heading = Heading(w);
        _wind = w.Clone();
        bool changed = SteerAir() | !v.IsEqualApprox(WindNow) | !heading.IsEqualApprox(WindHeading);
        WindNow = v;
        WindHeading = heading;
        return changed;
    }

    public static void Steer(Node3D host, List<Emitter> emitters)
    {
        if (emitters.Count == 0) return;
        foreach (var m in emitters)
            if (GodotObject.IsInstanceValid(m.Node) && m.Node.ProcessMaterial is ParticleProcessMaterial pm)
            {
                pm.Gravity = Pushed(m);
                if (m.Brake > 0) FollowWind(m, pm);
            }
    }

    static readonly Dictionary<string, ImageTexture> Sprites = new();
    static readonly Dictionary<string, Image> SpriteImages = new();

    public static Texture2D Sprite(string name)
    {
        if (Sprites.TryGetValue(name, out var t)) return t;
        var img = SpriteImage(name).Duplicate() as Image ?? SpriteImage(name);
        img.GenerateMipmaps();
        return Sprites[name] = ImageTexture.CreateFromImage(img);
    }

    static Image SpriteImage(string name)
    {
        if (SpriteImages.TryGetValue(name, out var have)) return have;
        const int S = 128;
        var px = new byte[S * S * 4];
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Seed = name.Length * 131 + name[0], Frequency = name == "puff" ? 0.028f : 0.045f, FractalOctaves = name == "puff" ? 3 : 4 };
        var rng = new Random(name.Aggregate(1847, (h, ch) => h * 31 + ch));
        var lumps = Enumerable.Range(0, 6).Select(_ => (x: (float)(rng.NextDouble() - 0.5) * 0.36f, y: (float)(rng.NextDouble() - 0.5) * 0.32f, r: 0.24f + (float)rng.NextDouble() * 0.12f)).ToArray();
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S - 0.5f, v = (y + 0.5f) / S - 0.5f;
                float d = Mathf.Sqrt(u * u + v * v) * 2;
                float n = noise.GetNoise2D(x, y) * 0.5f + 0.5f;
                float a = 0, lum = 1;
                switch (name)
                {
                    case "puff":
                    case "dust":
                    {
                        float body = 0;
                        foreach (var (lx, ly, lr) in lumps)
                        {
                            float dx = u - lx, dy = v - ly;
                            body = Mathf.Max(body, 1 - Mathf.Sqrt(dx * dx + dy * dy) / lr);
                        }
                        body = Mathf.Clamp(body * 1.25f, 0, 1);
                        float grain = name == "dust" ? 0.55f + 0.45f * (noise.GetNoise2D(x * 3.1f, y * 3.1f) * 0.5f + 0.5f) : 1;
                        a = Mathf.Pow(Mathf.SmoothStep(0, 1, body), 1.4f) * (0.5f + 0.5f * n) * grain * Mathf.Clamp((1 - d) * 2.2f, 0, 1);
                        lum = name == "dust" ? 0.9f + 0.1f * n : Mathf.Clamp(0.8f - v * 0.6f + (n - 0.5f) * 0.18f, 0.5f, 1);
                        break;
                    }
                    case "mist":
                    {
                        float g = Mathf.Exp(-d * d * 3.2f);
                        a = g * (0.6f + 0.4f * n) * Mathf.Clamp((1 - d) * 4, 0, 1);
                        break;
                    }
                    case "flame":
                    {
                        float ty = (v + 0.5f);
                        float half = 0.08f + 0.3f * Mathf.Pow(Mathf.Clamp(ty, 0, 1), 0.7f);
                        float wob = (noise.GetNoise2D(x * 0.7f, y * 1.6f)) * 0.08f;
                        float side = Mathf.Abs(u + wob) / Mathf.Max(0.02f, half);
                        float shape = Mathf.Clamp(1 - side, 0, 1) * Mathf.Clamp((1 - Mathf.Abs(v - 0.05f) * 2.3f) * 1.5f, 0, 1);
                        a = Mathf.Pow(shape, 0.8f) * (0.7f + 0.3f * n) * Mathf.Clamp((1 - d) * 2.5f, 0, 1);
                        lum = 0.75f + 0.25f * shape;
                        break;
                    }
                    case "flash":
                    {
                        float r = d * (1 + 0.35f * noise.GetNoise2D(x * 0.5f, y * 0.5f));
                        float core = Mathf.Exp(-r * r * 18);
                        a = Mathf.Clamp(core + Mathf.Exp(-r * r * 5) * 0.45f, 0, 1) * Mathf.Clamp((1 - d) * 3, 0, 1);
                        lum = 0.85f + 0.15f * core;
                        break;
                    }
                    case "muzzle":
                    {
                        float along = 1 - Mathf.Clamp(v + 0.5f, 0, 1);
                        float wob = noise.GetNoise2D(x * 0.9f, y * 0.9f);
                        float du = u / 0.2f, dv = (along - 0.34f) / 0.23f, rb = Mathf.Sqrt(du * du + dv * dv) * (1 + 0.45f * wob);
                        float ball = Mathf.Clamp((1 - rb) * 2.2f, 0, 1);
                        float jet = along < 0.34f ? Mathf.Clamp((1 - Mathf.Abs(u + wob * 0.05f) / (0.025f + 0.5f * along)) * 2, 0, 1) : 0;
                        float rag = noise.GetNoise2D(x * 2.2f + 40, y * 2.2f) * 0.5f + 0.5f;
                        a = Mathf.Clamp(Mathf.Max(ball, jet) * (0.7f + 0.3f * rag), 0, 1);
                        lum = 0.75f + 0.25f * Mathf.Max(ball, jet);
                        break;
                    }
                    case "streak":
                    {
                        a = Mathf.Exp(-u * u * 60) * Mathf.Clamp((0.5f - Mathf.Abs(v)) * 5, 0, 1);
                        break;
                    }
                    case "spark":
                    case "drop":
                    {
                        float core = Mathf.Exp(-d * d * (name == "spark" ? 30 : 12));
                        float glow = Mathf.Exp(-d * d * 5) * (name == "spark" ? 0.45f : 0.25f);
                        a = Mathf.Clamp(core + glow, 0, 1) * Mathf.Clamp((1 - d) * 4, 0, 1);
                        break;
                    }
                }
                int i = (y * S + x) * 4;
                byte l8 = (byte)Mathf.Clamp(lum * 255, 0, 255);
                px[i] = l8;
                px[i + 1] = l8;
                px[i + 2] = l8;
                px[i + 3] = (byte)Mathf.Clamp(a * 255, 0, 255);
            }
        var img = Image.CreateFromData(S, S, false, Image.Format.Rgba8, px);
        return SpriteImages[name] = img;
    }

    static readonly Dictionary<string, ImageTexture> Icons = new();

    public static bool IsEffect(PieceDef d) => d.Kind != "model" && d.Tags.Contains("effect");

    public static Texture2D? ListIcon(PieceDef d) =>
        !IsEffect(d) || d.Effect is { OwnPicture: true } && d.Image != null ? null : Icon(d.Effect ?? EffectKinds.FromName(d.Name));

    public static Texture2D Icon(EffectDef e)
    {
        var key = $"{e.Kind}|{e.Color}|{e.Smoke}|{EffectKinds.Mode(e)}";
        if (Icons.TryGetValue(key, out var t)) return t;
        const int S = IconPx;
        var acc = new float[S * S * 4];
        var c = ColorOf(e);
        PaintIcon(acc, e, c);
        var bytes = new byte[S * S * 4];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Mathf.Clamp(acc[i] * 255, 0, 255);
        return Icons[key] = ImageTexture.CreateFromImage(Image.CreateFromData(S, S, false, Image.Format.Rgba8, bytes));
    }

    const int IconPx = 96;

    static void PaintIcon(float[] acc, EffectDef e, Color c)
    {
        var flame = new Color(1, 0.62f, 0.2f, 0.95f);
        var flashC = new Color(1, 0.85f, 0.5f, 1);
        if (PaintSeaIcon(acc, e, c, flame, flashC) || PaintOldWarIcon(acc, e, c, flame)) return;
        switch (e.Kind)
        {
            case "powder":
                for (int i = 0; i < 6; i++) Stamp(acc, "puff", c, 0.12f + i * 0.155f, 0.62f - (i % 2) * 0.08f, 0.42f, false);
                foreach (var x in new[] { 0.2f, 0.5f, 0.8f }) Stamp(acc, "flash", flashC, x, 0.8f, 0.24f, true);
                break;
            case "cannon":
                if (e.Smoke) { Stamp(acc, "puff", c, 0.62f, 0.5f, 0.62f, false); Stamp(acc, "puff", c, 0.4f, 0.58f, 0.5f, false); }
                Stamp(acc, "flash", flashC, 0.2f, 0.66f, 0.5f, true);
                break;
            case "burst":
                if (e.Smoke) Stamp(acc, "puff", c, 0.5f, 0.36f, 0.62f, false);
                for (int i = 0; i < 7; i++) Stamp(acc, "dust", new Color(0.4f, 0.32f, 0.24f), 0.2f + i * 0.1f, 0.62f - Mathf.Sin(i * 0.9f) * 0.12f, 0.2f, false);
                Stamp(acc, "flame", flame, 0.5f, 0.66f, 0.5f, true);
                Stamp(acc, "flash", flashC, 0.5f, 0.72f, 0.42f, true);
                break;
            case "fire":
                if (e.Smoke) { Stamp(acc, "puff", c, 0.48f, 0.26f, 0.5f, false); Stamp(acc, "puff", c, 0.6f, 0.14f, 0.4f, false); }
                Stamp(acc, "flame", flame, 0.36f, 0.68f, 0.42f, true);
                Stamp(acc, "flame", flame, 0.62f, 0.66f, 0.46f, true);
                Stamp(acc, "flame", new Color(1, 0.85f, 0.45f, 0.9f), 0.5f, 0.7f, 0.5f, true);
                foreach (var (x, y) in new[] { (0.3f, 0.36f), (0.7f, 0.42f), (0.56f, 0.3f) }) Stamp(acc, "spark", new Color(1, 0.7f, 0.3f), x, y, 0.1f, true);
                break;
            case "dust":
                for (int i = 0; i < 5; i++) Stamp(acc, "dust", c, 0.15f + i * 0.17f, 0.64f - (i % 2) * 0.1f, 0.5f, false);
                break;
            case "fog":
                Stamp(acc, "mist", c, 0.3f, 0.6f, 0.8f, false);
                Stamp(acc, "mist", c, 0.7f, 0.56f, 0.8f, false);
                Stamp(acc, "mist", c, 0.5f, 0.66f, 0.7f, false);
                break;
            case "spray":
                for (int i = 0; i < 14; i++) Stamp(acc, "drop", c, 0.5f + Mathf.Sin(i * 2.3f) * 0.12f * (i / 14f + 0.3f), 0.85f - i * 0.05f, 0.14f, false);
                Stamp(acc, "mist", c, 0.5f, 0.76f, 0.6f, false);
                break;
            case "sparks":
                for (int i = 0; i < 12; i++) Stamp(acc, "spark", c, 0.5f + Mathf.Sin(i * 1.9f) * 0.3f, 0.85f - i * 0.06f, 0.16f, true);
                break;
            case "rain":
                Stamp(acc, "mist", c, 0.5f, 0.78f, 0.9f, false);
                for (int i = 0; i < 16; i++) Stamp(acc, "streak", c with { A = 0.8f }, 0.12f + (i * 0.37f % 1) * 0.76f, 0.2f + (i * 0.61f % 1) * 0.6f, 0.26f, false);
                break;
            case "snow":
                for (int i = 0; i < 26; i++) Stamp(acc, "drop", c, 0.08f + (i * 0.37f % 1) * 0.84f, 0.1f + (i * 0.61f % 1) * 0.8f, 0.09f + (i % 3) * 0.02f, false);
                break;
            case "lamps":
                for (int i = 0; i < 7; i++) Stamp(acc, "spark", c, 0.15f + (i * 0.41f % 1) * 0.7f, 0.3f + (i * 0.67f % 1) * 0.45f, 0.2f, true);
                break;
            case "camp":
                foreach (var (x, y) in new[] { (0.22f, 0.7f), (0.5f, 0.6f), (0.78f, 0.72f), (0.36f, 0.85f), (0.66f, 0.88f) })
                {
                    if (e.Smoke) Stamp(acc, "puff", c with { A = 0.6f }, x + 0.03f, y - 0.25f, 0.24f, false);
                    Stamp(acc, "spark", new Color(1, 0.6f, 0.25f, 0.5f), x, y, 0.28f, true);
                    Stamp(acc, "flame", flame, x, y - 0.02f, 0.16f, true);
                }
                break;
            case "rocket":
                for (int i = 0; i < 12; i++) { float u = i / 11f; Stamp(acc, i % 2 == 0 ? "spark" : "puff", i % 2 == 0 ? new Color(1, 0.75f, 0.35f) : c with { A = 0.5f }, 0.1f + u * 0.72f,
                    0.8f - 2.2f * u * (1 - u) * 0.6f - u * 0.15f, i % 2 == 0 ? 0.1f : 0.16f, i % 2 == 0); }
                Stamp(acc, "flash", flashC, 0.86f, 0.62f, 0.3f, true);
                break;
            case "flare":
                Stamp(acc, "spark", new Color(1, 1, 0.95f, 0.5f), 0.5f, 0.32f, 0.7f, true);
                Stamp(acc, "spark", new Color(1, 1, 1), 0.5f, 0.32f, 0.22f, true);
                for (int i = 0; i < 5; i++) Stamp(acc, "puff", c with { A = 0.35f }, 0.5f + i * 0.015f, 0.42f + i * 0.1f, 0.12f + i * 0.02f, false);
                break;
            case "barrage":
                foreach (var (x, y, z) in new[] { (0.25f, 0.62f, 0.34f), (0.7f, 0.5f, 0.42f), (0.5f, 0.82f, 0.3f) })
                {
                    Stamp(acc, "puff", c with { A = 0.7f }, x, y - 0.12f, z, false);
                    Stamp(acc, "flame", flame, x, y, z * 0.8f, true);
                }
                break;
            case "tracer":
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 9; j++) Stamp(acc, "spark", c, 0.12f + j * 0.095f, 0.78f - j * 0.07f - i * 0.12f + (j % 2) * 0.01f, 0.08f, true);
                break;
            default:
                for (int i = 0; i < 5; i++) Stamp(acc, "puff", c, 0.5f + Mathf.Sin(i * 1.3f) * 0.08f + i * 0.03f, 0.8f - i * 0.15f, 0.34f + i * 0.08f, false);
                break;
        }
    }

    static readonly Color IconEarth = new(0.62f, 0.53f, 0.4f, 0.6f);

    static bool PaintOldWarIcon(float[] acc, EffectDef e, Color c, Color flame)
    {
        switch (e.Kind)
        {
            case "arrows":
            case "firearrows":
            {
                bool fire = e.Kind == "firearrows";
                Stamp(acc, "mist", new Color(0, 0, 0, 0.55f), 0.5f, 0.84f, 0.95f, false);
                var ink = fire ? flame : c with { A = 0.95f };
                for (int i = 0; i < 11; i++) Stamp(acc, "streak", ink, 0.12f + (i * 0.37f % 1) * 0.76f, 0.22f + (i * 0.53f % 1) * 0.5f, 0.3f, fire);
                if (fire) foreach (var x in new[] { 0.3f, 0.62f, 0.8f }) Stamp(acc, "flame", flame, x, 0.82f, 0.18f, true);
                return true;
            }
            case "javelins":
                for (int i = 0; i < 5; i++) Stamp(acc, "streak", c.Lightened(0.35f), 0.16f + i * 0.17f, 0.3f + (i * 0.53f % 1) * 0.4f, 0.55f, false);
                for (int i = 0; i < 4; i++) Stamp(acc, "dust", IconEarth, 0.2f + i * 0.2f, 0.86f, 0.22f, false);
                return true;
            case "slingstones":
                for (int i = 0; i < 14; i++) Stamp(acc, "streak", c.Lightened(0.45f), 0.1f + (i * 0.37f % 1) * 0.8f, 0.2f + (i * 0.61f % 1) * 0.5f, 0.12f, false);
                for (int i = 0; i < 6; i++) Stamp(acc, "dust", IconEarth, 0.12f + i * 0.15f, 0.84f - (i % 2) * 0.05f, 0.24f, false);
                return true;
            case "rockfall":
                foreach (var (x, y, z) in new[] { (0.3f, 0.22f, 0.2f), (0.62f, 0.4f, 0.26f), (0.45f, 0.6f, 0.16f) }) Stamp(acc, "drop", new Color(0.3f, 0.28f, 0.25f), x, y, z, false);
                for (int i = 0; i < 5; i++) Stamp(acc, "dust", c, 0.25f + i * 0.12f, 0.84f - (i % 2) * 0.06f, 0.34f, false);
                return true;
            case "melee":
                for (int i = 0; i < 5; i++) Stamp(acc, "dust", c, 0.15f + i * 0.17f, 0.66f - (i % 2) * 0.12f, 0.5f, false);
                return true;
            case "stones":
                for (int i = 0; i < 6; i++) { float u = i / 5f; Stamp(acc, "dust", c with { A = 0.35f + 0.1f * i }, 0.12f + u * 0.5f, 0.55f - 1.6f * u * (1 - u) * 0.5f, 0.08f, false); }
                Stamp(acc, "drop", new Color(0.3f, 0.28f, 0.25f), 0.66f, 0.5f, 0.24f, false);
                for (int i = 0; i < 5; i++) Stamp(acc, "dust", c, 0.6f + i * 0.07f, 0.8f - (i % 2) * 0.06f, 0.36f, false);
                return true;
            default:
                return false;
        }
    }

    static bool PaintSeaIcon(float[] acc, EffectDef e, Color c, Color flame, Color flashC)
    {
        switch (e.Kind)
        {
            case "column":
                foreach (var (x, top) in new[] { (0.22f, 0.3f), (0.5f, 0.14f), (0.78f, 0.24f) })
                {
                    for (int i = 0; i < 7; i++) { float u = i / 6f; Stamp(acc, "puff", c, x + Mathf.Sin(i * 2.1f + x * 9) * 0.02f, 0.84f - u * (0.84f - top), 0.24f - u * 0.1f, false); }
                    Stamp(acc, "mist", c with { A = 0.8f }, x, 0.86f, 0.36f, false);
                }
                return true;
            case "broadside":
                if (e.Smoke) { Stamp(acc, "puff", c, 0.42f, 0.36f, 0.56f, false); Stamp(acc, "puff", c, 0.68f, 0.3f, 0.46f, false); }
                Stamp(acc, "mist", new Color(0.86f, 0.9f, 0.93f, 0.8f), 0.5f, 0.86f, 0.9f, false);
                Stamp(acc, "puff", new Color(1, 0.55f, 0.15f), 0.36f, 0.6f, 0.46f, false);
                Stamp(acc, "puff", new Color(1, 0.75f, 0.3f), 0.3f, 0.64f, 0.32f, false);
                Stamp(acc, "flash", flashC, 0.24f, 0.66f, 0.42f, true);
                return true;
            case "screen":
                for (int i = 0; i < 7; i++) { float u = i / 6f; Stamp(acc, "puff", c, 0.12f + u * 0.76f, 0.8f - u * 0.42f, 0.26f + u * 0.24f, false); }
                return true;
            case "oil":
                if (e.Smoke) for (int i = 0; i < 6; i++) { float u = i / 5f; Stamp(acc, "puff", c, 0.42f + u * 0.2f, 0.62f - u * 0.5f, 0.32f + u * 0.26f, false); }
                Stamp(acc, "flame", flame, 0.3f, 0.8f, 0.3f, true);
                Stamp(acc, "flame", flame, 0.56f, 0.78f, 0.34f, true);
                Stamp(acc, "flame", new Color(1, 0.85f, 0.45f, 0.9f), 0.44f, 0.82f, 0.3f, true);
                return true;
            case "wake":
                for (int i = 0; i < 18; i++) { float u = i / 17f; Stamp(acc, "mist", c with { A = 0.4f + 0.5f * u }, 0.12f + u * 0.7f, 0.88f - u * 0.7f, 0.14f + u * 0.06f, false); }
                Stamp(acc, "mist", c, 0.84f, 0.17f, 0.3f, false);
                return true;
            default:
                return false;
        }
    }

    static void Stamp(float[] acc, string sprite, Color tint, float cx, float cy, float size, bool add)
    {
        var src = SpriteImage(sprite);
        int n = Math.Max(2, (int)(size * IconPx));
        var im = (Image)src.Duplicate();
        im.Resize(n, n, Image.Interpolation.Bilinear);
        int x0 = (int)(cx * IconPx - n / 2f), y0 = (int)(cy * IconPx - n / 2f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int X = x0 + x, Y = y0 + y;
                if (X < 0 || Y < 0 || X >= IconPx || Y >= IconPx) continue;
                var p = im.GetPixel(x, y);
                float a = p.A * tint.A;
                if (a <= 0.002f) continue;
                int i = (Y * IconPx + X) * 4;
                float r = p.R * tint.R, g = p.G * tint.G, b = p.B * tint.B;
                if (add)
                {
                    acc[i] = Mathf.Min(1, acc[i] + r * a);
                    acc[i + 1] = Mathf.Min(1, acc[i + 1] + g * a);
                    acc[i + 2] = Mathf.Min(1, acc[i + 2] + b * a);
                    acc[i + 3] = Mathf.Min(1, acc[i + 3] + a);
                }
                else
                {
                    float da = acc[i + 3], oa = a + da * (1 - a);
                    if (oa <= 0) continue;
                    acc[i] = (r * a + acc[i] * da * (1 - a)) / oa;
                    acc[i + 1] = (g * a + acc[i + 1] * da * (1 - a)) / oa;
                    acc[i + 2] = (b * a + acc[i + 2] * da * (1 - a)) / oa;
                    acc[i + 3] = oa;
                }
            }
    }

    static CurveTexture CurveTex((float x, float y)[] pts)
    {
        var c = new Curve { MaxValue = 4 };
        foreach (var (x, y) in pts) c.AddPoint(new Vector2(x, y));
        return new CurveTexture { Curve = c };
    }

    static GradientTexture1D Ramp((float at, Color c)[] stops) =>
        new() { Gradient = new Gradient { Offsets = stops.Select(s => s.at).ToArray(), Colors = stops.Select(s => s.c).ToArray() } };
}

public partial class EffectLight : OmniLight3D
{
    public float Base = 1;
    public int Mode;
    public float Every = 5, Phase, Speed = 1;
    double _t;

    public override void _Ready() { _t = Phase; LightEnergy = Mode == 2 ? 0 : Base; }

    public override void _Process(double delta)
    {
        _t += delta * Speed;
        switch (Mode)
        {
            case 1:
                float f = Mathf.Sin((float)_t * 13.1f) * 0.5f + Mathf.Sin((float)_t * 7.3f + 1.7f) * 0.35f + Mathf.Sin((float)_t * 23.7f) * 0.15f;
                LightEnergy = Base * (0.82f + 0.14f * f);
                break;
            case 2:
                float since = (float)(_t % Every);
                LightEnergy = Base * Mathf.Exp(-since * 9f);
                Visible = since < 0.8f;
                break;
        }
    }
}

public partial class EffectLamps : Node3D
{
    readonly List<(MeshInstance3D glow, float seed)> _glows = new();
    readonly List<EffectLight> _lights = new();
    double _t;

    public void Setup(Vector3[] at, float size, Color c, int lights, float range, float energy, float speed)
    {
        var mat = EffectFx.Glow(c);
        var quad = new QuadMesh { Size = Vector2.One * size };
        for (int i = 0; i < at.Length; i++)
        {
            var g = new MeshInstance3D { Mesh = quad, MaterialOverride = mat, Position = at[i] + new Vector3(0, size * 0.5f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(g);
            _glows.Add((g, i * 1.618f % 1));
        }
        for (int i = 0; i < lights; i++)
        {
            var l = new EffectLight
            {
                LightColor = c, OmniRange = range, OmniAttenuation = 1.5f, ShadowEnabled = false, Base = energy, Mode = 1, Phase = i * 2.3f, Speed = speed,
                Position = at[i * at.Length / lights] + new Vector3(0, size, 0),
            };
            AddChild(l);
            _lights.Add(l);
        }
        SetProcess(at.Length > 0);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        foreach (var (g, seed) in _glows)
        {
            float t = (float)_t;
            float f = 0.86f + 0.1f * Mathf.Sin(t * (5 + seed * 4) + seed * 31) + 0.04f * Mathf.Sin(t * (13 + seed * 7));
            g.Scale = Vector3.One * f;
        }
    }
}

public partial class EffectProjectiles : Node3D
{
    public sealed class Shot
    {
        public Node3D Head = null!;
        public readonly List<GpuParticles3D> Trail = new();
        public readonly List<GpuParticles3D> Burst = new();
        public OmniLight3D? Light;
        public Vector3 From, To;
        public float Phase, Wobble;
        public bool Landed = true;
    }

    public readonly List<Shot> Shots = new();
    public bool Flare;
    public float Every = 5, Flight = 1.5f, Arc = 2, Speed = 1, LightBase = 3;
    double _t;

    public override void _Process(double delta)
    {
        _t += delta * Speed;
        foreach (var s in Shots) Place(s, (float)((_t + s.Phase * Every) % Every));
    }

    void Place(Shot s, float t)
    {
        bool aloft;
        if (!Flare)
        {
            aloft = t < Flight;
            if (aloft)
            {
                float u = t / Flight;
                var p = s.From.Lerp(s.To, u);
                p.Y += Arc * 4 * u * (1 - u);
                const float CongreveWobbleWaves = 9, CongreveWobbleSize = 0.08f;
                p.X += Mathf.Sin(u * CongreveWobbleWaves + s.Wobble) * Arc * CongreveWobbleSize;
                s.Head.Position = p;
                s.Landed = false;
            }
            else if (!s.Landed)
            {
                s.Landed = true;
                foreach (var b in s.Burst) { b.Position = s.To; b.Restart(); }
            }
        }
        else
        {
            float hang = Every * 0.75f;
            aloft = t < Flight + hang;
            if (aloft)
            {
                float y = t < Flight ? Arc * (t / Flight) * (2 - t / Flight) : Arc * (1 - 0.8f * (t - Flight) / hang);
                s.Head.Position = s.From with { Y = y };
            }
            if (s.Light != null)
            {
                float k = !aloft ? 0 : t < Flight ? 0.3f : Mathf.Clamp((Flight + hang - t) / (hang * 0.2f), 0, 1);
                s.Light.LightEnergy = LightBase * k * (0.93f + 0.07f * Mathf.Sin(t * 17 + s.Wobble));
                s.Light.Visible = k > 0;
            }
        }
        s.Head.Visible = aloft;
        foreach (var tr in s.Trail) tr.Emitting = aloft;
    }
}

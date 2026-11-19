using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class MotionPreview(TableContext c, PieceLayer pieces, Func<PieceNode, bool> busy)
{
    public const string Off = "off", Jumps = "jump", Flies = "fly";
    const float FlyHeights = 0.5f, LiftEase = 2.5f, JumpEvery = 1.6f, PulseEvery = 2f, LongestStep = 0.1f;
    const float FloorHeights = 1.3f, CellHeights = 0.2f, FloorAbove = 0.002f;

    const string FloorLook = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;
uniform vec2 shift;
uniform float cell = 0.25;
uniform float radius = 1.3;
uniform float blot = 0.1;
uniform vec4 ink : source_color = vec4(0.12, 0.1, 0.08, 0.42);
varying vec2 at;
void vertex() {
    at = VERTEX.xz;
}
void fragment() {
    vec2 q = (at + shift) / cell;
    vec2 w = fwidth(q);
    float soft = max(w.x, w.y) * 0.75;
    float spot = 1.0 - smoothstep(blot - soft, blot + soft, length(fract(q) - 0.5));
    float fall = 1.0 - smoothstep(0.45, 1.0, length(at) / radius);
    ALBEDO = ink.rgb;
    ALPHA = ink.a * spot * fall;
}";

    public sealed record Choice(int Ahead = 0, int Aside = 0, bool Run = false, string? Stance = null, int Turn = 0, string Air = Off, float Speed = 1);

    static Shader? _floorShader;

    PieceNode? _node;
    IFigureMotion? _motion;
    string? _made;
    BodyLean _lean = new();
    Vector2 _tilt;
    Vector3 _travel, _vel;
    float _surface, _turned, _lift, _climb, _jumpIn, _pulseIn;
    MeshInstance3D? _floor;
    ShaderMaterial? _floorShade;

    public Choice Now { get; set; } = new();
    public string? Piece { get; private set; }
    public bool Playing => _node != null;
    public Vector3 Travelled => _travel;

    public static bool Moves(PieceDef? def) => def != null && (def.Rig != null || def.Walk is { Count: > 0 });

    static string? Made(PieceDef? def) => def?.Rig ?? (def?.Walk is { } frames ? string.Join("|", frames) : null);

    public void Follow(string? pieceId)
    {
        if (pieceId == Piece) return;
        Release();
        Piece = pieceId;
        (_lean, _tilt, _travel, _vel, _turned, _lift, _climb, _jumpIn, _pulseIn) = (new BodyLean(), Vector2.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0, 0, 0);
    }

    public void Tick(double delta)
    {
        if (Piece == null) return;
        var node = pieces.NodeOf(Layers.Umpire, Piece) is { } n && GodotObject.IsInstanceValid(n) ? n : null;
        if (node == null || !Moves(node.Def) || busy(node))
        {
            Release();
            return;
        }
        if (node != _node || Made(node.Def) != _made)
        {
            Release();
            Take(node);
        }
        float h = node.Thickness;
        if (h <= 0 || _motion == null) return;
        var now = Now;
        float dt = Mathf.Min((float)delta, LongestStep) * Mathf.Clamp(now.Speed, 0, 1);
        _turned += now.Turn * Walker.TurnRate * dt;
        float yaw = AvatarBody.YawOf(node.Piece.RotDeg) + _turned;
        var forward = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        var right = new Vector3(-forward.Z, 0, forward.X);
        var heading = (forward * now.Ahead + right * now.Aside).LimitLength();
        bool flies = now.Air == Flies;
        var want = heading * h * (flies ? now.Run ? Walker.BoostHeights : Walker.CruiseHeights : Walker.PaceOf(now.Stance, now.Run));
        float grip = !flies ? Walker.Grip : heading == Vector3.Zero ? Walker.AirDrag : Walker.AirGrip;
        _vel = _vel.Lerp(want, 1 - Mathf.Exp(-dt * grip));
        _travel += _vel * dt;
        Rise(now, h, dt);
        var feet = Coords.ToWorld(node.Piece.Pos, _surface + _lift) + _travel;
        if (dt > 0) _tilt = _lean.Step(feet, h, dt, yaw);
        node.ShowPose(BodyLean.Leaned(node.HandPose(node.Piece.Pos, AvatarBody.RotOf(yaw), _surface + _lift), _tilt));
        node.Visible = true;
        if (_motion is Rigged rigged)
        {
            float above = _lift / h;
            (rigged.Style, rigged.Above, rigged.Aloft, rigged.Ground) = (now.Stance, above, above > FlightFx.AirShare, (0, 0, 0));
            if (flies && now.Run && heading != Vector3.Zero && rigged.Aloft)
            {
                _pulseIn -= dt;
                if (_pulseIn <= 0)
                {
                    rigged.Pulse();
                    _pulseIn = PulseEvery;
                }
            }
            else _pulseIn = 0;
        }
        _motion.Step(feet, yaw, h, dt);
        Floor(node, h);
    }

    void Rise(Choice now, float h, float dt)
    {
        if (now.Air == Flies)
        {
            _climb = 0;
            _lift = Mathf.Lerp(_lift, FlyHeights * h, 1 - Mathf.Exp(-dt * LiftEase));
            return;
        }
        _jumpIn -= dt;
        if (now.Air == Jumps && _lift <= 0 && _jumpIn <= 0)
        {
            _climb = Walker.JumpHeights * h;
            _jumpIn = JumpEvery;
        }
        if (_lift <= 0 && _climb <= 0) return;
        _climb -= Walker.FallHeights * h * dt;
        _lift += _climb * dt;
        if (_lift <= 0) (_lift, _climb) = (0, 0);
    }

    void Take(PieceNode node)
    {
        node.Refresh(node.Piece);
        _surface = node.Position.Y - node.LiftY - node.PartsBelow;
        node.BeginSettle();
        _node = node;
        _made = Made(node.Def);
        _motion = Rigged.Wants(node.Def) ? new Rigged(node, c) : new Gait(node, c);
    }

    void Floor(PieceNode node, float h)
    {
        if (node.GetParent() is not Node3D parent) return;
        if (_floor == null || !GodotObject.IsInstanceValid(_floor) || _floor.GetParent() != parent)
        {
            _floor?.QueueFree();
            _floorShader ??= new Shader { Code = FloorLook };
            _floorShade = new ShaderMaterial { Shader = _floorShader };
            _floorShade.SetShaderParameter("cell", CellHeights);
            _floorShade.SetShaderParameter("radius", FloorHeights);
            _floor = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = Vector2.One * (2 * FloorHeights) }, MaterialOverride = _floorShade, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            parent.AddChild(_floor);
        }
        _floor.Position = Coords.ToWorld(node.Piece.Pos, _surface + FloorAbove * h);
        _floor.Scale = Vector3.One * h;
        _floorShade!.SetShaderParameter("shift", new Vector2(Mathf.PosMod(_travel.X / h, CellHeights), Mathf.PosMod(_travel.Z / h, CellHeights)));
    }

    void Release()
    {
        _motion?.Stop();
        _motion = null;
        _made = null;
        if (_floor != null && GodotObject.IsInstanceValid(_floor)) _floor.QueueFree();
        (_floor, _floorShade) = (null, null);
        if (_node is not { } was) return;
        _node = null;
        if (!GodotObject.IsInstanceValid(was)) return;
        was.EndSettle();
        was.Visible = true;
        was.Refresh(was.Piece);
    }
}

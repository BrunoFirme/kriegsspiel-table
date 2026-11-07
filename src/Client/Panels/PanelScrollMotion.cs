namespace KTable.Client.Panels;

public enum ScrollMovement { Unrestricted, Elastic, Clamped }

public sealed class PanelScrollMotion
{
    const float Stop = 1, ScrollingSlowdown = 3, Follow = 10, RubberPull = 0.55f, Tiny = 0.0001f;
    const float SmoothA = 0.48f, SmoothB = 0.235f;

    public V2 Offset;
    public V2 Velocity;
    public bool Dragging { get; private set; }
    bool _scrolling;
    V2 _dragFrom, _pointerFrom, _previous;
    readonly V2 _room, _view;
    readonly bool _vertical, _horizontal, _inertia;
    readonly ScrollMovement _movement;
    readonly float _sensitivity, _deceleration, _elasticity;

    public PanelScrollMotion(PanelBox view, V2 offset)
    {
        (_room, _view) = PanelScrolls.Measure(view);
        _vertical = view.Extra.GetValueOrDefault("vertical") is not { } v || PanelValues.Bool(v);
        _horizontal = PanelValues.Bool(view.Extra.GetValueOrDefault("horizontal"));
        _movement = Movement(view.Extra.GetValueOrDefault("movementtype"));
        _inertia = view.Extra.GetValueOrDefault("inertia") is not { } i || PanelValues.Bool(i);
        _sensitivity = view.Extra.TryGetValue("scrollsensitivity", out var s) ? PanelValues.Float(s) : 1;
        _deceleration = view.Extra.TryGetValue("decelerationrate", out var d) ? PanelValues.Float(d) : 0.135f;
        _elasticity = view.Extra.TryGetValue("elasticity", out var e) ? PanelValues.Float(e) : 0.1f;
        Offset = offset;
        _previous = offset;
    }

    public static ScrollMovement Movement(string? s)
    {
        if (s != null && int.TryParse(s, out var n) && n is >= 0 and <= 2) return (ScrollMovement)n;
        return PanelValues.Enum<ScrollMovement>(s) ?? ScrollMovement.Clamped;
    }

    V2 OutOfBounds(V2 at)
    {
        if (_movement == ScrollMovement.Unrestricted) return V2.Zero;
        var inside = new V2(Math.Clamp(at.X, -_room.X, 0), Math.Clamp(at.Y, 0, _room.Y));
        return inside - at;
    }

    V2 Allowed(V2 at) => new(_horizontal ? at.X : Offset.X, _vertical ? at.Y : Offset.Y);

    public void Wheel(V2 delta)
    {
        delta = delta with { Y = -delta.Y };
        if (_vertical && !_horizontal)
        {
            if (Math.Abs(delta.X) > Math.Abs(delta.Y)) delta = delta with { Y = delta.X };
            delta = delta with { X = 0 };
        }
        if (_horizontal && !_vertical)
        {
            if (Math.Abs(delta.Y) > Math.Abs(delta.X)) delta = delta with { X = delta.Y };
            delta = delta with { Y = 0 };
        }
        var to = Offset + delta * _sensitivity;
        if (_movement == ScrollMovement.Clamped) to += OutOfBounds(to);
        Offset = Allowed(to);
        _scrolling = true;
    }

    public void BeginDrag(V2 pointer)
    {
        Velocity = V2.Zero;
        Dragging = true;
        _dragFrom = Offset;
        _pointerFrom = pointer;
    }

    public void Drag(V2 pointer)
    {
        if (!Dragging) return;
        var to = _dragFrom + (pointer - _pointerFrom);
        var off = OutOfBounds(to);
        to += off;
        if (_movement == ScrollMovement.Elastic)
        {
            if (off.X != 0) to = to with { X = to.X - Rubber(off.X, _view.X) };
            if (off.Y != 0) to = to with { Y = to.Y - Rubber(off.Y, _view.Y) };
        }
        Offset = Allowed(to);
    }

    public void EndDrag() => Dragging = false;

    static float Rubber(float over, float size) => (1 - 1 / (Math.Abs(over) * RubberPull / Math.Max(Tiny, size) + 1)) * size * Math.Sign(over);

    public bool Moving => Dragging || Velocity != V2.Zero || OutOfBounds(Offset) != V2.Zero;

    public void Step(float dt)
    {
        if (dt <= 0) return;
        var off = OutOfBounds(Offset);
        if (!Dragging && (off != V2.Zero || Velocity != V2.Zero))
        {
            float vx = Velocity.X, vy = Velocity.Y;
            var at = new V2(Axis(Offset.X, off.X, ref vx, dt), Axis(Offset.Y, off.Y, ref vy, dt));
            Velocity = new V2(vx, vy);
            if (_movement == ScrollMovement.Clamped) at += OutOfBounds(at);
            Offset = Allowed(at);
        }
        if (Dragging && _inertia)
        {
            var now = (Offset - _previous) * (1 / dt);
            Velocity = Lerp(Velocity, now, dt * Follow);
        }
        _previous = Offset;
        _scrolling = false;
    }

    float Axis(float at, float off, ref float speed, float dt)
    {
        if (_movement == ScrollMovement.Elastic && off != 0)
        {
            float time = _scrolling ? _elasticity * ScrollingSlowdown : _elasticity;
            float next = SmoothDamp(at, at + off, ref speed, time, dt);
            if (Math.Abs(speed) < Stop) speed = 0;
            return next;
        }
        if (!_inertia)
        {
            speed = 0;
            return at;
        }
        speed *= MathF.Pow(_deceleration, dt);
        if (Math.Abs(speed) < Stop) speed = 0;
        return at + speed * dt;
    }

    static V2 Lerp(V2 a, V2 b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return a + (b - a) * t;
    }

    public static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
    {
        smoothTime = Math.Max(Tiny, smoothTime);
        float omega = 2 / smoothTime;
        float x = omega * dt;
        float exp = 1 / (1 + x + SmoothA * x * x + SmoothB * x * x * x);
        float change = current - target;
        float goal = target;
        target = current - change;
        float temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        float output = target + (change + temp) * exp;
        if (goal - current > 0 == output > goal)
        {
            output = goal;
            velocity = (output - goal) / dt;
        }
        return output;
    }

    public static float BarValue(PanelBox bar, int axis, V2 local, V2 grab)
    {
        if (bar.Part("Sliding Area") is not { } area || area.Part("Handle") is not { } handle) return 0;
        var span = handle.RectSize - handle.SizeDelta;
        var corner = local - grab - area.RectMin - span * 0.5f;
        float size = Math.Clamp(handle.AnchorMax[axis] - handle.AnchorMin[axis], 0, 1);
        float remaining = area.RectSize[axis] * (1 - size);
        return remaining <= 0 ? -1 : Math.Clamp(corner[axis] / remaining, 0, 1);
    }

    public static V2 FromBar(PanelBox view, int axis, float value, V2 offset)
    {
        var (room, _) = PanelScrolls.Measure(view);
        return axis == 1 ? offset with { Y = (1 - value) * room.Y } : offset with { X = -value * room.X };
    }
}

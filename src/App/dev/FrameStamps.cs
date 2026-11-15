using Godot;

namespace KTable.App;

public partial class FrameStamps : Node
{
    ulong _processFrom, _physicsFrom, _processTo, _drawFrom, _drawTo;

    public double ProcessMs { get; private set; }
    public double PhysicsMs { get; private set; }
    public double LastProcessMs { get; private set; }
    public double LastPhysicsMs { get; private set; }
    public double LastBeforeDrawMs { get; private set; }
    public double LastDrawMs { get; private set; }
    public double LastPresentMs { get; private set; }

    public FrameStamps()
    {
        ProcessPriority = int.MaxValue;
        ProcessPhysicsPriority = int.MaxValue;
    }

    public override void _Ready()
    {
        GetTree().ProcessFrame += () =>
        {
            ulong now = Time.GetTicksUsec();
            LastProcessMs = ProcessMs;
            LastPhysicsMs = PhysicsMs;
            LastBeforeDrawMs = (_drawFrom - _processTo) / 1000.0;
            LastDrawMs = (_drawTo - _drawFrom) / 1000.0;
            LastPresentMs = (now - _drawTo) / 1000.0 - PhysicsMs;
            PhysicsMs = 0;
            _processFrom = now;
        };
        GetTree().PhysicsFrame += () => _physicsFrom = Time.GetTicksUsec();
        RenderingServer.FramePreDraw += () => _drawFrom = Time.GetTicksUsec();
        RenderingServer.FramePostDraw += () => _drawTo = Time.GetTicksUsec();
    }

    public override void _Process(double delta)
    {
        _processTo = Time.GetTicksUsec();
        ProcessMs = (_processTo - _processFrom) / 1000.0;
    }

    public override void _PhysicsProcess(double delta) => PhysicsMs += (Time.GetTicksUsec() - _physicsFrom) / 1000.0;
}

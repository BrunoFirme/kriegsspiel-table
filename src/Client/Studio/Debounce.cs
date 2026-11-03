namespace KTable.Client.Studio;

public sealed class Debounce
{
    public double In = -1, Max = -1;

    public bool Pending => In > 0 || Max > 0;

    public void Arm(double delay, double max)
    {
        In = delay;
        if (Max < 0) Max = max;
    }

    public void Clear() => In = Max = -1;

    public bool Tick(double delta)
    {
        if (!Pending) return false;
        In -= delta; Max -= delta;
        return In <= 0 || Max <= 0;
    }
}

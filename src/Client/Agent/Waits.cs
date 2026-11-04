namespace KTable.Client.Agent;

public static class Waits
{
    public const double Poll = 0.05;
    public const double LandingSeconds = 3;

    public static async Task<bool> Until(Func<bool> done, double seconds, Func<double, Task> delay)
    {
        for (double t = 0; t < seconds; t += Poll)
        {
            if (done()) return true;
            await delay(Poll);
        }
        return done();
    }

    public static Task<bool> Landed(ClientSession s, Func<double, Task> delay) => Until(() => s.CaughtUp, LandingSeconds, delay);
}

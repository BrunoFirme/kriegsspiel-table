using KTable.Server;

namespace KTable.Client;

public static class SaveReports
{
    public static string Describe(KeptAside k)
    {
        var what = k.Why switch
        {
            KeptAsideWhy.Unreadable => $"The log of the play after the last whole save could not be read ({k.Detail})",
            KeptAsideWhy.Unreplayed => $"{k.Changes} change(s) after the last whole save could not be replayed ({k.Detail})",
            _ => $"{k.Changes} change(s) in the log after the last whole save do not carry on from it (a change before them is missing from the log, or they are another branch of the game)",
        };
        return $"{what}, so the game opened {(k.Why == KeptAsideWhy.Unreadable ? "as it was last written whole" : "at the last change that could be")}."
            + (k.Files.Count > 0 ? " What it does not hold is kept: " + string.Join(", ", k.Files.Select(Path.GetFileName)) + "." : "");
    }

    public static string Describe(Recovery r) =>
        (r.LiveError != null ? $"The save could not be read ({r.LiveError}); it was kept as {Path.GetFileName(r.LiveKeptAs ?? "")}." : "The save file was missing.")
        + $" Continuing from the checkpoint taken at the {r.From.Describe()}: {Path.GetFileName(r.From.Path)}."
        + (r.FailedCheckpoints.Count > 0 ? $" Newer checkpoints that could not be read: {string.Join("; ", r.FailedCheckpoints)}." : "")
        + (r.LogKept.Count > 0 ? $" The log of the play after the unreadable save is kept with it: {string.Join(", ", r.LogKept.Select(Path.GetFileName))}." : "")
        + (r.LogLeft != null ? $" The log of the play after the unreadable save is still beside it ({r.LogLeft}); it is not laid over this game." : "");
}

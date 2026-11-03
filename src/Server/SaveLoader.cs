using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server;

public enum KeptAsideWhy
{
    Unreadable,
    Unreplayed,
    Gap,
}

public sealed record KeptAside(KeptAsideWhy Why, int Changes, string? Detail, IReadOnlyList<string> Files);

public sealed record LoadReport
{
    public int ReplayedFromLog { get; init; }
    public bool LogTorn { get; init; }
    public int LogNotFollowing { get; init; }
    public string? LogUnreadable { get; init; }
    public int ReplayFailed { get; init; }
    public string? ReplayFailure { get; init; }
    public bool SaveHoldsUnreplayed { get; init; }
    public KeptAside? KeptAside { get; init; }
}

public sealed record Recovery(string? LiveError, string? LiveKeptAs, Checkpoint From, IReadOnlyList<string> FailedCheckpoints, IReadOnlyList<string> LogKept, string? LogLeft);

public static class SaveLoader
{
    public static GameServer Load(string path, PackageStore? store = null)
    {
        var save = Json.Load<SaveFile>(path);
        if (save.Format > SaveFile.CurrentFormat)
            throw new SaveTooNewException($"this save was written by a newer version of the app (save format {save.Format}; this version reads up to {SaveFile.CurrentFormat}): update the app");
        if (string.IsNullOrEmpty(save.ScenarioId) || save.Snapshot == null || save.Snapshot.Layers == null)
            throw new InvalidDataException("not a Kriegsspiel Table save");
        var state = save.Snapshot;
        int failed = 0;
        string? failure = null;
        var inSave = (save.Commands ?? new()).Where(c => c.Seq > state.LastSeq).OrderBy(c => c.Seq).ToList();
        for (int i = 0; i < inSave.Count; i++)
        {
            if (ApplyReplayed(state, Json.Clone(inSave[i])) is { } err) { failed = inSave.Count - i; failure = err; break; }
            state.LastSeq = inSave[i].Seq;
        }
        bool saveHoldsUnreplayed = failed > 0;
        int notFollowingCount = 0;
        int replayed = 0;
        var turns = save.Turns ?? new();
        foreach (var t in turns) t.Normalize();
        var logged = SaveLog.Read(path, SaveLog.StampOf(save.Created, save.ScenarioId), out bool torn, out var unreadable);
        if (failed > 0) failed += logged.Count(c => c.Seq > inSave[^1].Seq);
        var following = failed > 0 ? new List<Command>() : SaveLog.FollowingOn(logged, state.LastSeq, out notFollowingCount);
        for (int i = 0; i < following.Count; i++)
        {
            var c = following[i];
            var ending = SaveFile.EndsTurn(state, c) ? SaveFile.RecordOf(state, Repeatable.UtcNow) : null;
            if (ApplyReplayed(state, c) is { } err) { failed = following.Count - i; failure = err; break; }
            if (ending != null) turns.Add(ending);
            if (c is RestoreTurn rt && rt.Index >= 0 && rt.Index < turns.Count) turns.RemoveRange(rt.Index, turns.Count - rt.Index);
            state.LastSeq = c.Seq;
            replayed++;
        }
        state.EnsureNextId();
        var srv = new GameServer(state, store, save.Access ?? new GameAccess(), created: save.Created)
        {
            Loaded = new LoadReport
            {
                ReplayedFromLog = replayed, LogTorn = torn, LogNotFollowing = notFollowingCount, LogUnreadable = unreadable,
                ReplayFailed = failed, ReplayFailure = failure, SaveHoldsUnreplayed = saveHoldsUnreplayed,
            },
        };
        srv.Turns.AddRange(turns);
        return srv;
    }

    static string? ApplyReplayed(GameState state, Command c)
    {
        string? err;
        try { err = StateMachine.Apply(state, c, out _); }
        catch (Exception e) { err = e.Message; }
        return err == null ? null : $"{c.GetType().Name}, change {c.Seq}: {err}";
    }

    static void KeepWhatWasNotReplayed(GameServer srv, string savePath)
    {
        var r = srv.Loaded;
        if (r.LogUnreadable == null && r.ReplayFailed == 0 && r.LogNotFollowing == 0) return;
        var why = r.LogUnreadable != null ? KeptAsideWhy.Unreadable : r.ReplayFailed > 0 ? KeptAsideWhy.Unreplayed : KeptAsideWhy.Gap;
        var reason = why switch { KeptAsideWhy.Unreadable => "unreadable", KeptAsideWhy.Unreplayed => "unreplayed", _ => "gap" };
        var kept = new List<string>();
        try
        {
            if (r.SaveHoldsUnreplayed && SaveStore.KeepCopy(savePath, reason, Repeatable.UtcNow) is { } copy) kept.Add(copy);
            kept.AddRange(SaveLog.SetAside(savePath, reason, Repeatable.UtcNow));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var what = why switch
            {
                KeptAsideWhy.Unreadable => $"The log of the play after the last whole save could not be read ({r.LogUnreadable})",
                KeptAsideWhy.Unreplayed => $"{r.ReplayFailed} change(s) after the last whole save could not be replayed ({r.ReplayFailure})",
                _ => $"{r.LogNotFollowing} change(s) in the log after the last whole save do not carry on from it (a change before them is missing from the log, or they are another branch of the game)",
            };
            throw new SaveHeldException($"{what}, and it could not be kept aside either ({e.Message}): the game was not opened, as playing on"
                + " would lose that play. Nothing was deleted. Close any program that may be holding the files (a virus scan, a backup) and open the game again.");
        }
        int changes = why == KeptAsideWhy.Unreplayed ? r.ReplayFailed : why == KeptAsideWhy.Gap ? r.LogNotFollowing : 0;
        srv.Loaded = r with { KeptAside = new KeptAside(why, changes, why == KeptAsideWhy.Unreadable ? r.LogUnreadable : r.ReplayFailure, kept) };
    }

    public static GameServer Open(SaveStore saves, string savePath, PackageStore? store, out Recovery? recovery)
    {
        Exception? liveError = null;
        for (int attempt = 0; liveError == null || liveError is IOException or UnauthorizedAccessException && attempt < 4; attempt++)
        {
            if (!File.Exists(savePath)) break;
            if (attempt > 0) Thread.Sleep(250 * attempt);
            try { var srv = Load(savePath, store); KeepWhatWasNotReplayed(srv, savePath); recovery = null; return srv; }
            catch (SaveTooNewException) { throw; }
            catch (SaveHeldException) { throw; }
            catch (Exception e) when (SaveStore.IsDamage(e) || SaveStore.IsUnreachable(e)) { liveError = e; }
        }
        if (liveError != null && SaveStore.IsHeld(liveError))
            throw new SaveHeldException($"The save is held by another program ({liveError.Message}): the game was not opened, as an older copy would lose"
                + " the play since. Nothing was changed. Close any program that may be holding it (a virus scan, a backup, a sync) and open the game again.");
        if (liveError != null && SaveStore.IsUnreachable(liveError))
            throw new SaveHeldException($"The save could not be read from the disk ({liveError.Message}): the game was not opened, as an older copy would"
                + " lose the play since. Nothing was changed. Check the drive or the synced folder it is on, and open the game again.");
        var failed = new List<string>();
        foreach (var cp in saves.Checkpoints(savePath))
        {
            GameServer srv;
            try { srv = Load(cp.Path, store); }
            catch (Exception e) when (SaveStore.IsDamage(e) || SaveStore.IsUnreachable(e)) { failed.Add($"{Path.GetFileName(cp.Path)}: {e.Message}"); continue; }
            string? kept;
            try { kept = liveError != null ? SaveStore.SetAside(savePath, "unreadable", Repeatable.UtcNow) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SaveHeldException($"The save could not be read ({liveError!.Message}), and it could not be set aside for the copy that"
                    + $" can be either ({e.Message}). Nothing was changed. Close any program that may be holding it and open the game again.");
            }
            string? logLeft = null;
            var logKept = new List<string>();
            try { logKept = SaveLog.SetAside(savePath, liveError != null ? "unreadable" : "no-save", Repeatable.UtcNow); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { logLeft = e.Message; }
            try { srv.SaveTo(savePath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SaveHeldException($"The game was recovered from the checkpoint taken at the {cp.Describe()}, but could not be written as the live save"
                    + $" ({e.Message}). Nothing was lost: the checkpoint is still there{(kept != null ? $" and the unreadable save is kept as {Path.GetFileName(kept)}" : "")}. Open the game again.");
            }
            recovery = new Recovery(liveError?.Message, kept, cp, failed, logKept, logLeft);
            return srv;
        }
        throw new InvalidDataException(liveError != null
            ? $"the save could not be read ({liveError.Message}) and no checkpoint could either" + (failed.Count > 0 ? ": " + string.Join("; ", failed) : "")
            : "no save and no checkpoint found for this game");
    }

    public static GameState Replay(Scenario s, IEnumerable<Command> log)
    {
        var g = GameState.FromScenario(s);
        foreach (var c in log.OrderBy(c => c.Seq))
        {
            StateMachine.Apply(g, Json.Clone(c), out _);
            g.LastSeq = c.Seq;
        }
        return g;
    }
}

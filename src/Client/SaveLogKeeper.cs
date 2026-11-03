using System.Diagnostics;
using KTable.Core;
using KTable.Server;

namespace KTable.Client;

sealed class SaveLogKeeper
{
    readonly SaveLogGenerations _generations;
    readonly Func<ClientLog> _log;
    readonly Action<string> _note;
    readonly Action<Action>? _disk;
    SaveLog? _saveLog;
    string? _path;
    readonly Stopwatch _sinceWhole = new();
    int _wholeAtTurn = -1;
    long _lastWholeBytes;
    double _sinceFlush;
    bool _failureTold;

    public SaveLogKeeper(SaveLogGenerations generations, Func<ClientLog> log, Action<string> note, Action<Action>? disk = null)
    {
        _generations = generations; _log = log; _note = note; _disk = disk;
    }

    public bool Working => _saveLog is { Working: true };

    public string State => _saveLog is { } l
        ? $"{l.Count} command(s) since the table was last written whole, {l.Bytes} bytes" + (l.Working ? "" : " (NOT WORKING)")
        : "no log";

    public bool Ensure(GameServer server, string path)
    {
        if (_saveLog != null && SaveStore.SamePath(_path, path)) return false;
        _saveLog?.Dispose();
        _path = path;
        _saveLog = new SaveLog(path, SaveLog.StampOf(server.Created, server.State.ScenarioId), _generations, _disk);
        _failureTold = false;
        server.Applied = c => _saveLog?.Append(c);
        _sinceWhole.Restart();
        _wholeAtTurn = server.State.Clock.Turn;
        return true;
    }

    public void Tick(double delta)
    {
        _sinceFlush += delta;
        if (_sinceFlush >= 1.0) { _sinceFlush = 0; _saveLog?.Flush(); }
        if (_failureTold || _saveLog is not { Failure: { } why }) return;
        _failureTold = true;
        _log().Error("save log stopped: " + why);
        _note($"The log of each change between whole saves stopped: {why}. Nothing is lost: the whole game is written at every save instead.");
    }

    public (long Generation, bool LiveLogIdle) SetAside(string path, int turn)
    {
        _saveLog?.Rotate();
        _sinceWhole.Restart();
        _wholeAtTurn = turn;
        return (_generations.Of(path), _saveLog is not { Working: true } || !SaveStore.SamePath(_path, path));
    }

    public void TakenWhole(long bytes) => _lastWholeBytes = bytes;

    public bool WholeSaveDue(bool closing, int turn)
    {
        if (closing || _lastWholeBytes == 0 || _saveLog is not { Working: true } log) return true;
        return log.Bytes >= Math.Max(8L << 20, _lastWholeBytes / 2)
            || _sinceWhole.Elapsed.TotalSeconds >= 120 || turn != _wholeAtTurn;
    }

    public void Close(GameServer? server)
    {
        if (_saveLog != null) { _saveLog.Dispose(); _saveLog = null; _path = null; }
        if (server != null) server.Applied = null;
    }
}

namespace KTable.Client.Studio;

public sealed class UndoHistory
{
    public const int Steps = 120;
    public const long Chars = 24_000_000;

    readonly List<string> _undo = new(), _redo = new();
    readonly List<long> _undoAt = new(), _redoAt = new();

    public void Start(string state)
    {
        _undo.Clear();
        _redo.Clear();
        _undoAt.Clear();
        _redoAt.Clear();
        _undo.Add(state);
        _undoAt.Add(0);
    }

    public bool Record(string state, long stamp = 0)
    {
        if (_undo.Count > 0 && _undo[^1] == state) return false;
        _undo.Add(state);
        _undoAt.Add(stamp);
        _redo.Clear();
        _redoAt.Clear();
        long total = _undo.Sum(x => (long)x.Length);
        while (_undo.Count > 2 && (_undo.Count > Steps || total > Chars))
        {
            total -= _undo[0].Length;
            _undo.RemoveAt(0);
            _undoAt.RemoveAt(0);
        }
        return true;
    }

    public IEnumerable<string> All => _undo.Concat(_redo).ToList();

    public bool CanUndo => _undo.Count > 1;
    public bool CanRedo => _redo.Count > 0;

    public long UndoStamp => CanUndo ? _undoAt[^1] : 0;
    public long RedoStamp => CanRedo ? _redoAt[^1] : 0;

    public string? Undo(long stamp = 0)
    {
        if (_undo.Count < 2) return null;
        _redo.Add(_undo[^1]);
        _redoAt.Add(stamp);
        _undo.RemoveAt(_undo.Count - 1);
        _undoAt.RemoveAt(_undoAt.Count - 1);
        return _undo[^1];
    }

    public string? Redo(long stamp = 0)
    {
        if (_redo.Count == 0) return null;
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _redoAt.RemoveAt(_redoAt.Count - 1);
        _undo.Add(s);
        _undoAt.Add(stamp);
        return s;
    }
}

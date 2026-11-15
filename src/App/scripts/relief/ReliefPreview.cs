using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed record PreviewKey(object Settings, Vec2[] Points)
{
    public bool Equals(PreviewKey? o) => o != null && Equals(Settings, o.Settings) && Points.AsSpan().SequenceEqual(o.Points);

    public override int GetHashCode() => HashCode.Combine(Settings, Points.Length);
}

sealed class ReliefPreview
{
    readonly ReliefCache _relief;
    readonly ReliefGrid? _under;
    ReliefGrid? _scratch;
    CellBox _scratchBox = CellBox.None;
    Task<CellBox>? _work;
    PreviewKey? _working, _failed;

    ReliefPreview(ReliefCache relief, SheetDef sheet, ReliefGrid? under)
    {
        _relief = relief;
        _under = under;
        Sheet = Json.Clone(sheet);
        Before = under ?? ReliefTool.Blank(sheet);
        Grid = Before.Copy();
    }

    public SheetDef Sheet { get; }
    public ReliefGrid Before { get; }
    public ReliefGrid Grid { get; }
    public CellBox Box { get; private set; } = CellBox.None;
    public PreviewKey? Shown { get; private set; }

    public static ReliefPreview? Of(Session s, SheetDef sheet)
    {
        var under = s.Relief.Under(sheet);
        return under == null && sheet.Relief is { Length: > 0 } ? null : new ReliefPreview(s.Relief, sheet, under);
    }

    public bool Holds(Session s) => s.View.Sheets.FirstOrDefault(x => x.Id == Sheet.Id) is { } now && ReferenceEquals(s.Relief.Under(now), _under) && Placed(now, Sheet);

    static bool Placed(SheetDef a, SheetDef b)
    {
        FaceBox ca = a.Crop ?? SheetGeometry.Whole, cb = b.Crop ?? SheetGeometry.Whole;
        return a.Pos == b.Pos && a.RotDeg == b.RotDeg && a.Size == b.Size && a.Height == b.Height && ca.X == cb.X && ca.Y == cb.Y && ca.W == cb.W && ca.H == cb.H;
    }

    public void Lay(PreviewKey key, Func<ReliefGrid, CellBox> shape)
    {
        _working = null;
        var was = Box;
        Grid.Take(Before, was);
        Box = shape(Grid);
        Shown = key;
        Show(was.Union(Box));
    }

    public void Work(PreviewKey key, Func<Func<ReliefGrid, CellBox>> make)
    {
        if (_work != null || Equals(key, Shown) || Equals(key, _failed)) return;
        _working = key;
        var scratch = _scratch ??= Before.Copy();
        var before = Before;
        var last = _scratchBox;
        var shape = make();
        _work = Task.Run(() =>
        {
            scratch.Take(before, last);
            return shape(scratch);
        });
    }

    public bool Working => _work != null;

    public void Tick()
    {
        if (_work is not { IsCompleted: true } done) return;
        _work = null;
        var key = _working;
        _working = null;
        if (!done.IsCompletedSuccessfully)
        {
            _scratchBox = new CellBox(0, 0, Before.Cols - 1, Before.Rows - 1);
            _failed = key;
            if (!Assets.Quitting) GD.PrintErr("[relief] working out a preview: " + done.Exception?.GetBaseException().Message);
            return;
        }
        _scratchBox = done.Result;
        if (key == null) return;
        var was = Box;
        Grid.Take(Before, was);
        Grid.Take(_scratch!, _scratchBox);
        Box = _scratchBox;
        Shown = key;
        Show(was.Union(Box));
    }

    void Show(CellBox dirty)
    {
        if (dirty.Empty) return;
        var where = ReliefTool.WorldBox(Sheet, Grid, dirty);
        if (Box.Empty) _relief.Unpreview(Sheet.Id, Grid, where);
        else _relief.Preview(Sheet.Id, Grid, where, dirty);
    }

    public void Drop()
    {
        _working = null;
        if (!Box.Empty) _relief.Unpreview(Sheet.Id, Grid, ReliefTool.WorldBox(Sheet, Grid, Box));
        Box = CellBox.None;
        Shown = null;
    }
}

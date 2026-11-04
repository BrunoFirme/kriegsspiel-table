using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed partial class TableDemos
{
    const double ReliefSettleSeconds = 4;

    void DemoRelief(Hud hud, string list)
    {
        var files = list.Split(';').Where(f => f.Length > 0).ToList();
        var reliefs = files.Where(ReliefImport.IsRelief).ToList();
        var jobs = ReliefSheets.Paired(reliefs, files.Except(reliefs).ToList());
        GD.Print($"[relief] importing {jobs.Count}: " + string.Join(", ", jobs.Select(j => $"{Path.GetFileName(j.Relief)} + {(j.Picture != null ? Path.GetFileName(j.Picture) : j.OwnPicture ? "own texture" : "none")}")));
        var used = jobs.Where(j => j.Picture != null).Select(j => j.Picture!).ToHashSet();
        var rest = files.Except(reliefs).Where(f => !used.Contains(f)).ToList();
        hud.Map.Reliefs.Import(jobs, true, true, () =>
        {
            if (rest.Count > 0) hud.Map.AddPictures(rest);
            Tree.CreateTimer(ReliefSettleSeconds * (rest.Count > 0 ? 2 : 1)).Timeout += () => { Drape(); Shape(hud, files); };
        });
        if (_args.ContainsKey("relief-tool")) Tree.CreateTimer(ReliefSettleSeconds * 3).Timeout += () => hud.T.SetTool(Tool.Relief);
    }

    void Drape()
    {
        var relief = Session.View.Sheets.FirstOrDefault(s => s.Relief != null && !s.HasPicture);
        var picture = Session.View.Sheets.FirstOrDefault(s => s.Relief == null);
        if (relief == null || picture == null) return;
        Session.Do(new MoveSheet { Id = picture.Id, Pos = relief.Pos, RotDeg = relief.RotDeg, Z = relief.Z + 1, Size = relief.Size });
        GD.Print($"[relief] laid {picture.Name} over {relief.Name}");
    }

    void DemoBlank(Hud hud, string size)
    {
        var n = size.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        hud.Map.Reliefs.AddBlank(n[0], n[1], why =>
        {
            if (why != null) { GD.Print("[relief] blank failed: " + why); return; }
            Tree.CreateTimer(ReliefSettleSeconds).Timeout += () =>
            {
                var sheet = Session.View.Sheets.Last();
                var grid = Session.Relief.Grid(sheet)?.Copy();
                GD.Print($"[relief] blank ground {sheet.Name} {sheet.Size.W:0.#}x{sheet.Size.H:0.#}, picture {sheet.HasPicture}, relief loaded {grid != null}, grid {grid?.Cols}x{grid?.Rows}");
                if (grid == null) return;
                for (int i = 0; i < 8; i++) ReliefBrush.Stroke(grid, new List<(double, double)> { (0.3, 0.4), (0.6, 0.5) }, 40, new Brush(BrushMode.Raise, 0, 0.6));
                ReliefBrush.Stroke(grid, new List<(double, double)> { (0.1, 0.8), (0.9, 0.75) }, 10, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Of("road")!.Value));
                ReliefBrush.Stroke(grid, new List<(double, double)> { (0.75, 0.25) }, 50, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Of("wood")!.Value));
                ReliefTool.Commit(Session, Json.Clone(sheet), grid, hud.Map.Reliefs.Share, m => GD.Print("[relief] " + m));
                Tree.CreateTimer(ReliefSettleSeconds).Timeout += () =>
                    GD.Print($"[relief] after shaping the blank: highest {Session.Ground.Highest:0.##}, painted {Session.Relief.Grid(Session.View.Sheets.Last())?.Painted}");
            };
        });
    }

    const int DropFrames = 40, CarryFrames = 240, HoldFrames = 20;
    const double StackSettleSeconds = 1.5;

    async System.Threading.Tasks.Task DropOnHill(Hud hud, SheetDef sheet)
    {
        var hill = Ground.FromUv(sheet, 0.42, 0.5);
        await DropAt(hud, 0, hill);
        await DropAt(hud, 1, hill);
        await _main.ToSignal(Tree.CreateTimer(StackSettleSeconds), Timer.SignalName.Timeout);
        var board = Session.View.Layer(Layers.Umpire);
        if (board.Count > 1 && hud.T.NodeOf(Layers.Umpire, board[0].Id) is { } a && hud.T.NodeOf(Layers.Umpire, board[1].Id) is { } b)
        {
            hud.T.Rig.Pitch = StackViewPitch;
            hud.T.Rig.Frame(a.Position, StackViewSize);
            GD.Print($"[relief] stack: base tilt {Tilt(a):0.#}°, top tilt {Tilt(b):0.#}°, same lean {a.Up.IsEqualApprox(b.Up)}, top stored y {b.Piece.Y:0.##}, gap over the base's top {b.Position.Y - a.TopAt(b.Position):0.###}");
        }
    }

    const float StackViewPitch = -0.3f, StackViewSize = 16;

    static double Tilt(PieceNode n) => Mathf.RadToDeg(n.Up.AngleTo(Vector3.Up));

    async System.Threading.Tasks.Task DropAt(Hud hud, int which, Vec2 hill)
    {
        var board = Session.View.Layer(Layers.Umpire);
        if (board.Count <= which || hud.T.NodeOf(Layers.Umpire, board[which].Id) is not { } n) { GD.Print("[relief] no piece to drop"); return; }
        var start = n.Piece.Pos;
        hud.T.SetSelection(new[] { n.Piece.Id });
        hud.T.Hand.Start(start);
        for (int i = 1; i <= CarryFrames; i++) { hud.T.Hand.Move(start + new Vec2((hill.X - start.X) * i / CarryFrames, (hill.Z - start.Z) * i / CarryFrames)); await _main.ToSignal(Tree,
            SceneTree.SignalName.ProcessFrame); }
        for (int i = 0; i < HoldFrames; i++) { hud.T.Hand.Move(hill); await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame); }
        hud.T.Hand.Drop();
        double worst = double.MaxValue;
        var trail = new List<string>();
        for (int i = 0; i < DropFrames; i++)
        {
            await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            double above = n.Position.Y - Session.Ground.At(Coords.FromWorld(n.Position));
            worst = System.Math.Min(worst, above);
            if (i % 5 == 0) trail.Add($"{above:0.##}");
        }
        var seat = Session.Ground.SeatOn(n.Piece.Pos, n.Piece.RotDeg, n.Footprint.X, n.Footprint.Y);
        GD.Print($"[relief] dropped piece {which} at {hill.X:0.#},{hill.Z:0.#}: ground there {Session.Ground.At(hill):0.##}, stored y {n.Piece.Y:0.##}, node {n.Position.Y:0.##}, tilt {Tilt(n):0.#}° (slope seat {seat.TiltDeg:0.#}°); height above the ground under its centre every 5 frames: {string.Join(" ", trail)}; lowest {worst:0.###}");
    }

    void Shape(Hud hud, List<string> files)
    {
        var sheets = Session.View.Sheets;
        var ground = Session.Ground;
        GD.Print($"[relief] sheets {sheets.Count}, ground {(ground.Any ? "raised" : "flat")}, highest {ground.Highest:0.##}: " +
            string.Join(", ", sheets.Select(s => $"{s.Name} {s.Size.W:0.#}x{s.Size.H:0.#} at {s.Pos.X:0.#},{s.Pos.Z:0.#} centre {ground.At(s.Pos):0.##}")));
        if (sheets.Count == 0) return;
        var first = sheets[0];
        var grid = Session.Relief.Grid(first)?.Copy();
        if (grid != null)
        {
            var road = new List<(double, double)> { (0.05, 0.85), (0.5, 0.75), (0.95, 0.8) };
            ReliefBrush.Stroke(grid, road, 6, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Of("road")!.Value));
            ReliefBrush.Stroke(grid, new List<(double, double)> { (0.2, 0.25), (0.35, 0.3) }, 18, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Of("wood")!.Value));
            ReliefBrush.Stroke(grid, new List<(double, double)> { (0.75, 0.2) }, 14, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Of("water")!.Value));
            for (int i = 0; i < 6; i++) ReliefBrush.Stroke(grid, new List<(double, double)> { (0.6, 0.55), (0.8, 0.6) }, 10, new Brush(BrushMode.Raise, 0, 1));
            ReliefTool.Commit(Session, Json.Clone(first), grid, hud.Map.Reliefs.Share, m => GD.Print("[relief] " + m));
        }
        var spots = new[] { first.Pos, first.Pos + new Vec2(first.Size.W * 0.2, first.Size.H * 0.1), first.Pos + new Vec2(-first.Size.W * 0.3, -first.Size.H * 0.2) };
        var picture = files.FirstOrDefault(f => !ReliefImport.IsRelief(f)) ?? files.Select(f => Path.ChangeExtension(f, ".png")).FirstOrDefault(File.Exists);
        if (picture != null)
        {
            var pid = UserPacks.IdFor("relief pieces");
            UserPacks.AddImages(Session.Store, pid, "Relief pieces", new[] { picture }, 6.0, "box", "", "Umpire");
            Net.SharePack(pid);
            if (Session.Store.Pack(pid)?.Pieces.Count > 0)
                foreach (var at in spots) Session.Do(new SpawnPiece { Layer = Layers.Umpire, Def = Session.Store.Pack(pid)!.Pieces[0].Id, Pos = at, RotDeg = 200 });
        }
        Session.Do(new Draw { Layer = Layers.Umpire,
            Stroke = new Stroke { Color = "#C0392B", Width = 1.2, Points = { first.Pos - new Vec2(first.Size.W * 0.45, 0), first.Pos + new Vec2(first.Size.W * 0.9, first.Size.H * 0.05) } } });
        foreach (var s in sheets) Session.Do(new LockSheet { Id = s.Id, Locked = true });
        Tree.CreateTimer(ReliefSettleSeconds).Timeout += () =>
        {
            var g = Session.Ground;
            GD.Print($"[relief] after shaping: {Session.View.Sheets.Count(s => s.Relief != null)} reliefs, painted {Session.View.Sheets.Count(s => Session.Relief.Grid(s)?.Painted == true)}, highest {g.Highest:0.##}");
            foreach (var p in Session.View.Layer(Layers.Umpire)) GD.Print($"[relief] piece {p.Id} at {p.Pos.X:0.#},{p.Pos.Z:0.#} y {p.Y:0.##} ground {g.At(p.Pos):0.##}");
            var dir = ScenarioFiles.SaveTable(Session.Store, Session.View, "Relief test", Session.ScenarioDir);
            var target = Path.Combine(Session.Store.Root, "scenarios", dir);
            var sc = Json.Load<Scenario>(Path.Combine(target, "scenario.json"));
            GD.Print($"[relief] saved scenario {dir}: packages {string.Join(",", sc.Sheets.Select(s => s.Package).Distinct())}, relief files kept {sc.Sheets.Where(s => s.Relief != null).All(s => File.Exists(Path.Combine(target, ReliefFiles.HeaderRel(s.Relief!))))}");
            if (_args.ContainsKey("relief-drop")) Callable.From(() => { _ = DropOnHill(hud, first); }).CallDeferred();
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using Json = KTable.Core.Json;
using Timer = Godot.Timer;

namespace KTable.App;

sealed partial class TableDemos
{
    readonly Main _main;
    readonly Dictionary<string, string> _args;
    readonly Table _table;
    readonly Hud _hud;
    ulong _openedAt;

    public TableDemos(Main main, Dictionary<string, string> args, Table table, Hud hud)
    {
        _main = main;
        _args = args;
        _table = table;
        _hud = hud;
    }

    Session Session => _main.Session;
    Net Net => _main.Net;
    SceneTree Tree => _main.GetTree();

    public void Run()
    {
        Driven(_table, _hud);
        Formation(_table);
        Picked(_table, _hud);
        Carried(_table);
        Crowd(_table);
        Moved(_table);
        Built(_table, _hud);
        Dice(_table);
        Loaded(_table);
        Setting(_table);
        Turns(_table);
        Camera(_table);
    }

    void Driven(Table table, Hud hud)
    {
        if (_args.ContainsKey("demo-camera")) _ = DemoCamera(table);
        if (_args.ContainsKey("demo-pointers")) _ = DemoPointers(table, hud);
        if (_args.TryGetValue("demo-mapperf", out var mapPerf)) _ = DemoMapPerf(table, mapPerf);
        if (_args.TryGetValue("demo-statekey", out var dsk)) _ = DemoStateKey(table, dsk);
        if (_args.TryGetValue("demo-hotkey", out var dhk)) _ = DemoHotkey(table, dhk);
        if (_args.TryGetValue("demo-script", out var dsc)) _ = DemoScript(table, dsc);
        if (_args.TryGetValue("demo-menu", out var dmn))
        {
            var nk = dmn.Split(',').Select(int.Parse).ToArray();
            Tree.CreateTimer(2.5).Timeout += () =>
            {
                var pc = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[nk[0]];
                GD.Print($"[menu] piece {pc.Id} rows: {string.Join(" | ", pc.MenuItems.Select(m => m.Label))}; lines {pc.Lines.Count}; widgets {pc.Widgets.Count}");
                Session.Do(new KTable.Core.Protocol.PickMenuItem { Layer = KTable.Core.Model.Layers.Umpire, Id = pc.Id, Index = nk[1] });
                Tree.CreateTimer(0.5).Timeout += () => GD.Print($"[menu] after: lines {pc.Lines.Count} ({string.Join(", ", pc.Lines.Select(l => $"{l.Points.Count} pts {l.Color} w{l.Width}"))})");
            };
        }
        if (_args.ContainsKey("demo-wheel")) _ = DemoWheel(table);
        if (_args.ContainsKey("demo-stack")) new TableDemo(table).Stack();
        if (_args.ContainsKey("demo-shapes")) new TableDemo(table).Shapes();
        if (_args.TryGetValue("demo-select", out var dsel)) new TableDemo(table).Select(int.Parse(dsel));
        if (_args.ContainsKey("demo-boards")) new TableDemo(table).Boards();
        if (_args.ContainsKey("demo-attach")) new TableDemo(table).Attach();
        if (_args.TryGetValue("demo-lock", out var dlk)) new TableDemo(table).Lock(int.Parse(dlk));
        if (_args.TryGetValue("demo-alttab", out var dat)) new TableDemo(table).AltTab(int.Parse(dat));
        if (_args.TryGetValue("demo-release", out var drl))
            _ = new ReleaseDemo(table, () => hud.Pieces.ItemCentre(0), () => hud.OpenPanel("pieces")).Run(drl)
                .ContinueWith(x => { if (x.Exception != null) GD.PushError("[release] " + x.Exception); }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        if (_args.TryGetValue("demo-state", out var dst)) { var a = dst.Split(','); new TableDemo(table).State(int.Parse(a[0]), a.Length > 1 ? int.Parse(a[1]) : 1, a.Length > 2 && a[2] == "stay"); }
        if (_args.ContainsKey("demo-undo")) new TableDemo(table).Undo();
    }

    void Formation(Table table)
    {
        if (_args.TryGetValue("demo-formation", out var dfm))
        {
            var fp = dfm.Split(',');
            Tree.CreateTimer(3.0).Timeout += () =>
            {
                var board = Session.View.Layer(KTable.Core.Model.Layers.Umpire).Take(int.Parse(fp[0])).ToList();
                if (board.Count == 0) { GD.Print("[form] nothing on the board"); return; }
                table.SetSelection(board.Select(p => p.Id).ToArray());
                var want = fp.Length > 1 ? fp[1] : "A";
                var first = board[0];
                int idx = first.Widgets.FindIndex(w => w.Label == want) + 1;
                for (int wi = 0; wi < first.Widgets.Count; wi++)
                {
                    var w = first.Widgets[wi];
                    GD.Print($"[form] widget {wi + 1}: face={w.Face} label=\"{w.Label}\" click={w.OnClick} colour={w.Color}");
                }
                if (int.TryParse(want, out var byIndex)) idx = byIndex;
                GD.Print($"[form] {board.Count} selected; pressing \"{want}\" (widget {idx}) on {first.Id}");
                if (idx <= 0) { GD.Print("[form] no such widget"); return; }
                var node = table.NodeOf(KTable.Core.Model.Layers.Umpire, first.Id)!;
                table.PressWidget(node, idx, first.Widgets[idx - 1]);
                Tree.CreateTimer(1.2).Timeout += () =>
                {
                    foreach (var p in board)
                    {
                        var live = Session.View.Find(KTable.Core.Model.Layers.Umpire, p.Id)!;
                        var marker = live.Widgets.FirstOrDefault(w => w.Face == "back" && w.OnClick is null or "" && w.Color == "#000000");
                        var mark = marker?.Label ?? "?";
                        var lit = string.Join("", live.Widgets.Where(w => w.OnClick is "setLine" or "setSquare" or "setColumn" or "setAssault")
                            .Select(w => w.Color is "#FF0000" or "#ff0000" ? w.Label : "."));
                        GD.Print($"[form]   {p.Id}: def {live.Def} mark \"{mark}\"  lit {lit}  state {live.State} widgets {live.Widgets.Count}");
                        if (table.NodeOf(KTable.Core.Model.Layers.Umpire, p.Id) is { } nd)
                        {
                            string Hit(double fx, double fz)
                            {
                                var a = -KTable.App.Coords.Yaw(live.RotDeg);
                                double dx = nd.Footprint.X / 2 * fx, dz = nd.Footprint.Y / 2 * fz;
                                var at = new KTable.Core.Model.Vec2(live.Pos.X + dx * Math.Cos(a) + dz * Math.Sin(a), live.Pos.Z - dx * Math.Sin(a) + dz * Math.Cos(a));
                                var wa = nd.WidgetAt(at);
                                return (nd.Contains(at) ? "hit" : "MISS") + (wa is { } x ? $"/widget {x.index}" : "");
                            }
                            GD.Print($"[form]     picture {live.Picture ?? "(own)"} shape {nd.Shape} footprint {nd.Footprint.X:0.00}x{nd.Footprint.Y:0.00} · centre {Hit(0, 0)} · half-left {Hit(-0.5, 0)} · half-right {Hit(0.5, 0)} · near-edge {Hit(0.8, 0)} · top {Hit(0, 0.8)}");
                        }
                    }
                };
            };
        }
    }

    void Picked(Table table, Hud hud)
    {
        if (_args.TryGetValue("demo-range", out var drg))
        {
            var rp = drg.Split(',');
            Tree.CreateTimer(3.0).Timeout += () =>
            {
                var board = Session.View.Layer(KTable.Core.Model.Layers.Umpire);
                if (board.Count == 0) { GD.Print("[range] nothing on the board"); return; }
                if (rp.Length > 1) table.RangeMetres = double.Parse(rp[1], System.Globalization.CultureInfo.InvariantCulture);
                var pick = board.Skip(int.Parse(rp[0])).Take(2).Select(p => p.Id).ToArray();
                table.SetSelection(pick);
                table.SetTool(Tool.Range);
                Tree.CreateTimer(1.5).Timeout += () =>
                {
                    GD.Print($"[range] readings on the table with {table.Selected.Count} picked: {table.RangeReadings}");
                    var next = board.Skip(int.Parse(rp[0]) + 2).Take(1).Select(p => p.Id).ToArray();
                    if (next.Length == 0) return;
                    table.SetSelection(next);
                    Tree.CreateTimer(0.5).Timeout += () => GD.Print($"[range] after picking one other piece: {table.RangeReadings} reading(s)");
                };
                var reaches = pick.Select(id => table.NodeOf(KTable.Core.Model.Layers.Umpire, id) is { } nd ? $"{id} reaches {table.RangeOf(nd):0.##}" : $"{id} not drawn");
                GD.Print($"[range] {pick.Length} selected, standing reach {table.RangeMetres:0.##}; " + string.Join(", ", reaches));
            };
        }
        if (_args.TryGetValue("name-size", out var nsz))
        {
            table.S.Art.Look.FaceTextScale = float.Parse(nsz, System.Globalization.CultureInfo.InvariantCulture);
            Tree.CreateTimer(3.0).Timeout += table.RefreshFaceText;
        }
        if (_args.TryGetValue("demo-type", out var dty))
        {
            var pt = dty.Split(',', 2);
            Tree.CreateTimer(4.0).Timeout += () => new TableDemo(table).TypeWidget(int.Parse(pt[0]), pt.Length > 1 ? pt[1] : "TEST");
        }
        if (_args.ContainsKey("demo-palette")) _ = DemoPalette(table, hud);
        if (_args.TryGetValue("demo-restore", out var drs)) _ = DemoRestore(table, drs);
        if (_args.ContainsKey("demo") && Session.IsUmpire)
            _ = Demo(table, hud).ContinueWith(x => { if (x.Exception != null) GD.PushError("[demo] " + x.Exception); }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        if (_args.ContainsKey("2d")) table.Rig.ToggleTopDown();
        if (_args.TryGetValue("select", out var selIdx))
            table.SetSelection(new[] { Session.View.Layer(KTable.Core.Model.Layers.Umpire)[int.Parse(selIdx)].Id });
        if (_args.ContainsKey("demo-transform")) _ = DemoTransform(table);
    }

    void Carried(Table table)
    {
        if (_args.TryGetValue("demo-hold", out var dh))
        {
            var a = dh.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var ids = Session.View.Layer(KTable.Core.Model.Layers.Umpire).Skip((int)a[0]).Take((int)(a[1] - a[0] + 1)).Select(p => p.Id).ToList();
            Tree.CreateTimer(3).Timeout += () => new TableDemo(table).Hold(ids, new KTable.Core.Model.Vec2(a[2], a[3]), (int)a[4], a.Length > 6 ? new KTable.Core.Model.Vec2(a[5], a[6]) : null);
            Tree.CreateTimer(9).Timeout += () =>
            {
                var l = Net.Server!.Log;
                GD.Print($"[hold] log={l.Count} " + string.Join(" | ", l.Select(c => c is KTable.Core.Protocol.Batch b ? $"Batch[{string.Join(",", b.Items.Select(i => i.GetType().Name))}]" : c.GetType().Name)));
                foreach (var id in ids.Take(3)) { var p = Net.Server.State.Find(KTable.Core.Model.Layers.Umpire, id)!; GD.Print($"[hold] {id} pos={p.Pos.X:0.0},{p.Pos.Z:0.0} rot={p.RotDeg:0} y={p.Y:0.00}"); }
                GD.Print($"[hold] pieces now {Net.Server.State.Layer(KTable.Core.Model.Layers.Umpire).Count}");
            };
        }
        if (_args.TryGetValue("demo-grab", out var dg))
        {
            int gi = int.Parse(dg);
            var gid = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[gi].Id;
            Tree.CreateTimer(2.5).Timeout += () => new TableDemo(table).Grab(gid);
            Tree.CreateTimer(9.5).Timeout += () =>
            {
                var l = Net.Server!.Log;
                GD.Print($"[grab] log={l.Count} " + string.Join(" | ", l.Select(c => c is KTable.Core.Protocol.Batch b ? $"Batch[{string.Join(",", b.Items.Select(i => i.GetType().Name))}]" : c.GetType().Name)));
                var gp = Net.Server.State.Find(KTable.Core.Model.Layers.Umpire, gid)!;
                GD.Print($"[grab] table: pos={gp.Pos.X:0.00},{gp.Pos.Z:0.00} rot={gp.RotDeg:0} y={gp.Y:0.00} faceDown={gp.FaceDown}");
            };
        }
    }

    void Crowd(Table table)
    {
        if (_args.TryGetValue("demo-crowd", out var dcr))
        {
            _openedAt = Time.GetTicksMsec();
            var cp = dcr.Split(',');
            int who = int.Parse(cp[0]);
            float dist = cp.Length > 1 ? float.Parse(cp[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
            Callable.From((Action)(async () =>
            {
                await _main.ToSignal(Tree.CreateTimer(3.0), Timer.SignalName.Timeout);
                var board = Session.View.Layer(KTable.Core.Model.Layers.Umpire);
                if (board.Count <= who) { GD.Print("[crowd] no such piece"); return; }
                if (dist > 0) table.Rig.Frame(Coords.ToWorld(board[who].Pos), dist);
                else table.FrameAll();
                async System.Threading.Tasks.Task<(double worst, double avg, int frames)> Watch(double seconds)
                {
                    double worst = 0, total = 0;
                    int frames = 0;
                    var until = Time.GetTicksMsec() + (ulong)(seconds * 1000);
                    while (Time.GetTicksMsec() < until)
                    {
                        double dt = await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame) is not null ? _main.GetProcessDeltaTime() : 0;
                        worst = Math.Max(worst, dt);
                        total += dt;
                        frames++;
                    }
                    return (worst, frames > 0 ? total / frames : 0, frames);
                }
                void Say(string what, (double worst, double avg, int frames) r) => GD.Print(
                    $"[crowd] {what}: {(r.avg > 0 ? 1 / r.avg : 0):0} fps avg, worst frame {r.worst * 1000:0} ms, {r.frames} frames · " +
                    $"draws={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} objects={Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0} " +
                    $"prims={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000:0}k nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} " +
                    $"vram={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0}MB heap={Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576:0}MB");
                GD.Print($"[crowd] {board.Count} pieces, the view {table.Rig.Distance:0} units out, the table opened {(Time.GetTicksMsec() - _openedAt) / 1000.0:0.0} s ago");
                GD.Print($"[crowd] shared art: {Session.Art.Meshes} meshes, {Session.Art.Materials} materials; {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount):0} resources in all");
                GD.Print($"[crowd] lettered: {table.LetteredCount} of {board.Count}; grid: {table.GridDebug()}; save log: {Net.SaveLogState}");
                if (table.NodeOf(KTable.Core.Model.Layers.Umpire, board[0].Id) is { } one)
                {
                    var parts = one.GetChildren().OfType<VisualInstance3D>().Select(c => $"{c.GetType().Name}{(c is MeshInstance3D mi ? "[" + (mi.Mesh?.GetSurfaceCount() ?? 0) + (mi.Visible ? "" : " hidden") + "]" : c.Visible ? "" : "[hidden]")}").ToList();
                    GD.Print($"[crowd] one piece is {parts.Count} drawable node(s): {string.Join(", ", parts)}");
                }
                Say("standing still", await Watch(2.5));
                var selClock = System.Diagnostics.Stopwatch.StartNew();
                table.SetSelection(board.Select(p => p.Id).ToArray());
                await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
                GD.Print($"[crowd] picking out all {board.Count}: {selClock.ElapsedMilliseconds} ms");
                Say("holding the whole board selected", await Watch(1.5));
                table.SetSelection(Array.Empty<string>());
                await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
                var from = table.Rig.Cam.UnprojectPosition(Coords.ToWorld(board[who].Pos, 0.2f));
                var to = from + new Vector2(260, 150);
                table.SetSelection(new[] { board[who].Id });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
                await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
                double worstDrag = 0, totalDrag = 0;
                int dragFrames = 0;
                for (int i = 1; i <= 90; i++)
                {
                    var at = from.Lerp(to, i / 90f);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (to - from) / 90f });
                    await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
                    double dt = _main.GetProcessDeltaTime();
                    worstDrag = Math.Max(worstDrag, dt);
                    totalDrag += dt;
                    dragFrames++;
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
                Say("carrying a piece", (worstDrag, dragFrames > 0 ? totalDrag / dragFrames : 0, dragFrames));
                await _main.ToSignal(Tree.CreateTimer(0.8), Timer.SignalName.Timeout);
                Say("after the drop", await Watch(1.5));
            })).CallDeferred();
        }
    }

    void Moved(Table table)
    {
        if (_args.TryGetValue("demo-piece", out var dpc))
        {
            var pa = dpc.Split(',');
            Tree.CreateTimer(pa.Length > 1 ? double.Parse(pa[1], System.Globalization.CultureInfo.InvariantCulture) : 4.0).Timeout += () =>
            {
                var board = Session.View.Layer(KTable.Core.Model.Layers.Umpire);
                int i = int.Parse(pa[0]);
                if (i < 0 || i >= board.Count) { GD.Print($"[piece] no piece {i} of {board.Count}"); return; }
                var p = board[i];
                GD.Print($"[piece] {i} of {board.Count}: {p.Id} pos={p.Pos.X:0.00},{p.Pos.Z:0.00} rot={p.RotDeg:0} seq={Session.View.LastSeq}");
            };
        }
        if (_args.TryGetValue("demo-crash", out var dcx))
        {
            Tree.CreateTimer(double.Parse(dcx, System.Globalization.CultureInfo.InvariantCulture)).Timeout += () =>
            {
                GD.Print($"[crash] stopping the process where it stands, without closing the table; log: {Net.SaveLogState}");
                OS.Kill(OS.GetProcessId());
            };
        }
        if (_args.TryGetValue("demo-push", out var dpu))
        {
            var pp = dpu.Split(',').Where(x => x != "steady").Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Tree.CreateTimer(3.0).Timeout += () => new TableDemo(table).Push((int)pp[0], new KTable.Core.Model.Vec2(pp[1], pp[2]), dpu.EndsWith(",steady"));
            Tree.CreateTimer(9.0).Timeout += () =>
            {
                var l = Net.Server!.Log;
                GD.Print($"[push] log={l.Count} " + string.Join(" | ", l.Select(c => c is KTable.Core.Protocol.Batch b ? $"Batch[{b.Items.Count}: {string.Join(",", b.Items.Select(i => i.GetType().Name))}]" : c.GetType().Name)));
            };
        }
        if (_args.TryGetValue("demo-mypieces", out var dmp))
        {
            Tree.CreateTimer(3).Timeout += () =>
            {
                var files = System.IO.Directory.GetFiles(dmp);
                var pid = UserPacks.IdFor($"{Session.Me.Name} test pieces");
                var n = UserPacks.AddImages(Session.Store, pid, $"{Session.Me.Name} test pieces", files, 2.0, "auto", "test", Session.Me.Name);
                var pk = Session.Store.Pack(pid)!;
                GD.Print($"[mypieces] made {n} pieces in {pid}: " + string.Join(", ", pk.Pieces.Select(p => $"{p.Name}({p.Shape} {p.Size.W:0.##}x{p.Size.H:0.##})")));
                Net.SharePack(pid);
                var ktp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ktable_test.ktpack");
                UserPacks.Export(Session.Store, pid, ktp);
                var other = new PackageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ktable_import_test"));
                var back = UserPacks.Import(other, ktp);
                GD.Print($"[mypieces] .ktpack round trip: {back} with {other.Pack(back)!.Pieces.Count} pieces, {new System.IO.FileInfo(ktp).Length / 1024} KB");
                Tree.CreateTimer(2).Timeout += () =>
                {
                    Session.Do(new KTable.Core.Protocol.SpawnPiece { Layer = KTable.Core.Model.Layers.Umpire, Def = pk.Pieces[0].Id, Pos = new KTable.Core.Model.Vec2(258, -279), RotDeg = 180 });
                    GD.Print($"[mypieces] game packs now: {string.Join(", ", Session.View.Packs)}");
                };
            };
            Session.Error += e => GD.Print("[mypieces] server said: " + e);
        }
    }

    void Built(Table table, Hud hud)
    {
        if (_args.TryGetValue("demo-sheets", out var dsh))
        {
            hud.Map.AddSheets(dsh.Split(';').ToList());
            Tree.CreateTimer(10).Timeout += () =>
            {
                var sh = Session.View.Sheets;
                GD.Print($"[blank] sheets on table: {sh.Count}: " + string.Join(", ", sh.Select(x => $"{x.Name} {x.Size.W:0.#}x{x.Size.H:0.#} at {x.Pos.X:0.#},{x.Pos.Z:0.#}")));
                if (sh.Count > 0) Session.Do(new KTable.Core.Protocol.MoveSheet { Id = sh[0].Id, Pos = sh[0].Pos, RotDeg = 0, Z = 0,
                    Size = new KTable.Core.Model.Size2(sh[0].Size.W * 1.5, sh[0].Size.H * 1.5) });
                foreach (var x in sh) Session.Do(new KTable.Core.Protocol.LockSheet { Id = x.Id, Locked = true });
                var pid = UserPacks.IdFor("demo pieces");
                UserPacks.AddImages(Session.Store, pid, "Demo pieces", new[] { dsh.Split(';').FirstOrDefault(p => !KTable.Core.Xcf.IsXcf(p)) ?? dsh.Split(';')[0] }, 2.0, "box", "", "Umpire");
                Net.SharePack(pid);
                if (Session.Store.Pack(pid)?.Pieces.Count > 0) Session.Do(new KTable.Core.Protocol.SpawnPiece { Layer = KTable.Core.Model.Layers.Umpire, Def = Session.Store.Pack(pid)!.Pieces[0].Id,
                    Pos = new KTable.Core.Model.Vec2(10, 0), RotDeg = 180 });
                Tree.CreateTimer(2).Timeout += () =>
                {
                    var dir = ScenarioFiles.SaveTable(Session.Store, Session.View, "Blank test", Session.ScenarioDir);
                    var sc = Json.Load<KTable.Core.Model.Scenario>(System.IO.Path.Combine(Session.Store.Root, "scenarios", dir, "scenario.json"));
                    var tilesOk = sc.Sheets.All(x => System.IO.File.Exists(System.IO.Path.Combine(Session.Store.TilesDir(x), "index.json")));
                    GD.Print($"[blank] saved scenario '{sc.Name}' in {dir}: sheets {sc.Sheets.Count} (packages {string.Join(",", sc.Sheets.Select(x => x.Package).Distinct())}, tiles present {tilesOk}), first sheet {(sc.Sheets.Count > 0 ? $"{sc.Sheets[0].Size.W:0.#}x{sc.Sheets[0].Size.H:0.#}" : "none")} locked {sc.Sheets.All(x => x.Locked)}, pieces {sc.UmpireBoard.Count}, packs {string.Join(",", sc.Packs)}");
                };
            };
        }
        if (_args.TryGetValue("demo-form", out var dform))
            Tree.CreateTimer(3).Timeout += () =>
            {
                hud.T.SetSelection(Session.View.Layer(KTable.Core.Model.Layers.Umpire).Select(p => p.Id));
                hud.T.SetTool(Tool.Formation);
                if (KTable.Client.Hand.Formations.Find(dform) is { } kind) hud.T.Forms.Form(kind);
            };
        if (_args.TryGetValue("demo-relief", out var drel)) DemoRelief(hud, drel);
        if (_args.TryGetValue("demo-blank", out var dbl)) DemoBlank(hud, dbl);
        if (_args.TryGetValue("demo-zone", out var dz))
            Session.Do(new KTable.Core.Protocol.SetZone { Zone = new KTable.Core.Model.Zone { Name = "Arevalillo", Owner = dz, Color = "#40C0FF",
                Polygon = { new(230, -300), new(290, -300), new(290, -240), new(230, -240) } } });
        if (_args.ContainsKey("demo-pointer"))
        {
            double t0 = 0;
            var timer = new Timer { WaitTime = 0.07, Autostart = true };
            timer.Timeout += () => { t0 += 0.07; Net.SendPointer(new KTable.Core.Model.Vec2(258 + Math.Cos(t0) * 6, -279 + Math.Sin(t0) * 6)); };
            _main.AddChild(timer);
        }
        if (_args.TryGetValue("demo-move", out var dm))
        {
            Tree.CreateTimer(4).Timeout += () =>
            {
                var p = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[int.Parse(dm)];
                Session.Do(new KTable.Core.Protocol.MovePiece { Layer = KTable.Core.Model.Layers.Umpire, Id = p.Id, Pos = p.Pos + new KTable.Core.Model.Vec2(2, 0), RotDeg = p.RotDeg });
                Session.Do(new KTable.Core.Protocol.MovePiece { Layer = KTable.Core.Model.Layers.Umpire, Id = p.Id, Pos = new KTable.Core.Model.Vec2(10, -10), RotDeg = p.RotDeg });
            };
            Session.Error += e => GD.Print("[co-umpire] server said: " + e);
            Session.Applied += c => GD.Print("[co-umpire] applied " + c.GetType().Name);
        }
        if (_args.TryGetValue("set-state", out var ss))
            foreach (var part in ss.Split(';'))
            {
                var kv = part.Split(':');
                var pc = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[int.Parse(kv[0])];
                Session.Do(new KTable.Core.Protocol.SetState { Layer = KTable.Core.Model.Layers.Umpire, Id = pc.Id, State = int.Parse(kv[1]) });
            }
        if (_args.TryGetValue("demo-ui", out var dui)) Tree.CreateTimer(2).Timeout += () => new HudDemo(hud).Run(dui);
        if (_args.TryGetValue("demo-install", out var dinst)) Tree.CreateTimer(3).Timeout += () => hud.InstallPack(dinst);
    }

    void Dice(Table table)
    {
        if (_args.ContainsKey("demo-dice"))
        {
            Net.SharePack("std.dice");
            var defs = Session.Store.Pack("std.dice")?.Pieces.Select(p => p.Id).ToList() ?? new();
            var at = Coords.FromWorld(table.Rig.Target);
            Tree.CreateTimer(0.5)
                .Timeout += () =>
                    { int i = 0; foreach (var d in defs) Session.Do(new KTable.Core.Protocol.SpawnPiece { Layer = KTable.Core.Model.Layers.Umpire, Def = d,
                        Pos = at + new KTable.Core.Model.Vec2(-4 + i++ * 1.1, 0), RotDeg = 180 }); };
            Tree.CreateTimer(2.5).Timeout += () =>
            {
                var ids = Session.View.Layer(KTable.Core.Model.Layers.Umpire).Where(p => p.Def.StartsWith("std.dice.")).Select(p => p.Id).ToList();
                table.SetSelection(ids);
                GD.Print($"[dice] rolling {ids.Count} dice: {table.RollDiceAtHand()}");
            };
            Tree.CreateTimer(4.0).Timeout += () =>
            {
                var rolls = Net.Server!.Log.OfType<KTable.Core.Protocol.RollDice>().Where(r => r.PieceIds.Count > 0).ToList();
                var pcs = Session.View.Layer(KTable.Core.Model.Layers.Umpire).Where(p => p.Def.StartsWith("std.dice.")).ToList();
                GD.Print($"[dice] rolls={rolls.Count} results={string.Join("|", rolls.Select(r => string.Join(",", r.Results)))} faces={string.Join(",", pcs.Select(p => $"{p.Def[9..]}={p.State + 1}"))}");
            };
            if (_args["demo-dice"] == "colour")
                Tree.CreateTimer(5.0).Timeout += () =>
                {
                    var pcs = Session.View.Layer(KTable.Core.Model.Layers.Umpire).Where(p => p.Def.StartsWith("std.dice.")).ToList();
                    Session.Do(new KTable.Core.Protocol.Batch { Items = pcs.Select((p, i) => (KTable.Core.Protocol.Command)new KTable.Core.Protocol.SetTint { Layer = KTable.Core.Model.Layers.Umpire,
                        Id = p.Id, Value = KTable.Core.Model.DieColors.All[1 + i % (KTable.Core.Model.DieColors.All.Length - 1)].Hex }).ToList() });
                    GD.Print($"[dice] coloured {pcs.Count} dice");
                };
        }
        if (_args.TryGetValue("demo-throw", out var dth))
        {
            Net.SharePack("std.dice");
            var at = Coords.FromWorld(table.Rig.Target);
            var defs = new[] { "std.dice.d6", "std.dice.d8", "std.dice.d20" };
            Tree.CreateTimer(0.5).Timeout += () =>
            {
                for (int i = 0; i < defs.Length; i++) Session.Do(new KTable.Core.Protocol.SpawnPiece { Layer = KTable.Core.Model.Layers.Umpire, Def = defs[i],
                    Pos = at + new KTable.Core.Model.Vec2(-1.5 + i * 1.5, -3), RotDeg = 180 });
            };
            List<string> Ids() => Session.View.Layer(KTable.Core.Model.Layers.Umpire).Where(p => p.Def.StartsWith("std.dice.")).Select(p => p.Id).ToList();
            Tree.CreateTimer(5.0).Timeout += () =>
            {
                if (dth == "roll") { table.SetSelection(Ids()); GD.Print($"[throw] R roll: {table.RollDiceAtHand()}"); }
                else new TableDemo(table).Throw(Ids(), at + new KTable.Core.Model.Vec2(0, -3), at + new KTable.Core.Model.Vec2(0, 0), 8);
            };
            foreach (var t in new[] { 5.25, 5.6, 6.2, 8.5 })
            {
                var tt = t;
                Tree.CreateTimer(tt).Timeout += () => GD.Print($"[throw] t={tt:0.00}: {table.DiceDebug()}");
            }
            Tree.CreateTimer(8.6).Timeout += () =>
            {
                var rolls = Net.Server!.Log.OfType<KTable.Core.Protocol.RollDice>().Where(r => r.PieceIds.Count > 0).ToList();
                var pcs = Session.View.Layer(KTable.Core.Model.Layers.Umpire).Where(p => p.Def.StartsWith("std.dice.")).ToList();
                GD.Print($"[throw] server results={string.Join("|", rolls.Select(r => string.Join(",", r.Results)))} pieces={string.Join(" ", pcs.Select(p => $"{p.Def[9..]}:face{p.State + 1}@{p.Pos.X:0.0},{p.Pos.Z:0.0},rot{p.RotDeg:0},y{p.Y:0.00}"))}");
            };
        }
    }

    void Loaded(Table table)
    {
        if (_args.TryGetValue("demo-perf", out var dpf))
        {
            int n = int.Parse(dpf);
            var defs = Session.View.Packs.SelectMany(p => Session.Store.Pack(p)?.Pieces ?? new()).Where(d => d.Kind != "model" && !d.Tags.Contains("effect") && d.Image != null).Select(d => d.Id).ToList();
            var at = Coords.FromWorld(table.Rig.Target);
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n)));
            Tree.CreateTimer(0.5).Timeout += () =>
            {
                if (defs.Count == 0) { GD.Print("[perf] no tile defs in this game's packs"); return; }
                for (int i = 0; i < n; i++)
                    Session.Do(new KTable.Core.Protocol.SpawnPiece { Layer = KTable.Core.Model.Layers.Umpire, Def = defs[i % defs.Count],
                        Pos = at + new KTable.Core.Model.Vec2((i % cols - cols / 2.0) * 1.7, (i / cols - cols / 2.0) * 1.1), RotDeg = 180 });
                table.Rig.Frame(Coords.ToWorld(at), cols * 1.7f * 1.15f);
            };
            var samples = new List<double>();
            foreach (var t in new[] { 8.0, 8.5, 9.0, 9.5, 10.0, 10.5, 11.0 }) Tree.CreateTimer(t).Timeout += () => samples.Add(Engine.GetFramesPerSecond());
            Tree.CreateTimer(11.2).Timeout += () =>
                GD.Print($"[perf] pieces={Session.View.Layer(KTable.Core.Model.Layers.Umpire).Count} fps min={samples.Min():0} avg={samples.Average():0}" +
                    $" draw calls={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} objects={Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0}" +
                    $" primitives={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):0} vram={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0} MB static={Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576:0} MB nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}");
        }
        if (_args.TryGetValue("demo-fx", out var dfx))
        {
            Net.SharePack("iks.james-effects");
            var pieces = Session.Store.Pack("iks.james-effects")?.Pieces ?? new();
            var wanted = new[] { "Blaze Medium Flicker", "Fire w/ Smoke", "Camp Fire", "Bomb", "Gun Fire", "GroundFog", "Crashing Spray", "Magic Flame" };
            if (dfx != "true") { var fc = dfx.Split(','); wanted = wanted.Skip(int.Parse(fc[0])).Take(int.Parse(fc[1])).ToArray(); }
            var defs = wanted.Select(wn => pieces.FirstOrDefault(d => d.Name.StartsWith(wn, StringComparison.OrdinalIgnoreCase))?.Id).Where(id => id != null).ToList();
            var at = Coords.FromWorld(table.Rig.Target);
            Tree.CreateTimer(0.5).Timeout += () =>
            {
                for (int i = 0; i < defs.Count; i++)
                    Session.Do(new KTable.Core.Protocol.SpawnPiece { Layer = KTable.Core.Model.Layers.Umpire, Def = defs[i]!, Pos = at + new KTable.Core.Model.Vec2(-defs.Count * 1.4 + i * 2.8, 0),
                        RotDeg = 180 });
                table.Rig.Frame(Coords.ToWorld(at), defs.Count * 2.8f);
                GD.Print($"[fx] spawned {defs.Count} of {wanted.Length}");
            };
        }
        if (_args.TryGetValue("demo-pov-zoom", out var dpz))
        {
            Tree.CreateTimer(double.Parse(dpz, System.Globalization.CultureInfo.InvariantCulture)).Timeout += async () =>
            {
                var first = Session.View.Layer(KTable.Core.Model.Layers.Umpire).FirstOrDefault();
                if (first == null) { GD.Print("[povzoom] no piece"); return; }
                table.SetSelection(new[] { first.Id });
                table.SetTool(Tool.Sighting);
                await _main.ToSignal(Tree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                GD.Print($"[povzoom] framed: distance {table.Rig.Distance:0.0}, close-up {table.DraftCloseUp}");
                var mid = _main.GetViewport().GetVisibleRect().Size / 2;
                for (int i = 0; i < 3; i++)
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, AltPressed = true, Position = mid, GlobalPosition = mid });
                await _main.ToSignal(Tree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
                GD.Print($"[povzoom] after Alt+wheel: distance {table.Rig.Distance:0.0}, close-up {table.DraftCloseUp}");
                await _main.ToSignal(Tree.CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                GD.Print($"[povzoom] a second later: distance {table.Rig.Distance:0.0}, close-up {table.DraftCloseUp}");
            };
        }
    }

    void Setting(Table table)
    {
        if (_args.TryGetValue("demo-grid", out var dgr))
        {
            var gv = dgr.Split(',');
            Session.Do(new KTable.Core.Protocol.SetGrid { Grid = new KTable.Core.Model.GridDef { Kind = gv[0],
                Pitch = gv.Length > 1 ? double.Parse(gv[1], System.Globalization.CultureInfo.InvariantCulture) : 10 } });
        }
        if (_args.TryGetValue("demo-light", out var dli))
        {
            var lv = dli.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Session.Do(new KTable.Core.Protocol.SetLighting { Lighting = new KTable.Core.Model.Lighting { SunEnergy = lv[0], Ambient = lv[1], Warmth = lv[2], Shadows = lv.Length < 4 || lv[3] > 0 } });
        }
        if (_args.ContainsKey("demo-physics"))
        {
            Net.SharePack(FirmeEffects.PackId);
            Net.SharePack("user.physblocks");
            var at = Coords.FromWorld(table.Rig.Target);
            Tree.CreateTimer(1.0).Timeout += () => new TableDemo(table).Physics(at, FirmeEffects.PackId + ".earthwork-rampart", "user.physblocks.block", "user.physblocks.marker");
        }
        if (_args.TryGetValue("demo-wind", out var dwi))
        {
            var wv = dwi.Split(',');
            var inv2 = System.Globalization.CultureInfo.InvariantCulture;
            Session.Do(new KTable.Core.Protocol.SetWind { Wind = new KTable.Core.Model.Wind { From = double.Parse(wv[0], inv2), Strength = double.Parse(wv[1], inv2) } });
        }
        if (_args.TryGetValue("demo-firme", out var dfe))
        {
            Net.SharePack(FirmeEffects.PackId);
            var defs = Session.Store.Pack(FirmeEffects.PackId)?.Pieces.ToList() ?? new();
            if (dfe != "true") { var fc = dfe.Split(','); defs = defs.Skip(int.Parse(fc[0])).Take(int.Parse(fc[1])).ToList(); }
            double turn = double.Parse(_args.GetValueOrDefault("demo-firme-turn", "180"), System.Globalization.CultureInfo.InvariantCulture);
            var at = Coords.FromWorld(table.Rig.Target);
            Tree.CreateTimer(0.5).Timeout += () =>
            {
                double rowW = defs.Count <= 4 ? defs.Sum(d => d.Size.W + 3) : Math.Max(14, Math.Sqrt(defs.Sum(d => (d.Size.W + 3) * (d.Size.H + 3))) * 1.2), x = 0, z = 0, rowH = 0;
                var cmds = new List<KTable.Core.Protocol.Command>();
                foreach (var d in defs)
                {
                    double w = d.Size.W + 3, h = d.Size.H + 3;
                    if (x + w > rowW && x > 0)
                    {
                        x = 0;
                        z += rowH;
                        rowH = 0;
                    }
                    cmds.Add(new KTable.Core.Protocol.SpawnPiece { Layer = KTable.Core.Model.Layers.Umpire, Def = d.Id, Pos = at + new KTable.Core.Model.Vec2(x + w / 2 - rowW / 2, z + h / 2),
                        RotDeg = turn });
                    x += w;
                    rowH = Math.Max(rowH, h);
                }
                if (cmds.Count > 0) Session.Do(new KTable.Core.Protocol.Batch { Items = cmds });
                GD.Print($"[fx] laid out {defs.Count} of Firme's Effects");
            };
        }
    }

    void Turns(Table table)
    {
        if (_args.TryGetValue("demo-flip", out var dfl))
        {
            int idx = int.Parse(dfl);
            Tree.CreateTimer(1.0).Timeout += () =>
            {
                var pc = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[idx];
                Session.Do(new KTable.Core.Protocol.FlipPiece { Layer = KTable.Core.Model.Layers.Umpire, Id = pc.Id, FaceDown = true });
                table.SetSelection(new[] { pc.Id });
            };
        }
        if (_args.TryGetValue("demo-flip-at", out var dfa))
        {
            Tree.CreateTimer(double.Parse(dfa, System.Globalization.CultureInfo.InvariantCulture)).Timeout += () => table.FlipSelection();
        }
        if (_args.TryGetValue("demo-turns", out var dtn))
        {
            var at = double.Parse(_args.GetValueOrDefault("demo-turns-at", "1.5"), System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 1; i <= int.Parse(dtn); i++) Tree.CreateTimer(at + i).Timeout += () => Session.Do(new KTable.Core.Protocol.AdvanceClock());
        }
        if (_args.ContainsKey("demo-history"))
        {
            var p0 = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[3];
            Tree.CreateTimer(1.5).Timeout += () => Session.Do(new KTable.Core.Protocol.AdvanceClock());
            Tree.CreateTimer(2.0)
                .Timeout += () =>
                    Session.Do(new KTable.Core.Protocol.MovePiece { Layer = KTable.Core.Model.Layers.Umpire, Id = p0.Id, Pos = p0.Pos + new KTable.Core.Model.Vec2(12, 0), RotDeg = p0.RotDeg });
            Tree.CreateTimer(2.5).Timeout += () => Session.Do(new KTable.Core.Protocol.AdvanceClock());
            Tree.CreateTimer(3.5).Timeout += () => { Net.RequestTurn(0); GD.Print($"[history] turns={Session.Turns.Count} live pos={Session.Live.Find(KTable.Core.Model.Layers.Umpire, p0.Id)!.Pos.X:0.#}"); };
            Tree.CreateTimer(4.5).Timeout += () => GD.Print($"[history] reviewing={Session.Reviewing} turn={Session.ReviewInfo?.Turn} shown pos={Session.View.Find(KTable.Core.Model.Layers.Umpire, p0.Id)!.Pos.X:0.#} clock={Session.View.Clock.Turn}");
        }
        if (_args.TryGetValue("demo-ruler", out var dr))
        {
            var a = dr.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Tree.CreateTimer(1).Timeout += () => new TableDemo(table).Ruler(new KTable.Core.Model.Vec2(a[0], a[1]), new KTable.Core.Model.Vec2(a[2], a[3]));
        }
    }

    void Camera(Table table)
    {
        if (_args.ContainsKey("cam") || _args.ContainsKey("pitch"))
        {
            PlaceCamera(table);
            Tree.CreateTimer(3.0).Timeout += () => PlaceCamera(table);
        }
    }
}

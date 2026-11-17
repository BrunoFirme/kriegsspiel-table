using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class WalkDemos(Main main, Dictionary<string, string> args, Table table, Hud hud)
{
    const double SlowFrameMs = 20;
    const float WalkArrivedHeights = 0.12f, WalkLookDown = -0.08f, CircleTurn = 1.3f, CirclePace = 0.4f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    Session Session => main.Session;
    Net Net => main.Net;
    SceneTree Tree => main.GetTree();

    public void Run()
    {
        Session.Error += e => GD.Print($"[error] {e}");
        Staged();
        Watched();
        if (args.ContainsKey("frame-probe") || args.ContainsKey("walk-trace"))
        {
            table.Settings.RestWhenIdle = false;
            Graphics.ApplyFrames(table.Settings);
        }
        FrameProbe();
        Trace();
        if (args.ContainsKey("walk-tell")) Tell();
        if (args.TryGetValue("walk-turn", out var turn)) Turning(float.Parse(turn, Inv));
        if (!args.TryGetValue("walk", out var where) && !args.ContainsKey("walk-path")) return;
        Walk(where);
        RideProbe();
    }

    void Staged()
    {
        if (args.ContainsKey("ground-scan")) Tree.CreateTimer(3).Timeout += ScanGround;
        if (args.ContainsKey("validation-table")) new ValidationTable(Session, Net, Tree).Build();
        if (args.TryGetValue("add-pack", out var pack)) Tree.CreateTimer(1.5).Timeout += () => { if (!Session.View.Packs.Contains(pack)) Net.SharePack(pack); };
        if (args.TryGetValue("place", out var placed)) Tree.CreateTimer(2.5).Timeout += () => Place(placed);
        if (args.TryGetValue("carry", out var carry)) _ = PickUp(Numbers(carry));
        if (args.TryGetValue("carry-into", out var into)) _ = CarryInto(Numbers(into));
        if (args.TryGetValue("walk-carry", out var walkCarry)) _ = WalkCarry(double.Parse(walkCarry, Inv));
        if (args.TryGetValue("take-control", out var takeControl)) _ = TakeControlLater(double.Parse(takeControl, Inv));
        if (args.TryGetValue("scale-tavern", out var scaleTavern)) _ = ScaleLater(Numbers(scaleTavern), false);
        if (args.TryGetValue("scale-self", out var scaleSelf)) _ = ScaleLater(Numbers(scaleSelf), true);
        if (args.TryGetValue("scale-menu", out var scaleMenu)) _ = ScaleMenu(double.Parse(scaleMenu, Inv));
        if (args.TryGetValue("fly", out var fly)) _ = FlyLater(double.Parse(fly, Inv));
        if (args.TryGetValue("keys", out var keys)) Typed(keys);
        if (args.TryGetValue("tavern-state", out var tavernState))
            Tree.CreateTimer(4).Timeout += () =>
            {
                foreach (var inn in Session.View.Layer(Layers.Umpire).Where(p => p.Def.EndsWith(".tavern", System.StringComparison.Ordinal)).ToList())
                    Session.Do(new KTable.Core.Protocol.SetState { Layer = Layers.Umpire, Id = inn.Id, State = int.Parse(tavernState, Inv) });
            };
        if (args.TryGetValue("measure", out var measure)) Tree.CreateTimer(double.Parse(measure, Inv)).Timeout += () => (table.Rig.Pitch, table.Walk.Measuring) = (-0.3f, true);
        if (args.TryGetValue("ping", out var ping)) Tree.CreateTimer(double.Parse(ping, Inv)).Timeout += () => table.Walk.PingAimed();
        if (args.TryGetValue("bounds-demo", out var boundShots)) _ = new BoundDemo(main, table, hud, boundShots).Run();
        if (args.TryGetValue("walk-course", out var course)) _ = new WalkCourse(main, table, course, args.GetValueOrDefault("walk-course-only", "")).Run();
        if (args.TryGetValue("dice-tray", out var tray)) _ = DiceLater(double.Parse(tray, Inv), "Roll");
        if (args.TryGetValue("dice-throw", out var toss)) _ = DiceLater(double.Parse(toss, Inv), "Throw");
        if (args.TryGetValue("throw-roll", out var throwRoll))
            Tree.CreateTimer(double.Parse(throwRoll, Inv)).Timeout += () =>
            {
                var path = table.Walk.ThrowPath();
                GD.Print($"[throw-roll] path {path?.Count}");
                Session.Do(new KTable.Core.Protocol.RollDice { Sides = 6, Count = 2, Open = true, Thrown = path });
                GD.Print("[throw-roll] sent");
            };
        if (args.TryGetValue("players-fly", out var lets))
            Tree.CreateTimer(double.Parse(lets, Inv)).Timeout += () =>
            {
                foreach (var b in table.Avatars.Bodies)
                    Session.Do(new KTable.Core.Protocol.SetBodyRights { Layer = Layers.Umpire, Id = b.Piece, Rights = new BodyRights { Fly = true } });
            };
        if (args.TryGetValue("party-menu", out var partyMenu))
            Tree.CreateTimer(double.Parse(partyMenu, Inv)).Timeout += () =>
                hud.World?.Panel.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Icon == Ui.Dot)?.EmitSignal(BaseButton.SignalName.Pressed);
        if (args.TryGetValue("piece-menu", out var pieceMenu)) _ = PieceMenu(pieceMenu.Split(','));
        if (args.TryGetValue("table-menu", out var tableMenu)) _ = TableMenu(double.Parse(tableMenu, Inv));
        if (args.TryGetValue("place-players", out var placing))
        {
            var again = new Timer { WaitTime = 2, Autostart = true };
            again.Timeout += () => PlacePlayers(placing);
            main.AddChild(again);
        }
        if (args.ContainsKey("part-probe"))
            Tree.CreateTimer(8).Timeout += () =>
            {
                foreach (var n in table.Pieces.Nodes.Where(n => n.Def?.Id.EndsWith(".tavern") == true))
                    foreach (var p in n.Parts)
                        GD.Print($"[part] {p.Def?.Id} at {p.GlobalPosition} piece {n.GlobalPosition} scale {n.Piece.Scale}");
            };
        if (args.TryGetValue("use-at", out var useAt))
            Tree.CreateTimer(double.Parse(useAt, Inv)).Timeout += () =>
            {
                table.Walk.UseAimed();
                GD.Print($"[use] states {string.Join(", ", Session.View.Layer(Layers.Umpire).Where(p => p.Def.EndsWith(".tavern")).Select(p => p.State))}");
            };
        if (args.TryGetValue("walls", out var walls)) Tree.CreateTimer(2).Timeout += () => table.Cutaway.Set(System.Enum.Parse<WallView>(walls, true));
        if (args.TryGetValue("storey", out var storey)) Tree.CreateTimer(3).Timeout += () => table.Cutaway.Step(int.Parse(storey, Inv) - 1);
        if (args.ContainsKey("no-suspension")) table.Suspension.Off = true;
        if (args.TryGetValue("go-to", out var goTo))
            Tree.CreateTimer(double.Parse(goTo, Inv)).Timeout += () => { if (table.Avatars.Named().FirstOrDefault() is { Peer: not 0 } who) table.FlyToFigure(who.Peer); };
        if (args.TryGetValue("walls-down", out var wd)) Tree.CreateTimer(double.Parse(wd, Inv)).Timeout += () => table.Cutaway.Set(WallView.Down);
        if (args.TryGetValue("lock-body", out var lb))
            Tree.CreateTimer(double.Parse(lb, Inv)).Timeout += () => OnBody(n => Session.Do(new KTable.Core.Protocol.SetLocked { Layer = Layers.Umpire, Id = n.Piece.Id, Value = true }));
        if (args.TryGetValue("menu-body", out var mb)) Tree.CreateTimer(double.Parse(mb, Inv)).Timeout += () => OnBody(n =>
        {
            table.SetSelection(new[] { n.Piece.Id });
            hud.Menus.Show(table.Rig.Cam.UnprojectPosition(n.GlobalPosition), n);
        });
        if (args.TryGetValue("turn-body", out var tb)) Tree.CreateTimer(double.Parse(tb, Inv)).Timeout += () => OnBody(n =>
            Session.Do(new KTable.Core.Protocol.MovePiece { Layer = Layers.Umpire, Id = n.Piece.Id, Pos = n.Piece.Pos, RotDeg = n.Piece.RotDeg + 90, Y = n.Piece.Y }));
    }

    void OnBody(System.Action<PieceNode> act)
    {
        var body = table.Avatars.Bodies.FirstOrDefault();
        GD.Print($"[bodies] {string.Join(", ", table.Avatars.Bodies.Select(b => $"{b.Name} in {b.Piece} (peer {b.Peer})"))}");
        if (body.Piece != null && table.Pieces.NodeOf(Layers.Umpire, body.Piece) is { } node) act(node);
    }

    void ScanGround()
    {
        foreach (var sheet in Session.View.Sheets)
            for (double z = sheet.Pos.Z - sheet.Size.H / 2 + 10; z < sheet.Pos.Z + sheet.Size.H / 2; z += 10)
            {
                double left = sheet.Pos.X - sheet.Size.W / 2 + 10;
                var heights = Enumerable.Range(0, (int)(sheet.Size.W / 10) - 1).Select(i => Session.Ground.At(new Vec2(left + i * 10, z)).ToString("0.0", Inv));
                GD.Print($"[ground] z {z:0} from x {left:0}: " + string.Join(" ", heights));
            }
    }

    void Place(string placed)
    {
        foreach (var one in placed.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(',')))
            Session.Do(new KTable.Core.Protocol.SpawnPiece
            {
                Layer = Layers.Umpire, Def = one[0], Pos = new Vec2(double.Parse(one[1], Inv), double.Parse(one[2], Inv)),
                RotDeg = one.Length > 3 ? double.Parse(one[3], Inv) : 0,
            });
    }

    void Watched()
    {
        if (!args.ContainsKey("watch-figures")) return;
        var watch = new Timer { WaitTime = 0.5, Autostart = true };
        watch.Timeout += () =>
        {
            if (Session.View.Layer(Layers.Umpire).LastOrDefault() is not { } last || table.Pieces.NodeOf(Layers.Umpire, last.Id) is not { } node) return;
            if (AvatarSolids.Where(node) is not { } space) return;
            foreach (var peer in table.Avatars.Peers().ToList())
                if (table.Avatars.At(peer) is { } at) GD.Print($"[figure] {peer} on the last piece at {space.AffineInverse() * at}, piece at {space.Origin}");
        };
        table.AddChild(watch);
    }

    void FrameProbe()
    {
        if (!args.TryGetValue("frame-probe", out var fp) || Numbers(fp) is not [var fromS, var forS]) return;
        var times = new List<double>();
        var physics = new List<double>();
        ulong began = Time.GetTicksMsec();
        bool told = false;
        GD.Print($"[frames] physics {ProjectSettings.GetSetting("physics/3d/physics_engine")}");
        Tree.CreateTimer(1).Timeout += () => Input.ParseInputEvent(new InputEventMouseMotion { Position = main.GetViewport().GetVisibleRect().Size / 2 });
        var stamps = new FrameStamps();
        var gcWas = System.GC.GetTotalPauseDuration();
        table.AddChild(stamps);
        Tree.ProcessFrame += () =>
        {
            double at = (Time.GetTicksMsec() - began) / 1000.0;
            if (at < fromS) return;
            if (at < fromS + forS)
            {
                times.Add(table.GetProcessDeltaTime() * 1000);
                if (stamps.LastPhysicsMs > 0) physics.Add(stamps.LastPhysicsMs);
                var gcNow = System.GC.GetTotalPauseDuration();
                if (times[^1] > SlowFrameMs)
                    GD.Print($"[frames] {Time.GetTicksMsec()} slow frame {times[^1]:0.0} ms: process before {stamps.LastProcessMs:0.0}, physics {stamps.LastPhysicsMs:0.0}, "
                        + $"then before draw {stamps.LastBeforeDrawMs:0.0}, draw {stamps.LastDrawMs:0.0}, present {stamps.LastPresentMs:0.0}, gc pause {(gcNow - gcWas).TotalMilliseconds:0.0}");
                gcWas = gcNow;
                return;
            }
            if (told || times.Count == 0) return;
            told = true;
            times.Sort();
            double mean = times.Average();
            GD.Print($"[frames] {times.Count} frames, mean {mean:0.00} ms ({1000 / mean:0} fps), median {times[times.Count / 2]:0.00}, "
                + $"99th {times[(int)(times.Count * 0.99)]:0.00}, worst {times[^1]:0.00} ms");
            physics.Sort();
            if (physics.Count > 0) GD.Print($"[frames] physics in {physics.Count} frames: mean {physics.Average():0.00} ms, 99th {physics[(int)(physics.Count * 0.99)]:0.00}, worst {physics[^1]:0.00} ms");
        };
    }

    void Walk(string? where)
    {
        double wait = double.Parse(args.GetValueOrDefault("walk-wait", "4"), Inv);
        var stops = new Queue<Vec2>(Points(args.GetValueOrDefault("walk-path", "")));
        float[] look = args.GetValueOrDefault("walk-look", "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, Inv)).ToArray();
        float yaw = look.Length > 0 ? Mathf.DegToRad(look[0]) : table.Rig.Yaw, pitch = look.Length > 1 ? Mathf.DegToRad(look[1]) : WalkLookDown;
        bool run = args.ContainsKey("walk-run"), held = look.Length > 0;
        double[] circle = Numbers(args.GetValueOrDefault("walk-circle", ""));
        double riseFor = double.Parse(args.GetValueOrDefault("walk-rise", "0"), Inv);
        ulong born = Time.GetTicksMsec();
        double Since() => (Time.GetTicksMsec() - born) / 1000.0;
        if (circle.Length == 2)
            table.Walk.Steer = () => stops.Count > 0 || Since() < circle[0] || Since() > circle[1]
                ? Steered(stops, ref yaw, pitch, run, held)
                : new Steering(table.Rig.Yaw + CircleTurn / Engine.PhysicsTicksPerSecond, pitch, new Vector2(0, CirclePace), false);
        else if (stops.Count > 0 || held)
            table.Walk.Steer = () => Steered(stops, ref yaw, pitch, run, held) is { } st ? st with { Rise = Since() > wait && Since() < wait + riseFor ? 1 : 0 } : null;
        Tree.CreateTimer(wait).Timeout += () => Enter(where);
    }

    void Turning(float degreesPerSecond)
    {
        Tree.ProcessFrame += () =>
        {
            if (GodotObject.IsInstanceValid(table) && table.Walk.Walking) table.Rig.Orbit(new Vector2(Mathf.DegToRad(degreesPerSecond) * (float)main.GetProcessDeltaTime() / 0.005f, 0));
        };
    }

    void Trace()
    {
        if (!args.ContainsKey("walk-trace")) return;
        float metre = 0;
        var vp = main.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        Tree.ProcessFrame += () =>
        {
            if (!GodotObject.IsInstanceValid(table) || !table.Walk.Walking) return;
            metre = metre > 0 ? metre : table.Walk.Height / AvatarPose.PersonMetres;
            var f = table.Walk.Feet;
            GD.Print($"[trace] {Time.GetTicksMsec()} {f.X:0.00000} {f.Z:0.00000} feet {f.Y / metre:0.000} m eye {table.Rig.Cam.GlobalPosition.Y / metre:0.000} m cam {WorldShift.ToTable(table.Rig.Cam.GlobalPosition).X:0.00000} {WorldShift.ToTable(table.Rig.Cam.GlobalPosition).Z:0.00000} yaw {Mathf.RadToDeg(table.Rig.Yaw):0} dt {main.GetProcessDeltaTime() * 1000:0.0} gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp):0.0} cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu():0.0} proc {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0} calls {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} objs {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame)} grounded {table.Walk.Grounded} lag {table.Walk.EyeLag / metre:0.000} shown {table.Walk.Pose.At.Y / metre:0.000} follows {table.Walk.Following} rides {table.Walk.Riding}");
        };
    }

    void Enter(string? where)
    {
        var start = Points(where ?? "").Select(p => (Vector3?)Coords.ToWorld(p)).FirstOrDefault();
        if (!table.Walk.Walking) table.Walk.Enter(start, asked: true);
        if (args.ContainsKey("walk-third")) table.Walk.ThirdPerson = true;
        if (args.TryGetValue("body", out var body) && AvatarBody.Clean(body) is { } spec) table.Walk.Body = spec;
        if (args.TryGetValue("body-file", out var bodyFile))
        {
            var kept = BodyFiles.Keep(bodyFile, Session.Store.Root, out var files, out var why);
            GD.Print($"[body] {bodyFile} -> {kept ?? why}");
            if (kept != null) Net.UploadThen(files, () => { table.Walk.Body = kept; GD.Print($"[body] wearing {kept}"); });
        }
        if (args.TryGetValue("walk-shrink", out var shrink))
            for (int i = 0; i < int.Parse(shrink, Inv); i++) Input.ParseInputEvent(new InputEventKey { Keycode = Key.Minus, Pressed = true });
        if (args.ContainsKey("walk-fly")) table.Walk.Fly(true);
        else if (start is { } there) table.Walk.MoveTo(there with { Y = float.Parse(args.GetValueOrDefault("walk-y", "0"), Inv) });
        GD.Print($"[walk] {Time.GetTicksMsec()} entered at {table.Walk.Feet}, height {table.Walk.Height}");
    }

    void RideProbe()
    {
        if (!args.ContainsKey("ride-probe")) return;
        Vector3? seen = null;
        float? rideWas = null;
        float bump = 0, travel = 0, worst = 0, lowest = float.MaxValue, highest = float.MinValue;
        int frames = 0;
        Tree.ProcessFrame += () =>
        {
            if (!GodotObject.IsInstanceValid(table) || table.Walk.Ride is not { } ride || AvatarSolids.Where(ride) is not { } space)
            {
                seen = null;
                return;
            }
            var here = space.AffineInverse() * table.Rig.Cam.GlobalPosition;
            if (rideWas is { } before)
            {
                bump = Mathf.Max(bump, Mathf.Abs(space.Origin.Y - before));
                travel += Mathf.Abs(space.Origin.Y - before);
            }
            rideWas = space.Origin.Y;
            if (seen is { } was) worst = Mathf.Max(worst, (here - was).Length());
            lowest = Mathf.Min(lowest, here.Y);
            highest = Mathf.Max(highest, here.Y);
            seen = here;
            if (++frames % 120 != 0) return;
            GD.Print($"[probe] eye on the ride {here}, largest step in a frame {worst:0.0000}, eye height on the ride {lowest:0.000} to {highest:0.000}, ride at {space.Origin}");
            GD.Print($"[bumps] ride's largest rise or fall in a frame {bump:0.0000}, up and down in all {travel:0.000}");
            (bump, travel, worst, lowest, highest) = (0, 0, 0, float.MaxValue, float.MinValue);
        };
    }

    void Tell()
    {
        var tell = new Timer { WaitTime = 1, Autostart = true };
        tell.Timeout += () =>
        {
            if (table.Walk.Riding) GD.Print($"[ride] own place on the ride {table.Walk.RideAt}");
            foreach (var b in table.Avatars.Bodies)
                GD.Print($"[bodies] {b.Name} in {b.Piece}: {(table.Pieces.NodeOf(Layers.Umpire, b.Piece) is {} bn ? $"{bn.Def?.Id} at {bn.TablePosition()} shown {bn.Visible} web {bn.Piece.WebMesh}" : "no node")}");
            foreach (var near in Session.View.Layer(Layers.Umpire).Where(q => System.Math.Abs(q.Pos.X - 286) < 3 && System.Math.Abs(q.Pos.Z + 75) < 3))
                GD.Print($"[near] {near.Id} {near.Def} at {near.Pos.X:0.000},{near.Pos.Z:0.000} y {near.Y:0.0000} scale {near.Scale} stack {near.Stack}");
            if (Session.View.Layer(Layers.Umpire).LastOrDefault() is { } lp && table.Pieces.NodeOf(Layers.Umpire, lp.Id) is { } ln)
                GD.Print($"[piece] last piece base {ln.GlobalPosition}, ground there {Session.Ground.At(lp.Pos):0.0000}, scale {lp.Scale}, below {ln.Def?.Below}, "
                    + $"thickness {ln.Thickness}, local {ln.Model?.Position}, aabb {ln.Model?.Mesh?.GetAabb()}, def {lp.Def}");
            if (table.Walk.Walking)
                GD.Print($"[walk] feet {table.Walk.Feet} grounded {table.Walk.Grounded} riding {table.Walk.Riding} solids {table.Walk.Solids} figures {table.Avatars.Count} body {table.Walk.BodyPiece} at {(table.Walk.BodyPiece is { } bp ? Session.View.Find(Layers.Umpire, bp)?.Pos.ToString() : "")}");
        };
        table.AddChild(tell);
    }

    async System.Threading.Tasks.Task PickUp(double[] a)
    {
        await main.ToSignal(Tree.CreateTimer(a[0]), SceneTreeTimer.SignalName.Timeout);
        if (Session.View.Layer(Layers.Umpire).LastOrDefault() is not { } piece) return;
        table.SetSelection(new[] { piece.Id });
        var from = piece.Pos;
        var to = new Vec2(a[1], a[2]);
        table.Hand.Start(from);
        GD.Print($"[carry] picked up {piece.Def} at {from.X:0.0},{from.Z:0.0}");
        double seconds = a.Length > 4 ? a[4] : 3;
        ulong began = Time.GetTicksMsec();
        bool turned = false;
        for (double k = 0; k < 1; k = (Time.GetTicksMsec() - began) / 1000.0 / seconds)
        {
            table.Hand.Move(new Vec2(from.X + (to.X - from.X) * k, from.Z + (to.Z - from.Z) * k));
            if (k >= 0.5 && !turned && a.Length > 3 && a[3] != 0) table.Hand.Rotate(a[3]);
            turned |= k >= 0.5;
            await main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
        table.Hand.Drop();
        await main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        var rests = Session.View.Find(Layers.Umpire, piece.Id);
        GD.Print($"[carry] set down, rests at {rests?.Pos.X:0.000},{rests?.Pos.Z:0.000} turned {rests?.RotDeg:0.0} y {rests?.Y:0.0000} ground {(rests != null ? Session.Ground.At(rests.Pos) : 0):0.0000} base {(rests != null && table.Pieces.NodeOf(Layers.Umpire, rests.Id) is { } rn ? rn.GlobalPosition.Y : 0):0.0000} tilt {(rests != null && table.Pieces.NodeOf(Layers.Umpire, rests.Id) is { } tn ? Mathf.RadToDeg(tn.GlobalBasis.Y.AngleTo(Vector3.Up)) : 0):0.0}");
    }

    async System.Threading.Tasks.Task ScaleLater(double[] a, bool self)
    {
        await main.ToSignal(Tree.CreateTimer(a[0]), SceneTreeTimer.SignalName.Timeout);
        var piece = self ? (table.Walk.BodyPiece is { } own ? Session.View.Find(Layers.Umpire, own) : null)
            : Session.View.Layer(Layers.Umpire).FirstOrDefault(p => p.Def.EndsWith(".tavern", System.StringComparison.Ordinal));
        if (piece == null) return;
        GD.Print($"[scale] {piece.Def} from {piece.Scale} by {a[1]}");
        Session.Do(new KTable.Core.Protocol.SetScale { Layer = Layers.Umpire, Id = piece.Id, Scale = piece.Scale * a[1] });
    }

    void PlacePlayers(string where)
    {
        if (!GodotObject.IsInstanceValid(table)) return;
        foreach (var name in table.Waiting())
        {
            if (where.Contains(',') && Numbers(where) is [var x, var z]) Net.Place(name, new Vector3((float)x, (float)Session.Ground.At(new Vec2(x, z)), (float)z), table.Rig.Yaw, null);
            else if (Session.View.Layer(Layers.Umpire).FirstOrDefault(p => p.Def.EndsWith("." + where, System.StringComparison.Ordinal) && table.Avatars.OwnerOf(p.Id) == null) is { } piece
                && table.NodeOf(Layers.Umpire, piece.Id) is { } node)
                Net.Place(name, node.TablePosition(), AvatarBody.YawOf(piece.RotDeg), piece.Id);
            GD.Print($"[place] {name} at {where}");
        }
    }

    void Typed(string script)
    {
        var tell = new Timer { WaitTime = 0.25, Autostart = true };
        tell.Timeout += () => { if (table.Walk.Walking) GD.Print($"[keyed] {Time.GetTicksMsec()} feet {table.Walk.Feet} speed {table.Walk.Speed:0.0} pitch {Mathf.RadToDeg(table.Rig.Pitch):0.0} flying {table.Walk.Flying}"); };
        main.AddChild(tell);
        foreach (var step in script.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var (at, what) = (double.Parse(step[..step.IndexOf(':')], Inv), step[(step.IndexOf(':') + 1)..]);
            var key = System.Enum.Parse<Key>(what.TrimEnd('+', '-'), ignoreCase: true);
            bool down = what.EndsWith('+');
            Tree.CreateTimer(at).Timeout += () =>
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down });
                GD.Print($"[key] {key} {(down ? "down" : "up")} feet {table.Walk.Feet} flying {table.Walk.Flying} speed {table.Walk.Speed:0.0}");
            };
        }
    }

    async System.Threading.Tasks.Task FlyLater(double after)
    {
        await main.ToSignal(Tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        if (!table.Walk.Walking) return;
        float yaw = table.Rig.Yaw;
        ulong born = Time.GetTicksMsec();
        double Since() => (Time.GetTicksMsec() - born) / 1000.0;
        table.Walk.Fly(true);
        table.Walk.Steer = () =>
        {
            yaw += 0.004f;
            if (!table.Walk.Flying) table.Walk.Fly(true);
            return new Steering(yaw, 0.12f, new Vector2(0, 1), Since() > 1.5 && Since() % 2 < 1, Since() < 1.5 ? 1 : 0);
        };
        var tell = new Timer { WaitTime = 0.25, Autostart = true };
        tell.Timeout += () => GD.Print($"[fly] {Since():0.00} speed {table.Walk.Speed:0.0} boost {table.Walk.Boosting} flash {table.Walk.Flash:0.00} feet {table.Walk.Feet.Y:0.000}");
        main.AddChild(tell);
    }

    async System.Threading.Tasks.Task DiceLater(double after, string action)
    {
        await main.ToSignal(Tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        Press("", hud.World?.Panel.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.TooltipText.StartsWith("Dice", System.StringComparison.Ordinal)));
        await main.ToSignal(Tree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        var shown = hud.GetTree().Root.FindChildren("*", "PopupPanel", true, false).OfType<PopupPanel>().FirstOrDefault(p => p.Visible);
        if (shown == null) return;
        Button? In(System.Func<Button, bool> which) => shown.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(which);
        foreach (var die in new[] { "d6", "d6", "d20" }) Press("", In(b => b.Text.EndsWith(die, System.StringComparison.Ordinal) && b.Text.Length <= die.Length + 1));
        await main.ToSignal(Tree.CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
        Press("", In(b => b.Text == action));
    }

    static void Press(string _, Button? button)
    {
        if (button == null) return;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        GD.Print($"[menu] pressed {button.Text}{button.TooltipText}");
    }

    async System.Threading.Tasks.Task PieceMenu(string[] at)
    {
        await main.ToSignal(Tree.CreateTimer(double.Parse(at[0], Inv)), SceneTreeTimer.SignalName.Timeout);
        if (Session.View.Layer(Layers.Umpire).FirstOrDefault(p => p.Def.EndsWith("." + at[1], System.StringComparison.Ordinal) && table.Avatars.OwnerOf(p.Id) == null) is not { } piece) return;
        table.SetSelection(new List<string> { piece.Id });
        hud.Menus.Show(hud.GetViewport().GetVisibleRect().Size / 2, table.SelectedNodes.FirstOrDefault());
        GD.Print($"[piece-menu] {piece.Id} players {string.Join(", ", table.Players())}");
        await main.ToSignal(Tree.CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        Press("Put ");
        await main.ToSignal(Tree.CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[piece-menu] bodies {string.Join(", ", table.Avatars.Bodies.Select(b => b.Name + " in " + b.Piece))}");
    }

    void Press(string starting)
    {
        var row = hud.GetTree().Root.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith(starting, System.StringComparison.Ordinal));
        for (Node? n = row; n != null; n = n.GetParent())
            if (n is BaseButton b)
            {
                b.EmitSignal(BaseButton.SignalName.Pressed);
                GD.Print($"[menu] pressed {row!.Text}");
                break;
            }
    }

    async System.Threading.Tasks.Task TableMenu(double after)
    {
        await main.ToSignal(Tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        table.SetSelection(new List<string>());
        hud.Menus.Show(hud.GetViewport().GetVisibleRect().Size / 2, null);
        GD.Print($"[table-menu] waiting {string.Join(", ", table.Waiting())}");
        await main.ToSignal(Tree.CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        Press("Place ");
    }

    async System.Threading.Tasks.Task ScaleMenu(double after)
    {
        await main.ToSignal(Tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        if (Session.View.Layer(Layers.Umpire).FirstOrDefault(p => p.Def.EndsWith(".tavern", System.StringComparison.Ordinal)) is not { } piece) return;
        double usual = piece.Scale;
        Session.Do(new KTable.Core.Protocol.SetScales { Layer = Layers.Umpire, Id = piece.Id, Scales = [usual * 0.5, usual * 2] });
        Session.Do(new KTable.Core.Protocol.SetScale { Layer = Layers.Umpire, Id = piece.Id, Scale = usual * 1.6 });
        await main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        table.SetSelection(new List<string> { piece.Id });
        hud.Menus.Show(hud.GetViewport().GetVisibleRect().Size / 2, table.SelectedNodes.FirstOrDefault());
        await main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        var head = hud.GetTree().Root.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text == "Scales");
        for (Node? n = head; n != null; n = n.GetParent())
            if (n is BaseButton b) { b.EmitSignal(BaseButton.SignalName.Pressed); break; }
        GD.Print($"[scale-menu] {piece.Id} scale {Session.View.Find(Layers.Umpire, piece.Id)?.Scale} kept {string.Join(", ", Session.View.Find(Layers.Umpire, piece.Id)?.Scales ?? [])} head {head != null}");
    }

    async System.Threading.Tasks.Task TakeControlLater(double after)
    {
        await main.ToSignal(Tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        if (Session.View.Layer(Layers.Umpire).FirstOrDefault(p => p.Def.EndsWith(".townsman", System.StringComparison.Ordinal)) is not { } piece
            || table.Pieces.NodeOf(Layers.Umpire, piece.Id) is not { } node) return;
        table.Walk.TakeControl(piece.Id, node.TablePosition());
        await main.ToSignal(Tree.CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[take-control] {piece.Id}: walking {table.Walk.Walking}, body {table.Walk.BodyPiece}, feet {table.Walk.Feet}, in control {table.Walk.InControl}");
    }

    async System.Threading.Tasks.Task WalkCarry(double after)
    {
        await main.ToSignal(Tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        if (Session.View.Layer(Layers.Umpire).FirstOrDefault(p => p.Def.EndsWith(".townsman", System.StringComparison.Ordinal) && p.Id != table.Walk.BodyPiece) is not { } piece) return;
        table.SetSelection(new[] { piece.Id });
        table.Hand.Start(piece.Pos);
        var centre = table.Rig.Cam.GetViewport().GetVisibleRect().Size / 2;
        ulong began = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - began < 1500)
        {
            table.Hand.Point(centre);
            await main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
        var aim = table.Rig.GroundAt(centre);
        table.Hand.Drop();
        await main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        var rests = Session.View.Find(Layers.Umpire, piece.Id);
        GD.Print($"[walk-carry] aimed at ground {aim}, set down at {rests?.Pos.X:0.0000},{rests?.Pos.Z:0.0000} y {rests?.Y:0.0000} ground {Session.Ground.At(rests?.Pos ?? piece.Pos):0.0000} feet {table.Walk.Feet} shift {WorldShift.By} node {(table.Pieces.NodeOf(Layers.Umpire, piece.Id) is { } pn ? $"{pn.TablePosition()} shown {pn.IsVisibleInTree()} model {pn.Model?.IsVisibleInTree()}" : "none")}");
    }

    async System.Threading.Tasks.Task CarryInto(double[] a)
    {
        await main.ToSignal(Tree.CreateTimer(a[0]), SceneTreeTimer.SignalName.Timeout);
        if (Session.View.Layer(Layers.Umpire).LastOrDefault() is not { } piece || table.Pieces.NodeOf(Layers.Umpire, piece.Id) is not { } node) return;
        var target = Coords.ToWorld(new Vec2(a[1], a[2]));
        target.Y = (float)Session.Ground.At(new Vec2(a[1], a[2]));
        table.Rig.Frame(target, (float)(a.Length > 3 ? a[3] : 0.6));
        await main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        table.SetSelection(new[] { piece.Id });
        table.Hand.Start(piece.Pos);
        var cam = table.Rig.Cam;
        Vector2 from = cam.UnprojectPosition(node.GlobalPosition), to = cam.GetViewport().GetVisibleRect().Size / 2;
        ulong began = Time.GetTicksMsec();
        for (double k = 0; k < 1; k = (Time.GetTicksMsec() - began) / 1500.0)
        {
            table.Hand.Point(from.Lerp(to, (float)k));
            await main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
        await main.ToSignal(Tree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        table.Hand.Drop();
        await main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        var rests = Session.View.Find(Layers.Umpire, piece.Id);
        GD.Print($"[carry-into] {piece.Def} rests at {rests?.Pos.X:0.000},{rests?.Pos.Z:0.000} y {rests?.Y:0.0000}, ground {Session.Ground.At(rests?.Pos ?? piece.Pos):0.0000}, walls {table.Cutaway.Mode} storey {table.Cutaway.Storey + 1} of {table.Cutaway.Storeys}, floors {string.Join(" ", table.Pieces.Nodes.Where(n => n.HasFloors && n.Model?.Mesh != null).Select(n => string.Join("/", MeshTops.Floors(n.Model!.Mesh!).Select(f => f.ToString("0.00", Inv))) + $" aabb {n.Model!.Mesh!.GetAabb()}"))}");
    }

    Steering? Steered(Queue<Vec2> stops, ref float yaw, float pitch, bool run, bool held)
    {
        var feet = table.Walk.Feet;
        while (stops.Count > 0)
        {
            var to = Coords.ToWorld(stops.Peek()) - feet;
            to.Y = 0;
            if (to.Length() < table.Walk.Height * WalkArrivedHeights)
            {
                stops.Dequeue();
                continue;
            }
            yaw = Mathf.Atan2(to.X, to.Z);
            return new Steering(yaw, pitch, new Vector2(0, 1), run);
        }
        return held ? new Steering(yaw, pitch, Vector2.Zero, false) : null;
    }

    static double[] Numbers(string text) => text.Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, Inv)).ToArray();

    static IEnumerable<Vec2> Points(string text) =>
        text.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(','))
            .Where(p => p.Length >= 2).Select(p => new Vec2(double.Parse(p[0], Inv), double.Parse(p[1], Inv)));
}

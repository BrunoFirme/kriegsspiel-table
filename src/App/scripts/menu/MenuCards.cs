using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Client.Effects;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class MenuCards : HudPart
{
    readonly ToolStrip _strip;
    readonly Func<PiecesPanel> _pieces;
    public MenuCards(HudContext ctx, ToolStrip strip, Func<PiecesPanel> pieces) : base(ctx) { _strip = strip; _pieces = pieces; }

    static string Short(PieceNode n)
    {
        var t = (n.Piece.Label is { Length: > 0 } l ? l : n.Def?.Name ?? "this piece").Trim();
        return t.Length > 22 ? t[..21] + "…" : t;
    }

    public void Show(Vector2 at, PieceNode? hit)
    {
        var menu = new MenuCard();
        var nodes = T.SelectedNodes.ToList();
        if (hit != null && nodes.Count > 0) PieceMenu(menu, hit, nodes);
        else TableMenu(menu, at);
        menu.Show(Ctx.Root, at);
    }

    void PieceMenu(MenuCard menu, PieceNode hit, List<PieceNode> nodes)
    {
        var one = nodes.Count == 1 ? nodes[0] : null;
        var head = one != null ? (one.Piece.Label is { Length: > 0 } lb ? lb : one.Def?.Name ?? one.Piece.Def) : $"{nodes.Count} pieces";
        if (one != null && NoteCard.Is(one.Def)) head = NoteFace.Told(head);
        var size = one != null ? Fmt.Footprint(one.Footprint.X, one.Footprint.Y, S.View) : "";
        menu.Head(Tally(hit, nodes) is { Length: > 0 } sum ? $"{head} · {sum}" : head, size, "How big the piece is on the table: table units, then the scenario's measure");
        menu.Separator();
        ActionRows(menu, nodes);
        if (one?.Def is { } def) StateAndCounterRows(menu, one, def);
        if (one is { IsDie: false, IsEffect: false }) ScriptRows(menu, one);
        SelectionRows(menu, hit, nodes);
        menu.Separator();
        bool allLocked = nodes.All(n => n.Piece.Locked);
        menu.Check("Locked", allLocked, () => S.Do(nodes.Count == 1
            ? new SetLocked { Layer = nodes[0].Layer, Id = nodes[0].Piece.Id, Value = !allLocked }
            : new Batch { Items = nodes.Select(n => (Command)new SetLocked { Layer = n.Layer, Id = n.Piece.Id, Value = !allLocked }).ToList() }));
        menu.Page("Options", () => OptionRows(menu, hit, nodes, one));
        menu.Separator();
        menu.Item("Cut", T.CutSelection, "Ctrl+X");
        menu.Item("Copy", T.CopySelection, "Ctrl+C");
        menu.Item("Delete", T.DeleteSelection, "Del");
        if (one is { IsDie: false, IsEffect: false }) WindowRows(menu, one);
        else if (one is { IsEffect: true }) EffectWindowRows(menu, one);
        else if (one == null) LineRow(menu, nodes);
    }

    void EffectWindowRows(MenuCard menu, PieceNode one)
    {
        menu.Separator();
        if (S.CanTouch(one.Layer, one.Piece)) menu.Item("Properties…", () => PropertiesDialog.Open(Ctx, one));
        StudioRow(menu, one);
    }

    void ActionRows(MenuCard menu, List<PieceNode> nodes)
    {
        if (nodes.Any(Table.IsDie)) menu.Item("Roll", () => T.RollDiceAtHand(), "R");
        menu.Item("Rotate 180°", () => T.RotateSmooth(180));
        if (nodes.Any(n => !n.IsDie && !n.IsEffect && n.Def?.Kind != "model")) menu.Item("Flip", T.FlipSelection, Cfg.Keys.Label("flip"));
        var effects = nodes.Where(n => n.IsEffect).ToList();
        if (effects.Count > 0) menu.Item("Play", () => effects.ForEach(n => S.Do(new PlayEffect { Layer = n.Layer, Id = n.Piece.Id })));
        if (S.IsUmpire && nodes[0].Layer == Layers.Umpire) menu.Item("POV capture", () => T.SetTool(Tool.Sighting), "V");
    }

    void ScriptRows(MenuCard menu, PieceNode one)
    {
        if (one.Piece.MenuItems.Count == 0 || !one.ScriptDrawingShows || !S.CanTouch(one.Layer, one.Piece)) return;
        menu.Separator("Script");
        for (int i = 0; i < one.Piece.MenuItems.Count; i++)
        {
            var mi = i;
            menu.Item(one.Piece.MenuItems[i].Label, () => S.Do(new PickMenuItem { Layer = one.Layer, Id = one.Piece.Id, Index = mi + 1 }));
        }
    }

    void WindowRows(MenuCard menu, PieceNode one)
    {
        menu.Separator();
        if (BookWindow.Is(one.Def)) menu.Item("Read…", () => BookWindow.Open(Ctx.Root, S, one));
        menu.Item("Properties…", () => PropertiesDialog.Open(Ctx, one));
        StudioRow(menu, one);
        if (S.IsUmpire) menu.Item("Scripting…", () => Ctx.ShowScriptWindow(one.Piece.Id), Cfg.Keys.Label("scriptWindow"));
        if (S.IsUmpire && AvatarBody.Takeable(one.Def) && !one.HasFloors && T.Avatars.OwnerOf(one.Piece.Id) is not { Peer: > 0 })
        {
            menu.Item(T.Walk.BodyPiece == one.Piece.Id ? "Release control" : "Take control", () => TakeControl(one));
            foreach (var name in T.Players()) menu.Item($"Assign to {name}", () => N.Place(name, one.TablePosition(), AvatarBody.YawOf(one.Piece.RotDeg), one.Piece.Id));
        }
        if (!Cfg.MakerTools) return;
        var pid = one.Piece.Id;
        var pguid = one.Piece.Guid;
        menu.Item("Copy ID", () => { AppClipboard.SetText(pid); Say($"Copied {pid}"); }, pid);
        if (pguid != null && pguid != pid) menu.Item("Copy GUID", () => { AppClipboard.SetText(pguid); Say($"Copied {pguid}"); }, pguid);
    }

    void PeopleRows(MenuCard menu, PieceNode one)
    {
        if (S.IsUmpire && AvatarBody.Takeable(one.Def) && (one.LifeSize || T.Avatars.OwnerOf(one.Piece.Id) != null)) BodyRows(menu, one);
        if (S.IsUmpire && T.Avatars.OwnerOf(one.Piece.Id) is { Peer: > 0 } walker) PlayerRows(menu, walker.Name, walker.Peer, walker.Name);
        if (S.IsLead && one.Layer == Layers.Umpire) DirectorRows(menu, one.Piece.Id);
    }

    void DirectorRows(MenuCard menu, string piece)
    {
        var umpires = S.Peers.Where(p => KTable.Core.Visibility.IsUmpire(p.Role)).Select(p => p.Name).ToList();
        List<string> Now() => S.View.Directors?.GetValueOrDefault(piece)?.ToList() ?? new();
        var listed = umpires.Union(Now(), StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (listed.Count == 0) return;
        menu.Separator("Directed by");
        foreach (string name in listed)
            menu.Toggle(name, Now().Contains(name, StringComparer.OrdinalIgnoreCase), _ =>
            {
                var next = Now();
                if (next.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) == 0) next.Add(name);
                S.Do(new SetDirectors { Id = piece, Names = next });
            });
    }

    void BodyRows(MenuCard menu, PieceNode one)
    {
        menu.Separator("Body rights");
        BodyRights Now() => S.View.Find(one.Layer, one.Piece.Id)?.Rights?.Clone() ?? new BodyRights();
        void Row(string name, Func<BodyRights, bool> has, Action<BodyRights, bool> set) =>
            menu.Toggle(name, has(Now()), _ =>
            {
                var next = Now();
                set(next, !has(next));
                S.Do(new SetBodyRights { Layer = one.Layer, Id = one.Piece.Id, Rights = next });
            });
        Row("Fly", r => r.Fly, (r, on) => r.Fly = on);
        Row("Third person", r => r.View, (r, on) => r.View = on);
        Row("Change walk", r => r.Walk, (r, on) => r.Walk = on);
    }

    public void ShowPlayer(Vector2 at, string name, long peer)
    {
        if (T.Avatars.Bodies.FirstOrDefault(b => b.Name == name).Piece is { } id && T.Pieces.NodeOf(Layers.Umpire, id) is { } body)
        {
            T.SetSelection([id]);
            Show(at, body);
            return;
        }
        var menu = new MenuCard();
        menu.Head(T.Waiting().Contains(name) ? $"{name} · waits" : name);
        PlayerRows(menu, name, peer, "");
        menu.Show(Ctx.Root, at);
    }

    void PlayerRows(MenuCard menu, string name, long peer, string label)
    {
        menu.Separator(label);
        if (T.CanFlyTo(name)) menu.Item("Go to", () => T.FlyToPlayer(name));
        RightsRows.Add(menu, () => S.View, name, rights => S.Do(rights));
        menu.Item($"Put {name} out…", () => PeopleList.AskPutOut(HudNode, N, name, peer));
    }

    void ScaleRows(MenuCard menu, PieceNode one)
    {
        var p = one.Piece;
        double usual = one.LifeSize ? AvatarPose.UnitsPerMetre(S.View) : 1;
        static bool Same(double a, double b) => Math.Abs(a - b) <= b * 1e-4;
        string Name(double s) => Same(s, usual) ? (one.LifeSize ? "Life size" : "Default size") : $"{s / usual:0.##}× {(one.LifeSize ? "life size" : "default")}";
        menu.Fold("Scales", "Sizes saved for this piece", false, _ => { }, () =>
        {
            foreach (var s in (p.Scales ?? []).Prepend(usual).Distinct())
                menu.Check(Name(s), Same(p.Scale, s), () => S.Do(new SetScale { Layer = one.Layer, Id = p.Id, Scale = s }));
            if (!Same(p.Scale, usual) && p.Scales?.Any(s => Same(s, p.Scale)) != true)
                menu.Check($"Save {Name(p.Scale)}", false, () => S.Do(new SetScales { Layer = one.Layer, Id = p.Id, Scales = (p.Scales ?? []).Append(p.Scale).ToList() }));
            if (p.Scales is { Count: > 0 }) menu.Check("Clear saved sizes", false, () => S.Do(new SetScales { Layer = one.Layer, Id = p.Id, Scales = new() }));
        });
    }

    void TakeControl(PieceNode one)
    {
        if (T.Walk.BodyPiece == one.Piece.Id) T.Walk.LeaveControl();
        else T.Walk.TakeControl(one.Piece.Id, one.TablePosition());
    }

    void StudioRow(MenuCard menu, PieceNode one)
    {
        if (one.Def is { } kind && StudioInPlace.PackToEdit(S, kind) is { } pack) menu.Item("Edit in studio…", () => StudioInPlace.Open(Ctx, pack, kind.Id, one.Layer, one.Piece.Id));
    }

    void LineRow(MenuCard menu, List<PieceNode> nodes)
    {
        if (nodes.Select(n => n.Def is { } k ? StudioInPlace.PackToEdit(S, k) : null).Distinct().ToList() is not [{ } pack]) return;
        var line = nodes.Select(n => new LinePiece(n.Layer, n.Piece.Id, n.Def!.Id, n.Piece.Label ?? "")).ToList();
        menu.Separator();
        menu.Item("Edit in studio…", () => StudioInPlace.Open(Ctx, pack, line));
    }

    static string Tally(PieceNode hit, List<PieceNode> nodes)
    {
        var shown = nodes.Where(n => n.IsDie).Select(n => PieceInstance.FacesOf(n.Def, n.Piece) is var f && f.Count > 0 ? f[Math.Clamp(n.Piece.ShownFace, 0, f.Count - 1)].Number : null).ToList();
        if (shown.Count > 1 && shown.All(v => v != null)) return $"{string.Join(" + ", shown)} = {shown.Sum()}";
        int groups = hit.Piece.WorthGroups;
        var counted = nodes.Where(n => groups == 0 || (n.Piece.WorthGroups & groups) != 0).ToList();
        return counted.Any(n => n.Piece.Worth != 0) ? $"worth {counted.Sum(n => n.Piece.Worth)}" : "";
    }

    void StateAndCounterRows(MenuCard menu, PieceNode one, PieceDef def)
    {
        if (Doors.Count(def, S.Def) is var doors && doors > 0) DoorRows(menu, one, def, doors);
        else if (def.States.Count > 1 && !BookWindow.Is(def))
        {
            menu.Separator("State");
            for (int i = 0; i < def.States.Count; i++)
            {
                var si = i;
                menu.Check(def.States[i].Name.Length > 0 ? def.States[i].Name : $"#{i + 1}", one.Piece.State == i,
                    () => S.Do(new SetState { Layer = one.Layer, Id = one.Piece.Id, State = si }), i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "");
            }
        }
        if (def.Counters.Count > 0)
        {
            menu.Separator();
            foreach (var cd in def.Counters)
            {
                var c = cd;
                menu.Stepper(c.Name, one.Piece.Counters.GetValueOrDefault(c.Name, c.Default), c.Min, c.Max,
                    value => S.Do(new SetCounter { Layer = one.Layer, Id = one.Piece.Id, Name = c.Name, Value = value }));
            }
        }
        var markers = def.Markers().Where(a => S.Store.Def(a.Piece) != null).ToList();
        if (markers.Count == 0) return;
        menu.Separator();
        foreach (var at in markers)
        {
            var a = at;
            menu.Item($"Place {S.Store.Def(a.Piece)!.Name}", () => S.Do(new SpawnPiece { Layer = one.Layer, Def = a.Piece, Pos = MarkerPlace.World(one.Piece.Pos, one.Piece.RotDeg, a.Offset), RotDeg = one.Piece.RotDeg }));
        }
    }

    void DoorRows(MenuCard menu, PieceNode one, PieceDef def, int doors)
    {
        menu.Separator("Doors");
        for (int k = 0; k < doors; k++)
        {
            if (Doors.Toggled(def, one.Piece.State, k, S.Def) is not { } to) continue;
            menu.Check(Doors.Name(def, k, S.Def), Doors.Open(def, one.Piece.State, k, S.Def), () => S.Do(new SetState { Layer = one.Layer, Id = one.Piece.Id, State = to }));
        }
    }

    void HolderRows(MenuCard menu, PieceNode hit, Action lead)
    {
        if (!hit.Piece.HoldsPieces(hit.Def) && hit.Piece.Stowed is not { Count: > 0 }) return;
        int held = hit.Piece.Stowed?.Count ?? 0;
        lead();
        menu.Item(held > 0 ? $"Open {Short(hit)} ({held})…" : $"Open {Short(hit)}…", () => HolderWindow.Open(Ctx.Root, S, T, hit));
        if (held > 1 && hit.Piece.TakeOrder is TakeOrders.Last or TakeOrders.Random)
            menu.Item(hit.Piece.TakeOrder == TakeOrders.Random ? "Draw at random" : "Take out top", () =>
            {
                if (hit.Piece.NextOut(Random.Shared) is { } next) S.Do(new Unstow { Layer = hit.Layer, Holder = hit.Piece.Id, Id = next.Id });
            });
    }

    void ReadyRows(MenuCard menu, PieceNode hit, Action lead)
    {
        if (!S.IsUmpire || !S.CanTouch(hit.Layer, hit.Piece)) return;
        var key = Cfg.Keys.Label("ready");
        var view = S.View;
        if (hit.Piece.ReadyNow is { } r)
        {
            var state = ReadyPiece.Of(hit.Piece, id => view.Find(hit.Layer, id));
            var held = hit.Piece.Stowed?.FirstOrDefault(p => p.Id == r.Id) ?? view.Find(hit.Layer, r.Id);
            var name = held == null ? "the ready piece" : HolderWindow.NameOf(held, S);
            if (state == Readiness.In) { lead(); menu.Item($"Release {name}", () => S.Do(new ReleaseReady { Layer = hit.Layer, Holder = hit.Piece.Id }), key); }
            else if (state == Readiness.Out) { lead(); menu.Item($"Recall {name}", () => S.Do(new RecallReady { Layer = hit.Layer, Holder = hit.Piece.Id }), key); }
        }
        else if (KTable.Client.ReadyKey.HolderOf(view, hit.Layer, hit.Piece.Id) is { } owner)
        {
            lead();
            menu.Item($"Recall into {HolderWindow.NameOf(owner, S)}", () => S.Do(new RecallReady { Layer = hit.Layer, Holder = owner.Id }), key);
        }
    }

    void SelectionRows(MenuCard menu, PieceNode hit, List<PieceNode> nodes)
    {
        bool led = false;
        void Lead()
        {
            if (led) return;
            led = true;
            menu.Separator();
        }
        if (nodes.Count > 1)
        {
            Lead();
            menu.Item("Form line, 1 rank", () => T.FormRows(1), "Ctrl+1");
            menu.Item("Form line, 2 ranks", () => T.FormRows(2), "Ctrl+2");
            menu.Item($"Attach to {Short(hit)}", () =>
            {
                var kids = nodes.Where(n => n != hit && n.Layer == hit.Layer).ToList();
                if (kids.Count == 0) return;
                S.Do(new Batch { Items = kids.Select(n => (Command)new Attach { Layer = hit.Layer, Parent = hit.Piece.Id, Child = n.Piece.Id }).ToList() });
            });
            if (hit.Piece.HoldsPieces(hit.Def))
                menu.Item($"Put into {Short(hit)}", () =>
                {
                    var others = nodes.Where(n => n != hit && n.Layer == hit.Layer).ToList();
                    if (others.Count == 0) return;
                    var stows = others.Select(n => (Command)new Stow { Layer = hit.Layer, Holder = hit.Piece.Id, Id = n.Piece.Id }).ToList();
                    S.Do(new Batch { Items = KTable.Client.ReadyKey.Stowing(S.View, hit.Layer, hit.Piece, stows) });
                });
        }
        var alone = nodes.Count == 1 ? nodes[0] : null;
        if (alone != null && S.View.Layer(alone.Layer).FirstOrDefault(p => (p.Attached ?? []).Contains(alone.Piece.Id)) is { } carrier && carrier.ReadyNow?.Id != alone.Piece.Id)
        {
            var from = carrier.Label is { Length: > 0 } named ? named : S.Def(carrier.Def)?.Name ?? carrier.Def;
            Lead();
            menu.Item($"Detach from {from}", () => S.Do(new Detach { Layer = alone.Layer, Child = alone.Piece.Id }));
        }
        HolderRows(menu, hit, Lead);
        ReadyRows(menu, hit, Lead);
    }

    void OptionRows(MenuCard menu, PieceNode hit, List<PieceNode> nodes, PieceNode? one)
    {
        menu.Ticks(OptionTicks(nodes));
        var flyers = nodes.Where(n => !n.IsDie && !n.IsEffect).ToList();
        if (flyers.Count > 0 && flyers.All(n => n.Piece.Flies)) menu.Add(AltitudeRows(flyers, flyers.Contains(hit) ? hit : flyers[0]));
        var alone = nodes.Count == 1 ? nodes[0] : null;
        if (alone is { IsDie: false, IsEffect: false } && S.IsUmpire && S.CanTouch(alone.Layer, alone.Piece)) ScaleRows(menu, alone);
        var smoky = nodes.Where(n => (n.IsEffect || n.Def?.Effect != null) && EffectKinds.HasSmokeSetting(n.EffectSpec)).ToList();
        if (smoky.Count > 0) menu.Add(SmokeRows(smoky, smoky.Contains(hit) ? hit : smoky[0]));
        if (nodes.All(n => (n.IsEffect || n.Def?.Effect != null) && EffectKinds.HasIntensity(n.EffectSpec))) menu.Add(IntensityRows(nodes, nodes.Contains(hit) ? hit : nodes[0]));
        var notes = nodes.Where(n => NoteCard.Is(n.Def)).ToList();
        if (notes.Count > 0) menu.Add(WritingRow(notes, notes.Contains(hit) ? hit : notes[0], menu.Pop));
        var tintable = nodes.Where(n => n.TakesTint() && S.CanTouch(n.Layer, n.Piece)).ToList();
        if (tintable.Count > 0) menu.Add(TintRows(tintable, tintable.Contains(hit) ? hit : tintable[0], menu.Pop));
        if (one is { IsDie: false, IsEffect: false }) PeopleRows(menu, one);
    }

    List<MenuTick> OptionTicks(List<PieceNode> nodes)
    {
        var ticks = new List<MenuTick>();
        void Tick(string text, bool on, Action action, string tip) => ticks.Add(new MenuTick(text, on, action, tip));
        void Option(List<PieceNode> some, string text, string option, bool on, bool setTo, string tip)
        {
            if (some.Count > 0) Tick(text, on, () => SetOption(some, option, setTo), tip);
        }
        var flyers = nodes.Where(n => !n.IsDie && !n.IsEffect).ToList();
        bool allFly = flyers.All(n => n.Piece.Flies);
        Option(flyers, "Flying", PieceOptions.Flies, allFly, !allFly, "Drawn at its altitude over the ground");
        if (GridSnap.On(S.View.Grid))
        {
            bool allSnap = nodes.All(n => GridSnap.Snaps(S.View.Grid, n.Piece.Snaps));
            Option(nodes, "Snap to grid", PieceOptions.Snaps, allSnap, !allSnap, "Set down on the scenario's grid");
        }
        bool allCollide = nodes.All(n => !n.Piece.NoCollision);
        Option(nodes, "Collision", PieceOptions.NoCollision, allCollide, allCollide, "Shoves and is shoved by other pieces; off, it passes through them");
        var holders = nodes.Where(n => !n.IsDie && !n.IsEffect && S.CanTouch(n.Layer, n.Piece)).ToList();
        bool allHold = holders.All(n => n.Piece.HoldsPieces(n.Def));
        Option(holders, "Container", PieceOptions.Holds, allHold, !allHold, "Pieces held over it a moment go inside, as into a bag");
        bool allSticky = nodes.All(n => !n.Piece.NotSticky);
        Option(nodes, "Carry stack", PieceOptions.NotSticky, allSticky, allSticky, "Lifting it lifts what rests on it");
        var boxable = nodes.Where(n => !n.Piece.Locked).ToList();
        bool allBoxed = boxable.All(n => !n.Piece.NotBoxSelected);
        Option(boxable, "Box-select", PieceOptions.NotBoxSelected, allBoxed, allBoxed, "Taken by a selection box dragged over it");
        bool allMeasured = nodes.All(n => n.Piece.MeasuresMoves);
        Option(nodes, "Move distance", PieceOptions.MeasuresMoves, allMeasured, !allMeasured, "Shows how far it is carried while held");
        var casters = nodes.Where(n => !n.IsEffect).ToList();
        bool allShadowed = casters.All(n => !n.Piece.NoShadow);
        Option(casters, "Shadow", PieceOptions.NoShadow, allShadowed, allShadowed, "Its shadow on the table, and the mark under it in flight");
        var dice = nodes.Where(n => n.IsDie).ToList();
        bool allReal = dice.All(n => n.Piece.PhysicalRoll);
        Option(dice, "Physical roll", PieceOptions.PhysicalRoll, allReal, !allReal, "A throw counts the face it lands on");
        var effects = nodes.Where(n => n.IsEffect).ToList();
        bool allRunning = effects.All(n => !n.Piece.EffectOff);
        Option(effects, "Running", PieceOptions.EffectOff, allRunning, allRunning, "The effect plays on the table");
        var pieces = _pieces();
        var kinds = nodes.Select(n => n.Piece.Def).Distinct().ToList();
        bool allHearted = kinds.All(pieces.IsFavourite);
        Tick("Favourite", allHearted, () =>
        {
            foreach (var k in kinds) if (pieces.IsFavourite(k) == allHearted) pieces.ToggleFavourite(k, rebuild: false);
            pieces.RebuildList();
        }, "Favourites come first in the Pieces panel");
        return ticks;
    }

    void SetOption(List<PieceNode> nodes, string name, bool value)
    {
        var cmds = nodes.Select(n => (Command)new SetPieceOption { Layer = n.Layer, Id = n.Piece.Id, Name = name, Value = value }).ToList();
        S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
    }

    void SheetRows(MenuCard menu, Vector2 at)
    {
        if (!S.IsUmpire || T.Rig.GroundAt(at) is not { } w) return;
        var here = T.Sheets.AllAt(Coords.FromWorld(w));
        if (here.Count == 0 || here[0].Def.Locked) return;
        var sheet = here[0].Def;
        menu.Head(SheetAsk.Named(sheet));
        menu.Fact(Fmt.Footprint(sheet.Size.W, sheet.Size.H, S.View), "How big the sheet lies on the table: table units, then the scenario's measure");
        menu.Separator();
        if (T.SelectedSheet != sheet.Id) menu.Item("Select", () => T.SelectSheet(sheet.Id), "Alt+click");
        menu.Check("Locked", sheet.Locked, () => S.Do(new LockSheet { Id = sheet.Id, Locked = !sheet.Locked }));
        menu.Item("Hide for me", () => T.ShowSheet(sheet.Id, false));
        if (S.View.Sheets.Count > 1)
        {
            menu.Item("Bring to front", () => T.Sheets.Restack(sheet.Id, 0));
            menu.Item("Send to back", () => T.Sheets.Restack(sheet.Id, int.MaxValue));
        }
        menu.Item("Frame", () => T.FrameSheet(sheet));
        menu.Item("Remove…", () => SheetAsk.Remove(HudNode, T, S, sheet));
        foreach (var under in here.Skip(1).Select(n => n.Def).Where(d => !d.Locked))
            menu.Item($"Select {SheetAsk.Named(under)} (underneath)", () => T.SelectSheet(under.Id));
        menu.Separator("Table");
    }

    bool NameRows(MenuCard menu, Vector2 at)
    {
        if (T.Rig.GroundAt(at) is not { } w || T.NamePick.Under(Coords.FromWorld(w)) is not { Text: { } text } name) return false;
        T.PickName(name.Id);
        menu.Head(text);
        menu.Separator();
        menu.Item("Edit", () => T.NamePick.Edit(name), "Enter");
        menu.Item("Remove", () => T.NamePick.Remove(), "Del");
        menu.Separator("Table");
        return true;
    }

    void TableMenu(MenuCard menu, Vector2 at)
    {
        if (S.IsUmpire && T.Waiting() is { Count: > 0 } waiting && T.StandAt(at) is { } stand)
        {
            foreach (var name in waiting) menu.Item($"Place {name} here", () => N.Place(name, stand, T.Rig.Yaw, null));
            menu.Separator();
        }
        if (!NameRows(menu, at)) SheetRows(menu, at);
        int clipped = T.Clipped;
        if (clipped > 0)
            menu.Item($"Paste {clipped} piece{(clipped > 1 ? "s" : "")} here", () => { if (T.Rig.GroundAt(at) is { } w) T.Paste(Coords.FromWorld(w)); }, "Ctrl+V");
        menu.Item("Select all", () => T.SelectAllEditable(), "Ctrl+A");
        menu.Separator();
        menu.Item("Ruler", () => T.SetTool(Tool.Ruler), "M");
        menu.Item("Capture a region", () => T.SetTool(Tool.Capture), "Ctrl+Shift+C");
        menu.Check("3D table", !T.Rig.TopDown, () => { T.Rig.ToggleTopDown(); _strip.SyncViewButton(); }, Cfg.Keys.Label("map2d"));
        menu.Item("Frame the map", T.FrameAll, "Home");
        if (T.Cam.KeepsViews)
        {
            menu.Separator("Views");
            menu.Add(ViewSlots(menu.Pop));
        }
        if (S.IsUmpire && S.View.MenuItems.Count > 0)
        {
            menu.Separator("Script");
            for (int i = 0; i < S.View.MenuItems.Count; i++)
            {
                var mi = i;
                menu.Item(S.View.MenuItems[i].Label, () => S.Do(new PickGlobalMenuItem { Index = mi + 1, Pos = T.Rig.GroundAt(at) is { } w ? Coords.FromWorld(w) : null }));
            }
        }
    }

    const int Swatch = 20, CheckColumn = 18, SwatchGap = 3, RowGap = 2, SlotGap = 3;

    Control ViewSlots(PopupPanel pop)
    {
        var views = T.Cam.Views;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(SlotGap));
        for (int n = 1; n <= CameraControl.ViewSlots; n++)
        {
            var slot = n;
            bool saved = views.ContainsKey(slot);
            var b = new Button
            {
                Text = slot.ToString(), CustomMinimumSize = Ui.Px(Swatch, Swatch), FocusMode = Control.FocusModeEnum.None,
                TooltipText = saved ? $"Go to view {slot}   (Shift+{slot})\nRight-click forgets it" : $"Keep this view as {slot}   (Ctrl+{slot})",
            };
            if (!saved) Ui.MakeQuiet(b);
            b.Pressed += () =>
            {
                pop.Hide();
                if (saved) T.Cam.GoToView(slot);
                else T.Cam.SaveView(slot);
            };
            if (saved) b.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) { pop.Hide(); T.Cam.ForgetView(slot); } };
            row.AddChild(b);
        }
        return row;
    }

    static readonly (string Name, int LinesToARule, string Tip)[] Writings =
    {
        ("Auto", 0, "As large as the note's lines allow"), ("Large", 1, "A line to a rule"), ("Small", 2, "Two lines to a rule"),
    };

    Control WritingRow(List<PieceNode> notes, PieceNode hit, PopupPanel pop)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(SwatchGap));
        row.AddChild(new Control { CustomMinimumSize = Ui.Px(CheckColumn, 0) });
        row.AddChild(new Label { Text = "Writing", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        foreach (var (name, linesToARule, tip) in Writings)
        {
            var b = new Button { Text = name, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
            if (hit.Piece.LinesToARule != linesToARule) Ui.MakeQuiet(b);
            b.Pressed += () =>
            {
                pop.Hide();
                var cmds = notes.Select(n => (Command)new SetNoteWriting { Layer = n.Layer, Id = n.Piece.Id, LinesToARule = linesToARule }).ToList();
                S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
            };
            row.AddChild(b);
        }
        return row;
    }

    Control AltitudeRows(List<PieceNode> nodes, PieceNode hit)
    {
        void Climb(double value)
        {
            var cmds = nodes.Select(n => (Command)new SetAltitude { Layer = n.Layer, Id = n.Piece.Id, Value = value }).ToList();
            S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
        }
        var (slider, shown) = AltitudeSlider.Parts(hit.Piece.FlightAltitude, Climb);
        return LabelledSlider("Altitude", slider, shown);
    }

    Control LabelledSlider(string name, HSlider slider, Control shown)
    {
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Px(RowGap));
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Px(SwatchGap));
        head.AddChild(new Control { CustomMinimumSize = Ui.Px(CheckColumn, 0) });
        head.AddChild(new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        head.AddChild(shown);
        rows.AddChild(head);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", Ui.Px(SwatchGap));
        line.AddChild(new Control { CustomMinimumSize = Ui.Px(CheckColumn, 0) });
        line.AddChild(slider);
        rows.AddChild(line);
        return rows;
    }

    Control SmokeRows(List<PieceNode> nodes, PieceNode hit)
    {
        void Thicken(double value)
        {
            var cmds = nodes.Select(n => (Command)new SetSmoke { Layer = n.Layer, Id = n.Piece.Id, Value = value == n.EffectSpec.Density ? null : value }).ToList();
            S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
        }
        bool dust = nodes.All(n => n.EffectSpec.Kind == "dust");
        var (slider, shown) = SmokeSlider.Parts(hit.PlacedEffect.Density, Thicken, whileDragging: false, dust ? SmokeSlider.DustTip : SmokeSlider.Tip);
        return LabelledSlider(dust ? "Dust" : "Smoke", slider, shown);
    }

    Control IntensityRows(List<PieceNode> nodes, PieceNode hit)
    {
        void Press(double value)
        {
            var cmds = nodes.Select(n => (Command)new SetIntensity { Layer = n.Layer, Id = n.Piece.Id, Value = value == EffectIntensity.AsMade ? null : value }).ToList();
            S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
        }
        var (slider, shown) = IntensitySlider.Parts(hit.Piece.Intensity ?? EffectIntensity.AsMade, Press);
        return LabelledSlider("Intensity", slider, shown);
    }

    Control TintRows(List<PieceNode> nodes, PieceNode hit, PopupPanel pop)
    {
        void Tint(string? value)
        {
            var cmds = nodes.Select(n => (Command)new SetTint { Layer = n.Layer, Id = n.Piece.Id, Value = value }).ToList();
            S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
        }
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Px(RowGap));
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Px(SwatchGap));
        head.AddChild(new Control { CustomMinimumSize = Ui.Px(CheckColumn, 0) });
        head.AddChild(new Label { Text = "Tint", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        if (nodes.Any(n => n.Piece.TintOverride != null))
        {
            var reset = new Button { Text = "Reset", TooltipText = "Back to the tint the piece was made with", FocusMode = Control.FocusModeEnum.None };
            Ui.MakeQuiet(reset);
            reset.Pressed += () => { pop.Hide(); Tint(null); };
            head.AddChild(reset);
        }
        rows.AddChild(head);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", Ui.Px(SwatchGap));
        line.AddChild(new Control { CustomMinimumSize = Ui.Px(CheckColumn, 0) });
        var swatches = new HBoxContainer();
        swatches.AddThemeConstantOverride("separation", Ui.Px(SwatchGap));
        var now = hit.TintShown();
        foreach (var (name, hex) in ColorChoices.All)
        {
            var colour = Coords.ParseColor(hex, Colors.White);
            var b = Ui.Swatch(colour, new Vector2(Swatch, Swatch), name, colour.IsEqualApprox(now));
            b.Pressed += () => { pop.Hide(); Tint(hex); };
            swatches.AddChild(b);
        }
        var more = new Button { Text = "+", CustomMinimumSize = Ui.Px(Swatch, Swatch), TooltipText = "Any colour", FocusMode = Control.FocusModeEnum.None };
        more.Pressed += () =>
        {
            var at = pop.Position;
            pop.Hide();
            PickTint(now, at, Tint);
        };
        swatches.AddChild(more);
        line.AddChild(Ui.Well(swatches));
        rows.AddChild(line);
        return rows;
    }

    void PickTint(Color start, Vector2I at, Action<string?> tint)
    {
        var pop = new PopupPanel();
        var picker = new ColorPicker
        {
            Color = start, EditAlpha = false, PresetsVisible = false, SamplerVisible = false, ColorModesVisible = false,
            SlidersVisible = true, HexVisible = true, CanAddSwatches = false,
        };
        pop.AddChild(picker);
        pop.PopupHide += () =>
        {
            pop.QueueFree();
            if (picker.Color != start) tint("#" + picker.Color.ToHtml(false));
        };
        Ctx.Root.AddChild(pop);
        pop.Popup(new Rect2I(at, Vector2I.Zero));
    }
}

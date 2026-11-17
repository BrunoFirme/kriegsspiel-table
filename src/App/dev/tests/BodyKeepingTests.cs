using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class BodyKeepingTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task A_body_made_for_a_walker_is_no_step_of_the_hosts_and_a_loaded_save_leaves_everyone_without_a_body(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 1, packs: ["test"]);
        var server = h.Net.Server ?? throw new TestFailure("the rig hosts a table");
        var feet = new Vector3(4, 0, 4);
        var shared = new List<int>();
        h.Net.BodiesReceived += bodies => shared.Add(bodies.Count);

        h.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(3, 0) });
        await r.Frames(2);
        h.S.UndoLast();
        await r.Frames(2);
        Check.That(h.S.MyUndoable.Count == 0 && h.S.MyRedoable.Count == 1, $"a move taken back waits to be redone: {h.S.MyUndoable.Count} undo, {h.S.MyRedoable.Count} redo");

        h.Net.AskBody(AvatarBody.OfPiece("x"), feet, 0, Vector2.Zero);
        await r.Frames(2);
        Check.Equal(1, server.Bodies.Count, "the host wears one body");
        string body = server.Bodies.Keys.Single();
        Check.That(body != "p1" && h.S.View.Find(U, body) != null, "it is a new piece on the host's table");
        Check.That(h.S.MyUndoable.Count == 0 && h.S.MyRedoable.Count == 1, $"making it is no step of the host's and the redo is still there: {h.S.MyUndoable.Count} undo, {h.S.MyRedoable.Count} redo");

        shared.Clear();
        h.Net.LoadOffline(server.State.DeepClone(), "Loaded");
        await r.Frames(3);
        Check.Equal(0, server.Bodies.Count, "after a save is loaded nobody wears a body");
        Check.That(shared.Count > 0 && shared.All(n => n == 0), $"and everyone is told at once: {string.Join(", ", shared)}");
        Check.That(server.State.Find(U, body) != null, "the piece that was the body is on the loaded table");

        h.Net.AskBody(AvatarBody.OfTaking(body), feet, 0, Vector2.Zero);
        Check.That(server.Bodies.ContainsKey(body), "it can be taken over like any piece");
        h.Net.AskBody(AvatarBody.OfTaking("p1"), feet, 0, Vector2.Zero);
        await r.Frames(2);
        Check.That(server.Bodies.Count == 1 && server.Bodies.ContainsKey("p1"), "the host moves on to another piece");
        Check.That(server.State.Find(U, body) != null, "and the loaded table's piece is not taken off as a body made for them would be");

        server.Bodies.Clear();
        h.Net.AskBody(AvatarBody.OfPiece("x"), feet, 0, Vector2.Zero);
        await r.Frames(2);
        string made = server.Bodies.Keys.Single();
        h.Net.AskBody(AvatarBody.OfTaking("p1"), feet, 0, Vector2.Zero);
        await r.Frames(2);
        Check.That(made != "p1" && server.State.Find(U, made) == null && h.S.View.Find(U, made) == null, "a body made since is still taken off when let go");
    }
}

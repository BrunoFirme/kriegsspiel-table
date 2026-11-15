using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ValidationTable(Session session, Net net, SceneTree tree)
{
    const string UnitPack = "tts.rvb-newcomer-red", UnitPrefix = "rvb-newcomer.", DicePack = "std.dice";
    const string Tavern = AvatarBody.StandardPack + ".tavern", Townsman = AvatarBody.StandardPack + ".townsman", Oak = AvatarBody.StandardPack + ".oak";
    const double Middle = 286, TavernZ = -72, BlueFront = -66.5, RedFront = -77.5;
    const double OakAside = 0.25, OakAhead = 0.2;
    const double Front = 1.9, Rank = 1.6, Horse = 1.6, Gun = 3.4, Townsfolk = 0.04, DoorGap = 0.46;
    const int Lines = 3, PerLine = 22, HorsePerWing = 8, Guns = 6;
    const double PacksAfter = 1.5, LayAfter = 3.5;
    static readonly string[] Horses = ["cuirassier", "dragoon", "hussar", "lancer"];
    static readonly string[] Command = ["army", "corps", "division", "division", "brigade", "brigade", "brigade", "brigade"];
    static readonly string[] Dice = ["d4", "d6", "d6", "d8", "d12", "d20"];

    public void Build()
    {
        tree.CreateTimer(PacksAfter).Timeout += () =>
        {
            foreach (var pack in new[] { AvatarBody.StandardPack, DicePack }.Where(p => !session.View.Packs.Contains(p))) net.SharePack(pack);
        };
        tree.CreateTimer(LayAfter).Timeout += Lay;
    }

    void Lay()
    {
        if (session.View.Layer(Layers.Umpire).Any(p => p.Def == Tavern)) return;
        var items = new List<Command>();
        if (session.View.Packs.Contains(UnitPack) && session.Store.Pack(UnitPack) is { } units)
        {
            var defs = units.Pieces.Select(d => d.Id).ToList();
            Army(items, defs, "blue", BlueFront, 1, 0);
            Army(items, defs, "red", RedFront, -1, 180);
            Stack(items, defs);
        }
        Put(items, Tavern, Middle, TavernZ, 0);
        Put(items, Oak, Middle + OakAside, TavernZ - OakAhead, 0);
        for (int i = 0; i < 4; i++) Put(items, Townsman, Middle - 1.5 * Townsfolk + i * Townsfolk, TavernZ - DoorGap, 0);
        for (int i = 0; i < Dice.Length; i++) Put(items, $"{DicePack}.{Dice[i]}", Middle - 10 + i * 0.9, BlueFront + 4.5, 0);
        session.Do(new Batch { Items = items });
        GD.Print($"[validation] laid {items.Count} pieces");
    }

    void Army(List<Command> items, List<string> defs, string side, double front, int rear, double rot)
    {
        string? Def(string kind) => defs.FirstOrDefault(d => d.StartsWith($"{UnitPrefix}{side}-{kind}-", System.StringComparison.Ordinal));
        double left = Middle - (PerLine - 1) * Front / 2;
        for (int line = 0; line < Lines; line++)
            for (int i = 0; i < PerLine; i++) Put(items, Def("infantry"), left + i * Front, front + rear * line * Rank, rot);
        for (int i = 0; i < HorsePerWing; i++)
        {
            var kind = Def(Horses[i % Horses.Length]);
            double z = front + rear * (i / 4) * Horse;
            Put(items, kind, left - Front - (i % 4) * Horse, z, rot);
            Put(items, kind, left + PerLine * Front + (i % 4) * Horse, z, rot);
        }
        for (int i = 0; i < Guns; i++) Put(items, Def("artillery"), Middle - (Guns - 1) * Gun / 2 + i * Gun, front - rear * Rank, rot);
        for (int i = 0; i < Command.Length; i++) Put(items, Def(Command[i]), Middle - (Command.Length - 1) + i * 2, front + rear * (Lines + 1) * Rank, rot);
    }

    void Stack(List<Command> items, List<string> defs)
    {
        string? Square(string side) => defs.FirstOrDefault(d => d.StartsWith($"{UnitPrefix}{side}-square-", System.StringComparison.Ordinal));
        for (int i = 0; i < 6; i++)
        {
            Put(items, Square("blue"), Middle + 6 + i * 0.9, BlueFront + 4.5, 0);
            Put(items, Square("red"), Middle + 6 + i * 0.9, BlueFront + 5.5, 0);
        }
    }

    static void Put(List<Command> items, string? def, double x, double z, double rot)
    {
        if (def != null) items.Add(new SpawnPiece { Layer = Layers.Umpire, Def = def, Pos = new Vec2(x, z), RotDeg = rot });
    }
}

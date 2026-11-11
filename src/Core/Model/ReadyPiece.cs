using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public enum Readiness { None, In, Out, Gone }

public sealed class ReadyPiece
{
    public const double Beside = 2;

    public string Id { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Vec2? Offset { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Turn { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool WasOut { get; set; }

    public ReadyPiece Clone() => (ReadyPiece)MemberwiseClone();

    public ReadyPiece Renamed(string id) => new() { Id = id, Offset = Offset, Turn = Turn, WasOut = WasOut };

    public static Readiness Of(PieceInstance holder, Func<string, PieceInstance?> onTable) => Of(holder, holder.ReadyNow, onTable);

    public static Readiness Of(PieceInstance holder, ReadyPiece? ready, Func<string, PieceInstance?> onTable) =>
        ready is not { } r ? Readiness.None
        : holder.Stowed?.Any(s => s.Id == r.Id) == true ? Readiness.In
        : onTable(r.Id) != null ? Readiness.Out
        : Readiness.Gone;

    public static Dictionary<string, string> OutHolders(IEnumerable<PieceInstance> pieces)
    {
        var holderOf = new Dictionary<string, string>();
        foreach (var h in pieces)
            if (h.ReadyNow is { } r && h.Stowed?.Any(s => s.Id == r.Id) != true) holderOf.TryAdd(r.Id, h.Id);
        return holderOf;
    }

    public static IEnumerable<ReadyPiece> All(PieceInstance holder) =>
        (holder.Ready is { } r ? [r] : Array.Empty<ReadyPiece>()).Concat(holder.ReadyIn?.Values ?? Enumerable.Empty<ReadyPiece>());

    public static void Rename(PieceInstance holder, Func<ReadyPiece, ReadyPiece?> map)
    {
        holder.Ready = holder.Ready is { } r ? map(r) : null;
        if (holder.ReadyIn == null) return;
        var kept = holder.ReadyIn.Select(e => (e.Key, Ready: map(e.Value))).Where(e => e.Ready != null).ToDictionary(e => e.Key, e => e.Ready!);
        holder.ReadyIn = kept.Count == 0 ? null : new(kept);
    }

    public static void KeepNow(PieceInstance holder, ReadyPiece now)
    {
        if (holder.ReadyIn?.ContainsKey(holder.State) == true) holder.ReadyIn[holder.State] = now;
        else holder.Ready = now;
    }

    public static Vec2 Turned(Vec2 v, double deg)
    {
        double rad = -deg * Math.PI / 180.0, c = Math.Cos(rad), s = Math.Sin(rad);
        return new Vec2(v.X * c - v.Z * s, v.X * s + v.Z * c);
    }

    public (Vec2 Pos, double RotDeg) OutBeside(PieceInstance holder) =>
        (holder.Pos + Turned(Offset ?? new Vec2(Beside, 0), holder.RotDeg), holder.RotDeg + Turn);
}

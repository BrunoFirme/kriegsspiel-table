using System;
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

    public ReadyPiece Clone() => (ReadyPiece)MemberwiseClone();

    public static Readiness Of(PieceInstance holder, Func<string, PieceInstance?> onTable) =>
        holder.Ready is not { } r ? Readiness.None
        : holder.Stowed?.Any(s => s.Id == r.Id) == true ? Readiness.In
        : onTable(r.Id) != null ? Readiness.Out
        : Readiness.Gone;

    public static Vec2 Turned(Vec2 v, double deg)
    {
        double rad = -deg * Math.PI / 180.0, c = Math.Cos(rad), s = Math.Sin(rad);
        return new Vec2(v.X * c - v.Z * s, v.X * s + v.Z * c);
    }

    public (Vec2 Pos, double RotDeg) OutBeside(PieceInstance holder) =>
        (holder.Pos + Turned(Offset ?? new Vec2(Beside, 0), holder.RotDeg), holder.RotDeg + Turn);
}

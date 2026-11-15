using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public readonly record struct BodyOf(string Piece, long Peer, string Name, int Slot);

static class AvatarBody
{
    const string PiecePrefix = "piece:", ModelPrefix = "model:", TakePrefix = "take:";
    const char Between = '|';
    public const int Longest = 300;
    public const string StandardPack = "std.adventuring", OwnModel = StandardPack + ".own-model", OwnModelTag = "own-model";

    public static string OfPiece(string def) => PiecePrefix + def;

    public static string OfModel(string mesh, string? texture) => ModelPrefix + mesh + Between + (texture ?? "");

    public static string OfTaking(string pieceId) => TakePrefix + pieceId;

    public static string? Clean(string wire) =>
        wire.Length is > 0 and <= Longest && (Piece(wire) != null || Model(wire) != null || Taking(wire) != null) ? wire : null;

    public static string? Piece(string? body) => After(body, PiecePrefix);

    public static string? Taking(string? body) => After(body, TakePrefix);

    static string? After(string? body, string prefix) =>
        body != null && body.StartsWith(prefix, System.StringComparison.Ordinal) && body.Length > prefix.Length ? body[prefix.Length..] : null;

    public static (string Mesh, string? Texture)? Model(string? body)
    {
        if (After(body, ModelPrefix) is not { } rest) return null;
        var parts = rest.Split(Between);
        if (parts.Length != 2 || !UploadAsset.IsWebModel(UploadAsset.ReliefPackage, parts[0])) return null;
        if (parts[1].Length == 0) return (parts[0], null);
        return UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, parts[1]) ? (parts[0], parts[1]) : null;
    }

    public static bool Takeable(PieceDef? def) =>
        def != null && def.Effect == null && !def.Tags.Contains("die") && !def.Tags.Contains(WallCutaway.BuildingTag) && !def.Tags.Contains(Doors.Tag);

    public static double RotOf(float yaw) => Godot.Mathf.PosMod(-Godot.Mathf.RadToDeg(yaw), 360);

    public static float YawOf(double rotDeg) => Godot.Mathf.DegToRad((float)-rotDeg);
}

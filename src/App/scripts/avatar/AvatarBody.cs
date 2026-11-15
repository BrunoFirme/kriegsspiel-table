using KTable.Core.Protocol;

namespace KTable.App;

static class AvatarBody
{
    const string PiecePrefix = "piece:", ModelPrefix = "model:";
    const char Between = '|';
    public const int Longest = 300;

    public static string OfPiece(string def) => PiecePrefix + def;

    public static string OfModel(string mesh, string? texture) => ModelPrefix + mesh + Between + (texture ?? "");

    public static string? Clean(string wire) => wire.Length is > 0 and <= Longest && (Piece(wire) != null || Model(wire) != null) ? wire : null;

    public static string? Piece(string? body) =>
        body != null && body.StartsWith(PiecePrefix, System.StringComparison.Ordinal) && body.Length > PiecePrefix.Length ? body[PiecePrefix.Length..] : null;

    public static (string Mesh, string? Texture)? Model(string? body)
    {
        if (body == null || !body.StartsWith(ModelPrefix, System.StringComparison.Ordinal)) return null;
        var parts = body[ModelPrefix.Length..].Split(Between);
        if (parts.Length != 2 || !UploadAsset.IsWebModel(UploadAsset.ReliefPackage, parts[0])) return null;
        if (parts[1].Length == 0) return (parts[0], null);
        return UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, parts[1]) ? (parts[0], parts[1]) : null;
    }
}

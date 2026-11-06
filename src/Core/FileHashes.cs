using System.Security.Cryptography;

namespace KTable.Core;

public static class FileHashes
{
    const int Sha256HexLength = 64;

    public static string Sha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static bool IsHashNamed(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return stem.Length == Sha256HexLength && stem.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    public static bool Matches(string path) => Sha256(path) == Path.GetFileNameWithoutExtension(path);
}

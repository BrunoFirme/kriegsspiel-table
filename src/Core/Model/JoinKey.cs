using System.Security.Cryptography;
using System.Text;

namespace KTable.Core.Model;

public sealed class JoinKey : IDisposable
{
    const int Bits = 256;
    const int NonceBytes = 32;
    public const int MaxPublicLength = 256;
    static readonly byte[] Purpose = Encoding.UTF8.GetBytes("kriegsspiel-table join v1\n");

    readonly ECDsa _ecdsa;

    JoinKey(ECDsa ecdsa)
    {
        _ecdsa = ecdsa;
        Public = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public string Public { get; }

    public static JoinKey New() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static JoinKey? From(byte[] pkcs8)
    {
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
            if (ecdsa.KeySize == Bits) return new JoinKey(ecdsa);
        }
        catch (CryptographicException) { }
        ecdsa.Dispose();
        return null;
    }

    public byte[] Private() => _ecdsa.ExportPkcs8PrivateKey();

    public string Sign(string nonce) => Convert.ToBase64String(_ecdsa.SignData(Signed(nonce), HashAlgorithmName.SHA256));

    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes));

    public static bool Proves(string? key, string nonce, string? proof)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxPublicLength || string.IsNullOrEmpty(proof)) return false;
        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            return ecdsa.KeySize == Bits && ecdsa.VerifyData(Signed(nonce), Convert.FromBase64String(proof), HashAlgorithmName.SHA256);
        }
        catch (Exception e) when (e is CryptographicException or FormatException) { return false; }
    }

    static byte[] Signed(string nonce) => [.. Purpose, .. Encoding.UTF8.GetBytes(nonce)];

    public void Dispose() => _ecdsa.Dispose();
}

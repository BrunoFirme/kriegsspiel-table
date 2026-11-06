using System.Security.Cryptography;
using System.Text;

namespace KTable.Core.Model;

public sealed class GameAccess
{
    public string Password { get; set; } = "";
    public List<Approved> Approved { get; set; } = new();

    static readonly char[] Base32 = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    public static string NewToken()
    {
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);
        var c = new char[16];
        for (int i = 0; i < 16; i++) c[i] = Base32[b[i] % Base32.Length];
        return new string(c);
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim().ToUpperInvariant()))).ToLowerInvariant();

    public Approved? Find(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : FindHash(Hash(token));

    public Approved? FindHash(string hash) => Approved.FirstOrDefault(a => a.TokenHash == hash);

    public GameAccess Clone() => new()
    {
        Password = Password,
        Approved = Approved.Select(a => new Approved { TokenHash = a.TokenHash, Name = a.Name, Role = a.Role }).ToList(),
    };
}

public sealed class Approved
{
    public string TokenHash { get; set; } = "";
    public string Name { get; set; } = "";
    public Role Role { get; set; } = Role.Observer;
}

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

    public Approved? FindKey(string? key) => string.IsNullOrEmpty(key) ? null : Approved.FirstOrDefault(a => a.Key == key);

    public void GiveKey(Approved entry, string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        foreach (var other in Approved.Where(a => a.Key == key)) other.Key = null;
        entry.Key = key;
    }

    public void GiveColorSlots()
    {
        var taken = Approved.Where(a => a.ColorSlot > 0).Select(a => a.ColorSlot).ToHashSet();
        int next = 1;
        foreach (var a in Approved.Where(a => a.ColorSlot <= 0))
        {
            while (taken.Contains(next)) next++;
            a.ColorSlot = next;
            taken.Add(next);
        }
    }

    public GameAccess Clone() => new()
    {
        Password = Password,
        Approved = Approved.Select(a => new Approved { TokenHash = a.TokenHash, Name = a.Name, Role = a.Role, Key = a.Key, ColorSlot = a.ColorSlot }).ToList(),
    };
}

public sealed class Approved
{
    public string TokenHash { get; set; } = "";
    public string Name { get; set; } = "";
    public Role Role { get; set; } = Role.Observer;
    public string? Key { get; set; }
    public int ColorSlot { get; set; }
}

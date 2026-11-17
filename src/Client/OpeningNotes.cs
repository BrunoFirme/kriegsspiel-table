using System.Security.Cryptography;
using System.Text;
using KTable.Core;

namespace KTable.Client;

public sealed class OpeningNotes(string dir)
{
    public const double SettledAfterSeconds = 5;
    const string Prefix = "opening-", Suffix = ".txt";
    const int NameBytes = 8;

    string? _watched;
    double _ran;

    static string KeyOf(string game) => Path.GetFullPath(game).ToUpperInvariant();

    string NoteOf(string game) =>
        Path.Combine(dir, Prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(KeyOf(game))), 0, NameBytes) + Suffix);

    public void Begin(string game)
    {
        _watched = game;
        _ran = 0;
        BestEffort.Try(() => Json.WriteAtomic(NoteOf(game), KeyOf(game)));
    }

    public bool LeftOver(string game) => File.Exists(NoteOf(game));

    public void End(string game)
    {
        BestEffort.DeleteFile(NoteOf(game));
        if (_watched != null && KeyOf(_watched) == KeyOf(game)) _watched = null;
    }

    public void Ran(double seconds)
    {
        if (_watched == null || (_ran += seconds) < SettledAfterSeconds) return;
        End(_watched);
    }

    public void Left()
    {
        if (_watched != null) End(_watched);
    }
}

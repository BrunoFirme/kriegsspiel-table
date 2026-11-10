using KTable.Core.Model;

namespace KTable.Client;

public static class PieceKinds
{
    public const string EffectTag = "effect", BookTag = "book";

    public static bool IsEffect(PieceDef? d) => d is { Kind: not "model" } && d.Tags.Contains(EffectTag);

    public static bool IsBook(PieceDef? d) => d is { } book && book.Tags.Contains(BookTag) && book.States.Count > 1;
}

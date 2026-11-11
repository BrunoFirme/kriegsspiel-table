using KTable.Core.Model;

namespace KTable.Client;

public enum PieceType { Piece, Prop, Marker, NoteCard, Book, Die, Effect }

public static class PieceKinds
{
    public const string EffectTag = "effect", BookTag = "book", PropTag = "prop", MarkerTag = "marker";

    public static bool IsEffect(PieceDef? d) => d is { Kind: not "model" } && d.Tags.Contains(EffectTag);

    public static bool IsBook(PieceDef? d) => d is { } book && book.Tags.Contains(BookTag) && book.States.Count > 1;

    public static PieceType TypeOf(PieceDef? d, PieceInstance p) =>
        d == null ? PieceType.Piece
        : IsEffect(d) ? PieceType.Effect
        : d.Kind == "model" && PieceInstance.FacesOf(d, p).Count > 1 ? PieceType.Die
        : IsBook(d) ? PieceType.Book
        : NoteCard.Is(d) ? PieceType.NoteCard
        : d.Tags.Contains(MarkerTag) ? PieceType.Marker
        : d.Tags.Contains(PropTag) ? PieceType.Prop
        : PieceType.Piece;
}

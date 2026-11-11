using KTable.Core.Model;

namespace KTable.Client.Tests;

public class PieceKindsTests
{
    static PieceDef Def(string kind, params string[] tags) => new() { Kind = kind, Tags = tags.ToList() };

    static readonly PieceInstance Plain = new();

    public static TheoryData<string, PieceDef?, PieceType> Kinds => new()
    {
        { "a tile with no tags", Def("flat"), PieceType.Piece },
        { "a model with no tags", Def("model"), PieceType.Piece },
        { "a piece whose pack is not here", null, PieceType.Piece },
        { "a prop", Def("model", "prop", "napoleonic"), PieceType.Prop },
        { "a marker", Def("flat", "marker"), PieceType.Marker },
        { "a note card, though tagged prop too", Def("flat", NoteCard.Tag, "prop"), PieceType.NoteCard },
        { "an effect", Def("flat", "effect"), PieceType.Effect },
        { "a model tagged effect is not one", Def("model", "effect"), PieceType.Piece },
        { "a book with one page is not one", Def("flat", "book"), PieceType.Piece },
    };

    [Theory, MemberData(nameof(Kinds))]
    public void Each_kind_falls_under_one_type(string what, PieceDef? def, PieceType expected) =>
        Assert.True(PieceKinds.TypeOf(def, Plain) == expected, $"{what}: {PieceKinds.TypeOf(def, Plain)}");

    [Fact]
    public void A_model_with_faces_is_a_die_and_a_book_needs_two_pages()
    {
        var die = Def("model", "prop");
        die.Faces = new() { new DieFace { Value = "1" }, new DieFace { Value = "2" } };
        Assert.Equal(PieceType.Die, PieceKinds.TypeOf(die, Plain));
        var book = Def("flat", "book");
        book.States = new() { new PieceStateDef { Name = "Page 1" }, new PieceStateDef { Name = "Page 2" } };
        Assert.Equal(PieceType.Book, PieceKinds.TypeOf(book, Plain));
    }
}

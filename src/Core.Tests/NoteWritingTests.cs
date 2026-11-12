using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class NoteWritingTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void A_note_s_writing_size_is_set_and_taken_back_by_undo()
    {
        var g = CoreSamples.State();
        var p = g.Find(U, "p-000001")!;
        Assert.Equal(0, p.LinesToARule);

        Assert.Null(StateMachine.Apply(g, new SetNoteWriting { Layer = U, Id = p.Id, LinesToARule = 2 }, out var back));
        Assert.Equal(2, p.LinesToARule);
        Assert.Equal(2, p.Clone().LinesToARule);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal(0, p.LinesToARule);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(NoteCard.MostLinesToARule + 1)]
    public void A_writing_size_no_note_has_is_refused_and_so_is_a_piece_that_is_not_there(int linesToARule)
    {
        var g = CoreSamples.State();
        Assert.NotNull(StateMachine.Apply(g, new SetNoteWriting { Layer = U, Id = "p-000001", LinesToARule = linesToARule }, out _));
        Assert.Equal(0, g.Find(U, "p-000001")!.LinesToARule);
        Assert.NotNull(StateMachine.Apply(g, new SetNoteWriting { Layer = U, Id = "nobody", LinesToARule = 1 }, out _));
    }
}

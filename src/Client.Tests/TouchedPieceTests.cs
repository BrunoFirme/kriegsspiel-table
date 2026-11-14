using System.Reflection;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class TouchedPieceTests
{
    static readonly HashSet<Type> ChangeTheirHolderToo = [typeof(Stow), typeof(Unstow)];
    static readonly HashSet<Type> DrawnWithoutARefresh = [typeof(PlayEffect), typeof(GlidePiece), typeof(EditWidget)];

    public static IEnumerable<object[]> CommandsAboutOnePiece() =>
        typeof(LayerCommand).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(LayerCommand).IsAssignableFrom(t))
            .Where(t => t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string))
            .Where(t => !ChangeTheirHolderToo.Contains(t) && !DrawnWithoutARefresh.Contains(t))
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(CommandsAboutOnePiece))]
    public void A_command_about_one_piece_names_it_so_only_that_piece_is_redrawn(Type command)
    {
        var c = (LayerCommand)Activator.CreateInstance(command)!;
        command.GetProperty("Id")!.SetValue(c, "p-000007");
        Assert.Equal("p-000007", TouchedPiece.IdOf(c));
    }

    [Fact]
    public void Commands_that_change_a_holder_too_name_no_single_piece()
    {
        Assert.Null(TouchedPiece.IdOf(new Stow { Holder = "p-000001", Id = "p-000002" }));
        Assert.Null(TouchedPiece.IdOf(new Unstow { Holder = "p-000001", Id = "p-000002" }));
        Assert.Null(TouchedPiece.IdOf(new SetReady { Holder = "p-000001" }));
    }

    [Fact]
    public void Attaching_and_detaching_name_the_piece_carried()
    {
        Assert.Equal("p-000002", TouchedPiece.IdOf(new Detach { Child = "p-000002" }));
        Assert.Equal("p-000003", TouchedPiece.IdOf(new RestorePiece { Piece = new() { Id = "p-000003" } }));
    }
}

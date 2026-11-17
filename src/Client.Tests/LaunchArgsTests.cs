using KTable.Client.Agent;

namespace KTable.Client.Tests;

public sealed class LaunchArgsTests
{
    [Theory]
    [InlineData("--path|game|--password|secret|--windowed", "--path|game|--windowed")]
    [InlineData("--password=secret|--path|game", "--path|game")]
    [InlineData("--path|game|--password", "--path|game")]
    [InlineData("--password|--windowed|-v", "--windowed|-v")]
    [InlineData("--path|game|--|--host|field|--password|secret", "--path|game")]
    [InlineData("--path|game|++|--password|secret", "--path|game")]
    [InlineData("--path|game|--name|Umpire", "--path|game|--name|Umpire")]
    public void The_recorded_command_line_never_holds_the_table_password_and_keeps_the_rest_in_order(string given, string recorded) =>
        Assert.Equal(recorded.Split('|'), LaunchArgs.Recorded(given.Split('|')));
}

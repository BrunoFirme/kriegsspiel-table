using KTable.Server;

namespace KTable.Client;

public sealed record SaveServices
{
    public required Action<List<Outgoing>> Dispatch { get; init; }
    public required Action<string> PostWriteBackDone { get; init; }
    public required Func<string> MirrorOf { get; init; }
    public required ClientLog Log { get; init; }
    public required Action<Action> Post { get; init; }
    public Func<double> Clock { get; init; } = () => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    public Func<bool> Quitting { get; init; } = () => false;
    public Action RequestPicture { get; init; } = () => { };
    public Func<double, byte[]?> PictureSince { get; init; } = _ => null;
    public Func<bool> PictureHeld { get; init; } = () => false;
}

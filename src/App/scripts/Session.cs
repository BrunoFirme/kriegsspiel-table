using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

public sealed class Session : ClientSession
{
    public Assets Assets { get; }
    public PieceArt Art { get; }
    public ReliefCache Relief { get; }

    public Session(Assets assets) : base(assets.Store)
    {
        Assets = assets;
        Art = new PieceArt(assets);
        Relief = new ReliefCache(assets, () => View.Sheets)
        {
            Built = id => View.Built.FindAll(b => b.Sheet == id),
            UnitsPerMetre = () => View.Calibration.UnitsPerMeter ?? 0,
        };
    }

    public Ground Ground => Relief.Ground;
}

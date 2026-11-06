using System.Collections.Generic;

namespace KTable.App;

interface IDevAids
{
    void LoadingDrawn();
    void Starting();
    void WatchForShot();
    void TitleShown(TitleScreen title);
    bool TakesOver();
    void SessionMade(Session session);
    void TableBuilt(Table table, Hud hud);
    void StudioOpened(PieceStudio studio);
}

static class DevAids
{
#if KTABLE_DEV
    public static IDevAids For(Main main, Dictionary<string, string> args) => new DevFlags(main, args);
#else
    public static IDevAids For(Main main, Dictionary<string, string> args) => new None();

    sealed class None : IDevAids
    {
        public void LoadingDrawn() { }
        public void Starting() { }
        public void WatchForShot() { }
        public void TitleShown(TitleScreen title) { }
        public bool TakesOver() => false;
        public void SessionMade(Session session) { }
        public void TableBuilt(Table table, Hud hud) { }
        public void StudioOpened(PieceStudio studio) { }
    }
#endif
}

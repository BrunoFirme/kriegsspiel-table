namespace KTable.Converter;

public static class PrefabRoots
{
    public readonly record struct Root(double X, double Y, double Z)
    {
        public static readonly Root One = new(1, 1, 1);
        public Root Across => new(Z, Y, X);
    }

    const double StockDie = 1.56, FigurineStand = 0.91461;

    static readonly Dictionary<string, Root> Measured = new(StringComparer.Ordinal)
    {
        ["BlockRectangle"] = new(1, 1, 2),
        ["Card"] = new(1, 0.065, 1), ["CardCustom"] = new(1, 0.065, 1),
        ["Deck"] = new(1, 1.4, 1), ["DeckCustom"] = new(1, 1.4, 1),
        ["Die_6"] = new(StockDie, StockDie, StockDie), ["Die_8"] = new(StockDie, StockDie, StockDie),
        ["Die_12"] = new(StockDie, StockDie, StockDie), ["Die_20"] = new(StockDie, StockDie, StockDie),
        ["Bag"] = new(0.8, 0.8, 0.8),
        [DefFactory.Figurine] = new(FigurineStand, FigurineStand, FigurineStand),
        ["Notecard"] = new(6.903, 0.02, 4.130),
    };

    static readonly HashSet<string> InOurSizes = new(StringComparer.Ordinal) { "BlockRectangle", "Die_6", "Die_8", "Die_12", "Die_20", DefFactory.Figurine };

    public static Root Of(string type) => Measured.GetValueOrDefault(type, Root.One);

    public static Root InSizesOf(string type) => InOurSizes.Contains(type) ? Of(type) : Root.One;
}

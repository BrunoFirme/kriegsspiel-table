namespace KTable.Core.Model;

public sealed record Ways(
    string[] Offered, double FarmVillage, double FarmTown, double Crofts, double RowsFrom, double BlockM, double GridLong, double GridWide, bool Cardinal,
    double LaneM, double StreetM, double SetbackM, string Open, string Seat, double Industry, double Trees, bool Walled, double GapOpenM, double Lanes)
{
    public const string Square = "square", Forum = "forum", Green = "green", Court = "courthouse", Maidan = "maidan", Plaza = "plaza", NoPlace = "none", Clearing = "clearing";

    const double VillageM = 120, TownM = 320, HousesFrom = 0.25, HousesFull = 0.7, WorksFrom = 0.1, WorksFull = 0.5;

    static Ways Make(string[] plans, double farmVillage, double farmTown, double crofts, double rowsFrom, string open, string seat, double blockM = 80, double gridLong = 0.8,
        double gridWide = 0.6, bool cardinal = false, double laneM = 2.5, double streetM = 4, double setbackM = 3, double industry = 0, double trees = 1, bool walled = false,
        double gapOpenM = 22, double lanes = 1) =>
        new(plans, farmVillage, farmTown, crofts, rowsFrom, blockM, gridLong, gridWide, cardinal, laneM, streetM, setbackM, open, seat, industry, trees, walled, gapOpenM, lanes);

    static readonly Dictionary<int, Ways> Book = new()
    {
        [Manners.Roman] = Make([Plans.Grid, Plans.Grown, Plans.Street], 0.5, 0.05, 0.25, 0.45, Forum, "basilica", blockM: 71, laneM: 1.8, streetM: 3, setbackM: 0.6),
        [Manners.Tribal] = Make([Plans.Scatter, Plans.Round, Plans.Grown], 0.95, 0.8, 1, 2, Clearing, "greathouse", trees: 1.3),
        [Manners.Persian] = Make([Plans.Maze, Plans.Grown, Plans.Grid], 0.6, 0.15, 0.12, 0.3, Maidan, "public", laneM: 1.4, streetM: 2.6, setbackM: 0.3, trees: 0.5, walled: true,
            gapOpenM: 6, lanes: 1.7),
        [Manners.Egyptian] = Make([Plans.Rows, Plans.Maze, Plans.Grown], 0.75, 0.3, 0, 0, NoPlace, "villa", blockM: 30, gridLong: 0.82, gridWide: 0.57, laneM: 1.5, streetM: 2.5,
            setbackM: 0.2, trees: 0.6, gapOpenM: 3),
        [Manners.Chinese] = Make([Plans.Wards, Plans.Street, Plans.Grid], 0.7, 0.3, 0.1, 0.35, Court, "public", blockM: 118, gridLong: 0.707, gridWide: 0.707, cardinal: true,
            laneM: 2, streetM: 6, setbackM: 1, trees: 0.7, gapOpenM: 8),
        [Manners.Continental] = Make([Plans.Grown, Plans.Street, Plans.Green, Plans.Round, Plans.Grid], 0.8, 0.25, 1, 0.8, Square, "public"),
        [Manners.Northern] = Make([Plans.Street, Plans.Grown, Plans.Green, Plans.Grid], 0.95, 0.5, 1.1, 2, Green, "public", blockM: 110, streetM: 7, setbackM: 6, trees: 1.3,
            gapOpenM: 26, lanes: 0.6),
        [Manners.Ottoman] = Make([Plans.Maze, Plans.Grown, Plans.Street], 0.7, 0.15, 0.8, 0.95, NoPlace, "public", laneM: 1.6, streetM: 3, setbackM: 1, trees: 1.5, walled: true,
            gapOpenM: 12, lanes: 1.5),
        [Manners.American] = Make([Plans.Grid, Plans.Street, Plans.Green], 0.6, 0.1, 0.9, 1.5, Court, "public", blockM: 104, gridLong: 0.74, gridWide: 0.67, streetM: 8, setbackM: 7,
            trees: 1.2, gapOpenM: 14),
        [Manners.Japanese] = Make([Plans.Grid, Plans.Street, Plans.Grown], 0.85, 0.02, 0.15, 0.35, NoPlace, "public", blockM: 64, laneM: 1.6, streetM: 3.2, setbackM: 0.4, trees: 0.8,
            gapOpenM: 5),
        [Manners.Western] = Make([Plans.Grown, Plans.Grid, Plans.Street], 0.6, 0.03, 0.3, 0.45, Square, "public", blockM: 76, streetM: 5, setbackM: 1, industry: 1, trees: 0.6,
            gapOpenM: 10),
        [Manners.Soviet] = Make([Plans.Street, Plans.Grid, Plans.Grown], 0.92, 0.25, 1.3, 2, Square, "public", blockM: 120, streetM: 9, setbackM: 6, industry: 0.7, trees: 1.1,
            gapOpenM: 24, lanes: 0.6),
        [Manners.NorthAfrican] = Make([Plans.Maze, Plans.Grid, Plans.Grown], 0.5, 0.08, 0, 0.2, Maidan, "public", laneM: 1.3, streetM: 2.6, setbackM: 0.2, industry: 0.25, trees: 0.5,
            walled: true, gapOpenM: 4, lanes: 1.8),
        [Manners.Pacific] = Make([Plans.Shore, Plans.Grid, Plans.Round], 0.9, 0.35, 0, 2, Clearing, "public", blockM: 90, streetM: 4, setbackM: 5, industry: 0.3, trees: 2.4,
            gapOpenM: 16, lanes: 0.4),
        [Manners.Nippon] = Make([Plans.Grid, Plans.Street, Plans.Grown], 0.8, 0.03, 0.1, 0.3, NoPlace, "public", blockM: 58, laneM: 1.6, streetM: 3.6, setbackM: 0.4, industry: 0.9,
            trees: 0.6, gapOpenM: 4),
    };

    public static Ways Of(int manner) => Book[Manners.Known(manner)];

    public static double Town(double radiusM, double houses, bool modern = false) =>
        Math.Clamp((radiusM - VillageM) / (TownM - VillageM), 0, 1) * Math.Clamp((houses - (modern ? WorksFrom : HousesFrom)) / ((modern ? WorksFull : HousesFull) - (modern ? WorksFrom : HousesFrom)),
            0, 1);

    public double Farming(double town) => FarmVillage + (FarmTown - FarmVillage) * Math.Clamp(town, 0, 1);
}

using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public static class CrowdBaseline
{
    public const string Id = "baseline";
    public const string Labourer = "labourer", Farmer = "farmer", Smith = "smith", Merchant = "merchant", Soldier = "soldier", Cleric = "cleric";
    public const string Housewife = "housewife", Washer = "washer", Youngster = "child";

    public static readonly IReadOnlyList<string> Bands = [CrowdEntry.Child, CrowdEntry.Youth, CrowdEntry.Adult, CrowdEntry.Elder];
    public static readonly IReadOnlyList<string> Wealths = [CrowdEntry.Poor, CrowdEntry.Middle, CrowdEntry.Rich];
    public static readonly IReadOnlyList<string> Materials = [CrowdSlots.Linen, "leather", CrowdSlots.Metal, CrowdSlots.Wood, CrowdSlots.Brass];

    public static readonly IReadOnlyList<HandItem> Items =
    [
        new() { Id = "spear", Hand = HandItem.Both, LengthM = 2.4 },
        new() { Id = "musket", Hand = HandItem.Shoulder, LengthM = 1.5 },
        new() { Id = "long-gun", Hand = HandItem.Shoulder, LengthM = 1.7 },
        new() { Id = "rifle", Hand = HandItem.Shoulder, LengthM = 1.25 },
        new() { Id = "bow", Hand = HandItem.Left, LengthM = 1.6 },
        new() { Id = "shield", Hand = HandItem.Left, LengthM = 1.1 },
        new() { Id = "hammer", Hand = HandItem.Right, LengthM = 0.4 },
        new() { Id = "hoe", Hand = HandItem.Both, LengthM = 1.4 },
        new() { Id = "basket", Hand = HandItem.Left, LengthM = 0.45 },
        new() { Id = "cane", Hand = HandItem.Right, LengthM = 0.9 },
        new() { Id = "bundle", Hand = HandItem.Back, LengthM = 0.6 },
        new() { Id = "sack", Hand = HandItem.Shoulder, LengthM = 0.7 },
        new() { Id = "torch", Hand = HandItem.Right, LengthM = 0.6 },
        new() { Id = "lantern", Hand = HandItem.Right, LengthM = 0.3 },
    ];

    static readonly IReadOnlyDictionary<string, double> AdultJoints = new Dictionary<string, double>
    {
        [H.Foot] = 0.04, [H.LowerLeg] = 0.285, [H.UpperLeg] = 0.47, [H.Hips] = 0.52, [H.Spine] = 0.61, [H.Chest] = 0.72, [H.UpperChest] = 0.755,
        [H.UpperArm] = 0.815, [H.Neck] = 0.845, [H.Head] = 0.867, [H.LowerArm] = 0.63, [H.Hand] = 0.485,
    };

    static readonly IReadOnlyDictionary<string, double> ChildJoints = new Dictionary<string, double>
    {
        [H.Foot] = 0.045, [H.LowerLeg] = 0.265, [H.UpperLeg] = 0.455, [H.Hips] = 0.505, [H.Spine] = 0.59, [H.Chest] = 0.69, [H.UpperChest] = 0.72,
        [H.UpperArm] = 0.78, [H.Neck] = 0.805, [H.Head] = 0.83, [H.LowerArm] = 0.6, [H.Hand] = 0.45,
    };

    const double YouthGrown = 0.72;

    public static IReadOnlyDictionary<string, double> Joints(string band) => band switch
    {
        CrowdEntry.Child => ChildJoints,
        CrowdEntry.Youth => AdultJoints.ToDictionary(j => j.Key, j => ChildJoints[j.Key] + (j.Value - ChildJoints[j.Key]) * YouthGrown),
        _ => AdultJoints,
    };

    public static IReadOnlyDictionary<string, (double Wide, double Deep)> Girths(string sex) => sex == CrowdEntry.Woman
        ? new Dictionary<string, (double, double)> { [H.Hips] = (0.106, 0.072), [H.Spine] = (0.072, 0.055), [H.Chest] = (0.092, 0.074), [H.UpperChest] = (0.094, 0.068), [H.Neck] = (0.04, 0.036) }
        : new Dictionary<string, (double, double)> { [H.Hips] = (0.095, 0.066), [H.Spine] = (0.083, 0.058), [H.Chest] = (0.1, 0.068), [H.UpperChest] = (0.104, 0.066), [H.Neck] = (0.044, 0.04) };

    public static readonly IReadOnlyDictionary<string, double> BandHeight = new Dictionary<string, double>
    {
        [CrowdEntry.Child] = 0.72, [CrowdEntry.Youth] = 0.93, [CrowdEntry.Adult] = 1, [CrowdEntry.Elder] = 0.985,
    };

    public static readonly IReadOnlyDictionary<string, double> BandShares = new Dictionary<string, double>
    {
        [CrowdEntry.Child] = 18, [CrowdEntry.Youth] = 10, [CrowdEntry.Adult] = 60, [CrowdEntry.Elder] = 12,
    };

    public static readonly IReadOnlyDictionary<string, (int Least, int Most)> BandAges = new Dictionary<string, (int, int)>
    {
        [CrowdEntry.Child] = (5, 11), [CrowdEntry.Youth] = (12, 17), [CrowdEntry.Adult] = (18, 55), [CrowdEntry.Elder] = (56, 77),
    };

    public static readonly IReadOnlyDictionary<string, string> CallingWealth = new Dictionary<string, string>
    {
        [Merchant] = CrowdEntry.Rich, [Cleric] = CrowdEntry.Middle, [Soldier] = CrowdEntry.Poor, [Smith] = CrowdEntry.Middle,
    };

    public static Nationality Person => Nationalities.Baseline;
}

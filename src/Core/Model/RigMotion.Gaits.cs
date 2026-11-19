namespace KTable.Core.Model;

public sealed partial class RigMotion
{
    sealed record Stride
    {
        public float Reach { get; init; }
        public float Behind { get; init; }
        public float Duty { get; init; } = 0.6f;
        public float Cadence { get; init; } = 1;
        public float Width { get; init; } = 1;
        public float ToeOut { get; init; } = 6;
        public float KneeRest { get; init; }
        public float KneeSwing { get; init; }
        public float Give { get; init; }
        public float Heel { get; init; }
        public float Push { get; init; }
        public float HeelOff { get; init; } = 0.55f;
        public float Tuck { get; init; } = 0.75f;
        public float Shift { get; init; } = 11;
        public float Bob { get; init; }
        public float Twist { get; init; }
        public float Roll { get; init; }
        public float Sway { get; init; }
        public float Lean { get; init; }
        public float Chin { get; init; }
        public float Arm { get; init; }
        public float ArmAhead { get; init; }
        public float ArmIn { get; init; }
        public float ElbowRest { get; init; }
        public float ElbowSwing { get; init; }
        public float ArmLag { get; init; }
        public float Wrist { get; init; }
        public float Crouch { get; init; }
    }

    sealed record Gait(string Id, string Name, float ArmOut, Stride Walk, Stride Run, bool Stance = false);

    public const string Natural = "natural", March = "march", Sneak = "sneak";

    static readonly Gait[] Sets =
    [
        new(Natural, "Natural", 5,
            new Stride
            {
                Reach = 27, Behind = 0.08f, Duty = 0.62f, Cadence = 0.92f, Width = 0.62f, ToeOut = 7, KneeRest = 6, KneeSwing = 58, Give = 7, Heel = 12, Push = 34, HeelOff = 0.5f,
                Twist = 5, Roll = 4, Sway = 0.011f, Lean = 3, Chin = 2, Arm = 13, ArmAhead = -2, ArmIn = 7, ElbowRest = 14, ElbowSwing = 18, ArmLag = 0.35f, Wrist = 7,
            },
            new Stride
            {
                Reach = 29, Behind = 0.12f, Duty = 0.29f, Cadence = 1.4f, Width = 0.45f, ToeOut = 5, KneeRest = 12, KneeSwing = 96, Give = 26, Heel = 6, Push = 38, HeelOff = 0.3f,
                Tuck = 0.5f, Bob = 0.022f, Twist = 8, Roll = 3, Sway = 0.004f, Lean = 9, Chin = 6, Arm = 30, ArmAhead = 6, ArmIn = 16, ElbowRest = 82, ElbowSwing = 16, ArmLag = 0.25f, Wrist = 5,
            }),
        new(March, "Marching", 3,
            new Stride
            {
                Shift = 14, Reach = 28, Behind = 0.08f, Duty = 0.6f, Cadence = 0.92f, Width = 0.7f, ToeOut = 9, KneeRest = 1, KneeSwing = 52, Give = 4, Heel = 18, Push = 24, HeelOff = 0.5f,
                Twist = 2, Roll = 1.5f, Sway = 0.005f, Lean = -2, Chin = -2, Arm = 18, ArmAhead = 0, ArmIn = 2, ElbowRest = 5, ElbowSwing = 4, ArmLag = 0.08f, Wrist = 0,
            },
            new Stride
            {
                Reach = 22, Behind = 0.08f, Duty = 0.4f, Cadence = 1.35f, Width = 0.6f, ToeOut = 7, KneeRest = 8, KneeSwing = 82, Give = 16, Heel = 8, Push = 32, HeelOff = 0.4f,
                Tuck = 0.5f, Bob = 0.012f, Twist = 3, Roll = 2, Sway = 0.004f, Lean = 4, Chin = 2, Arm = 16, ArmAhead = 4, ArmIn = 6, ElbowRest = 68, ElbowSwing = 8, ArmLag = 0.15f, Wrist = 0,
            }, Stance: true),
        new("heavy", "Heavy", 17,
            new Stride
            {
                Shift = 16, Reach = 25, Behind = 0.04f, Duty = 0.65f, Cadence = 1.14f, Width = 1.25f, ToeOut = 15, KneeRest = 10, KneeSwing = 44, Give = 14, Heel = 7, Push = 16, HeelOff = 0.6f,
                Twist = 8, Roll = 5, Sway = 0.02f, Lean = 5, Chin = 3, Arm = 11, ArmAhead = 2, ArmIn = 0, ElbowRest = 24, ElbowSwing = 8, ArmLag = 0.5f, Wrist = 4, Crouch = 0.01f,
            },
            new Stride
            {
                Reach = 31, Behind = 0.06f, Duty = 0.37f, Cadence = 1.7f, Width = 1.05f, ToeOut = 12, KneeRest = 14, KneeSwing = 76, Give = 30, Heel = 4, Push = 30, HeelOff = 0.4f,
                Tuck = 0.5f, Bob = 0.012f, Twist = 11, Roll = 5, Sway = 0.02f, Lean = 10, Chin = 6, Arm = 22, ArmAhead = 6, ArmIn = 2, ElbowRest = 62, ElbowSwing = 10, ArmLag = 0.45f, Wrist = 4,
                Crouch = 0.01f,
            }),
        new("graceful", "Graceful", 6,
            new Stride
            {
                Shift = 9, Reach = 28, Behind = 0.08f, Duty = 0.58f, Cadence = 0.96f, Width = 0.12f, ToeOut = 10, KneeRest = 2, KneeSwing = 54, Give = 6, Heel = 7, Push = 27, HeelOff = 0.42f,
                Twist = 9, Roll = 7, Sway = 0.012f, Lean = -1, Chin = -1, Arm = 9, ArmAhead = -5, ArmIn = 3, ElbowRest = 12, ElbowSwing = 16, ArmLag = 0.6f, Wrist = 16,
            },
            new Stride
            {
                Reach = 30, Behind = 0.1f, Duty = 0.29f, Cadence = 1.4f, Width = 0.3f, ToeOut = 8, KneeRest = 10, KneeSwing = 104, Give = 22, Heel = 2, Push = 42, HeelOff = 0.25f,
                Tuck = 0.5f, Bob = 0.032f, Twist = 9, Roll = 4, Sway = 0.006f, Lean = 5, Chin = 3, Arm = 24, ArmAhead = 2, ArmIn = 12, ElbowRest = 70, ElbowSwing = 18, ArmLag = 0.45f, Wrist = 12,
            }),
        new("stooped", "Stooped", 8,
            new Stride
            {
                Shift = 17, Reach = 23, Behind = -0.05f, Duty = 0.7f, Cadence = 1.5f, Width = 1.05f, ToeOut = 11, KneeRest = 15, KneeSwing = 30, Give = 4, Heel = 3, Push = 5, HeelOff = 0.78f,
                Twist = 3, Roll = 2, Sway = 0.011f, Lean = 20, Chin = 16, Arm = 5, ArmAhead = 12, ArmIn = 2, ElbowRest = 30, ElbowSwing = 5, ArmLag = 0.5f, Wrist = 3, Crouch = 0.025f,
            },
            new Stride
            {
                Reach = 30, Behind = 0.05f, Duty = 0.42f, Cadence = 2.2f, Width = 1, ToeOut = 10, KneeRest = 20, KneeSwing = 58, Give = 10, Heel = 2, Push = 10, HeelOff = 0.6f,
                Tuck = 0.5f, Bob = 0.005f, Twist = 4, Roll = 2, Sway = 0.012f, Lean = 25, Chin = 20, Arm = 12, ArmAhead = 16, ArmIn = 4, ElbowRest = 52, ElbowSwing = 8, ArmLag = 0.4f, Wrist = 3,
                Crouch = 0.03f,
            }),
        new(Sneak, "Sneaking", 16,
            new Stride
            {
                Shift = 7, Reach = 19, Behind = -0.1f, Duty = 0.7f, Cadence = 0.8f, Width = 0.85f, ToeOut = 4, KneeRest = 24, KneeSwing = 74, Give = 5, Heel = -8, Push = 8, HeelOff = 0.75f,
                Twist = 3, Roll = 2, Sway = 0.015f, Lean = 17, Chin = 14, Arm = 6, ArmAhead = 10, ArmIn = 4, ElbowRest = 48, ElbowSwing = 6, ArmLag = 0.3f, Wrist = 0, Crouch = 0.06f,
            },
            new Stride
            {
                Reach = 26, Behind = 0.1f, Duty = 0.5f, Cadence = 1.3f, Width = 0.8f, ToeOut = 4, KneeRest = 30, KneeSwing = 90, Give = 10, Heel = -4, Push = 14, HeelOff = 0.6f,
                Tuck = 0.5f, Bob = 0.008f, Twist = 4, Roll = 2, Sway = 0.01f, Lean = 22, Chin = 18, Arm = 12, ArmAhead = 10, ArmIn = 6, ElbowRest = 70, ElbowSwing = 8, ArmLag = 0.3f, Wrist = 0,
                Crouch = 0.07f,
            }, Stance: true),
    ];

    public static IReadOnlyList<(string Id, string Name)> Gaits { get; } = Sets.Where(g => !g.Stance).Select(g => (g.Id, g.Name)).ToList();

    public static bool Known(string? gait) => gait == null || Sets.Any(g => g.Id == gait);
}

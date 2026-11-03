using System.Numerics;
using KTable.Client.Hand;
using KTable.Server;

namespace KTable.Client.Tests;

public class MotionBufferTests
{
    const double Late = 0.05;
    static readonly PieceKey A = new("umpire", "p-1");
    static readonly PieceKey B = new("umpire", "p-2");

    readonly MotionBuffer _buffer = new();

    static MotionSample At(PieceKey piece, float x, float turnDeg = 0) =>
        new(piece, new Pose(new Vector3(x, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turnDeg * MathF.PI / 180)));

    void Heard(double t, params MotionSample[] samples) => _buffer.Add(7, t, samples, t + Late);

    Pose? PoseOf(PieceKey piece, double now) => _buffer.Poses(now).Where(p => p.Piece == piece).Select(p => (Pose?)p.Pose).FirstOrDefault();

    static bool Never(long carrier, PieceKey piece) => false;

    [Fact]
    public void Playback_runs_a_tenth_of_a_second_behind_and_blends_between_samples()
    {
        Heard(0, At(A, 0, 0));
        Heard(0.1, At(A, 10, 90));
        var mid = PoseOf(A, 0.05 + Late + MotionBuffer.DelaySeconds)!.Value;
        Assert.Equal(5, mid.At.X, 3);
        Assert.Equal(45, Math.Acos(Quaternion.Normalize(mid.Turn).W) * 2 * 180 / Math.PI, 1);
    }

    [Fact]
    public void Nothing_plays_before_its_first_sample_is_due()
    {
        Heard(0, At(A, 3));
        Assert.Null(PoseOf(A, Late));
        Assert.NotNull(PoseOf(A, Late + MotionBuffer.DelaySeconds + 0.01));
    }

    [Fact]
    public void After_the_last_sample_the_piece_holds_there_then_finishes()
    {
        Heard(0, At(A, 0));
        Heard(0.1, At(A, 4));
        var after = 0.1 + Late + MotionBuffer.DelaySeconds + 0.1;
        Assert.Equal(4, PoseOf(A, after)!.Value.At.X, 3);
        Assert.Empty(_buffer.Finished(after, Never));
        var done = _buffer.Finished(after + MotionBuffer.EndAfterSeconds, Never);
        Assert.Equal((A, 0f, 4f), (done.Single().Piece, done.Single().From.X, done.Single().To.X));
        Assert.Equal(0, _buffer.Count);
    }

    [Fact]
    public void A_piece_still_in_its_carrier_s_hand_does_not_finish_until_the_carrier_goes_quiet()
    {
        Heard(0, At(A, 0));
        Assert.Empty(_buffer.Finished(1, (_, _) => true));
        Assert.Single(_buffer.Finished(MotionBuffer.SilentForSeconds + 0.2, (_, _) => true));
    }

    [Fact]
    public void A_late_or_repeated_sample_is_ignored()
    {
        Heard(0.2, At(A, 2));
        Heard(0.1, At(A, 99));
        Heard(0.2, At(A, 99));
        Assert.Equal(2, PoseOf(A, 0.2 + Late + MotionBuffer.DelaySeconds + 0.5)!.Value.At.X, 3);
    }

    [Fact]
    public void Each_piece_plays_on_its_own()
    {
        Heard(0, At(A, 1), At(B, -1));
        Heard(0.1, At(A, 2));
        var now = 0.1 + Late + MotionBuffer.DelaySeconds;
        Assert.Equal((2f, -1f), (PoseOf(A, now)!.Value.At.X, PoseOf(B, now)!.Value.At.X));
    }

    [Fact]
    public void A_carrier_s_clock_is_read_from_the_quickest_arrival()
    {
        _buffer.Add(7, 100, [At(A, 0)], 1000.3);
        _buffer.Add(7, 100.1, [At(A, 10)], 1000.15);
        Assert.Equal(5, PoseOf(A, 1000.1 + MotionBuffer.DelaySeconds)!.Value.At.X, 3);
    }

    [Fact]
    public void Another_carrier_taking_a_piece_starts_it_afresh()
    {
        Heard(0, At(A, 1));
        _buffer.Add(8, 50, [At(A, 9)], 0.1);
        Assert.Equal(9, PoseOf(A, 0.1 + MotionBuffer.DelaySeconds)!.Value.At.X, 3);
    }
}

public class FlightShareTests
{
    static readonly PieceKey A = new("umpire", "p-1");
    readonly FlightShare _share = new();
    readonly List<(double T, int Count)> _sent = new();

    public FlightShareTests() => _share.Send += (t, s) => _sent.Add((t, s.Count));

    static MotionSample At(float x) => new(A, new Pose(new Vector3(x, 0, 0), Quaternion.Identity));

    [Fact]
    public void A_moving_piece_is_sent_thirty_times_a_second()
    {
        for (var i = 0; i <= 60; i++) _share.Tick(i / 60.0, [At(i)]);
        Assert.InRange(_sent.Count, 29, 31);
    }

    [Fact]
    public void A_piece_at_rest_is_sent_four_times_a_second()
    {
        for (var i = 0; i <= 60; i++) _share.Tick(i / 60.0, [At(1)]);
        Assert.InRange(_sent.Count, 4, 5);
    }

    [Fact]
    public void Nothing_moving_sends_nothing()
    {
        _share.Tick(0, []);
        Assert.Empty(_sent);
    }
}

public class SampleWireTests
{
    [Fact]
    public void Samples_survive_the_wire()
    {
        List<MotionSample> samples = [new(new("umpire", "p-1"), new Pose(new Vector3(1, 2, 3), Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f))))];
        var (layers, ids, poses) = CarryWire.PackSamples(samples);
        var back = CarryWire.UnpackSamples(layers, ids, poses)!.Single();
        Assert.Equal(samples[0].Piece, back.Piece);
        Assert.Equal(samples[0].Pose.At, back.Pose.At);
        Assert.Equal(1, Math.Abs(Quaternion.Dot(samples[0].Pose.Turn, back.Pose.Turn)), 4);
    }

    [Fact]
    public void Broken_samples_are_refused()
    {
        Assert.Null(CarryWire.UnpackSamples(["umpire"], ["p-1"], new float[6]));
        Assert.Null(CarryWire.UnpackSamples(["umpire"], ["p-1"], [0, 0, 0, 0, 0, 0, 0]));
        Assert.Null(CarryWire.UnpackSamples(["umpire"], ["p-1"], [float.NaN, 0, 0, 0, 0, 0, 1]));
    }
}

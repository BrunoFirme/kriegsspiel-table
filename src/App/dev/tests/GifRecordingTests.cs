using System.IO;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class GifRecordingTests
{
    sealed class Frames : IGifFrames
    {
        readonly TestRunner _r;
        public Frames(TestRunner r) => _r = r;
        public bool Gone { get; set; }
        public bool Closed { get; private set; }
        int _drawn;

        public Task<Vector2I> Open() => Task.FromResult(new Vector2I(16, 8));

        public async Task Draw(bool grabbing)
        {
            _drawn++;
            await _r.Frames(1);
        }

        public Image Grab()
        {
            var img = Image.CreateEmpty(16, 8, false, Image.Format.Rgba8);
            img.Fill(_drawn % 2 == 0 ? Colors.Red : Colors.Blue);
            return img;
        }

        public void Close() => Closed = true;
    }

    static string Temp(string name) => Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N") + name);

    [NodeTest]
    static async Task Stopping_a_recording_keeps_a_gif_of_its_frames_and_a_source_that_goes_away_ends_it_as_dropped(TestRunner r)
    {
        var file = Temp(".gif");
        var frames = new Frames(r);
        var rec = new GifRecording(frames, file);
        var run = rec.Run();
        await r.Frames(5);
        rec.Stop();
        var end = await run;
        Check.Equal(GifRecording.End.Stopped, end, "a stop ends it as stopped");
        Check.That(rec.Frames >= 1 && frames.Closed, $"frames were written and the source closed: {rec.Frames}");
        var head = File.ReadAllBytes(file);
        Check.That(head.Length > 6 && System.Text.Encoding.ASCII.GetString(head, 0, 6) == "GIF89a", "the file is a GIF");
        var gone = new Frames(r);
        var dropped = new GifRecording(gone, Temp(".gif"));
        var running = dropped.Run();
        await r.Frames(3);
        gone.Gone = true;
        Check.Equal(GifRecording.End.Dropped, await running, "a source that goes away drops the recording");
        Check.That(gone.Closed, "and is closed");
    }
}

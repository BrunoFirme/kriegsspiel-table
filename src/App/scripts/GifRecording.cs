using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Gif;

namespace KTable.App;

interface IGifFrames
{
    Task<Vector2I> Open();
    Task Draw(bool grabbing);
    Image Grab();
    void Close();
    bool Gone { get; }
}

sealed class GifRecording
{
    public const int FramesPerSecond = 10;
    public const double MostSeconds = 60;
    public const long StopAtBytes = 9_000_000;
    const int QueueDepth = 4, AbandonAfterMs = 30_000;
    const double MsPerSecond = 1000, FrameSeconds = 1.0 / FramesPerSecond;

    public enum End { Stopped, LongestTime, LargestFile, Dropped, Failed }

    readonly IGifFrames _source;
    readonly string _file;
    readonly BlockingCollection<(byte[] rgba, double at)> _frames = new(QueueDepth);
    long _bytes;
    int _written;
    volatile bool _stop, _drop;
    volatile string? _failure;
    double _endAt;

    public GifRecording(IGifFrames source, string file)
    {
        _source = source;
        _file = file;
    }

    public double Seconds { get; private set; }
    public long Bytes => Interlocked.Read(ref _bytes);
    public int Frames => Volatile.Read(ref _written);
    public string? Failure => _failure;
    public event Action? Ticked;

    public void Stop() => _stop = true;

    public void Drop() => _drop = true;

    public async Task<End> Run()
    {
        var size = await _source.Open();
        var worker = Task.Run(() => Encode(size.X, size.Y));
        ulong started = Time.GetTicksMsec();
        double due = 0;
        End end;
        try
        {
            while (true)
            {
                if (Reason() is { } why)
                {
                    end = why;
                    break;
                }
                bool grabbing = (Time.GetTicksMsec() - started) / MsPerSecond >= due;
                await _source.Draw(grabbing);
                Seconds = (Time.GetTicksMsec() - started) / MsPerSecond;
                if (grabbing) due = Grab(due);
                Ticked?.Invoke();
            }
        }
        finally
        {
            _endAt = Seconds;
            _frames.CompleteAdding();
            _source.Close();
        }
        await worker;
        return _failure != null ? End.Failed : end;
    }

    End? Reason()
    {
        if (_failure != null) return End.Failed;
        if (_source.Gone || _drop) return End.Dropped;
        if (_stop) return End.Stopped;
        if (Seconds >= MostSeconds) return End.LongestTime;
        return Bytes >= StopAtBytes ? End.LargestFile : null;
    }

    double Grab(double due)
    {
        var img = _source.Grab();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        _frames.TryAdd((img.GetData(), Seconds));
        double next = due + FrameSeconds;
        return next < Seconds ? Seconds + FrameSeconds : next;
    }

    void Encode(int width, int height)
    {
        if (!Write(width, height)) KTable.Core.BestEffort.DeleteFile(_file);
    }

    bool Write(int width, int height)
    {
        try
        {
            using var file = new FileStream(_file, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
            var gif = new GifWriter(file, width, height);
            while (!_frames.IsCompleted)
            {
                if (!_frames.TryTake(out var frame, AbandonAfterMs))
                {
                    if (_frames.IsCompleted) break;
                    _failure = "no picture came for " + AbandonAfterMs / 1000 + " s";
                    return false;
                }
                gif.Add(frame.rgba, frame.at);
                Volatile.Write(ref _written, gif.Frames);
                Interlocked.Exchange(ref _bytes, file.Position);
            }
            gif.Finish(_endAt);
            file.Flush(flushToDisk: true);
            Interlocked.Exchange(ref _bytes, file.Position);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _failure = e.Message;
            return false;
        }
    }
}

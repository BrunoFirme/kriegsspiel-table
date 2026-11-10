using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Gif;

namespace KTable.App;

sealed class PovRecording
{
    public const int FramesPerSecond = 10;
    public const double MostSeconds = 60;
    public const long StopAtBytes = 9_000_000;
    public const int EyeHeight = 432, AboveSide = 720;
    const int QueueDepth = 4, AbandonAfterMs = 30_000;
    const double MsPerSecond = 1000, FrameSeconds = 1.0 / FramesPerSecond;

    public enum End { Stopped, LongestTime, LargestFile, Dropped, Failed }

    readonly Sighting _sighting;
    readonly SightingDraft _draft;
    readonly string _file;
    readonly BlockingCollection<(byte[] rgba, double at)> _frames = new(QueueDepth);
    long _bytes;
    int _written;
    volatile bool _stop;
    volatile string? _failure;
    double _endAt;

    public PovRecording(Sighting sighting, SightingDraft draft, string file)
    {
        _sighting = sighting;
        _draft = draft;
        _file = file;
    }

    public double Seconds { get; private set; }
    public long Bytes => Interlocked.Read(ref _bytes);
    public int Frames => Volatile.Read(ref _written);
    public string? Failure => _failure;
    public event Action? Ticked;

    public void Stop() => _stop = true;

    public async Task<End> Run()
    {
        var shot = await _sighting.OpenPicture(_draft, _draft.FirstPerson ? EyeHeight : AboveSide);
        _sighting.Recording = true;
        var size = shot.Size;
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
                await _sighting.DrawFrame(shot, _draft);
                Seconds = (Time.GetTicksMsec() - started) / MsPerSecond;
                if (Seconds >= due) due = Grab(shot, due);
                Ticked?.Invoke();
            }
        }
        finally
        {
            _endAt = Seconds;
            _frames.CompleteAdding();
            _sighting.Recording = false;
            _sighting.ClosePicture(shot);
        }
        await worker;
        return _failure != null ? End.Failed : end;
    }

    End? Reason()
    {
        if (_failure != null) return End.Failed;
        if (_sighting.Draft != _draft) return End.Dropped;
        if (_stop) return End.Stopped;
        if (Seconds >= MostSeconds) return End.LongestTime;
        return Bytes >= StopAtBytes ? End.LargestFile : null;
    }

    double Grab(SubViewport shot, double due)
    {
        var img = shot.GetTexture().GetImage();
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

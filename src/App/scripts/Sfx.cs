using System;
using System.Collections.Generic;
using Godot;
using KTable.Client.Prefs;

namespace KTable.App;

enum Sound { Lift, Wood, WoodHeavy, Card, Plastic, Die, Dice, DiceGrab, Tick, Flip, Shake, Shuffle }

sealed partial class Sfx : Node
{
    const string BusName = "Table";
    const int Voices = 16, StartsPerFrame = 6;

    static Sfx? _current;
    static Dictionary<Sound, AudioStream[]>? _bank;
    static readonly Random Rng = new();

    public static Sfx? Current => _current != null && IsInstanceValid(_current) ? _current : null;

    public static int Takes(Sound s) => _bank != null && _bank.TryGetValue(s, out var t) ? t.Length : 0;

    readonly List<(AudioStreamPlayer3D p, float gain)> _voices = new();
    Settings _cfg = null!;
    bool _quiet, _inFront = true;
    ulong _frame;
    int _started;

    public static void Install(Node host, Settings cfg, bool quiet)
    {
        var s = new Sfx { Name = "Sfx", _cfg = cfg, _quiet = quiet };
        host.AddChild(s);
        _current = s;
    }

    public override void _Ready()
    {
        _bank ??= Load();
        Bus();
        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer3D
            {
                Bus = BusName, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
                PanningStrength = 0.6f, DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled, MaxPolyphony = 1,
            };
            AddChild(p);
            _voices.Add((p, 0));
        }
        Apply();
    }

    public override void _ExitTree() { if (_current == this) _current = null; }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn) { _inFront = true; Apply(); }
        else if (what == NotificationApplicationFocusOut) { _inFront = false; Apply(); }
    }

    public void Apply()
    {
        int bus = Bus();
        if (bus < 0) return;
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Clamp(_cfg.SoundVolume, 0, 1)));
        AudioServer.SetBusMute(bus, _quiet || _cfg.SoundMuted || _cfg.SoundVolume <= 0 || (!_inFront && !_cfg.SoundInBackground));
    }

    public void Play(Sound s, Vector3 at, float gain, float pitch)
    {
        if (_quiet || _bank == null || !_bank.TryGetValue(s, out var takes) || takes.Length == 0) return;
        var frame = Engine.GetProcessFrames();
        if (frame != _frame) { _frame = frame; _started = 0; }
        if (_started >= StartsPerFrame) return;
        int pick = -1;
        float quietest = gain;
        for (int i = 0; i < _voices.Count; i++)
        {
            var (p, g) = _voices[i];
            if (!p.Playing) { pick = i; break; }
            if (g < quietest) { quietest = g; pick = i; }
        }
        if (pick < 0) return;
        var v = _voices[pick].p;
        v.Stop();
        v.Stream = takes[Rng.Next(takes.Length)];
        v.GlobalPosition = at;
        v.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(gain, 0.001f, 1));
        v.PitchScale = Mathf.Clamp(pitch * (0.94f + 0.12f * (float)Rng.NextDouble()), 0.5f, 2f);
        v.Play();
        _voices[pick] = (v, gain);
        _started++;
    }

    public void PlayAfter(double seconds, Sound s, Vector3 at, float gain, float pitch)
    {
        if (seconds <= 0.001) { Play(s, at, gain, pitch); return; }
        GetTree().CreateTimer(seconds).Timeout += () => { if (IsInstanceValid(this)) Play(s, at, gain, pitch); };
    }

    static int Bus()
    {
        int i = AudioServer.GetBusIndex(BusName);
        if (i >= 0) return i;
        AudioServer.AddBus();
        i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, BusName);
        AudioServer.SetBusSend(i, "Master");
        return i;
    }

    static Dictionary<Sound, AudioStream[]> Load()
    {
        var bank = new Dictionary<Sound, AudioStream[]>();
        foreach (Sound s in Enum.GetValues<Sound>())
        {
            var takes = new List<AudioStream>();
            var name = s.ToString().ToLowerInvariant();
            for (int k = 1; k <= 8; k++)
            {
                var path = $"res://sounds/{name}_{k}.ogg";
                if (!ResourceLoader.Exists(path)) break;
                try { if (ResourceLoader.Load<AudioStream>(path) is { } a) takes.Add(a); }
                catch (Exception e) { GD.PushWarning($"[sound] {path} could not be read: {e.Message}"); }
            }
            if (takes.Count == 0) GD.PushWarning($"[sound] no takes of {name}: it stays silent");
            bank[s] = takes.ToArray();
        }
        return bank;
    }
}

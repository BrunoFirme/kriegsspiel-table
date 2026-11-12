using System.Threading.Tasks;
using Godot;
using KTable.Client;

namespace KTable.App;

sealed class PovFrames : IGifFrames
{
    public const int EyeHeight = 432, AboveSide = 720;

    readonly Sighting _sighting;
    readonly SightingDraft _draft;
    readonly float _times;
    SubViewport? _shot;

    public PovFrames(Sighting sighting, SightingDraft draft, bool large)
    {
        _sighting = sighting;
        _draft = draft;
        _times = large ? GifRecording.LargeTimes : 1;
    }

    public bool Gone => _sighting.Draft != _draft;

    public async Task<Vector2I> Open()
    {
        _shot = await _sighting.OpenPicture(_draft, _draft.FirstPerson ? Scaled(EyeHeight) : _sighting.PictureSide(_draft, Scaled(AboveSide)));
        GifRecording.Sharpen(_shot);
        _cut = _sighting.PictureCut(_shot, _draft);
        _sighting.Recording = true;
        return _cut.Size;
    }

    Rect2I _cut;

    int Scaled(int px) => Mathf.RoundToInt(px * _times);

    public Task Draw(bool grabbing) => _sighting.DrawFrame(_shot!, _draft);

    public Image Grab() => _shot!.GetTexture().GetImage().GetRegion(_cut);

    public void Close()
    {
        _sighting.Recording = false;
        if (_shot != null) _sighting.ClosePicture(_shot);
    }
}

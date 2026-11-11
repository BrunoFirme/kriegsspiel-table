using System;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

sealed class RegionFrames : IGifFrames
{
    public const int LongestSide = 800;

    readonly Table _table;
    readonly Node _host;
    readonly Rect2 _rect;
    readonly int _times;
    SubViewport? _vp;
    Camera3D? _cam;
    Rect2 _cut;

    public RegionFrames(Table table, Node host, Rect2 rect, bool large)
    {
        _table = table;
        _host = host;
        _rect = rect;
        _times = large ? GifRecording.LargeTimes : 1;
    }

    public bool Gone => !GodotObject.IsInstanceValid(_table) || !_table.IsInsideTree() || !GodotObject.IsInstanceValid(_host);

    public async Task<Vector2I> Open()
    {
        var window = _table.GetViewport().GetVisibleRect().Size;
        _cut = _rect.Intersection(new Rect2(Vector2.Zero, window));
        float k = Mathf.Min(_times, LongestSide * _times / Mathf.Max(1f, Mathf.Max(_cut.Size.X, _cut.Size.Y)));
        var size = new Vector2I(Math.Max(2, (int)(_cut.Size.X * k)), Math.Max(2, (int)(_cut.Size.Y * k)));
        _vp = new SubViewport
        {
            Size = size, World3D = _table.GetViewport().FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa2X, TransparentBg = false,
        };
        GifRecording.Sharpen(_vp);
        _cam = new Camera3D { CullMask = Table.WorldLayer | Table.TextLayer, KeepAspect = Camera3D.KeepAspectEnum.Height };
        _vp.AddChild(_cam);
        _host.AddChild(_vp);
        await Draw(true);
        await Draw(true);
        return size;
    }

    void Follow()
    {
        if (_cam == null || !GodotObject.IsInstanceValid(_table)) return;
        var main = _table.Rig.Cam;
        var window = _table.GetViewport().GetVisibleRect().Size;
        if (window.Y < 1 || window.X < 1) return;
        bool flat = main.Projection == Camera3D.ProjectionType.Orthogonal;
        float kept = main.Projection == Camera3D.ProjectionType.Perspective ? 2 * main.Near * Mathf.Tan(Mathf.DegToRad(main.Fov) / 2) : main.Size;
        float tall = main.KeepAspect == Camera3D.KeepAspectEnum.Height ? kept : kept * window.Y / window.X;
        float perUnit = tall / window.Y;
        var shift = (_cut.GetCenter() - window / 2) * perUnit;
        _cam.Near = main.Near;
        _cam.Far = main.Far;
        _cam.Size = _cut.Size.Y * perUnit;
        if (flat)
        {
            _cam.Projection = Camera3D.ProjectionType.Orthogonal;
            _cam.HOffset = main.HOffset + shift.X;
            _cam.VOffset = main.VOffset - shift.Y;
        }
        else
        {
            var from = main.Projection == Camera3D.ProjectionType.Frustum ? main.FrustumOffset : Vector2.Zero;
            _cam.Projection = Camera3D.ProjectionType.Frustum;
            _cam.HOffset = main.HOffset;
            _cam.VOffset = main.VOffset;
            _cam.FrustumOffset = from + new Vector2(shift.X, -shift.Y);
        }
        _cam.GlobalTransform = main.GlobalTransform;
    }

    public async Task Draw(bool grabbing)
    {
        if (!grabbing)
        {
            await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
            return;
        }
        _vp!.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePreDraw);
        Follow();
        await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    public Image Grab() => _vp!.GetTexture().GetImage();

    public void Close()
    {
        if (_vp != null && GodotObject.IsInstanceValid(_vp)) _vp.QueueFree();
    }
}

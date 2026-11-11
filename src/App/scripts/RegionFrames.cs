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
    SubViewport? _vp;
    Camera3D? _cam;
    Rect2I _cut;
    Vector2I _size;

    public RegionFrames(Table table, Node host, Rect2 rect)
    {
        _table = table;
        _host = host;
        _rect = rect;
    }

    public bool Gone => !GodotObject.IsInstanceValid(_table) || !_table.IsInsideTree() || !GodotObject.IsInstanceValid(_host);

    public async Task<Vector2I> Open()
    {
        var window = _table.GetViewport().GetVisibleRect().Size;
        _vp = new SubViewport
        {
            Size = new Vector2I(Math.Max(2, (int)window.X), Math.Max(2, (int)window.Y)), World3D = _table.GetViewport().FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa2X, TransparentBg = false,
        };
        _cam = new Camera3D { CullMask = Table.WorldLayer | Table.TextLayer };
        _vp.AddChild(_cam);
        _host.AddChild(_vp);
        Follow();
        _cut = new Rect2I((Vector2I)_rect.Position, (Vector2I)_rect.Size).Intersection(new Rect2I(Vector2I.Zero, _vp.Size));
        float k = Mathf.Min(1f, LongestSide / (float)Math.Max(1, Math.Max(_cut.Size.X, _cut.Size.Y)));
        _size = new Vector2I(Math.Max(2, (int)(_cut.Size.X * k)), Math.Max(2, (int)(_cut.Size.Y * k)));
        await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return _size;
    }

    void Follow()
    {
        if (_cam == null || !GodotObject.IsInstanceValid(_table)) return;
        var main = _table.Rig.Cam;
        _cam.Projection = main.Projection;
        _cam.Fov = main.Fov;
        _cam.Size = main.Size;
        _cam.Near = main.Near;
        _cam.Far = main.Far;
        _cam.KeepAspect = main.KeepAspect;
        _cam.HOffset = main.HOffset;
        _cam.VOffset = main.VOffset;
        _cam.FrustumOffset = main.FrustumOffset;
        _cam.GlobalTransform = main.GlobalTransform;
    }

    public async Task Draw()
    {
        await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePreDraw);
        Follow();
        await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    public Image Grab()
    {
        var img = _vp!.GetTexture().GetImage().GetRegion(_cut);
        if (img.GetWidth() != _size.X || img.GetHeight() != _size.Y) img.Resize(_size.X, _size.Y, Image.Interpolation.Lanczos);
        return img;
    }

    public void Close()
    {
        if (_vp != null && GodotObject.IsInstanceValid(_vp)) _vp.QueueFree();
    }
}

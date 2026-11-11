using System;
using Godot;

namespace KTable.App;

sealed class TablePicture : HudPart
{
    const int Width = 320, Height = 180, RenderScale = 2;
    const float JpegQuality = 0.82f;

    byte[]? _latest;
    double _takenAt = double.NegativeInfinity;
    bool _taking;

    public TablePicture(HudContext ctx) : base(ctx) { }

    public static double Now => Time.GetTicksMsec() / 1000.0;

    public byte[]? Since(double t) => _takenAt >= t ? _latest : null;

    void TakeOnWaking()
    {
        T.Woke -= TakeOnWaking;
        _afterRest = false;
        Take();
    }

    bool _afterRest;

    public async void Take()
    {
        if (_taking || !GodotObject.IsInstanceValid(HudNode)) return;
        if (T.Resting)
        {
            if (!_afterRest) T.Woke += TakeOnWaking;
            _afterRest = true;
            return;
        }
        _taking = true;
        SubViewport? vp = null;
        try
        {
            vp = new SubViewport
            {
                Size = new Vector2I(Width * RenderScale, Height * RenderScale), World3D = T.GetViewport().FindWorld3D(),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false,
            };
            var main = T.Rig.Cam;
            var cam = new Camera3D
            {
                Projection = main.Projection, Fov = main.Fov, Size = main.Size, Near = main.Near, Far = main.Far,
                KeepAspect = Camera3D.KeepAspectEnum.Width, CullMask = Table.WorldLayer | Table.TextLayer,
            };
            vp.AddChild(cam);
            HudNode.AddChild(vp);
            cam.GlobalTransform = main.GlobalTransform;
            await HudNode.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await HudNode.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!GodotObject.IsInstanceValid(vp)) return;
            var img = vp.GetTexture().GetImage();
            img.Resize(Width, Height, Image.Interpolation.Lanczos);
            _latest = img.SaveJpgToBuffer(JpegQuality);
            _takenAt = Now;
        }
        catch (Exception e) { GD.PushWarning("the table's picture for a save could not be taken: " + e.Message); }
        finally
        {
            if (vp != null && GodotObject.IsInstanceValid(vp)) vp.QueueFree();
            _taking = false;
        }
    }
}

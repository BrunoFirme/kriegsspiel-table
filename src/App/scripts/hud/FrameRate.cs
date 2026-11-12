using System.Globalization;
using Godot;

namespace KTable.App;

public partial class FrameRate : Label
{
    const double Every = 0.5;
    const int FontSize = 12;
    double _spent, _slowest;
    int _frames;

    public FrameRate()
    {
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        Modulate = Ui.Muted;
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeFontSizeOverride("font_size", FontSize);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _spent += delta;
        _frames++;
        if (delta > _slowest) _slowest = delta;
        if (_spent < Every) return;
        Text = Shown(_frames / _spent, _spent / _frames * 1000);
        TooltipText = string.Create(CultureInfo.InvariantCulture, $"Slowest frame of the last half second: {_slowest * 1000:0.0} ms");
        _spent = _slowest = 0;
        _frames = 0;
    }

    public static string Shown(double fps, double ms) => string.Create(CultureInfo.InvariantCulture, $"{fps:0} fps · {ms:0.0} ms");
}

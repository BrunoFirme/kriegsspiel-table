using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class ScenarioDialog
{
    sealed class SunRows
    {
        readonly SunRose _rose;
        readonly SpinBox _box;
        readonly HSlider _height;
        bool _sync;

        public event Action<bool>? Changed;

        public SunRows(GridContainer form, Lighting l)
        {
            form.AddChild(new Label { Text = "From" });
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Gap.Block);
            _rose = new SunRose { From = l.SunFrom, Height = l.SunHeight, Shining = l.SunEnergy > SunLit };
            row.AddChild(_rose);
            _box = new SpinBox { MinValue = 0, MaxValue = 360, Step = 1, Value = Math.Round(l.SunFrom), Suffix = "°", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = "0 north, 90 east, 180 south, 270 west" };
            row.AddChild(_box);
            form.AddChild(row);
            static string Words(double v) => v >= Lighting.Overhead ? "overhead" : $"{v:0}°";
            _height = LightRow(form, "Height", Lighting.LowestSun, Lighting.HighestSun, 1, l.SunHeight, Words);
            _rose.Turned += (deg, final) =>
            {
                _sync = true;
                _box.Value = Math.Round(deg) % 360;
                _sync = false;
                Changed?.Invoke(final);
            };
            _box.ValueChanged += v =>
            {
                if (_sync) return;
                _rose.From = v;
                Changed?.Invoke(true);
            };
            bool dragging = false;
            _height.DragStarted += () => dragging = true;
            _height.ValueChanged += _ => { if (!_sync) Changed?.Invoke(!dragging); };
            _height.DragEnded += _ => { dragging = false; Changed?.Invoke(true); };
        }

        public double From => _rose.From;
        public double Height => _height.Value;

        public void Show(double from, double height)
        {
            _sync = true;
            _rose.From = from;
            _box.Value = Math.Round(from);
            _height.Value = height;
            _sync = false;
        }

        public void Shine(Lighting l)
        {
            _rose.Height = l.SunHeight;
            _rose.Shining = l.SunEnergy > SunLit;
        }
    }
}

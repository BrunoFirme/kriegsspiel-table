using System;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class ScenarioDialog
{
    sealed class DaylightRows
    {
        public readonly GridContainer Form = Ui.Form(Ui.DialogLabels);
        readonly OptionButton _by = new() { TooltipText = "Type when the sun rises and sets, or let the latitude and the game's date say" };
        readonly LineEdit _rise, _set;
        readonly SpinBox _latitude;
        readonly Label _riseName = new() { Text = "Sunrise" }, _setName = new() { Text = "Sunset" }, _latitudeName = new() { Text = "Latitude" };
        const int Typed = 0, ByLatitude = 1;
        const float HourWidth = 72;

        public event Action? Changed;

        public DaylightRows(Lighting l)
        {
            _by.AddItem("Sunrise and sunset", Typed);
            _by.AddItem("Latitude and date", ByLatitude);
            _by.Selected = l.Latitude != null ? ByLatitude : Typed;
            _by.ItemSelected += _ =>
            {
                Show();
                Changed?.Invoke();
            };
            _rise = Hour(l.Sunrise ?? DayLight.UsualSunrise);
            _set = Hour(l.Sunset ?? DayLight.UsualSunset);
            _latitude = new SpinBox
            {
                MinValue = -Lighting.MostLatitude, MaxValue = Lighting.MostLatitude, Step = 0.5, Value = l.Latitude ?? 0, Suffix = "°",
                TooltipText = "North of the equator is above zero, south of it below",
            };
            _latitude.ValueChanged += _ => Changed?.Invoke();
            Form.AddChild(new Label { Text = "Daylight" });
            Form.AddChild(_by);
            foreach (var c in new Control[] { _riseName, _rise, _setName, _set, _latitudeName, _latitude }) Form.AddChild(c);
            Ui.AlignForm(Form);
            Show();
        }

        LineEdit Hour(double hour)
        {
            var field = new LineEdit { Text = DayLight.HourWords(hour), CustomMinimumSize = new Vector2(HourWidth, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, TooltipText = "An hour of the day, as 05:30" };
            string kept = field.Text;
            void Take()
            {
                var said = DayLight.HourTyped(field.Text) is { } typed ? DayLight.HourWords(typed) : kept;
                field.Text = said;
                if (said == kept) return;
                kept = said;
                Changed?.Invoke();
            }
            field.TextSubmitted += _ => Take();
            field.FocusExited += Take;
            return field;
        }

        void Show()
        {
            bool typed = _by.Selected != ByLatitude;
            _riseName.Visible = _rise.Visible = _setName.Visible = _set.Visible = typed;
            _latitudeName.Visible = _latitude.Visible = !typed;
        }

        public void Into(Lighting l)
        {
            l.Latitude = _by.Selected == ByLatitude ? _latitude.Value : null;
            l.Sunrise = DayLight.HourTyped(_rise.Text) is { } rise && rise != DayLight.UsualSunrise ? rise : null;
            l.Sunset = DayLight.HourTyped(_set.Text) is { } set && set != DayLight.UsualSunset ? set : null;
        }
    }

    string ClockLine(Lighting look)
    {
        var sunNow = DayLight.SunWords(DayLight.At(S.View.Clock, look));
        var (rise, set) = DayLight.Daylight(S.View.Clock, look);
        return $"{Fmt.Clock(S.View.Clock)}: {DayLight.Describe(S.View.Clock, look)}{(sunNow.Length > 0 ? ", " + sunNow : "")} · sunrise {DayLight.HourWords(rise)}, sunset {DayLight.HourWords(set)}";
    }
}

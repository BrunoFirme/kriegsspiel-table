using KTable.Server;

namespace KTable.Client.Hand;

public readonly record struct HandAt(float X, float Z, float Y);

public sealed class CarryShare
{
    const double LayoutEverySeconds = 0.5;
    const double PlaceEverySeconds = FlightShare.EverySeconds;

    List<CarriedPiece> _layout = [];
    double _layoutAt = double.NegativeInfinity, _placeAt = double.NegativeInfinity;
    HandAt _at;
    bool _sharing;

    public event Action<IReadOnlyList<CarriedPiece>, HandAt, double>? SendLayout;
    public event Action<HandAt, double>? SendPlace;

    public void Tick(double now, IReadOnlyList<CarriedPiece> held, HandAt at)
    {
        if (held.Count == 0)
        {
            if (!_sharing) return;
            _sharing = false;
            _layout = [];
            SendLayout?.Invoke([], at, now);
            return;
        }
        var samePieces = _layout.Select(p => (p.Layer, p.Id)).SequenceEqual(held.Select(p => (p.Layer, p.Id)));
        var reshaped = !_layout.SequenceEqual(held) && now - _layoutAt >= PlaceEverySeconds;
        if (!_sharing || !samePieces || reshaped || now - _layoutAt >= LayoutEverySeconds)
        {
            _sharing = true;
            _layout = held.ToList();
            _layoutAt = _placeAt = now;
            _at = at;
            SendLayout?.Invoke(_layout, at, now);
            return;
        }
        if (now - _placeAt < PlaceEverySeconds - FlightShare.SlackSeconds || at == _at) return;
        _placeAt = now;
        _at = at;
        SendPlace?.Invoke(at, now);
    }
}

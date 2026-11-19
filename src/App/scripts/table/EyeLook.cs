using Godot;

namespace KTable.App;

sealed class EyeLook
{
    const string ReliefParameter = "ground_relief", CloudParameter = "cloud_shade", CloudScaleParameter = "cloud_scale", CloudDriftParameter = "cloud_drift";
    const float CloudM = 600, CloudDark = 0.55f, CloudSpeedMs = 6, LeastWind = 0.25f, MostWind = 1.5f, SkyDrift = 0.012f;

    readonly TableContext _c;
    readonly TableStage _stage;
    readonly Table _table;
    GroundGrass? _grass;
    EyeHorizon? _horizon;
    Wanted _shown;
    (float Shade, float Scale, Vector2 Drift) _cloud;
    Vector2 _windSeen = new(float.NaN, 0);
    bool _treesSway;

    public readonly record struct Wanted(bool Haze, bool Sun, bool Relief, bool Footing, bool Grass, float UnitsPerMetre, bool World = false);

    public EyeLook(TableContext c, TableStage stage, Table table) => (_c, _stage, _table) = (c, stage, table);

    public Wanted Shown => _shown;

    public GroundGrass? Grass => _grass;

    public EyeHorizon? Horizon => _horizon;

    public float CloudShade => _cloud.Shade;

    public void Tick(bool immersed, float walkerUnitsPerMetre, Camera3D cam)
    {
        float map = (float)(_c.S?.View.Calibration.UnitsPerMeter ?? 0);
        var cfg = _c.Settings;
        bool on = immersed && cfg != null;
        var want = new Wanted(on && cfg!.EyeHaze, on && cfg!.EyeSun, on && cfg!.EyeRelief, on && cfg!.EyeFooting, on && cfg!.EyeGrass && map > 0,
            map > 0 ? map : walkerUnitsPerMetre, on);
        _stage.Haze(want.Haze, want.UnitsPerMetre);
        _stage.Filmic(on && cfg!.EyeFilmic);
        if (want != _shown) Show(want);
        if (want.Grass) (_grass ??= new GroundGrass(_table)).Show(cam, want.UnitsPerMetre, cfg!.EyeGrassReachM);
        else _grass?.Hide();
        if (want.World) (_horizon ??= new EyeHorizon(_table)).Show(want.UnitsPerMetre);
        else _horizon?.Hide();
        var wind = Wind();
        Blow(wind, on);
        Cloud(on && cfg!.EyeClouds ? CloudDark * _stage.SunShare : 0, want.UnitsPerMetre, wind);
    }

    static Vector2 Wind()
    {
        var flat = new Vector2(EffectFx.WindNow.X, EffectFx.WindNow.Z);
        return flat.Length() > 1e-4f ? flat.Normalized() * Mathf.Clamp(flat.Length(), LeastWind, MostWind) : new Vector2(LeastWind, 0);
    }

    void Blow(Vector2 wind, bool sway)
    {
        if (wind == _windSeen && sway == _treesSway) return;
        if (wind != _windSeen) RaisedSmoke.Blow(wind);
        (_windSeen, _treesSway) = (wind, sway);
        TableRaised.TreeMaterial.SetShaderParameter("wind", sway ? wind : Vector2.Zero);
        _stage.Drift(sway ? wind * SkyDrift : Vector2.Zero);
    }

    void Cloud(float shade, float unitsPerMetre, Vector2 wind)
    {
        var want = (shade, unitsPerMetre > 0 ? 1 / (CloudM * unitsPerMetre) : 0, wind * (CloudSpeedMs / CloudM));
        if (want == _cloud) return;
        _cloud = want;
        RenderingServer.GlobalShaderParameterSet(CloudParameter, want.Item1);
        RenderingServer.GlobalShaderParameterSet(CloudScaleParameter, want.Item2);
        RenderingServer.GlobalShaderParameterSet(CloudDriftParameter, want.Item3);
    }

    void Show(Wanted want)
    {
        _shown = want;
        GroundTiles.Sunlit = want.Sun;
        GroundTiles.Foot(want.Footing);
        GroundTiles.World(want.World);
        _stage.EyeShadows(want.World, want.UnitsPerMetre);
        _table.MapLettering.Root.Visible = !want.World;
        _table.Contours.Root.Visible = !want.World;
        RenderingServer.GlobalShaderParameterSet(ReliefParameter, want.Relief ? 1f : 0f);
    }

    public void Leave()
    {
        _stage.Haze(false, 0);
        _stage.Filmic(false);
        Cloud(0, 0, Vector2.Zero);
        TableRaised.TreeMaterial.SetShaderParameter("wind", Vector2.Zero);
        _stage.Drift(Vector2.Zero);
        Show(default);
        _grass?.Drop();
        _grass = null;
        _horizon?.Drop();
        _horizon = null;
    }
}

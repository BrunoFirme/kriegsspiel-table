namespace KTable.Client.Scenery;

public interface IPropNoise
{
    float GetNoise2D(float x, float y);
    float GetNoise3D(float x, float y, float z);
}

public delegate IPropNoise PerlinNoise(int seed, float frequency);

using KTable.Client.Geometry;

namespace KTable.Client;

public readonly record struct Rgba(float R, float G, float B, float A)
{
    public static readonly Rgba White = new(1, 1, 1, 1);

    public Rgba(float r, float g, float b) : this(r, g, b, 1) { }

    public Rgba Lerp(Rgba to, float weight) => new(Mathf.Lerp(R, to.R, weight), Mathf.Lerp(G, to.G, weight), Mathf.Lerp(B, to.B, weight), Mathf.Lerp(A, to.A, weight));
}

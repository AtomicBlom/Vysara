using System.Numerics;

namespace BinaryVibrance.Vysara.Drawing;

/// <summary>
/// The pixel scale a path is tessellated for, rounded to buckets half an octave wide, so a path that scales a little
/// keeps its tessellation and one that zooms a lot gets a new one. Curves are flattened to within a quarter pixel at the
/// top of the bucket of the largest scale the transform applies; antialiasing reaches half a pixel past each edge at
/// the bottom of the bucket of the smallest, so it is never narrower than a pixel however the bucket rounds.
/// </summary>
public readonly record struct TessellationScale
{
	private const int MinBucket = -32;
	private const int MaxBucket = 32;

	private TessellationScale(int flattenBucket, int marginBucket)
	{
		FlattenBucket = flattenBucket;
		MarginBucket = marginBucket;
	}

	/// <summary>The bucket of the largest pixels-per-unit the transform applies: <c>floor(2 log2 scale)</c>.</summary>
	public int FlattenBucket { get; }

	/// <summary>The bucket of the smallest pixels-per-unit the transform applies.</summary>
	public int MarginBucket { get; }

	/// <summary>One unit to one pixel.</summary>
	public static TessellationScale Identity { get; } = For(1f);

	/// <summary>The flattening tolerance in local units: a quarter pixel at the top of the flattening bucket.</summary>
	public float Tolerance => 0.25f / MathF.Pow(2f, (FlattenBucket + 1) * 0.5f);

	/// <summary>Half a pixel in local units at the bottom of the margin bucket: how far antialiasing reaches past an
	/// edge.</summary>
	public float HalfPixel => 0.5f / MathF.Pow(2f, MarginBucket * 0.5f);

	/// <summary>The scale of a uniform transform of <paramref name="pixelsPerUnit"/>.</summary>
	public static TessellationScale For(float pixelsPerUnit) => new(Bucket(pixelsPerUnit), Bucket(pixelsPerUnit));

	/// <summary>The scale of a local-to-pixel transform: its largest and smallest stretch (singular values), so a
	/// non-uniform scale or a skew flattens for its longest axis and antialiases for its shortest.</summary>
	public static TessellationScale For(in Matrix3x2 localToPixels)
	{
		var stretch = TransformStretch.Of(localToPixels);
		return new TessellationScale(Bucket(stretch.Largest), Bucket(stretch.Smallest));
	}

	private static int Bucket(float scale) =>
		!(scale > 0) || !float.IsFinite(scale)
			? scale > 0 ? MaxBucket : MinBucket
			: Math.Clamp((int)MathF.Floor(2f * MathF.Log2(scale)), MinBucket, MaxBucket);
}

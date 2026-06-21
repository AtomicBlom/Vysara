using System.Numerics;

namespace SUIsei;

/// <summary>
/// A composition-tree post effect applied to a group's render-to-texture layer (PlanningUI.md §D2.5). A visual that
/// carries an effect is always composited as one bitmap (exactly like a sub-1 group opacity), and the effect is
/// applied when the layer is composited back into its parent. <see cref="Id"/> selects the effect the backend runs;
/// <see cref="P0"/>..<see cref="P2"/> are its parameters. Id 0 is the identity (opacity-only); id 1 is a Gaussian
/// blur whose pixel sigma is <see cref="P0"/>.
/// </summary>
public readonly record struct LayerEffect(int Id, float P0 = 0f, float P1 = 0f, float P2 = 0f)
{
	/// <summary>A Gaussian blur of the given pixel sigma.</summary>
	public static LayerEffect Blur(float sigma) => new(1, sigma);

	/// <summary>Pack into the <c>BeginLayer</c> marker's <see cref="Drawing.OverlayVertex.Custom"/> channel.</summary>
	internal Vector4 ToParams() => new(Id, P0, P1, P2);
}

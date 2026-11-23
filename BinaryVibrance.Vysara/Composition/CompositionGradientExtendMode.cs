namespace BinaryVibrance.Vysara.Composition;

/// <summary>What a gradient paints before its start and past its end, as WinUI's <c>CompositionGradientExtendMode</c>.</summary>
public enum CompositionGradientExtendMode
{
	/// <summary>The first stop's colour before the first stop, the last stop's after the last.</summary>
	Clamp,

	/// <summary>The gradient repeats, each repeat starting again from its first stop.</summary>
	Wrap,

	/// <summary>The gradient repeats, every other repeat running backwards, so the colour never jumps.</summary>
	Mirror,
}

namespace SUIsei.Composition;

/// <summary>How a surface brush scales its image into the box it paints, as WinUI's <c>CompositionStretch</c>.</summary>
public enum CompositionStretch
{
	/// <summary>The image keeps its own size.</summary>
	None,

	/// <summary>The image is scaled to the box each way, its aspect ratio ignored.</summary>
	Fill,

	/// <summary>The image is scaled by the same amount each way to fit inside the box.</summary>
	Uniform,

	/// <summary>The image is scaled by the same amount each way to cover the box.</summary>
	UniformToFill,
}

namespace BinaryVibrance.Vysara.Media;

/// <summary>What a gradient brush's points are measured in, as WinUI's <c>BrushMappingMode</c>.</summary>
public enum BrushMappingMode
{
	/// <summary>DIPs in the local space of the element painted.</summary>
	Absolute,

	/// <summary>Fractions of the area painted: (0,0) is its top left and (1,1) its bottom right.</summary>
	RelativeToBoundingBox,
}

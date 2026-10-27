namespace SUIsei;

/// <summary>What a gradient's points are measured in, as WinUI's <c>CompositionMappingMode</c>.</summary>
public enum CompositionMappingMode
{
	/// <summary>Local units of the content painted, the same space as its size.</summary>
	Absolute,

	/// <summary>Fractions of the box painted: (0,0) is its top left and (1,1) its bottom right.</summary>
	Relative,
}

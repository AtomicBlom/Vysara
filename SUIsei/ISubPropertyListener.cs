namespace SUIsei;

/// <summary>
/// Something that shows a shared sub-property object (a brush, a transform, a gradient stop, a geometry) and is told
/// when that object changes, so it can bring what it draws up to date without re-reading anything else. An element
/// listens to the brushes, transform and clip geometry it shows while it is in a live tree; a brush listens to its
/// gradient stops, a transform group to its children, and a geometry to its figures, segments and points.
/// </summary>
internal interface ISubPropertyListener
{
	/// <summary><paramref name="source"/>, which this listener shows, changed what it paints, how it transforms or the shape
	/// it outlines. It is usually a dependency object; a <see cref="Media.PointCollection"/> or
	/// <see cref="Media.DoubleCollection"/>, which as in WinUI is not one, reports itself.</summary>
	void OnSubPropertyChanged(object source);
}

namespace SUIsei;

/// <summary>
/// Something that shows a shared sub-property object (a brush, a transform, a gradient stop) and is told when that
/// object changes, so it can bring what it draws up to date without re-reading anything else. An element listens to the
/// brushes and transform it shows while it is in a live tree; a brush listens to its gradient stops, and a transform
/// group to its children.
/// </summary>
internal interface ISubPropertyListener
{
	/// <summary><paramref name="source"/>, which this listener shows, changed what it paints or how it transforms.</summary>
	void OnSubPropertyChanged(DependencyObject source);
}

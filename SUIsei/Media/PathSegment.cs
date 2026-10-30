using System.Globalization;
using System.Text;
using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>
/// One piece of a <see cref="PathFigure"/>'s outline, drawn from where the piece before it ended, as WinUI's
/// <c>PathSegment</c>. A segment belongs to one figure's <see cref="PathFigure.Segments"/> at a time.
/// </summary>
public abstract class PathSegment : DependencyObject, ISubPropertyListener
{
	private ListenerList _listeners;

	private protected PathSegment()
	{
	}

	/// <summary>Whether a segment collection holds this segment.</summary>
	internal bool IsOwned => _listeners.Count > 0;

	/// <summary>Draw this segment into <paramref name="path"/>, from its current point.</summary>
	internal abstract void AppendTo(CompositionPath path);

	/// <summary>Write this segment as path markup: a command letter and its absolute numbers.</summary>
	internal abstract void AppendMarkup(StringBuilder sb);

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	private protected void Changed() => _listeners.Notify(this);

	void ISubPropertyListener.OnSubPropertyChanged(object source) => Changed();

	/// <summary>The change callback of a property that only changes what the segment draws.</summary>
	private protected static void OnChanged<TOwner, TValue>(TOwner segment, TValue oldValue, TValue newValue) where TOwner : PathSegment =>
		segment.Changed();

	/// <summary>The change callback of a segment's <see cref="PointCollection"/> property: listen to the new
	/// collection instead of the old one.</summary>
	private protected static void OnPointsChanged<TOwner>(TOwner segment, PointCollection? oldValue, PointCollection? newValue) where TOwner : PathSegment
	{
		oldValue?.RemoveListener(segment);
		newValue?.AddListener(segment);
		segment.Changed();
	}

	/// <summary>A poly segment's points: the collection it holds, or a new empty one it then holds, as WinUI creates a
	/// segment's collection on first use.</summary>
	private protected PointCollection GetOrCreatePoints<TOwner>(DependencyProperty<TOwner, PointCollection?> property) where TOwner : PathSegment
	{
		if (GetValue(property) is { } points)
			return points;
		points = [];
		SetValue(property, points);
		return points;
	}

	/// <summary>Append " x,y" for each point, with round-trippable numbers.</summary>
	private protected static void AppendPoints(StringBuilder sb, ReadOnlySpan<Point> points)
	{
		foreach (var p in points)
			AppendPoint(sb, p);
	}

	private protected static void AppendPoint(StringBuilder sb, Point p) =>
		sb.Append(CultureInfo.InvariantCulture, $" {p.X:R},{p.Y:R}");
}

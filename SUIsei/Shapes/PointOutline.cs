using SUIsei.Media;

namespace SUIsei.Shapes;

/// <summary>
/// The outline of a <see cref="Polyline"/> or a <see cref="Polygon"/>: one figure through its points, closed for a
/// polygon, rebuilt in place on the first read after they change, so moving the points allocates nothing.
/// </summary>
internal sealed class PointOutline(bool closed)
{
	private readonly CompositionPath _path = new();
	private bool _current;

	/// <summary>The points or the fill rule changed: rebuild on the next read.</summary>
	public void Invalidate() => _current = false;

	/// <summary>The outline through <paramref name="points"/>, filled by <paramref name="fillRule"/>.</summary>
	public CompositionPath Build(PointCollection? points, FillRule fillRule)
	{
		if (_current)
			return _path;
		_path.Clear();
		_path.FillRule = fillRule;
		var span = points is null ? default : points.AsSpan();
		if (span.Length > 0)
		{
			_path.MoveTo(span[0].ToVector2());
			for (var i = 1; i < span.Length; i++)
				_path.LineTo(span[i].ToVector2());
			if (closed)
				_path.Close();
		}
		_current = true;
		return _path;
	}

	/// <summary>Start or stop <paramref name="listener"/> hearing changes to <paramref name="points"/>.</summary>
	public static void Listen(ISubPropertyListener listener, PointCollection? points, bool listen)
	{
		if (points is null)
			return;
		if (listen)
			points.AddListener(listener);
		else
			points.RemoveListener(listener);
	}
}

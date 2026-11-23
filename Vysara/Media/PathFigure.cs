using System.Globalization;
using System.Text;
using Vysara.Composition;

namespace Vysara.Media;

/// <summary>
/// One connected outline of a <see cref="PathGeometry"/>, as WinUI's <c>PathFigure</c>: it starts at
/// <see cref="StartPoint"/> and runs through its <see cref="Segments"/> in order. A figure belongs to one geometry's
/// <see cref="PathGeometry.Figures"/> at a time.
/// </summary>
[ContentProperty(Name = nameof(Segments))]
public sealed class PathFigure : DependencyObject, ISubPropertyListener
{
	public static readonly DependencyProperty<PathFigure, Point> StartPointProperty =
		DependencyProperty.Register(nameof(StartPoint), new PropertyMetadata<PathFigure, Point> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<PathFigure, bool> IsClosedProperty =
		DependencyProperty.Register(nameof(IsClosed), new PropertyMetadata<PathFigure, bool> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<PathFigure, bool> IsFilledProperty =
		DependencyProperty.Register(nameof(IsFilled), new PropertyMetadata<PathFigure, bool> { DefaultValue = true, PropertyChanged = OnChanged });

	public static readonly DependencyProperty<PathFigure, PathSegmentCollection?> SegmentsProperty =
		DependencyProperty.Register(nameof(Segments), new PropertyMetadata<PathFigure, PathSegmentCollection?>
		{
			PropertyChanged = static (figure, oldValue, newValue) =>
			{
				oldValue?.RemoveListener(figure);
				newValue?.AddListener(figure);
				figure.Changed();
			},
		});

	private ListenerList _listeners;

	/// <summary>Where the figure starts.</summary>
	public Point StartPoint { get => GetValue(StartPointProperty); set => SetValue(StartPointProperty, value); }

	/// <summary>Whether a line joins the last segment's end back to <see cref="StartPoint"/>. False by default.</summary>
	public bool IsClosed { get => GetValue(IsClosedProperty); set => SetValue(IsClosedProperty, value); }

	/// <summary>Whether the figure's interior takes part in a fill. True by default.</summary>
	public bool IsFilled { get => GetValue(IsFilledProperty); set => SetValue(IsFilledProperty, value); }

	/// <summary>The segments, drawn in order from <see cref="StartPoint"/>.</summary>
	public PathSegmentCollection Segments
	{
		get
		{
			if (GetValue(SegmentsProperty) is { } segments)
				return segments;
			segments = [];
			SetValue(SegmentsProperty, segments);
			return segments;
		}
		set => SetValue(SegmentsProperty, value);
	}

	/// <summary>Whether a figure collection holds this figure.</summary>
	internal bool IsOwned => _listeners.Count > 0;

	internal void AppendTo(CompositionPath path)
	{
		path.MoveTo(StartPoint.ToVector2(), IsFilled);
		if (GetValue(SegmentsProperty) is { } segments)
		{
			for (var i = 0; i < segments.Count; i++)
				segments[i].AppendTo(path);
		}
		if (IsClosed)
			path.Close();
	}

	internal void AppendMarkup(StringBuilder sb)
	{
		sb.Append('M');
		sb.Append(CultureInfo.InvariantCulture, $" {StartPoint.X:R},{StartPoint.Y:R}");
		if (GetValue(SegmentsProperty) is { } segments)
		{
			for (var i = 0; i < segments.Count; i++)
				segments[i].AppendMarkup(sb);
		}
		if (IsClosed)
			sb.Append(" Z");
	}

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	private void Changed() => _listeners.Notify(this);

	void ISubPropertyListener.OnSubPropertyChanged(object source) => Changed();

	private static void OnChanged<TValue>(PathFigure figure, TValue oldValue, TValue newValue) => figure.Changed();
}

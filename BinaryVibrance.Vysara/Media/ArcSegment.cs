using System.Globalization;
using System.Numerics;
using System.Text;
using BinaryVibrance.Vysara.Composition;

namespace BinaryVibrance.Vysara.Media;

/// <summary>
/// An elliptical arc to <see cref="Point"/>, as WinUI's <c>ArcSegment</c>: part of an ellipse with radii
/// <see cref="Size"/>, rotated by <see cref="RotationAngle"/> degrees. Of the four arcs such an ellipse can draw between
/// the two points, <see cref="IsLargeArc"/> and <see cref="SweepDirection"/> pick one. Radii too small to reach the end
/// grow, keeping their ratio, until they just do.
/// </summary>
public sealed class ArcSegment : PathSegment
{
	public static readonly DependencyProperty<ArcSegment, Point> PointProperty =
		DependencyProperty.Register(nameof(Point), new PropertyMetadata<ArcSegment, Point> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<ArcSegment, Size> SizeProperty =
		DependencyProperty.Register(nameof(Size), new PropertyMetadata<ArcSegment, Size> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<ArcSegment, float> RotationAngleProperty =
		DependencyProperty.Register(nameof(RotationAngle), new PropertyMetadata<ArcSegment, float> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<ArcSegment, bool> IsLargeArcProperty =
		DependencyProperty.Register(nameof(IsLargeArc), new PropertyMetadata<ArcSegment, bool> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<ArcSegment, SweepDirection> SweepDirectionProperty =
		DependencyProperty.Register(nameof(SweepDirection), new PropertyMetadata<ArcSegment, SweepDirection> { PropertyChanged = OnChanged });

	/// <summary>Where the arc ends.</summary>
	public Point Point { get => GetValue(PointProperty); set => SetValue(PointProperty, value); }

	/// <summary>The ellipse's x and y radii. A zero radius draws a straight line.</summary>
	public Size Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

	/// <summary>How far the ellipse's x axis is rotated, in degrees.</summary>
	public float RotationAngle { get => GetValue(RotationAngleProperty); set => SetValue(RotationAngleProperty, value); }

	/// <summary>Whether the arc is the one that sweeps more than 180°.</summary>
	public bool IsLargeArc { get => GetValue(IsLargeArcProperty); set => SetValue(IsLargeArcProperty, value); }

	/// <summary>Which way the arc turns, as seen on screen.</summary>
	public SweepDirection SweepDirection { get => GetValue(SweepDirectionProperty); set => SetValue(SweepDirectionProperty, value); }

	internal override void AppendTo(CompositionPath path) =>
		path.ArcTo(new Vector2(Size.Width, Size.Height), RotationAngle, IsLargeArc, SweepDirection.ToComposition(), Point.ToVector2());

	internal override void AppendMarkup(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(c, $" A {Size.Width:R},{Size.Height:R} {RotationAngle:R} {(IsLargeArc ? 1 : 0)} {(SweepDirection == SweepDirection.Clockwise ? 1 : 0)}");
		AppendPoint(sb, Point);
	}
}

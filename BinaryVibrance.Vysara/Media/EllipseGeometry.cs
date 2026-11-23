using System.Numerics;
using BinaryVibrance.Vysara.Composition;

namespace BinaryVibrance.Vysara.Media;

/// <summary>An axis-aligned ellipse around <see cref="Center"/>, as WinUI's <c>EllipseGeometry</c>. A zero radius draws
/// nothing.</summary>
public sealed class EllipseGeometry : Geometry
{
	public static readonly DependencyProperty<EllipseGeometry, Point> CenterProperty =
		DependencyProperty.Register(nameof(Center), new PropertyMetadata<EllipseGeometry, Point> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<EllipseGeometry, float> RadiusXProperty =
		DependencyProperty.Register(nameof(RadiusX), new PropertyMetadata<EllipseGeometry, float> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<EllipseGeometry, float> RadiusYProperty =
		DependencyProperty.Register(nameof(RadiusY), new PropertyMetadata<EllipseGeometry, float> { PropertyChanged = OnChanged });

	public Point Center { get => GetValue(CenterProperty); set => SetValue(CenterProperty, value); }

	public float RadiusX { get => GetValue(RadiusXProperty); set => SetValue(RadiusXProperty, value); }

	public float RadiusY { get => GetValue(RadiusYProperty); set => SetValue(RadiusYProperty, value); }

	private protected override void BuildPath(CompositionPath path)
	{
		var radii = new Vector2(MathF.Abs(RadiusX), MathF.Abs(RadiusY));
		if (radii.X == 0 || radii.Y == 0)
			return;
		// Two half-ellipse arcs from the rightmost point, clockwise on screen as WinUI's ellipse runs.
		var c = Center.ToVector2();
		path.MoveTo(c + new Vector2(radii.X, 0));
		path.ArcTo(radii, 0, false, CompositionSweepDirection.Clockwise, c - new Vector2(radii.X, 0));
		path.ArcTo(radii, 0, false, CompositionSweepDirection.Clockwise, c + new Vector2(radii.X, 0));
		path.Close();
	}
}

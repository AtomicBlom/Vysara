using System.Numerics;
using SUIsei.Composition;

namespace SUIsei.Shapes;

/// <summary>
/// A rectangle, its corners optionally rounded, as WinUI's <c>Rectangle</c>. It is sized by the space it is given rather
/// than by an outline of its own: its <see cref="Shape.Stretch"/> is <see cref="Stretch.Fill"/> by default, and its
/// stroke lies inside that space. It asks only for room for its pen, so give it a size or a stretching layout slot.
/// </summary>
public sealed class Rectangle : Shape
{
	/// <summary>The corners' radius along x.</summary>
	public static readonly DependencyProperty<Rectangle, float> RadiusXProperty =
		DependencyProperty.Register(nameof(RadiusX), new PropertyMetadata<Rectangle, float> { PropertyChanged = static (r, _, _) => r.InvalidateContent() });

	/// <summary>The corners' radius along y.</summary>
	public static readonly DependencyProperty<Rectangle, float> RadiusYProperty =
		DependencyProperty.Register(nameof(RadiusY), new PropertyMetadata<Rectangle, float> { PropertyChanged = static (r, _, _) => r.InvalidateContent() });

	private readonly CompositionPath _path = new();
	private (Rect Box, Vector2 Radii) _built = (new Rect(float.NaN, 0f, 0f, 0f), default);

	/// <summary>The corners' radius along x: with <see cref="RadiusY"/> they are rounded by quarter ellipses, each radius
	/// at most half the side it rounds. 0, the default, or a 0 <see cref="RadiusY"/>, gives square corners.</summary>
	public float RadiusX { get => GetValue(RadiusXProperty); set => SetValue(RadiusXProperty, value); }

	/// <summary>The corners' radius along y; see <see cref="RadiusX"/>.</summary>
	public float RadiusY { get => GetValue(RadiusYProperty); set => SetValue(RadiusYProperty, value); }

	private protected override Stretch DefaultStretch => Stretch.Fill;

	private protected override CompositionPath DefiningPath
	{
		get
		{
			var box = BoxIn(new Size(LayoutRect.Width, LayoutRect.Height));
			var radii = new Vector2(RadiusX, RadiusY);
			if (_built != (box, radii))
			{
				_built = (box, radii);
				_path.Clear();
				_path.AddRoundedRectangle(box, radii);
			}
			return _path;
		}
	}

	protected override Size MeasureOverride(Size available) => MeasureBox(available);

	protected override Size ArrangeOverride(Size finalSize) => finalSize;
}

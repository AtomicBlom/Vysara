using SUIsei.Composition;

namespace SUIsei.Shapes;

/// <summary>
/// An ellipse, as WinUI's <c>Ellipse</c>: the one inscribed in the space it is given, its <see cref="Shape.Stretch"/>
/// <see cref="Stretch.Fill"/> by default (a circle with <see cref="Stretch.Uniform"/>), its stroke inside that space.
/// It asks only for room for its pen, so give it a size or a stretching layout slot.
/// </summary>
public sealed class Ellipse : Shape
{
	private readonly CompositionPath _path = new();
	private Rect _built = new(float.NaN, 0f, 0f, 0f);

	private protected override Stretch DefaultStretch => Stretch.Fill;

	private protected override CompositionPath DefiningPath
	{
		get
		{
			var box = BoxIn(new Size(LayoutRect.Width, LayoutRect.Height));
			if (_built != box)
			{
				_built = box;
				_path.Clear();
				_path.AddEllipse(box);
			}
			return _path;
		}
	}

	protected override Size MeasureOverride(Size available) => MeasureBox(available);

	protected override Size ArrangeOverride(Size finalSize) => finalSize;
}

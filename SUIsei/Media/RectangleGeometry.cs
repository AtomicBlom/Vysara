using System.Numerics;
using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>
/// An axis-aligned rectangle, as WinUI's <c>RectangleGeometry</c>: what <see cref="UIElement.Clip"/> takes. In XAML,
/// <c>Rect="x,y,width,height"</c>.
/// </summary>
public sealed class RectangleGeometry : Geometry
{
	public static readonly DependencyProperty<RectangleGeometry, Rect> RectProperty =
		DependencyProperty.Register(nameof(Rect), new PropertyMetadata<RectangleGeometry, Rect> { PropertyChanged = OnChanged });

	/// <summary>The rectangle, in the coordinates of whatever draws or clips with it.</summary>
	public Rect Rect { get => GetValue(RectProperty); set => SetValue(RectProperty, value); }

	private protected override void BuildPath(CompositionPath path)
	{
		var r = Rect;
		if (r.Width <= 0 || r.Height <= 0)
			return;
		path.MoveTo(new Vector2(r.X, r.Y));
		path.LineTo(new Vector2(r.Right, r.Y));
		path.LineTo(new Vector2(r.Right, r.Bottom));
		path.LineTo(new Vector2(r.X, r.Bottom));
		path.Close();
	}
}

namespace Vysara.Media;

/// <summary>
/// Maps points and rects from one coordinate space to another, as WinUI's <c>GeneralTransform</c>: the base of every
/// <see cref="Transform"/>, and what <see cref="UIElement.TransformToVisual"/> returns. A transform that cannot map a
/// point (one into an element scaled to nothing) reports it through <see cref="TryTransform"/>.
/// </summary>
public abstract class GeneralTransform : DependencyObject
{
	/// <summary>The inverse transform, or null when there is none.</summary>
	public GeneralTransform? Inverse => InverseCore;

	/// <summary>Map a point.</summary>
	/// <exception cref="InvalidOperationException">The transform cannot map the point.</exception>
	public Point TransformPoint(Point point) =>
		TryTransform(point, out var result) ? result : throw new InvalidOperationException("The transform cannot map this point.");

	/// <summary>Map a point, returning false when the transform cannot.</summary>
	public bool TryTransform(Point inPoint, out Point outPoint) => TryTransformCore(inPoint, out outPoint);

	/// <summary>The axis-aligned bounds of <paramref name="rect"/> once mapped.</summary>
	public Rect TransformBounds(Rect rect) => TransformBoundsCore(rect);

	protected abstract GeneralTransform? InverseCore { get; }

	protected abstract bool TryTransformCore(Point inPoint, out Point outPoint);

	protected abstract Rect TransformBoundsCore(Rect rect);
}

using System.Numerics;

namespace Vysara.Media;

/// <summary>
/// A 2D affine transform, as WinUI's <c>Transform</c>: an element's <see cref="UIElement.RenderTransform"/>, or a child
/// of a <see cref="TransformGroup"/>. Its properties are dependency properties, so a storyboard animates them through
/// paths such as <c>(UIElement.RenderTransform).(CompositeTransform.TranslateX)</c>. A change moves every element showing
/// the transform on the visual path, with no layout. Like a brush, a transform may be shared: an element registers with
/// it while it is in a live tree.
/// </summary>
public abstract class Transform : GeneralTransform
{
	private ListenerList _listeners;

	private protected Transform()
	{
	}

	/// <summary>The transform as a matrix, for row vectors: a point maps as <c>Vector2.Transform(p, matrix)</c>.</summary>
	internal abstract Matrix3x2 ToMatrix();

	/// <summary>How many uses by live elements and groups are registered.</summary>
	internal int ListenerCount => _listeners.Count;

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	/// <summary>Tell everything showing this transform that it changed.</summary>
	private protected void Changed() => _listeners.Notify(this);

	protected override GeneralTransform? InverseCore =>
		Matrix3x2.Invert(ToMatrix(), out var inverse) ? new MatrixTransform { Matrix = inverse } : null;

	protected override bool TryTransformCore(Point inPoint, out Point outPoint)
	{
		var p = Vector2.Transform(inPoint.ToVector2(), ToMatrix());
		outPoint = new Point(p.X, p.Y);
		return float.IsFinite(p.X) && float.IsFinite(p.Y);
	}

	protected override Rect TransformBoundsCore(Rect rect) => rect.TransformBounds(ToMatrix());

	/// <summary>Register a float property of a transform that moves it when it changes.</summary>
	private protected static DependencyProperty<TOwner, float> Register<TOwner>(string name, float defaultValue = 0f)
		where TOwner : Transform =>
		DependencyProperty.Register(name,
			new PropertyMetadata<TOwner, float> { DefaultValue = defaultValue, PropertyChanged = static (t, _, _) => t.Changed() });

	private protected static float Radians(float degrees) => degrees * (MathF.PI / 180f);
}

using System.Numerics;
using SUIsei.Markup;

namespace SUIsei.Media;

/// <summary>
/// A 2D shape, as WinUI's <c>Geometry</c>: what a <c>Path</c> draws and what <see cref="UIElement.Clip"/> clips to.
/// Every property of every part is a dependency property, so geometries are stylable and their numbers animatable.
/// <para>
/// A change anywhere in a geometry, down to one point of a segment, is reported to its listeners: the group or element
/// using it. An element registers while it is in a live tree and unregisters when it leaves, so a geometry kept in a
/// resource dictionary never keeps removed elements alive. Each geometry keeps its <see cref="CompositionPath"/>, the
/// neutral path the compositor and the tessellator read, and rebuilds it in place after a change, reusing its storage.
/// </para>
/// <para>
/// A geometry is drawn in the local space of the element that draws it, through its <see cref="Transform"/> if it has
/// one. The transform is applied where the geometry is used rather than to its path, so moving it does not rebuild the
/// path.
/// </para>
/// </summary>
public abstract class Geometry : DependencyObject, ISubPropertyListener
{
	/// <summary>The transform applied to the geometry where it is used, as WinUI's <c>Geometry.Transform</c>.</summary>
	public static readonly DependencyProperty<Geometry, Transform?> TransformProperty =
		DependencyProperty.Register(nameof(Transform), new PropertyMetadata<Geometry, Transform?>
		{
			PropertyChanged = static (geometry, oldValue, newValue) =>
			{
				if (geometry._listeners.Count > 0)
				{
					oldValue?.RemoveListener(geometry);
					newValue?.AddListener(geometry);
				}
				geometry._listeners.Notify(geometry);
			},
		});

	private readonly CompositionPath _path = new();
	private ListenerList _listeners;
	private bool _pathCurrent;

	private protected Geometry()
	{
	}

	/// <summary>
	/// A transform applied to the geometry's points where it is used: what a shape or a group draws, and what a clip clips
	/// to. A stroke follows the transformed points at its own thickness, so a scale here stretches the outline and not the
	/// pen. Null, the default, uses the geometry as it is. A change to the transform reaches the geometry's users like any
	/// other change. The geometry's own path is not rebuilt, though what uses it may redo its own work: a group rebuilds its
	/// combined path, and a tessellation of the geometry is redone. To move a drawing without that, animate the element's
	/// <see cref="UIElement.RenderTransform"/>.
	/// </summary>
	public Transform? Transform { get => GetValue(TransformProperty); set => SetValue(TransformProperty, value); }

	/// <summary>The <see cref="Transform"/> as a matrix, identity without one.</summary>
	internal Matrix3x2 TransformMatrix => Transform?.ToMatrix() ?? Matrix3x2.Identity;

	/// <summary>The tight axis-aligned bounds of the geometry through its <see cref="Transform"/>: the extremes of its
	/// curves, not their control points.</summary>
	public Rect Bounds => CompositionPath.GetBounds(TransformMatrix);

	/// <summary>
	/// The geometry as a neutral path, rebuilt in place on the first read after a change. Its
	/// <see cref="SUIsei.CompositionPath.Version"/> moves with every rebuild.
	/// </summary>
	internal CompositionPath CompositionPath
	{
		get
		{
			if (!_pathCurrent)
			{
				_path.Clear();
				_path.FillRule = FillRule.EvenOdd;
				BuildPath(_path);
				_pathCurrent = true;
			}
			return _path;
		}
	}

	/// <summary>
	/// Parse WinUI path markup (<c>F1 M 0,0 L 10,0 10,10 Z</c>) into a <see cref="PathGeometry"/> of figures and segments,
	/// as a <c>Path.Data</c> attribute is: relative commands become absolute, a run of one command becomes a poly segment,
	/// and <c>S</c> and <c>T</c> become Béziers with their reflected control point. The name is WPF's; WinUI converts the
	/// same markup with no public method.
	/// </summary>
	/// <exception cref="FormatException">The markup is not valid; the message names the character where it went wrong.</exception>
	public static PathGeometry Parse(string markup)
	{
		ArgumentNullException.ThrowIfNull(markup);
		var geometry = new PathGeometry();
		var builder = new PathMarkupBuilder(geometry);
		if (!PathMarkup.TryParse(markup, builder, out var error))
			throw new FormatException($"'{markup}' is not valid path markup, {error}.");
		builder.Finish();
		return geometry;
	}

	/// <summary>Draw this geometry into <paramref name="path"/>, which is empty, with the even-odd fill rule.</summary>
	private protected abstract void BuildPath(CompositionPath path);

	// The geometry hears its transform only while something hears the geometry, so a shared transform keeps no unused
	// geometry alive, as an element hears a brush only while it is in a live tree.
	internal void AddListener(ISubPropertyListener listener)
	{
		_listeners.Add(listener);
		if (_listeners.Count == 1)
			Transform?.AddListener(this);
	}

	internal void RemoveListener(ISubPropertyListener listener)
	{
		var before = _listeners.Count;
		_listeners.Remove(listener);
		if (before == 1 && _listeners.Count == 0)
			Transform?.RemoveListener(this);
	}

	/// <summary>How many listeners hear this geometry's changes: the elements using it while they are in a live tree, and
	/// the groups holding it.</summary>
	internal int ListenerCount => _listeners.Count;

	/// <summary>Something this geometry draws has changed: rebuild its path on the next read and tell its listeners.</summary>
	private protected void Changed()
	{
		_pathCurrent = false;
		_listeners.Notify(this);
	}

	void ISubPropertyListener.OnSubPropertyChanged(object source)
	{
		// The transform moved: the path is unchanged, and only its users need to hear. Anything else changed the path.
		if (ReferenceEquals(source, Transform))
			_listeners.Notify(this);
		else
			Changed();
	}

	/// <summary>The change callback of a property that only changes what the geometry draws.</summary>
	private protected static void OnChanged<TOwner, TValue>(TOwner geometry, TValue oldValue, TValue newValue) where TOwner : Geometry =>
		geometry.Changed();
}

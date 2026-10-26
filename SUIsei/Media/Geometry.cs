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
/// WinUI's <c>Geometry.Transform</c> is not here yet, a gap the <see cref="Transform"/> family can now fill. A
/// geometry is drawn in the local space of the element that draws it.
/// </para>
/// </summary>
public abstract class Geometry : DependencyObject, ISubPropertyListener
{
	private readonly CompositionPath _path = new();
	private ListenerList _listeners;
	private bool _pathCurrent;

	private protected Geometry()
	{
	}

	/// <summary>The tight axis-aligned bounds of the geometry, in its own coordinates: the extremes of its curves, not
	/// their control points.</summary>
	public Rect Bounds => CompositionPath.Bounds;

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

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	/// <summary>How many listeners hear this geometry's changes: the elements using it while they are in a live tree, and
	/// the groups holding it.</summary>
	internal int ListenerCount => _listeners.Count;

	/// <summary>Something this geometry draws has changed: rebuild its path on the next read and tell its listeners.</summary>
	private protected void Changed()
	{
		_pathCurrent = false;
		_listeners.Notify(this);
	}

	void ISubPropertyListener.OnSubPropertyChanged(object source) => Changed();

	/// <summary>The change callback of a property that only changes what the geometry draws.</summary>
	private protected static void OnChanged<TOwner, TValue>(TOwner geometry, TValue oldValue, TValue newValue) where TOwner : Geometry =>
		geometry.Changed();
}

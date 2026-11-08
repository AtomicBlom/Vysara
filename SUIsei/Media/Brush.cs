using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>
/// What paints an element's area, text or border, as WinUI's <c>Brush</c>: a <see cref="SolidColorBrush"/>, a
/// <see cref="LinearGradientBrush"/> or an <see cref="ImageBrush"/>.
/// <para>
/// A brush is shareable, as in WinUI: put one in a <c>ResourceDictionary</c> and give it to many elements. Each brush
/// keeps one retained <see cref="SUIsei.Composition.CompositionBrush"/>, which every element showing it paints with, and a change to
/// the brush updates that paint in place and marks those elements' visuals dirty, with no layout and no allocation.
/// An element registers with a brush while it is in a live tree and unregisters when it leaves, so a long-lived brush
/// does not keep removed elements, or the trees a hot-reload rebuild replaced, alive.
/// </para>
/// <para>
/// Because the brush is shared, so are its animations: a storyboard that animates a shared brush, such as
/// <c>(Border.Background).(SolidColorBrush.Color)</c> on a border whose background is a resource, recolours every element
/// showing that brush, as it does in WinUI. Give an element its own brush to animate it alone.
/// </para>
/// <para>
/// Like any dependency object, a brush belongs to the UI thread of the elements that show it. The brushes of SUIsei's
/// built-in control look are the exception: they are sealed, so they are shared by every control on every thread, keep
/// no listeners, and throw if anything tries to change them.
/// </para>
/// </summary>
public abstract class Brush : DependencyObject
{
	/// <summary>Multiplies the alpha of everything the brush paints; animatable.</summary>
	public static readonly DependencyProperty<Brush, float> OpacityProperty =
		DependencyProperty.Register(nameof(Opacity),
			new PropertyMetadata<Brush, float>
			{
				DefaultValue = 1f,
				PropertyChanged = static (b, _, value) =>
				{
					b.CompositionBrush.Opacity = value;
					b.Changed();
				},
			});

	private ListenerList _listeners;

	private protected Brush(CompositionBrush paint) => CompositionBrush = paint;

	/// <summary>The brush's opacity, from 0 (transparent) to 1 (as painted).</summary>
	public float Opacity { get => GetValue(OpacityProperty); set => SetValue(OpacityProperty, value); }

	/// <summary>The retained paint every element showing this brush draws with, kept up to date in place.</summary>
	internal CompositionBrush CompositionBrush { get; }

	/// <summary>How many uses by live elements (and other listeners) are registered: what the brush currently keeps
	/// alive.</summary>
	internal int ListenerCount => _listeners.Count;

	// A sealed brush never changes, so it has nothing to tell anyone and keeps no listeners; that also keeps the built-in
	// look's shared brushes free of writes from every UI thread that shows them.
	internal void AddListener(ISubPropertyListener listener)
	{
		if (!IsSealed)
			_listeners.Add(listener);
	}

	internal void RemoveListener(ISubPropertyListener listener)
	{
		if (!IsSealed)
			_listeners.Remove(listener);
	}

	/// <summary>A sealed brush of <paramref name="color"/>, for SUIsei's built-in look.</summary>
	internal static SolidColorBrush Sealed(HdrColor color)
	{
		var brush = new SolidColorBrush(color);
		brush.Seal();
		return brush;
	}

	/// <summary>Tell everything showing this brush that its paint changed.</summary>
	private protected void Changed() => _listeners.Notify(this);
}

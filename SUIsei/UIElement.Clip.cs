using SUIsei.Extensions;
using SUIsei.Media;

namespace SUIsei;

public abstract partial class UIElement
{
	/// <summary>The rectangle this element's rendering and hit-testing are clipped to, as WinUI's
	/// <c>UIElement.Clip</c>.</summary>
	public static readonly DependencyProperty<UIElement, RectangleGeometry?> ClipProperty =
		DependencyProperty.Register(nameof(Clip), new PropertyMetadata<UIElement, RectangleGeometry?>
		{
			PropertyChanged = static (element, oldValue, newValue) => element.OnClipChanged(oldValue, newValue),
		});

	/// <summary>
	/// The rectangle, in this element's own coordinates, that its subtree draws and is hit-tested inside. Null, the
	/// default, clips nothing. A change to the geometry's <see cref="RectangleGeometry.Rect"/> applies at the next
	/// composite with no relayout, while the element is in a live tree.
	/// <para>
	/// <c>suisei:Clipping.ClipToBounds</c> clips to the element's layout bounds; with both set, the element clips to
	/// their intersection. A rectangle with a negative width or height clips everything away.
	/// </para>
	/// </summary>
	public RectangleGeometry? Clip { get => GetValue(ClipProperty); set => SetValue(ClipProperty, value); }

	private void OnClipChanged(RectangleGeometry? oldValue, RectangleGeometry? newValue)
	{
		if (AttachedRoot is not null)
		{
			Listen(oldValue, false);
			Listen(newValue, true);
		}
		ApplyClip();
	}

	/// <summary>Start or stop listening to <paramref name="geometry"/>, as to a brush or a transform.</summary>
	private void Listen(Geometry? geometry, bool listen)
	{
		if (geometry is null)
			return;
		if (listen)
			geometry.AddListener(this);
		else
			geometry.RemoveListener(this);
	}

	// Bring the visual's clip up to date without a relayout; the visual marks itself dirty only if the clip moved.
	private void ApplyClip() => Visual.Clip = EffectiveClip();

	/// <summary>The clip the element's visual applies, in its local space: its layout bounds when it clips to them
	/// (<see cref="ClipToBounds"/> or <c>suisei:Clipping.ClipToBounds</c>), its <see cref="Clip"/> rectangle, the
	/// intersection when both apply, or null for none.</summary>
	internal Rect? EffectiveClip()
	{
		Rect? clip = ClipToBounds || Clipping.GetClipToBounds(this) ? new Rect(0f, 0f, LayoutRect.Width, LayoutRect.Height) : null;
		if (Clip is not { } geometry)
			return clip;
		var rect = geometry.Rect;
		if (rect.Width < 0f || rect.Height < 0f)
			rect = new Rect(rect.X, rect.Y, 0f, 0f);
		return clip is { } bounds ? bounds.Intersect(rect) : rect;
	}
}

using System.Globalization;
using System.Numerics;
using System.Text;
using SUIsei.Animation;
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// One UI surface: owns the element <see cref="Content"/> tree and the composition <see cref="Visual"/> tree it
/// composites through a pluggable <see cref="IUICompositor"/>. Holds <b>no</b> engine or backend handle, so
/// <see cref="LayoutPass"/> + <see cref="Render"/> run to completion headless — input crosses in as a size, the
/// product is a neutral <see cref="DrawListData"/> (the headless-testable invariant).
/// </summary>
public sealed class UIRoot
{
	private readonly IUICompositor _compositor;
	// Overlay elements composited above Content (popups/flyouts), bottom-to-top. A full-surface entry (a light-dismiss
	// layer) fills the surface; a positioned entry takes its desired size at its offset.
	private readonly List<(UIElement Element, Vector2 Position, bool FullSurface)> _overlays = [];

	public UIRoot(DrawListId surface, IUICompositor? compositor = null)
	{
		Surface = surface;
		_compositor = compositor ?? new DrawListCompositor();
	}

	/// <summary>The surface this root composites into.</summary>
	public DrawListId Surface { get; }

	/// <summary>The authoring (element) tree. Propagates this root reference down through the element tree so
	/// children can reach <see cref="InputManager"/> for pointer capture.</summary>
	public UIElement? Content
	{
		get;
		set
		{
			if (ReferenceEquals(field, value)) return;
			field?.SetAttachedRoot(null);
			field = value;
			field?.SetAttachedRoot(this);
		}
	}

	/// <summary>The host's <see cref="InputManager"/>, set by <see cref="UIHost"/> so elements can call
	/// <see cref="UIElement.CapturePointer"/>.</summary>
	internal InputManager? InputManager { get; set; }

	/// <summary>The host's <see cref="AnimationClock"/>, set by <see cref="UIHost"/> so controls can begin/stop
	/// storyboards (e.g. <see cref="VisualStateManager"/>). Null for a root used standalone in tests.</summary>
	internal AnimationClock? Clock { get; set; }

	/// <summary>The composition tree this root renders.</summary>
	public RootVisual Visual { get; } = new();

	/// <summary>True for an HDR surface (drives colour linearization downstream).</summary>
	public bool IsHdrSurface { get => Visual.IsHdrSurface; set => Visual.IsHdrSurface = value; }

	/// <summary>DIP→pixel scale: window DPI/96 for an overlay, author-chosen texel density for an in-world quad
	/// Layout runs in DIPs; the render walk scales positions by this.</summary>
	public float LayoutScale { get; set; } = 1f;

	/// <summary>Add an overlay element composited above <see cref="Content"/> (a popup/flyout, or its full-surface
	/// light-dismiss layer), positioned in DIPs. Marks the tree dirty so the next composite shows it.</summary>
	internal void AddOverlay(UIElement element, Vector2 position, bool fullSurface = false)
	{
		element.SetAttachedRoot(this);
		_overlays.Add((element, position, fullSurface));
		Visual.MarkDirty();
	}

	/// <summary>Remove a previously-added overlay element. Marks the tree dirty so it disappears on the next composite.</summary>
	internal void RemoveOverlay(UIElement element)
	{
		if (_overlays.RemoveAll(o => ReferenceEquals(o.Element, element)) == 0)
			return;
		element.SetAttachedRoot(null);
		Visual.MarkDirty();
	}

	/// <summary>The overlay elements above content (bottom-to-top) — read by hit-testing (top-most first).</summary>
	internal IReadOnlyList<(UIElement Element, Vector2 Position, bool FullSurface)> Overlays => _overlays;

	/// <summary>The top-most element drawn at <paramref name="surfacePoint"/> (surface pixels): overlays top-most first,
	/// then content, each hit-tested through the root visual's DIP→pixel scale and clipped to the surface, as they are
	/// composited. Null outside the surface or where nothing hittable draws.</summary>
	internal UIElement? HitTest(Vector2 surfacePoint)
	{
		var surface = new Rect(0f, 0f, Visual.SurfaceSize.X, Visual.SurfaceSize.Y);
		if (!surface.Contains(surfacePoint))
			return null;
		for (var i = _overlays.Count - 1; i >= 0; i--)
		{
			if (_overlays[i].Element.HitTest(surfacePoint, Visual.Transform, surface) is { } hit)
				return hit;
		}
		return Content?.HitTest(surfacePoint, Visual.Transform, surface);
	}

	/// <summary>Measure + arrange the element tree against the surface (in pixels), then sync the composition tree.
	/// Layout happens in DIPs (<c>pixels / LayoutScale</c>); the root visual carries the DIP→pixel scale.</summary>
	public void LayoutPass(Size availablePixels)
	{
		// Update the root visual's surface size + DIP→pixel scale, marking dirty only on a real change so a fully
		// static frame produces no damage (and the compositor's frame-level no-op kicks in).
		var size = new Vector2(availablePixels.Width, availablePixels.Height);
		if (Visual.SurfaceSize != size)
		{
			Visual.SurfaceSize = size;
			Visual.MarkDirty();
		}
		var transform = Matrix3x2.CreateScale(LayoutScale);
		if (Visual.Transform != transform)
		{
			Visual.Transform = transform;
			Visual.MarkDirty();
		}

		var availDip = new Size(availablePixels.Width / LayoutScale, availablePixels.Height / LayoutScale);

		Content?.Measure(availDip);
		Content?.Arrange(new Rect(0f, 0f, availDip.Width, availDip.Height));

		foreach (var (element, position, fullSurface) in _overlays)
		{
			element.Measure(availDip);
			element.Arrange(fullSurface
				? new Rect(0f, 0f, availDip.Width, availDip.Height)
				: new Rect(position.X, position.Y, element.DesiredSize.Width, element.DesiredSize.Height));
		}

		SyncRootChildren();
	}

	// Re-link the root visual's children to [Content, ...overlays] — only when the set actually changed, so a static
	// frame leaves the tree clean (preserving the compositor's no-op).
	private void SyncRootChildren()
	{
		var desired = (Content is not null ? 1 : 0) + _overlays.Count;
		var changed = Visual.Children.Count != desired;
		if (!changed)
		{
			var i = 0;
			if (Content is not null && !ReferenceEquals(Visual.Children[i++], Content.Visual))
				changed = true;
			for (var o = 0; !changed && o < _overlays.Count; o++)
				if (!ReferenceEquals(Visual.Children[i++], _overlays[o].Element.Visual))
					changed = true;
		}
		if (!changed)
			return;

		Visual.Clear();
		if (Content is not null)
			Visual.Add(Content.Visual);
		foreach (var (element, _, _) in _overlays)
			Visual.Add(element.Visual);
		Visual.MarkDirty();
	}

	/// <summary>Composite the current visual tree to a neutral <see cref="DrawListData"/> (default-path convenience —
	/// returns <see cref="DrawListData.Empty"/> when a foreign compositor is in use).</summary>
	public DrawListData Render()
	{
		_compositor.Composite(Visual, Surface);
		return _compositor is DrawListCompositor d ? d.LastDraw(Surface) : DrawListData.Empty;
	}

	/// <summary>The element tree annotated with <c>DesiredSize</c>/<c>LayoutRect</c> after a layout pass — the
	/// layout-test snapshot.</summary>
	public string DumpLayout()
	{
		var sb = new StringBuilder();
		if (Content is not null)
			DumpLayout(sb, Content, 0);
		return sb.ToString();
	}

	private static void DumpLayout(StringBuilder sb, UIElement el, int depth)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(' ', depth * 2);
		sb.Append(c, $"{el.GetType().Name} desired={el.DesiredSize} rect={el.LayoutRect}\n");
		foreach (var child in el.VisualChildren)
			DumpLayout(sb, child, depth + 1);
	}
}

using System.Globalization;
using System.Numerics;
using System.Text;
using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara;

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
	// How many entries at the end of _overlays are diagnostics overlays, which stay above every other overlay.
	private int _diagnosticOverlays;

	public UIRoot(DrawListId surface, IUICompositor? compositor = null)
	{
		Surface = surface;
		_compositor = compositor ?? new DrawListCompositor();
	}

	/// <summary>The surface this root composites into.</summary>
	public DrawListId Surface { get; }

	/// <summary>The view this root belongs to, or null for a root built on its own rather than by
	/// <see cref="UIHost.CreateRoot"/>.</summary>
	internal UIView? View { get; set; }

	/// <summary>The authoring (element) tree. Propagates this root reference down through the element tree so
	/// children can reach <see cref="InputManager"/> for pointer capture. The text effects in it are checked against
	/// <see cref="Font"/> first (<see cref="Extensions.TextEffects"/>), throwing before anything changes.</summary>
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

	/// <summary>The host's <see cref="FocusManager"/>, set by <see cref="UIHost"/>, so a subtree leaving the root gives up
	/// focus. Null for a root used standalone in tests.</summary>
	internal FocusManager? FocusManager { get; set; }

	// A subtree left this root: let go of the focus and pointer state held for any element in it. The events that tell those
	// elements so are raised by the host once layout is done.
	internal void ReleaseWithin(UIElement subtree)
	{
		FocusManager?.ReleaseWithin(subtree);
		InputManager?.ReleaseWithin(subtree);
	}

	/// <summary>The composition tree this root renders.</summary>
	public RootVisual Visual { get; } = new();

	/// <summary>True for an HDR surface (drives colour linearization downstream).</summary>
	public bool IsHdrSurface { get => Visual.IsHdrSurface; set => Visual.IsHdrSurface = value; }

	/// <summary>DIP→pixel scale: window DPI/96 for an overlay, author-chosen texel density for an in-world quad
	/// Layout runs in DIPs; the render walk scales positions by this.</summary>
	public float LayoutScale
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			// Rounded layout is rounded to this scale's pixels: lay it out again for the new ones
			if (UseLayoutRounding)
				InvalidateLayout();
		}
	} = 1f;

	/// <summary>
	/// Whether layout places every element's edges on whole pixels of this root's surface, as WinUI's
	/// <c>UseLayoutRounding</c> does: each element rounds its edges in its parent, which has already rounded its own, so
	/// every element lands on the pixel grid. A thin line then covers whole pixels wherever it moves, instead of
	/// spreading across two at fractional positions; anything that depends on how many pixels a line covers (crisp hairlines,
	/// a glow made from what is brighter than white) holds steady as it moves. Off by default, unlike WinUI, so a
	/// surface's layout is the same as it always was unless it asks; a render transform still places content anywhere.
	/// </summary>
	public bool UseLayoutRounding
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			InvalidateLayout();
		}
	}

	// Lay everything under the root out again, overlays included
	private void InvalidateLayout()
	{
		Content?.InvalidateMeasureTree();
		foreach (var (element, _, _) in _overlays)
			element.InvalidateMeasureTree();
	}

	/// <summary>
	/// How much of the bottom of the surface an on-screen keyboard covers, in surface pixels: zero (the default) while no
	/// keyboard is up. The host or app sets it as the keyboard comes and goes; the next layout pass arranges the elements
	/// marked <see cref="Extensions.InputPane.AvoidsKeyboardProperty"/> clear of it.
	/// </summary>
	public float InputPaneHeight { get; set => field = MathF.Max(0f, value); }

	/// <summary>
	/// The top edge of the on-screen keyboard in layout units (DIPs) as of the last layout pass, or null while there is no
	/// keyboard.
	/// </summary>
	internal float? InputPaneTop { get; private set; }

	/// <summary>
	/// How far in from each edge of the surface the screen hides content, in surface pixels: a phone's camera cutout or
	/// rounded corners. Zero (the default) on a display that hides none. The host or app sets it; the next layout pass
	/// arranges the elements marked <see cref="Extensions.SafeArea.EdgesProperty"/> clear of it.
	/// </summary>
	public Thickness SafeAreaInsets { get; set; }

	/// <summary>
	/// The part of the surface the screen hides nothing of, in layout units (DIPs), as of the last layout pass, or null
	/// while the whole surface is safe.
	/// </summary>
	internal Rect? SafeRect { get; private set; }

	/// <summary>
	/// The font this root's text measures and draws with: <see cref="FontAtlas.Default"/> unless set. A text element lays
	/// its run out against it, shaper included, and hands the same atlas to its content, so a run draws in the space
	/// layout gave it and a host binds the page the glyphs were laid out against. Setting a different font re-measures
	/// everything under the root. It is the font of all text that names no family, or one the host's
	/// <see cref="UIHost.Fonts"/> doesn't hold (<see cref="FontFor"/>). A text effect the new font's distance field cannot
	/// hold is drawn clamped to it (<see cref="Extensions.TextEffects"/>).
	/// </summary>
	public FontAtlas Font
	{
		get => field ??= FontAtlas.Default;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			if (ReferenceEquals(field, value))
				return;
			field = value;
			InvalidateText();
		}
	}

	/// <summary>The fonts families name, set by <see cref="UIHost"/>; null for a root used standalone, whose text all
	/// draws in <see cref="Font"/>.</summary>
	internal FontCollection? Fonts { get; set; }

	/// <summary>The font text in <paramref name="family"/> measures and draws with: the one the host's fonts hold under
	/// its name, or <see cref="Font"/> for no family or one they don't hold.</summary>
	internal FontAtlas FontFor(Media.FontFamily? family) =>
		family is not null && Fonts is { } fonts && fonts.TryGet(family.Source, out var font) ? font : Font;

	// The fonts text is drawn in changed: everything under the root measures again, overlays included
	internal void InvalidateText()
	{
		Content?.InvalidateMeasureTree();
		foreach (var (element, _, _) in _overlays)
			element.InvalidateMeasureTree();
	}

	/// <summary>Add an overlay element composited above <see cref="Content"/> (a popup/flyout, or its full-surface
	/// light-dismiss layer), positioned in DIPs. It goes above the other overlays but below any diagnostics overlay, which
	/// stays top-most. Marks the tree dirty so the next composite shows it.</summary>
	internal void AddOverlay(UIElement element, Vector2 position, bool fullSurface = false)
	{
		element.SetAttachedRoot(this);
		_overlays.Insert(_overlays.Count - _diagnosticOverlays, (element, position, fullSurface));
		Visual.MarkDirty();
	}

	/// <summary>Add a diagnostics overlay: a full-surface entry above every other overlay, kept there as popups open
	/// beneath it, and left out of <see cref="Diagnostics"/> tree walks and picks.</summary>
	internal void AddDiagnosticOverlay(UIElement element)
	{
		element.SetAttachedRoot(this);
		_overlays.Add((element, Vector2.Zero, true));
		_diagnosticOverlays++;
		Visual.MarkDirty();
	}

	/// <summary>Whether <paramref name="element"/> is one of this root's diagnostics overlays.</summary>
	internal bool IsDiagnosticOverlay(UIElement element)
	{
		for (var i = _overlays.Count - _diagnosticOverlays; i < _overlays.Count; i++)
		{
			if (ReferenceEquals(_overlays[i].Element, element))
				return true;
		}
		return false;
	}

	/// <summary>The elements at the top of this root's tree: <see cref="Content"/>, then the overlays bottom-to-top,
	/// without the diagnostics overlays.</summary>
	internal UIElement[] TopLevelElements()
	{
		var count = _overlays.Count - _diagnosticOverlays;
		var result = new List<UIElement>(count + 1);
		if (Content is not null)
			result.Add(Content);
		for (var i = 0; i < count; i++)
			result.Add(_overlays[i].Element);
		return [.. result];
	}

	/// <summary>Move a positioned overlay to <paramref name="position"/> (DIPs) and arrange it there at once, at the size it
	/// was last measured to, so something placed by its own size (a tooltip) shows where it belongs this frame.</summary>
	internal void MoveOverlay(UIElement element, Vector2 position)
	{
		var index = _overlays.FindIndex(o => ReferenceEquals(o.Element, element));
		if (index < 0 || _overlays[index].FullSurface)
			return;
		_overlays[index] = (element, position, false);
		element.Arrange(new Rect(position.X, position.Y, element.DesiredSize.Width, element.DesiredSize.Height));
		SyncRootChildren();
		Visual.MarkDirty();
	}

	/// <summary>Remove a previously-added overlay element, a diagnostics overlay included. Marks the tree dirty so it
	/// disappears on the next composite.</summary>
	internal void RemoveOverlay(UIElement element)
	{
		var index = _overlays.FindIndex(o => ReferenceEquals(o.Element, element));
		if (index < 0)
			return;
		if (index >= _overlays.Count - _diagnosticOverlays)
			_diagnosticOverlays--;
		_overlays.RemoveAt(index);
		element.SetAttachedRoot(null);
		Visual.MarkDirty();
	}

	/// <summary>The overlay elements above content (bottom-to-top) — read by hit-testing (top-most first).</summary>
	internal IReadOnlyList<(UIElement Element, Vector2 Position, bool FullSurface)> Overlays => _overlays;

	/// <summary>The top-most element drawn at <paramref name="surfacePoint"/> (surface pixels): overlays top-most first,
	/// then content, each hit-tested through the root visual's DIP→pixel scale and clipped to the surface, as they are
	/// composited. Null outside the surface or where nothing hittable draws. With <paramref name="includeHidden"/>, elements
	/// whose <see cref="UIElement.IsHitTestVisible"/> is false are tested too; with <paramref name="diagnosticsPick"/>, the
	/// diagnostics overlays are looked through at what is beneath them.</summary>
	internal UIElement? HitTest(Vector2 surfacePoint, bool includeHidden = false, bool diagnosticsPick = false)
	{
		var surface = new Rect(0f, 0f, Visual.SurfaceSize.X, Visual.SurfaceSize.Y);
		if (!surface.Contains(surfacePoint))
			return null;
		var top = diagnosticsPick ? _overlays.Count - _diagnosticOverlays : _overlays.Count;
		for (var i = top - 1; i >= 0; i--)
		{
			if (_overlays[i].Element.HitTest(surfacePoint, Visual.Transform, surface, includeHidden) is { } hit)
				return hit;
		}
		return Content?.HitTest(surfacePoint, Visual.Transform, surface, includeHidden);
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
		var inputPaneTop = InputPaneHeight > 0f ? (float?)((availablePixels.Height - InputPaneHeight) / LayoutScale) : null;
		var insets = SafeAreaInsets;
		var safeRect = insets == default
			? (Rect?)null
			: new Rect(insets.Left / LayoutScale, insets.Top / LayoutScale,
				MathF.Max(0f, availDip.Width - (insets.Left + insets.Right) / LayoutScale),
				MathF.Max(0f, availDip.Height - (insets.Top + insets.Bottom) / LayoutScale));
		if (inputPaneTop != InputPaneTop || safeRect != SafeRect)
		{
			InputPaneTop = inputPaneTop;
			SafeRect = safeRect;
			Content?.InvalidateArrangeTree();
			foreach (var (element, _, _) in _overlays)
				element.InvalidateArrangeTree();
		}

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

	/// <summary>Composite the current visual tree and return the list it produced: the compositor's
	/// <see cref="IDrawListSource.LastDraw"/>, or <see cref="DrawListData.Empty"/> when the compositor is not an
	/// <see cref="IDrawListSource"/>.</summary>
	public DrawListData Render()
	{
		_compositor.Composite(Visual, Surface);
		return LastDraw();
	}

	// The list the last successful composite produced: what a surface keeps showing when its composite fails. A compositor
	// that produces no draw lists hands out the empty one.
	internal DrawListData LastDraw() => _compositor is IDrawListSource source ? source.LastDraw(Surface) : DrawListData.Empty;

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

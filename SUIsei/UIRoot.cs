using System.Globalization;
using System.Numerics;
using System.Text;
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

	/// <summary>The composition tree this root renders (PlanningUI.md §D2.5).</summary>
	public RootVisual Visual { get; } = new();

	/// <summary>True for an HDR surface (drives colour linearization downstream).</summary>
	public bool IsHdrSurface { get => Visual.IsHdrSurface; set => Visual.IsHdrSurface = value; }

	/// <summary>DIP→pixel scale: window DPI/96 for an overlay, author-chosen texel density for an in-world quad
	/// (PlanningUI.md §D2.8). Layout runs in DIPs; the render walk scales positions by this.</summary>
	public float LayoutScale { get; set; } = 1f;

	/// <summary>Measure + arrange the element tree against the surface (in pixels), then sync the composition tree.
	/// Layout happens in DIPs (<c>pixels / LayoutScale</c>); the root visual carries the DIP→pixel scale.</summary>
	public void LayoutPass(Size availablePixels)
	{
		// Update the root visual's surface size + DIP→pixel scale, marking dirty only on a real change so a fully
		// static frame produces no damage (and the compositor's frame-level no-op kicks in — §D2.4/§D2.5).
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

		if (Content is null)
		{
			if (Visual.Children.Count != 0)
				Visual.Clear();
			return;
		}

		var availDip = new Size(availablePixels.Width / LayoutScale, availablePixels.Height / LayoutScale);
		Content.Measure(availDip);
		Content.Arrange(new Rect(0f, 0f, availDip.Width, availDip.Height));

		// Link the content's visual as the root's single child — only when it actually changed (re-linking every
		// frame would dirty the root and defeat the no-op).
		if (Visual.Children.Count != 1 || !ReferenceEquals(Visual.Children[0], Content.Visual))
		{
			Visual.Clear();
			Visual.Add(Content.Visual);
		}
	}

	/// <summary>Composite the current visual tree to a neutral <see cref="DrawListData"/> (default-path convenience —
	/// returns <see cref="DrawListData.Empty"/> when a foreign compositor is in use).</summary>
	public DrawListData Render()
	{
		_compositor.Composite(Visual, Surface);
		return _compositor is DrawListCompositor d ? d.LastDraw(Surface) : DrawListData.Empty;
	}

	/// <summary>The element tree annotated with <c>DesiredSize</c>/<c>LayoutRect</c> after a layout pass — the
	/// layout-test snapshot (PlanningUI.md §D8.1).</summary>
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

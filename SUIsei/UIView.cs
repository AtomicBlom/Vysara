
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// A root handle returned by <see cref="UIHost.CreateRoot"/>: the <see cref="UIRoot"/> (element + composition trees),
/// its <see cref="Layer"/>, and whether it is <see cref="Interactive"/>. Keep it to swap the root's content or tear
/// it down. Non-interactive views skip hit-test/focus entirely — useful for pure composites like health bars.
/// </summary>
public sealed class UIView
{
	internal UIView(UIRoot root, UILayer layer, bool interactive)
	{
		Root = root;
		Layer = layer;
		Interactive = interactive;
	}

	/// <summary>The element <c>Content</c> tree + composition <c>RootVisual</c> this view renders.</summary>
	public UIRoot Root { get; }

	/// <summary>Screen-space layer — z-order for hit-test priority and DIP→pixel scale.</summary>
	public UILayer Layer { get; set; }

	/// <summary>When false, the view is never hit-tested or focused — a pure composite.</summary>
	public bool Interactive { get; }

	/// <summary>The draw-list id this view composites into — hand to the engine painter to submit the composited frame.</summary>
	public DrawListId Surface => Root.Surface;
}

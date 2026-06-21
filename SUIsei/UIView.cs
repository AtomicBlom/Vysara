
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// A root handle returned by <see cref="UIHost.CreateRoot"/> (PlanningUI.md §D5.1): the <see cref="UIRoot"/> (element
/// + composition trees), its <see cref="Placement"/>, and whether it is <see cref="Interactive"/>. Keep it to swap
/// the root's content or tear it down. Non-interactive views skip hit-test/focus entirely (MU5 health bars/nameplates).
/// </summary>
public sealed class UIView
{
	internal UIView(UIRoot root, UIPlacement placement, bool interactive)
	{
		Root = root;
		Placement = placement;
		Interactive = interactive;
	}

	/// <summary>The element <c>Content</c> tree + composition <c>RootVisual</c> this view renders.</summary>
	public UIRoot Root { get; }

	/// <summary>Overlay z-layer (MU2) / in-world quad transform (MU5).</summary>
	public UIPlacement Placement { get; set; }

	/// <summary>When false, the view is never hit-tested or focused — a pure composite.</summary>
	public bool Interactive { get; }

	/// <summary>The draw-list id this view composites into — hand to the engine painter to submit the composited frame.</summary>
	public DrawListId Surface => Root.Surface;
}

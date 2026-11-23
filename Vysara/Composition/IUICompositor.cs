
using Vysara.Drawing;

namespace Vysara.Composition;

/// <summary>The portability boundary: renders a surface's <see cref="RootVisual"/> tree.
/// Loom ships <see cref="DrawListCompositor"/>; a foreign engine substitutes its own and consumes the tree directly.</summary>
public interface IUICompositor
{
	/// <summary>Turn one surface's composition tree into rendering.</summary>
	void Composite(RootVisual root, DrawListId surface);
}

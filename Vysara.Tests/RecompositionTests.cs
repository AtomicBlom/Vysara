using Vysara.Composition;
using Vysara.Controls;
using Vysara.Drawing;
using Vysara.Media;

namespace Vysara.Tests;

/// <summary>
/// Incremental recomposition: a fully-clean frame is a no-op — the compositor re-submits the cached
/// <c>DrawListData</c> with the same <c>Revision</c> and does not recomposite — while a
/// visual-only change marks the tree dirty and forces exactly one rebuild. (Output stays byte-identical; the
/// subtree-granular vertex-range reuse the design also describes is a deferred compositor-internal optimization.)
/// </summary>
public sealed class RecompositionTests
{
	[Test]
	public void A_clean_frame_is_a_no_op_with_the_same_revision()
	{
		var compositor = new DrawListCompositor();
		var root = new UIRoot(new DrawListId(0), compositor)
		{
			Content = new Border { Width = 50f, Height = 50f, Background = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)) },
		};

		root.LayoutPass(new Size(100f, 100f));
		var first = root.Render();
		compositor.CompositeCount.ShouldBe(1);

		root.LayoutPass(new Size(100f, 100f)); // nothing changed
		var second = root.Render();

		compositor.CompositeCount.ShouldBe(1);          // no recomposite happened
		second.Revision.ShouldBe(first.Revision);        // same frame data re-submitted
		second.Dump().ShouldBe(first.Dump());            // byte-identical
	}

	[Test]
	public void A_visual_change_triggers_exactly_one_recomposite()
	{
		var compositor = new DrawListCompositor();
		var border = new Border { Width = 50f, Height = 50f, Background = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)) };
		var root = new UIRoot(new DrawListId(0), compositor) { Content = border };

		root.LayoutPass(new Size(100f, 100f));
		root.Render();
		compositor.CompositeCount.ShouldBe(1);

		border.Background = new SolidColorBrush(HdrColor.FromRgb(0, 255, 0)); // visual-only change
		root.LayoutPass(new Size(100f, 100f));
		var after = root.Render();

		compositor.CompositeCount.ShouldBe(2);
		after.Revision.ShouldBe(2u);
	}
}

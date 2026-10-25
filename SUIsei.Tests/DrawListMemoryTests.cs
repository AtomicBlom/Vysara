using System.Numerics;
using SUIsei.Controls;
using SUIsei.Drawing;

namespace SUIsei.Tests;

/// <summary>
/// The compositor's retained draw-list memory: each surface keeps two buffer sets and fills them in turn, so a dirty
/// surface recomposites without allocating, the list built last stays readable while the next one builds, and a list
/// whose buffers a later composite has refilled throws instead of handing out someone else's geometry.
/// </summary>
public sealed class DrawListMemoryTests
{
	[Test]
	public void Recompositing_a_dirty_surface_allocates_nothing_once_warm()
	{
		// Every path the walk emits: rect fills, a glyph run, an image, and a faded group drawn as a layer.
		var fading = new Border { Width = 40f, Height = 20f, Background = HdrColor.FromRgb(200, 40, 40) };
		var group = new StackPanel { Opacity = 0.5f };
		group.Children.Add(new Border { Width = 30f, Height = 10f, Background = HdrColor.FromRgb(40, 200, 40) });
		group.Children.Add(new Border { Width = 30f, Height = 10f, Background = HdrColor.FromRgb(40, 40, 200) });
		var panel = new StackPanel { Background = HdrColor.FromRgb(20, 20, 20) };
		panel.Children.Add(fading);
		panel.Children.Add(new TextBlock { Text = "Allocation-free", FontSize = 16f });
		panel.Children.Add(new Image { Source = ImageSource.FromRgba8(2, 2, new byte[16]), Width = 8f, Height = 8f, Stretch = Stretch.Fill });
		panel.Children.Add(group);

		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		// The first frames grow both buffer sets to this surface's size and let the JIT settle.
		const int warm = 8;
		const int measured = 64;
		for (var i = 0; i < warm; i++)
			Frame(i);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < measured; i++)
			Frame(i);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		allocated.ShouldBe(0L);
		host.DrawLists[0].Data.Revision.ShouldBe((uint)(warm + measured)); // every frame really recomposited
		host.DrawLists[0].Data.Images.Count.ShouldBe(1);

		void Frame(int i)
		{
			// An opacity animation writes the visual and marks it dirty: no relayout and no content rebuild, so the
			// recomposite is the only work in the frame.
			fading.Visual.Opacity = (i & 1) == 0 ? 0.25f : 0.75f;
			fading.Visual.MarkDirty();
			host.Render();
		}
	}

	[Test]
	public void A_list_stays_readable_through_the_next_composite_and_throws_once_its_buffers_are_reused()
	{
		var border = new Border { Width = 10f, Height = 10f, Background = HdrColor.FromRgb(255, 0, 0) };
		var root = new UIRoot(new DrawListId(0)) { Content = border };
		root.LayoutPass(new Size(20f, 20f));

		var first = root.Render();
		var firstDump = first.Dump();

		border.Background = HdrColor.FromRgb(0, 255, 0);
		var second = root.Render();

		// The next composite filled the other buffer set, so the previous list is still intact beside it.
		second.ShouldNotBeSameAs(first);
		second.Revision.ShouldBe(first.Revision + 1);
		first.Dump().ShouldBe(firstDump);
		second.Dump().ShouldNotBe(firstDump);

		border.Background = HdrColor.FromRgb(0, 0, 255);
		var third = root.Render(); // refills the set the first list reads

		Should.Throw<InvalidOperationException>(() => first.Vertices);
		Should.Throw<InvalidOperationException>(() => first.Indices);
		Should.Throw<InvalidOperationException>(() => first.Commands);
		Should.Throw<InvalidOperationException>(() => first.Images);
		Should.Throw<InvalidOperationException>(() => first.Dump());

		// The lists whose buffers are still current read normally.
		second.Commands.Length.ShouldBe(1);
		third.Commands.Length.ShouldBe(1);
	}

	[Test]
	public void A_clean_frame_resubmits_the_same_list_and_keeps_it_readable()
	{
		var root = new UIRoot(new DrawListId(0))
		{
			Content = new Border { Width = 10f, Height = 10f, Background = HdrColor.FromRgb(255, 0, 0) },
		};
		root.LayoutPass(new Size(20f, 20f));

		var first = root.Render();
		var dump = first.Dump();
		for (var i = 0; i < 5; i++)
			root.Render().ShouldBeSameAs(first); // nothing dirty: no composite, so nothing is reused

		first.Dump().ShouldBe(dump);
	}
}

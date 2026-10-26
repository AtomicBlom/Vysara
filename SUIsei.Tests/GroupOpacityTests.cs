using System.Numerics;
using SUIsei.Drawing;

namespace SUIsei.Tests;

/// <summary>
/// Group-opacity coverage at the draw-list (description) level — the headless proof of the double-blend fix.
/// A container with sub-1 opacity (or a group effect) must composite its subtree as one
/// render-to-texture layer rather than folding the fade into each child's vertex alpha (which double-blends
/// overlapping children). The compositor expresses that as a <c>BeginLayer</c>/<c>EndLayer</c> marker pair around the
/// subtree, emitted at <b>full</b> alpha. Asserted on CPU via <see cref="DrawListData.Dump"/> — no backend.
/// </summary>
public sealed class GroupOpacityTests
{
	private static DrawListData Composite(RootVisual root)
	{
		var compositor = new DrawListCompositor();
		compositor.Composite(root, new DrawListId(0));
		return compositor.LastDraw(new DrawListId(0));
	}

	private static SpriteVisual Fill(float x, float y, float w, float h, HdrColor color) => new()
	{
		Transform = Matrix3x2.CreateTranslation(x, y),
		Content = new RectContent(new Vector2(w, h), new CompositionColorBrush(color)),
	};

	private static RootVisual SurfaceWith(Visual child, float w = 100f, float h = 100f)
	{
		var root = new RootVisual { SurfaceSize = new Vector2(w, h) };
		root.Add(child);
		return root;
	}

	[Test]
	public void A_faded_container_with_children_wraps_them_in_a_layer_at_full_alpha()
	{
		// Two overlapping rects inside a 50%-opacity container — the canonical double-blend reproducer.
		var group = new ContainerVisual { Opacity = 0.5f };
		group.Add(Fill(0, 0, 60, 60, HdrColor.FromRgb(255, 0, 0)));   // red
		group.Add(Fill(30, 30, 60, 60, HdrColor.FromRgb(0, 0, 255))); // blue, overlaps the red

		var draw = Composite(SurfaceWith(group));
		var dump = draw.Dump();

		// A BeginLayer/EndLayer pair brackets the subtree; the two fills batch into one run between them.
		draw.Commands.Length.ShouldBe(3);
		dump.ShouldContain("tag=5(BeginLayer)");
		dump.ShouldContain("tag=6(EndLayer)");

		// The children are emitted at FULL alpha (the fix): the fade is NOT folded into their vertices.
		dump.ShouldContain("col=(1,0,0,1)");  // red, opaque
		dump.ShouldContain("col=(0,0,1,1)");  // blue, opaque
		dump.ShouldNotContain("col=(0.5,0,0,0.5)"); // the pre-fix folded red would look like this

		// BeginLayer carries the group's fade tint (a,a,a,a) and bounds (the full surface, no clip).
		dump.ShouldContain("col=(0.5,0.5,0.5,0.5)");
		dump.ShouldContain("BeginLayer) tex=0 clip=(0,0 100x100)");
	}

	[Test]
	public void A_childless_faded_sprite_stays_on_the_cheap_fold_with_no_layer()
	{
		// A leaf has no overlap, so its opacity folds straight into vertex alpha — no layer, no RT round-trip.
		var draw = Composite(SurfaceWith(new SpriteVisual
		{
			Opacity = 0.5f,
			Content = new RectContent(new Vector2(60, 60), new CompositionColorBrush(HdrColor.FromRgb(255, 0, 0))),
		}));
		var dump = draw.Dump();

		draw.Commands.Length.ShouldBe(1);
		dump.ShouldNotContain("BeginLayer");
		dump.ShouldContain("col=(0.5,0,0,0.5)"); // red folded to 50% alpha
	}

	[Test]
	public void Nested_faded_containers_emit_nested_layer_pairs()
	{
		var inner = new ContainerVisual { Opacity = 0.5f };
		inner.Add(Fill(0, 0, 40, 40, HdrColor.FromRgb(0, 255, 0)));
		var outer = new ContainerVisual { Opacity = 0.5f };
		outer.Add(inner);

		var draw = Composite(SurfaceWith(outer));
		var dump = draw.Dump().ReplaceLineEndings("\n");

		// Order: open outer, open inner, the fill, close inner, close outer.
		var begins = dump.Split("BeginLayer").Length - 1;
		var ends = dump.Split("EndLayer").Length - 1;
		begins.ShouldBe(2);
		ends.ShouldBe(2);
		dump.IndexOf("BeginLayer", StringComparison.Ordinal)
			.ShouldBeLessThan(dump.IndexOf("EndLayer", StringComparison.Ordinal));
	}

	[Test]
	public void A_group_effect_forces_a_layer_even_at_full_opacity()
	{
		var group = new ContainerVisual { Effect = LayerEffect.Blur(4f) }; // Opacity stays 1
		group.Add(Fill(0, 0, 40, 40, HdrColor.FromRgb(255, 255, 255)));

		var dump = Composite(SurfaceWith(group)).Dump();

		dump.ShouldContain("tag=5(BeginLayer)");
		// The effect id+params ride the BeginLayer marker's Custom channel: (id=1 blur, sigma=4, _, _).
		dump.ShouldContain("custom=(1,4,0,0)");
	}
}

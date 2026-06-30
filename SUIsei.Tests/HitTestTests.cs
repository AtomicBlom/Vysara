using System.Numerics;

using SUIsei.Controls;
using SUIsei.Drawing;

namespace SUIsei.Tests;

/// <summary>
/// 2D hit-testing: a laid-out tree, then <see cref="UIElement.HitTest"/> with a
/// point in the element's local space — reverse-z (top-most wins), respecting <c>IsHitTestVisible</c>/
/// <c>Visibility</c>/<c>RenderTransform</c>, and only where an element actually draws (so transparent panels fall
/// through). Pure over the laid-out tree — no engine.
/// </summary>
public sealed class HitTestTests
{
	private static T Laid<T>(T content, float w = 200f, float h = 200f) where T : UIElement
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(w, h));
		return content;
	}

	[Test]
	public void Reverse_z_returns_the_topmost_overlapping_element()
	{
		var bottom = new Border { Width = 50, Height = 50, Background = HdrColor.FromRgb(255, 0, 0) };
		var top = new Border { Width = 50, Height = 50, Background = HdrColor.FromRgb(0, 255, 0) };
		var canvas = new Canvas();
		canvas.Children.Add(bottom);
		canvas.Children.Add(top); // added last ⇒ drawn on top
		Laid(canvas);

		canvas.HitTest(new Vector2(10, 10)).ShouldBe(top);
	}

	[Test]
	public void IsHitTestVisible_false_is_skipped()
	{
		var b = new Border { Width = 50, Height = 50, Background = HdrColor.FromRgb(255, 0, 0), IsHitTestVisible = false };
		var canvas = new Canvas();
		canvas.Children.Add(b);
		Laid(canvas);

		canvas.HitTest(new Vector2(10, 10)).ShouldBeNull();
	}

	[Test]
	public void Collapsed_element_is_not_hit()
	{
		var b = new Border { Width = 50, Height = 50, Background = HdrColor.FromRgb(255, 0, 0), Visibility = Visibility.Collapsed };
		var canvas = new Canvas();
		canvas.Children.Add(b);
		Laid(canvas);

		canvas.HitTest(new Vector2(10, 10)).ShouldBeNull();
	}

	[Test]
	public void A_transparent_panel_lets_the_point_fall_through()
	{
		var canvas = new Canvas(); // no background ⇒ no drawn content
		Laid(canvas);

		canvas.HitTest(new Vector2(10, 10)).ShouldBeNull();
	}

	[Test]
	public void Render_transform_offsets_the_hit_region()
	{
		var b = new Border { Width = 50, Height = 50, Background = HdrColor.FromRgb(255, 0, 0), RenderTransform = Matrix3x2.CreateTranslation(100f, 0f) };
		var canvas = new Canvas();
		canvas.Children.Add(b);
		Laid(canvas);

		canvas.HitTest(new Vector2(120f, 10f)).ShouldBe(b);   // shifted +100 in x
		canvas.HitTest(new Vector2(10f, 10f)).ShouldBeNull(); // its original cell is now empty
	}
}

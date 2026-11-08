using System.Numerics;
using System.Text;
using SUIsei.Composition;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;
using SUIsei.Media.Imaging;

namespace SUIsei.Tests;

/// <summary>
/// What a frame does with a value that can't be drawn as set. Paint the setters accept is drawn, clamped where it has no
/// defined meaning, or refused where it is set; and a composite that still throws leaves its surface showing its last good
/// list, composites the whole tree again next frame, and lets the host finish the frame before the exception leaves it.
/// </summary>
public sealed class RenderFailureTests
{
	private static readonly Vector2 Surface = new(200f, 200f);

	// A brush no compositor knows how to paint: the one way left to make a composite throw.
	private sealed class UnpaintableBrush : CompositionBrush
	{
		internal override void Describe(StringBuilder sb) => sb.Append("unpaintable");
	}

	private static (UIHost Host, Border Border) Hosted(Brush background)
	{
		var host = new UIHost();
		var border = new Border { Width = 50f, Height = 50f, Background = background };
		host.CreateRoot(UILayer.Default).Root.Content = border;
		host.Tick(0.016, 0f, Surface);
		return (host, border);
	}

	private static LinearGradientBrush Gradient(HdrColor end) => new()
	{
		GradientStops = { new GradientStop { Color = new HdrColor(0f, 0f, 0f, 1f), Offset = 0f }, new GradientStop { Color = end, Offset = 1f } },
	};

	/// <summary>sRGB interpolation is not defined above paper white, so a stop above it draws as a stop at it, as an
	/// 8-bit colour would, rather than stopping the frame.</summary>
	[Test]
	public void An_srgb_gradient_stop_above_paper_white_draws_as_paper_white()
	{
		var (overHost, _) = Hosted(Gradient(new HdrColor(2f, 2f, 2f, 1f)));
		var (whiteHost, _) = Hosted(Gradient(new HdrColor(1f, 1f, 1f, 1f)));

		Should.NotThrow(overHost.Render);
		whiteHost.Render();

		var over = overHost.DrawLists[0].Data.Vertices.ToArray().Select(v => v.Color);
		var white = whiteHost.DrawLists[0].Data.Vertices.ToArray().Select(v => v.Color);
		over.ShouldBe(white);
	}

	/// <summary>Where an sRGB gradient holds a stop's colour (before its first stop, past its last, and throughout one with
	/// no length) the stop is clamped to paper white as it is between stops, so there is no step where the stops begin.</summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public void An_srgb_gradient_clamps_a_stop_above_paper_white_outside_its_stops_too(bool degenerate)
	{
		LinearGradientBrush Inset(HdrColor bright)
		{
			var brush = new LinearGradientBrush
			{
				StartPoint = new Point(0f, 0f),
				EndPoint = degenerate ? new Point(0f, 0f) : new Point(1f, 0f),
				GradientStops =
				{
					new GradientStop { Color = bright, Offset = 0.25f },
					new GradientStop { Color = new HdrColor(0f, 0f, 0f, 1f), Offset = 0.75f },
				},
			};
			if (degenerate)
				brush.GradientStops.Add(new GradientStop { Color = bright, Offset = 1f });
			return brush;
		}

		var (overHost, _) = Hosted(Inset(new HdrColor(2f, 2f, 2f, 1f)));
		var (whiteHost, _) = Hosted(Inset(new HdrColor(1f, 1f, 1f, 1f)));
		overHost.Render();
		whiteHost.Render();

		var over = overHost.DrawLists[0].Data.Vertices.ToArray().Select(v => v.Color).ToArray();
		var white = whiteHost.DrawLists[0].Data.Vertices.ToArray().Select(v => v.Color).ToArray();
		over.Max(c => (float)c.R).ShouldBe(1f); // the rect's edge, before the first stop, is drawn at paper white
		over.ShouldBe(white);
	}

	/// <summary>A linear-light gradient keeps a stop above paper white: that is what it is for.</summary>
	[Test]
	public void A_linear_light_gradient_keeps_a_stop_above_paper_white()
	{
		var over = Gradient(new HdrColor(2f, 2f, 2f, 1f));
		over.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
		var white = Gradient(new HdrColor(1f, 1f, 1f, 1f));
		white.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
		var (overHost, _) = Hosted(over);
		var (whiteHost, _) = Hosted(white);

		overHost.Render();
		whiteHost.Render();

		overHost.DrawLists[0].Data.Vertices.ToArray().Max(v => (float)v.Color.R)
			.ShouldBeGreaterThan(whiteHost.DrawLists[0].Data.Vertices.ToArray().Max(v => (float)v.Color.R));
	}

	[Test]
	public void An_image_brush_is_refused_as_a_text_foreground_where_it_is_set()
	{
		var image = new ImageBrush { ImageSource = BitmapSource.FromRgba8(2, 2, new byte[16]) };

		Should.Throw<ArgumentException>(() => new TextBlock().Foreground = image).Message.ShouldContain("cannot paint text");
		Should.Throw<ArgumentException>(() => new ContentPresenter().Foreground = image);
	}

	/// <summary>A control's foreground takes any brush, as WinUI's does, since a template may paint more than text with it.
	/// Text an image brush reaches draws nothing, as glyphs can't sample an image, and the frame still draws.</summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public void A_control_takes_an_image_brush_as_its_foreground_and_text_it_reaches_draws_nothing(bool textBox)
	{
		Control Make() => textBox ? new TextBox { Text = "Go" } : new Button { Content = "Go" };
		static DrawListData Draw(Control control)
		{
			var root = new UIRoot(new DrawListId(0)) { Content = control };
			root.LayoutPass(new Size(200f, 100f));
			return root.Render();
		}

		var image = new ImageBrush { ImageSource = BitmapSource.FromRgba8(2, 2, new byte[16]) };
		var painted = Make();
		painted.Foreground = new SolidColorBrush(new HdrColor(1f, 0f, 0f, 1f));
		DrawsText(Draw(painted)).ShouldBeTrue();

		var imaged = Make();
		Should.NotThrow(() => imaged.Foreground = image);
		imaged.Foreground.ShouldBeSameAs(image);
		DrawListData draw = null!;
		Should.NotThrow(() => draw = Draw(imaged));
		DrawsText(draw).ShouldBeFalse();
	}

	private static readonly DependencyProperty<Border, CornerRadius> BoundRadiusProperty =
		DependencyProperty.RegisterAttached("BoundRadius", new PropertyMetadata<Border, CornerRadius>());

	/// <summary>A value bound from a template comes from another property, checked where it was set there, so a part that
	/// would refuse it set directly takes it and draws what it can: text bound to an image brush draws nothing, and a border
	/// bound to radii that differ draws at the top-left radius.</summary>
	[Test]
	public void A_template_bound_value_a_part_would_refuse_is_taken_and_drawn_as_it_can_be()
	{
		static DrawListData Draw(Border border)
		{
			border.Width = border.Height = 50f;
			var root = new UIRoot(new DrawListId(0)) { Content = border };
			root.LayoutPass(new Size(100f, 100f));
			return root.Render();
		}

		var image = new ImageBrush { ImageSource = BitmapSource.FromRgba8(2, 2, new byte[16]) };
		var button = new Button();
		var text = new TextBlock { Text = "Go" };
		button.RegisterTemplateBinding(Control.ForegroundProperty, text, TextBlock.ForegroundProperty);
		Should.NotThrow(() => button.Foreground = image);
		text.Foreground.ShouldBeSameAs(image);

		var red = new HdrColor(1f, 0f, 0f, 1f);
		var source = new Border();
		var part = new Border { Background = new SolidColorBrush(red) };
		source.RegisterTemplateBinding(BoundRadiusProperty, part, Border.CornerRadiusProperty);
		Should.NotThrow(() => source.SetValue(BoundRadiusProperty, new CornerRadius(4f, 1f, 2f, 3f)));
		part.CornerRadius.ShouldBe(new CornerRadius(4f, 1f, 2f, 3f));
		DrawListData drawn = null!;
		Should.NotThrow(() => drawn = Draw(part));
		var uniform = Draw(new Border { Background = new SolidColorBrush(red), CornerRadius = new CornerRadius(4f) });
		drawn.Vertices.ToArray().ShouldBe(uniform.Vertices.ToArray());
	}

	// Whether any glyph the list draws is visible.
	private static bool DrawsText(DrawListData draw)
	{
		var vertices = draw.Vertices.Span;
		var indices = draw.Indices.Span;
		foreach (var cmd in draw.Commands.Span)
		{
			if (cmd.Tag != (uint)UIPrimKind.Glyph)
				continue;
			for (var i = cmd.IndexOffset; i < cmd.IndexOffset + cmd.IndexCount; i++)
			{
				if ((float)vertices[(int)cmd.VertexOffset + indices[(int)i]].Color.A > 0f)
					return true;
			}
		}
		return false;
	}

	/// <summary>
	/// A composite that throws keeps the surface's last good list: the frames after it neither re-submit a half-built list
	/// nor treat the walked visuals as drawn, so the change that failed is composited again, and draws once it can.
	/// Another view still draws that frame, and the exception still leaves Render.
	/// </summary>
	[Test]
	public void A_composite_that_throws_keeps_the_last_good_list_and_composites_again_next_frame()
	{
		var host = new UIHost();
		var failing = new Border { Width = 50f, Height = 50f, Background = new SolidColorBrush(new HdrColor(1f, 0f, 0f, 1f)) };
		var other = new Border { Width = 50f, Height = 50f, Background = new SolidColorBrush(new HdrColor(0f, 1f, 0f, 1f)) };
		var failingView = host.CreateRoot(UILayer.Default);
		failingView.Root.Content = failing;
		var otherView = host.CreateRoot(UILayer.Default);
		otherView.Root.Content = other;
		host.Tick(0.016, 0f, Surface);
		host.Render();
		var good = host.DrawLists[0].Data.Revision;
		var otherBefore = host.DrawLists[1].Data.Revision;

		var content = failing.Visual.Content.ShouldBeOfType<RectContent>();
		var paint = content.Fill;
		content.Fill = new UnpaintableBrush();
		failing.Visual.MarkDirty();
		((SolidColorBrush)other.Background!).Color = new HdrColor(0f, 0f, 1f, 1f);

		Should.Throw<NotSupportedException>(host.Render);
		host.DrawLists.Count.ShouldBe(2);
		host.DrawLists[0].Data.Revision.ShouldBe(good); // the last good list, not a half-built one
		host.DrawLists[1].Data.Revision.ShouldNotBe(otherBefore); // the other view drew its change
		failingView.Root.Visual.IsDirty.ShouldBeTrue(); // the whole tree is composited again

		Should.Throw<NotSupportedException>(host.Render); // composited again, so it fails again, loudly
		content.Fill = paint;
		failing.Visual.MarkDirty();
		host.Render();
		host.DrawLists[0].Data.Revision.ShouldNotBe(good);
		// The same paint again draws what the last good list drew: damage measured against that list, not the half-built one
		// (which would differ everywhere), is nothing.
		host.DrawLists[0].Data.Damage.IsEmpty.ShouldBeTrue();
	}

	// Make the border's composite throw; returns the paint to put back.
	private static CompositionBrush? Break(Border border)
	{
		var content = border.Visual.Content.ShouldBeOfType<RectContent>();
		var paint = content.Fill;
		content.Fill = new UnpaintableBrush();
		border.Visual.MarkDirty();
		return paint;
	}

	private static void Mend(Border border, CompositionBrush? paint)
	{
		((RectContent)border.Visual.Content!).Fill = paint;
		border.Visual.MarkDirty();
	}

	private static Border Box(HdrColor color) => new() { Width = 50f, Height = 50f, Background = new SolidColorBrush(color) };

	/// <summary>A failed composite hands out the last good list again, which changes nothing: its damage is empty, frame
	/// after frame while the composite keeps failing.</summary>
	[Test]
	public void A_failed_composite_shows_the_last_good_list_again_with_no_damage()
	{
		var (host, border) = Hosted(new SolidColorBrush(new HdrColor(1f, 0f, 0f, 1f)));
		host.Render();
		var good = host.DrawLists[0].Data;
		var revision = good.Revision;

		Break(border);
		Should.Throw<NotSupportedException>(host.Render);
		var shown = host.DrawLists[0].Data;
		shown.ShouldBeSameAs(good);
		shown.Revision.ShouldBe(revision);
		shown.Damage.IsEmpty.ShouldBeTrue();

		Should.Throw<NotSupportedException>(host.Render);
		host.DrawLists[0].Data.Revision.ShouldBe(revision);
		host.DrawLists[0].Data.Damage.IsEmpty.ShouldBeTrue();
	}

	/// <summary>A surface whose first composite throws has no good list to show: it hands out
	/// <see cref="DrawListData.Empty"/>, shared by every surface, and leaves it as it is. Its first good list repaints
	/// everything.</summary>
	[Test]
	public void A_first_composite_that_throws_hands_out_the_empty_list_untouched()
	{
		var host = new UIHost();
		var border = Box(new HdrColor(1f, 0f, 0f, 1f));
		host.CreateRoot(UILayer.Default).Root.Content = border;
		var other = Box(new HdrColor(0f, 1f, 0f, 1f));
		host.CreateRoot(UILayer.Default).Root.Content = other;
		host.Tick(0.016, 0f, Surface);
		var paint = Break(border);

		Should.Throw<NotSupportedException>(host.Render);
		host.DrawLists[0].Data.ShouldBeSameAs(DrawListData.Empty);
		Should.Throw<NotSupportedException>(host.Render);
		host.DrawLists[0].Data.ShouldBeSameAs(DrawListData.Empty);
		DrawListData.Empty.Revision.ShouldBe(0u);
		DrawListData.Empty.TextureRevision.ShouldBe(0u);
		DrawListData.Empty.Damage.IsEmpty.ShouldBeTrue();

		Mend(border, paint);
		host.Render();
		host.DrawLists[0].Data.Damage.ShouldBe(new Rect(0f, 0f, Surface.X, Surface.Y));
	}

	/// <summary>
	/// A host may draw nothing from a render that throws, so what the other views' lists changed that frame is still to
	/// be drawn: their next lists carry that damage although nothing changed since, for as long as renders keep throwing,
	/// and the frame after the first render that returns has none.
	/// </summary>
	[Test]
	public void The_damage_of_a_render_that_threw_is_carried_into_the_next_render()
	{
		var host = new UIHost();
		var drawn = Box(new HdrColor(0f, 1f, 0f, 1f));
		host.CreateRoot(UILayer.Default).Root.Content = drawn;
		var failing = Box(new HdrColor(1f, 0f, 0f, 1f));
		host.CreateRoot(UILayer.Default).Root.Content = failing;
		host.Tick(0.016, 0f, Surface);
		host.Render();

		((SolidColorBrush)drawn.Background!).Color = new HdrColor(0f, 0f, 1f, 1f);
		var paint = Break(failing);
		host.Tick(0.016, 0f, Surface);
		Should.Throw<NotSupportedException>(host.Render);
		var recoloured = host.DrawLists[0].Data;
		var changed = new Rect(75f, 75f, 50f, 50f);
		recoloured.Damage.ShouldBe(changed);
		var revision = recoloured.Revision;

		host.Tick(0.016, 0f, Surface);
		Should.Throw<NotSupportedException>(host.Render); // still failing, and the recolour still undrawn
		host.DrawLists[0].Data.Revision.ShouldBe(revision);
		host.DrawLists[0].Data.Damage.ShouldBe(changed);

		Mend(failing, paint);
		host.Tick(0.016, 0f, Surface);
		host.Render(); // returns: a host draws this one
		host.DrawLists[0].Data.Revision.ShouldBe(revision);
		host.DrawLists[0].Data.Damage.ShouldBe(changed);

		host.Tick(0.016, 0f, Surface);
		host.Render();
		host.DrawLists[0].Data.Damage.IsEmpty.ShouldBeTrue();
	}

	/// <summary>
	/// Texels rewritten under a surface whose composite keeps failing reach the host at once, as they would on a clean
	/// frame: the last good list shown again moves its texture revision and damages what the image covers, rather than
	/// showing the old texels until the composite stops failing.
	/// </summary>
	[Test]
	public void Texels_rewritten_while_a_composite_keeps_failing_damage_the_last_good_list()
	{
		var host = new UIHost();
		var bitmap = new WriteableBitmap(2, 2);
		var border = Box(new HdrColor(1f, 0f, 0f, 1f));
		var grid = new Grid();
		grid.Children.Add(border);
		grid.Children.Add(new Image { Source = bitmap, Width = 20f, Height = 20f, Stretch = Stretch.Fill });
		host.CreateRoot(UILayer.Default).Root.Content = grid;
		host.Tick(0.016, 0f, Surface);
		host.Render();
		var good = host.DrawLists[0].Data;
		var (revision, textureRevision) = (good.Revision, good.TextureRevision);

		var paint = Break(border);
		bitmap.Invalidate();
		Should.Throw<NotSupportedException>(host.Render);
		var shown = host.DrawLists[0].Data;
		shown.Revision.ShouldBe(revision);
		shown.TextureRevision.ShouldBe(textureRevision + 1);
		shown.Damage.ShouldBe(new Rect(90f, 90f, 20f, 20f));

		Should.Throw<NotSupportedException>(host.Render); // the damage carried, as that render threw too
		host.DrawLists[0].Data.TextureRevision.ShouldBe(textureRevision + 1);
		host.DrawLists[0].Data.Damage.ShouldBe(new Rect(90f, 90f, 20f, 20f));

		// The same paint again recomposites to what the last good list drew, and the texels were reported already: what
		// this render says changed is only the damage the renders that threw carried.
		Mend(border, paint);
		host.Render();
		host.DrawLists[0].Data.Revision.ShouldNotBe(revision);
		host.DrawLists[0].Data.TextureRevision.ShouldBe(textureRevision + 1);
		host.DrawLists[0].Data.Damage.ShouldBe(new Rect(90f, 90f, 20f, 20f));

		host.Render();
		host.DrawLists[0].Data.Damage.IsEmpty.ShouldBeTrue();
	}
}

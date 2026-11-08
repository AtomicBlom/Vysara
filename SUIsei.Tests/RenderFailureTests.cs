using System.Numerics;
using System.Text;
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

		Should.Throw<NotSupportedException>(() => new TextBlock().Foreground = image).Message.ShouldContain("cannot paint text");
		Should.Throw<NotSupportedException>(() => new Button().Foreground = image);
		Should.Throw<NotSupportedException>(() => new ContentPresenter().Foreground = image);
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
}

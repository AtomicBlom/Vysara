using System.Numerics;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Rasterizer;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// A rounded Border's fill and ring ramp their edge over a pixel, half of it outside the box. At a fractional position
/// that outer half falls in pixels past the element's rect, and it is drawn there: a radio button's ring centred half a
/// pixel down kept its top and right fringe cut flat while the quad stopped at the rect.
/// </summary>
public sealed class RoundedBoxEdgeTests
{
	private const int Size = 32;

	// The sum of the red channel over a Size×Size surface, with a white 18×18 circle at (4, 4 + offset): a ring or a disc.
	private static float Coverage(float offset, bool ring)
	{
		var white = new SolidColorBrush(HdrColor.FromRgb(255, 255, 255));
		var box = new Border
		{
			Width = 18f,
			Height = 18f,
			CornerRadius = new CornerRadius(9f),
			Background = ring ? null : white,
			BorderBrush = ring ? white : null,
			BorderThickness = new Thickness(ring ? 1.5f : 0f),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(4f + offset, 4f + offset, 0f, 0f),
		};
		var root = new UIRoot(new DrawListId(0)) { Content = box };
		root.LayoutPass(new Size(Size, Size));
		var rast = new SoftwareRasterizer(Size, Size);
		rast.Clear(new Vector4(0f, 0f, 0f, 1f));
		rast.Render(root.Render());
		var sum = 0f;
		for (var y = 0; y < Size; y++)
			for (var x = 0; x < Size; x++)
				sum += rast.ReadLinear(x, y).X;
		return sum;
	}

	[Test]
	[Arguments(0.25f, false)]
	[Arguments(0.5f, false)]
	[Arguments(0.75f, false)]
	[Arguments(0.25f, true)]
	[Arguments(0.5f, true)]
	[Arguments(0.75f, true)]
	public void A_rounded_box_at_a_fractional_position_covers_what_it_does_on_whole_pixels(float offset, bool ring)
	{
		var whole = Coverage(0f, ring);
		Coverage(offset, ring).ShouldBe(whole, whole * 0.01f);
	}
}

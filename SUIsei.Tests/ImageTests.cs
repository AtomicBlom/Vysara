using SUIsei.Controls;
using SUIsei.Drawing;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="Image"/>: measures to the source's natural size, and the compositor emits a
/// <see cref="UIPrimKind.Image"/> command sampling the source's slot in the frame's
/// <see cref="DrawListData.TextureSlots"/>, which the host uploads and binds by slot.
/// </summary>
public sealed class ImageTests
{
	private static ImageSource TestSource(int width = 4, int height = 2) =>
		ImageSource.FromRgba8(width, height, new byte[width * height * 4]);

	[Test]
	public void Measures_to_its_natural_size()
	{
		var image = new Image { Source = TestSource(40, 20), Stretch = Stretch.None };

		image.Measure(new Size(1000f, 1000f));

		image.DesiredSize.ShouldBe(new Size(40f, 20f));
	}

	[Test]
	public void Compositor_emits_an_image_command_and_lists_the_source()
	{
		var source = TestSource();
		var image = new Image { Source = source, Width = 32f, Height = 16f, Stretch = Stretch.Fill };
		var root = new UIRoot(UISurface.Hud) { Content = image };

		root.LayoutPass(new Size(32f, 16f));
		var draw = root.Render();

		var slot = draw.TextureSlots.ToList().IndexOf(source);
		slot.ShouldBeGreaterThan(0); // slot 0 is the white texel
		draw.Commands.ToArray().ShouldContain(c => (UIPrimKind)c.Tag == UIPrimKind.Image && c.TextureIndex == slot);
	}
}

using System.Numerics;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Media.Imaging;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="DrawListData.Damage"/> is what changed since the list the surface handed out before: a recomposite's is
/// where its vertices moved, and a list handed out again unchanged has none, unless a texture it samples has changed
/// its texels since, when it covers what was drawn with that texture.
/// </summary>
public sealed class DamageTests
{
	private static readonly Vector2 Surface = new(200f, 200f);

	private static UIHost Hosted(UIElement content)
	{
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = content;
		return host;
	}

	private static DrawListData Frame(UIHost host)
	{
		host.Tick(0, 0f, Surface);
		host.Render();
		return host.DrawLists[0].Data;
	}

	[Test]
	public void A_list_handed_out_again_unchanged_has_no_damage()
	{
		var brush = new SolidColorBrush(HdrColor.FromRgb(220, 40, 40));
		var box = new Border { Width = 50f, Height = 50f, Background = brush };
		var host = Hosted(box);
		Frame(host).Damage.ShouldBe(new Rect(0f, 0f, Surface.X, Surface.Y)); // the first build repaints everything

		brush.Color = HdrColor.FromRgb(40, 40, 220);
		var recoloured = Frame(host);
		recoloured.Damage.ShouldBe(new Rect(75f, 75f, 50f, 50f));
		var revision = recoloured.Revision;

		// The compositor hands out the same view again, so read what it says now rather than compare it with itself.
		var again = Frame(host);
		again.ShouldBeSameAs(recoloured);
		again.Revision.ShouldBe(revision);
		again.Damage.IsEmpty.ShouldBeTrue();
	}

	[Test]
	public void Rewritten_texels_damage_one_frame_and_the_next_unchanged_one_has_none()
	{
		var bitmap = new WriteableBitmap(2, 2);
		var image = new Image { Source = bitmap, Width = 20f, Height = 20f, Stretch = Stretch.Fill };
		var host = Hosted(image);
		var first = Frame(host);
		var (revision, textureRevision) = (first.Revision, first.TextureRevision);

		bitmap.Invalidate();
		var rewritten = Frame(host);
		rewritten.Revision.ShouldBe(revision);
		rewritten.TextureRevision.ShouldBe(textureRevision + 1);
		rewritten.Damage.ShouldBe(new Rect(90f, 90f, 20f, 20f));

		var again = Frame(host);
		again.Revision.ShouldBe(revision);
		again.TextureRevision.ShouldBe(textureRevision + 1);
		again.Damage.IsEmpty.ShouldBeTrue();
	}

	/// <summary>A builder does not compare a list with the one before it, so its list damages the whole surface unless
	/// the build names what changed: a list saying nothing changed would never be drawn by a host that repaints only
	/// damage.</summary>
	[Test]
	public void A_builder_list_damages_the_whole_surface_unless_its_build_names_what_changed()
	{
		var builder = new DrawListBuilder();
		builder.Begin(Surface);
		builder.End().Damage.ShouldBe(new Rect(0f, 0f, Surface.X, Surface.Y));

		builder.Begin(Surface);
		builder.End(damage: new Rect(10f, 20f, 30f, 40f)).Damage.ShouldBe(new Rect(10f, 20f, 30f, 40f));
	}
}

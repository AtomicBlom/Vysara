namespace SUIsei.Tests;

/// <summary>
/// The sRGB→linear + premultiply contract of the UI authoring colour (PlanningUI.md §D4.4). This is the "pure
/// math" the doc calls out for headless testing: <see cref="HdrColor.ToPremultiplied"/> must linearize the
/// sRGB-authored channels and premultiply <em>in linear space</em> so the navy/blue HUD colours round-trip back to
/// their authored sRGB bytes when the sRGB backbuffer re-encodes the shader's linear output on store.
/// </summary>
public sealed class HdrColorTests
{
	[Test]
	public void FromRgb_mid_grey_linearizes_to_about_half()
	{
		// sRGB 188 (~0.737) is the perceptual midpoint; its linear value is ~0.5. Full alpha ⇒ premult == linear.
		var c = HdrColor.FromRgb(188, 188, 188).ToPremultiplied().ToVector4();
		((float)c.X).ShouldBe(0.5f, 0.02f);
		((float)c.W).ShouldBe(1f, 0.001f);
	}

	[Test]
	public void Opaque_white_stays_one_and_black_stays_zero()
	{
		var white = HdrColor.FromRgb(255, 255, 255).ToPremultiplied().ToVector4();
		((float)white.X).ShouldBe(1f, 0.001f);

		var black = HdrColor.FromRgb(0, 0, 0).ToPremultiplied().ToVector4();
		((float)black.X).ShouldBe(0f, 0.001f);
	}

	[Test]
	public void Alpha_premultiplies_after_linearization()
	{
		// White at 50% alpha: linear 1.0 × 0.5 = 0.5 premultiplied RGB, alpha 0.5 (premult must follow linearize).
		var c = HdrColor.FromRgb(255, 255, 255, 128).ToPremultiplied().ToVector4();
		var a = 128 / 255f;
		((float)c.X).ShouldBe(a, 0.01f);
		((float)c.W).ShouldBe(a, 0.01f);
	}

	[Test]
	public void Above_paper_white_extends_past_one()
	{
		// An opt-in HDR brush (>1) must linearize past 1.0, not clamp — it is what survives to the RGBA16F blend.
		var c = new HdrColor(2f, 2f, 2f, 1f).ToPremultiplied().ToVector4();
		((float)c.X).ShouldBeGreaterThan(1f);
	}
}

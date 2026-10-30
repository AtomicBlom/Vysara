using SUIsei.Controls;
using SUIsei.Media;
using SUIsei.Media.Imaging;

namespace SUIsei.Tests;

/// <summary>
/// Style system: <see cref="Style"/> writes the Style rung of the DP precedence ladder (below Local, above
/// Default); <see cref="Style.BasedOn"/> chains base-first so derived setters override base; assigning a new
/// <see cref="Style"/> clears the previous style's setter values before applying the new ones.
/// </summary>
public sealed class StyleTests
{
	[Test]
	public void Style_setter_writes_the_style_rung()
	{
		var tb = new TextBlock();
		var style = new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 32f } }
		};

		tb.Style = style;

		tb.FontSize.ShouldBe(32f);
	}

	[Test]
	public void Local_value_outranks_style()
	{
		var tb = new TextBlock { FontSize = 20f };
		var style = new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 32f } }
		};

		tb.Style = style;

		tb.FontSize.ShouldBe(20f); // Local wins
	}

	[Test]
	public void Clearing_style_reverts_to_default()
	{
		var tb = new TextBlock();
		var defaultSize = tb.FontSize;
		var style = new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 48f } }
		};

		tb.Style = style;
		tb.FontSize.ShouldBe(48f);

		tb.Style = null;
		tb.FontSize.ShouldBe(defaultSize);
	}

	[Test]
	public void Replacing_style_clears_previous_setter_values()
	{
		var tb = new TextBlock();
		var style1 = new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 24f } }
		};
		var style2 = new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 36f } }
		};

		tb.Style = style1;
		tb.FontSize.ShouldBe(24f);

		tb.Style = style2;
		tb.FontSize.ShouldBe(36f);
	}

	[Test]
	public void Style_not_lost_when_replaced_with_one_that_omits_the_property()
	{
		var tb = new TextBlock();
		var defaultSize = tb.FontSize;
		var style1 = new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 24f } }
		};
		var style2 = new Style(); // no FontSize setter

		tb.Style = style1;
		tb.Style = style2;

		tb.FontSize.ShouldBe(defaultSize); // style1's setter was cleared; no new setter → falls to Default
	}

	[Test]
	public void BasedOn_applies_base_style_first_so_derived_setters_override()
	{
		var tb = new TextBlock();
		var baseStyle = new Style
		{
			Setters =
			{
				new Setter { Property = TextBlock.FontSizeProperty, Value = 12f },
			}
		};
		var derived = new Style
		{
			BasedOn = baseStyle,
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 24f } }
		};

		tb.Style = derived;

		tb.FontSize.ShouldBe(24f); // derived wins over base
	}

	[Test]
	public void BasedOn_properties_not_overridden_by_derived_take_base_value()
	{
		var tb = new TextBlock();
		var baseStyle = new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 18f } }
		};
		var derived = new Style
		{
			BasedOn = baseStyle,
			Setters = { new Setter { Property = TextBlock.TextProperty, Value = "hello" } }
		};

		tb.Style = derived;

		tb.FontSize.ShouldBe(18f); // from base
		tb.Text.ShouldBe("hello");   // from derived
	}

	/// <summary>Every setter's value is checked before any is applied: a style with one the element refuses (here in its
	/// base, an image brush for text, which can't paint glyphs) throws and leaves the element as it was, its old style still
	/// applied and still its style.</summary>
	[Test]
	public void A_style_with_a_refused_setter_applies_none_of_its_setters()
	{
		var text = new TextBlock();
		var foreground = text.Foreground;
		var old = new Style { Setters = { new Setter { Property = FrameworkElement.WidthProperty, Value = 10f } } };
		text.Style = old;
		var bad = new Style
		{
			BasedOn = new Style
			{
				Setters =
				{
					new Setter { Property = FrameworkElement.HeightProperty, Value = 20f },
					new Setter { Property = TextBlock.ForegroundProperty, Value = new ImageBrush { ImageSource = BitmapSource.FromRgba8(2, 2, new byte[16]) } },
				},
			},
			Setters = { new Setter { Property = FrameworkElement.WidthProperty, Value = 30f } },
		};

		Should.Throw<ArgumentException>(() => text.Style = bad);
		text.Style.ShouldBeSameAs(old);
		text.Width.ShouldBe(10f);
		text.Height.ShouldBe(float.NaN);
		text.Foreground.ShouldBe(foreground);

		Should.Throw<ArgumentException>(() => bad.Apply(text));
		text.Width.ShouldBe(10f);
		text.Height.ShouldBe(float.NaN);

		var wrongType = new Style { Setters = { new Setter { Property = FrameworkElement.WidthProperty, Value = "wide" } } };
		Should.Throw<ArgumentException>(() => text.Style = wrongType);
		text.Width.ShouldBe(10f);
	}

	[Test]
	public void FindResource_walks_up_the_element_tree()
	{
		var root = new Border();
		var child = new TextBlock();
		root.Child = child;

		var dict = new ResourceDictionary();
		dict.Add("MyStyle", new Style
		{
			Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 22f } }
		});
		root.Resources = dict;

		var found = child.FindResource("MyStyle");

		found.ShouldNotBeNull();
		found.ShouldBeOfType<Style>();
	}

	[Test]
	public void FindResource_returns_null_when_key_is_absent()
	{
		var tb = new TextBlock();
		tb.FindResource("missing").ShouldBeNull();
	}

	[Test]
	public void ResourceDictionary_MergedDictionaries_searched_in_reverse_order()
	{
		var merged1 = new ResourceDictionary();
		merged1.Add("Key", "first");

		var merged2 = new ResourceDictionary();
		merged2.Add("Key", "second");

		var dict = new ResourceDictionary();
		dict.MergedDictionaries.Add(merged1);
		dict.MergedDictionaries.Add(merged2);

		dict.TryFind("Key", out var value).ShouldBeTrue();
		value.ShouldBe("second"); // merged2 (higher index) wins
	}
}
